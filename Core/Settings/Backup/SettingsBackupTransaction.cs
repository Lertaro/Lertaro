using System.Text.Json;

namespace Lertaro.Core;

internal static class SettingsBackupTransaction
{
    internal sealed class Journal
    {
        public int Version { get; set; } = 1;
        public string State { get; set; } = "prepared";
        public bool IncludesPluginFiles { get; set; }
        public List<OriginalFile> Files { get; set; } = [];
    }
    internal sealed record OriginalFile(string Path, SettingsBackupFile? Original);

    public static string Apply(string stagingDirectory, string dataDirectory, bool allowPluginFiles)
    {
        using var staging = new SettingsBackupPaths(stagingDirectory);
        using var target = new SettingsBackupPaths(dataDirectory);
        SettingsBackupManifest manifest;
        using (var metadata = staging.Read("manifest.json"))
            manifest = JsonSerializer.Deserialize<SettingsBackupManifest>(SettingsBackupFormat.ReadJson(metadata), SettingsBackupFormat.JsonOptions)
                ?? throw new InvalidDataException("Missing staged manifest.");
        SettingsBackup.ValidateManifest(manifest, allowPluginFiles);
        foreach (var file in manifest.Files) SettingsBackup.VerifyFile(staging, file);
        using (var settings = staging.Read("user-settings.json")) SettingsBackupFormat.ValidateSettings(settings);

        var transaction = "ConfigBackups/" + Guid.NewGuid().ToString("N");
        var journal = new Journal { IncludesPluginFiles = manifest.IncludesPluginFiles };
        foreach (var file in manifest.Files)
        {
            try
            {
                using var original = target.Read(file.Path);
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original));
                original.Position = 0;
                var backupPath = transaction + "/original/" + file.Path;
                target.Write(backupPath, output => original.CopyTo(output));
                journal.Files.Add(new(file.Path, new(backupPath, original.Length, hash, file.Category)));
            }
            catch (FileNotFoundException) { journal.Files.Add(new(file.Path, null)); }
        }
        // Every original and the durable journal exist before the first live replacement.
        SaveJournal(target, transaction, journal);
        var applied = new List<OriginalFile>();
        try
        {
            RotateMainSettingsBackups(target);
            // ponytail: v1 restores exact paths and retains unlisted files. Plugin rename migration
            // needs explicit stable-ID aliases in a later format, never a guessed name prefix.
            foreach (var file in manifest.Files)
            {
                using var content = staging.Read(file.Path);
                target.Write(file.Path, output => content.CopyTo(output));
                applied.Add(journal.Files[applied.Count]);
            }
            journal.State = "committed";
            SaveJournal(target, transaction, journal);
        }
        catch (Exception failure)
        {
            try
            {
                Rollback(target, applied);
                journal.State = "rolled-back";
                SaveJournal(target, transaction, journal);
            }
            catch (Exception rollback)
            {
                throw new AggregateException($"Import and rollback failed. Recovery files: {target.Resolve(transaction)}", failure, rollback);
            }
            throw;
        }
        return target.Resolve(transaction);
    }

    private static void RotateMainSettingsBackups(SettingsBackupPaths target)
    {
        FileStream current;
        try { current = target.Read("user-settings.json"); }
        catch (FileNotFoundException) { return; }
        using (current)
        {
            for (var index = 4; index >= 1; index--)
            {
                FileStream previous;
                try { previous = target.Read($"user-settings.json.bak.{index}"); }
                catch (FileNotFoundException) { continue; }
                using (previous) target.Write($"user-settings.json.bak.{index + 1}", output => previous.CopyTo(output));
            }
            target.Write("user-settings.json.bak.1", output => current.CopyTo(output));
        }
    }

    private static void SaveJournal(SettingsBackupPaths target, string transaction, Journal journal) =>
        target.Write(transaction + "/journal.json", output => JsonSerializer.Serialize(output, journal, SettingsBackupFormat.JsonOptions));

    public static void Recover(string dataDirectory)
    {
        using var target = new SettingsBackupPaths(dataDirectory);
        foreach (var path in target.Files("ConfigBackups").Where(p => p.Split('/').Length == 3 && p.EndsWith("/journal.json", StringComparison.Ordinal)).ToList())
        {
            Journal journal;
            using (var stream = target.Read(path))
                journal = JsonSerializer.Deserialize<Journal>(SettingsBackupFormat.ReadJson(stream), SettingsBackupFormat.JsonOptions)
                    ?? throw new InvalidDataException($"Invalid recovery journal: {path}");
            if (journal.Version != 1 || journal.State is not ("prepared" or "committed" or "rolled-back") || journal.Files == null)
                throw new InvalidDataException($"Unsupported recovery journal: {path}");
            if (journal.State != "prepared") continue;
            var transaction = path[..path.LastIndexOf('/')];
            foreach (var file in journal.Files)
            {
                SettingsBackupFormat.Category(file.Path, journal.IncludesPluginFiles);
                if (file.Original != null && file.Original.Path != transaction + "/original/" + file.Path)
                    throw new InvalidDataException($"Invalid recovery path: {path}");
            }
            Rollback(target, journal.Files);
            journal.State = "rolled-back";
            SaveJournal(target, transaction, journal);
        }
    }

    private static void Rollback(SettingsBackupPaths target, IEnumerable<OriginalFile> files)
    {
        var originals = files.Reverse().ToList();
        // A corrupt rollback copy must not partially undo a transaction.
        foreach (var file in originals)
            if (file.Original != null) SettingsBackup.VerifyFile(target, file.Original);
        foreach (var file in originals)
        {
            if (file.Original == null)
            {
                target.CreateParents(file.Path);
                File.Delete(target.Resolve(file.Path));
            }
            else
            {
                using var original = target.Read(file.Original.Path);
                target.Write(file.Path, output => original.CopyTo(output));
            }
        }
    }
}
