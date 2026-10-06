using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Lertaro.Core;

/// <summary>Content-only, versioned settings backups. Call export/apply before starting any plugins.</summary>
public static class SettingsBackup
{
    public const string FileName = "lertaro-settings.zip";

    public static SettingsBackupManifest Export(string dataDirectory, string destination, string applicationVersion,
        bool includePluginFiles = false, string? applicationDirectory = null)
    {
        using var source = new SettingsBackupPaths(dataDirectory);
        var fullDestination = Path.GetFullPath(destination);
        if (fullDestination.StartsWith(source.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a backup destination outside the live user data directory.");
        using (var settings = source.Read("user-settings.json")) SettingsBackupFormat.ValidateSettings(settings);
        var manifest = new SettingsBackupManifest
        {
            ApplicationVersion = applicationVersion, IncludesPluginFiles = includePluginFiles,
            Plugins = SettingsBackupInventory.Read(source, applicationDirectory)
        };
        manifest.BridgeVersion = manifest.Plugins.FirstOrDefault(p => p.Name == "Lertaro.Plugins.FlowLauncherBridge")?.Version;
        var paths = new List<string> { "user-settings.json" };
        paths.AddRange(source.Files("FlowData/Settings"));
        paths.AddRange(source.Files("Calendar"));
        if (includePluginFiles) paths.AddRange(source.Files("FlowData/Plugins"));
        if (paths.Count > SettingsBackupFormat.MaximumFiles) throw new InvalidDataException("Too many backup files.");
        using var target = new SettingsBackupPaths(Path.GetDirectoryName(fullDestination)!);
        // Build and reread the complete ZIP before atomically replacing the previous export.
        target.Write(Path.GetFileName(fullDestination), output =>
        {
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true, SettingsBackupFormat.Utf8))
            {
                long total = 0;
                foreach (var path in paths)
                {
                    using var input = source.Read(path);
                    var length = input.Length;
                    total = checked(total + length);
                    if (total > SettingsBackupFormat.MaximumBytes) throw new InvalidDataException("Backup size limit exceeded.");
                    var hash = Convert.ToHexString(SHA256.HashData(input));
                    input.Position = 0;
                    var entry = zip.CreateEntry(path, CompressionLevel.NoCompression);
                    using var content = entry.Open();
                    if (SettingsBackupFormat.CopyBounded(input, content, length) != length)
                        throw new IOException($"Backup file changed while reading: {path}");
                    manifest.Files.Add(new(path, length, hash, SettingsBackupFormat.Category(path, includePluginFiles)));
                }
                var metadataBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, SettingsBackupFormat.JsonOptions);
                if (metadataBytes.Length > SettingsBackupFormat.MaximumJsonBytes) throw new InvalidDataException("Backup manifest is too large.");
                using var metadata = zip.CreateEntry("manifest.json", CompressionLevel.NoCompression).Open();
                metadata.Write(metadataBytes);
            }
            output.Flush();
            output.Position = 0;
            using var verification = new ZipArchive(output, ZipArchiveMode.Read, true, SettingsBackupFormat.Gbk);
            foreach (var file in manifest.Files)
            {
                using var content = verification.GetEntry(file.Path)!.Open();
                if (!Convert.ToHexString(SHA256.HashData(content)).Equals(file.Sha256, StringComparison.Ordinal))
                    throw new IOException($"Could not verify exported file: {file.Path}");
            }
        });
        return manifest;
    }

    /// <summary>Validates the complete source and writes only into an empty, private staging directory.</summary>
    public static SettingsBackupManifest Prepare(string sourcePath, string stagingDirectory, bool allowPluginFiles = false)
    {
        using var source = new SettingsBackupPaths(Path.GetDirectoryName(Path.GetFullPath(sourcePath))!);
        using var input = source.Read(Path.GetFileName(sourcePath));
        using var staging = new SettingsBackupPaths(stagingDirectory);
        if (Directory.EnumerateFileSystemEntries(stagingDirectory).Any()) throw new IOException("Backup staging must be empty.");
        SettingsBackupManifest manifest;
        if (input.ReadByte() == 'P' && input.ReadByte() == 'K')
        {
            input.Position = 0;
            // UTF-8 flagged entries always override this fallback, independently of the OS ANSI code page.
            using var zip = new ZipArchive(input, ZipArchiveMode.Read, true, SettingsBackupFormat.Gbk);
            manifest = ReadArchive(zip, staging, allowPluginFiles);
        }
        else
        {
            input.Position = 0;
            var json = SettingsBackupFormat.ReadJson(input);
            if (UserSettingsPersistence.TryParse(json) == null) throw new InvalidDataException("Invalid legacy user settings.");
            // Legacy GBK/BOM JSON is normalized for the UTF-8 settings reader; opaque plugin files never are.
            staging.Write("user-settings.json", output => output.Write(SettingsBackupFormat.Utf8.GetBytes(json)));
            using var file = staging.Read("user-settings.json");
            manifest = new SettingsBackupManifest { ApplicationVersion = "legacy-json", IsLegacyJson = true };
            manifest.Files.Add(new("user-settings.json", file.Length, Convert.ToHexString(SHA256.HashData(file)), "user-settings"));
        }
        using (var settings = staging.Read("user-settings.json")) SettingsBackupFormat.ValidateSettings(settings);
        string mainJson;
        bool legacyEncoding;
        using (var settings = staging.Read("user-settings.json")) mainJson = SettingsBackupFormat.ReadJson(settings, out legacyEncoding);
        if (legacyEncoding)
        {
            staging.Write("user-settings.json", output => output.Write(SettingsBackupFormat.Utf8.GetBytes(mainJson)));
            using var normalized = staging.Read("user-settings.json");
            var index = manifest.Files.FindIndex(f => f.Path.Equals("user-settings.json", StringComparison.OrdinalIgnoreCase));
            manifest.Files[index] = manifest.Files[index] with { Length = normalized.Length, Sha256 = Convert.ToHexString(SHA256.HashData(normalized)) };
        }
        staging.Write("manifest.json", output => JsonSerializer.Serialize(output, manifest, SettingsBackupFormat.JsonOptions));
        return manifest;
    }

    private static SettingsBackupManifest ReadArchive(ZipArchive zip, SettingsBackupPaths staging, bool allowPluginFiles)
    {
        if (zip.Entries.Count > SettingsBackupFormat.MaximumFiles * 2 + 1) throw new InvalidDataException("Too many backup entries.");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            var isDirectory = entry.FullName.EndsWith('/');
            var name = SettingsBackupPaths.Validate(isDirectory ? entry.FullName[..^1] : entry.FullName);
            if (!names.Add(name) || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 ||
                ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
                throw new InvalidDataException($"Duplicate or linked backup entry: {entry.FullName}");
            if (isDirectory)
            {
                if (entry.Length != 0) throw new InvalidDataException("Backup directory entries must be empty.");
                continue;
            }
            entries.Add(name, entry);
        }
        if (!entries.Remove("manifest.json", out var metadata)) throw new InvalidDataException("Missing backup manifest.");
        if (metadata.Length > SettingsBackupFormat.MaximumJsonBytes) throw new InvalidDataException("Backup manifest is too large.");
        SettingsBackupManifest manifest;
        using (var stream = metadata.Open())
            manifest = JsonSerializer.Deserialize<SettingsBackupManifest>(SettingsBackupFormat.ReadJson(stream), SettingsBackupFormat.JsonOptions)
                ?? throw new InvalidDataException("Invalid backup manifest.");
        ValidateManifest(manifest, allowPluginFiles);
        foreach (var file in manifest.Files)
        {
            if (!entries.Remove(file.Path, out var entry) || entry.Length != file.Length)
                throw new InvalidDataException($"Missing or incorrect backup entry: {file.Path}");
            staging.Write(file.Path, output =>
            {
                using var content = entry.Open();
                if (SettingsBackupFormat.CopyBounded(content, output, file.Length) != file.Length)
                    throw new InvalidDataException($"Truncated backup entry: {file.Path}");
            });
            VerifyFile(staging, file);
        }
        if (entries.Count != 0) throw new InvalidDataException("The ZIP contains unlisted files.");
        return manifest;
    }

    internal static void ValidateManifest(SettingsBackupManifest manifest, bool allowPluginFiles)
    {
        if (manifest.FormatVersion != 1) throw new InvalidDataException($"Unsupported backup format version: {manifest.FormatVersion}");
        if (manifest.IncludesPluginFiles && !allowPluginFiles) throw new InvalidDataException("This backup includes executable plugin files. Explicit consent is required.");
        if (manifest.Files == null || manifest.Files.Count == 0 || manifest.Files.Count > SettingsBackupFormat.MaximumFiles)
            throw new InvalidDataException("Invalid backup file list.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var file in manifest.Files)
        {
            if (file == null || file.Length < 0 || file.Length > SettingsBackupFormat.MaximumBytes || file.Sha256 == null ||
                file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit) ||
                SettingsBackupFormat.Category(file.Path, manifest.IncludesPluginFiles) != file.Category || !names.Add(file.Path))
                throw new InvalidDataException("Invalid backup file descriptor.");
            total = checked(total + file.Length);
        }
        if (total > SettingsBackupFormat.MaximumBytes || !names.Contains("user-settings.json"))
            throw new InvalidDataException("Invalid backup size or missing user settings.");
        foreach (var name in names)
        {
            var parts = name.Split('/');
            for (var i = 1; i < parts.Length; i++)
                if (names.Contains(string.Join('/', parts.Take(i)))) throw new InvalidDataException("Backup file/directory collision.");
        }
    }

    internal static void VerifyFile(SettingsBackupPaths tree, SettingsBackupFile file)
    {
        using var input = tree.Read(file.Path);
        if (input.Length != file.Length || !Convert.ToHexString(SHA256.HashData(input)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Backup integrity check failed: {file.Path}");
    }

    public static string Apply(string stagingDirectory, string dataDirectory, bool allowPluginFiles = false) => SettingsBackupTransaction.Apply(stagingDirectory, dataDirectory, allowPluginFiles);
    public static void RecoverInterruptedImports(string dataDirectory) => SettingsBackupTransaction.Recover(dataDirectory);

    /// <summary>All App instances hold a shared lease. Transfers require an exclusive lease.</summary>
    public static FileStream OpenSession(string dataDirectory, bool exclusive)
    {
        using var root = new SettingsBackupPaths(dataDirectory);
        var path = root.Resolve(".settings-session.lock");
        try
        {
            using var existing = root.Read(".settings-session.lock");
        }
        catch (FileNotFoundException)
        {
            using var created = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        }
        return new FileStream(path, FileMode.Open, exclusive ? FileAccess.ReadWrite : FileAccess.Read,
            exclusive ? FileShare.None : FileShare.Read);
    }
}
