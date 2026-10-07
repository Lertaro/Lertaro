using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace Lertaro.Core.Tests.Settings;

[TestClass]
public sealed class SettingsBackupRecoveryTests
{
    private readonly string _root = Directory.CreateTempSubdirectory("LertaroBackupRecovery-").FullName;
    private string Folder(string name) => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
    private static string Write(string root, string relative, string value)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, value);
        if (relative == "user-settings.json") File.WriteAllText(Path.Combine(root, "plugin-settings.json"), "{}");
        return path;
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, true);

    [TestMethod]
    public void Recover_InterruptedImport_RestoresOriginalsRemovesNewFilesAndIsIdempotent()
    {
        var source = Folder("source");
        Write(source, "user-settings.json", "{\"Theme\":\"new\"}");
        Write(source, "Calendar/new.json", "new reminder");
        var zip = Path.Combine(_root, "backup.zip");
        SettingsBackup.Export(source, zip, "test");
        var stage = Folder("stage");
        SettingsBackup.Prepare(zip, stage);
        var target = Folder("target");
        Write(target, "user-settings.json", "{\"Theme\":\"original\"}");
        Write(target, "Calendar/unrelated.json", "keep");
        var recovery = SettingsBackup.Apply(stage, target);
        var journalPath = Path.Combine(recovery, "journal.json");
        var journal = JsonSerializer.Deserialize<SettingsBackupTransaction.Journal>(File.ReadAllText(journalPath), SettingsBackupFormat.JsonOptions)!;
        // Represents termination after file replacements but before persisting the commit marker.
        journal.State = "prepared";
        File.WriteAllText(journalPath, JsonSerializer.Serialize(journal, SettingsBackupFormat.JsonOptions));

        SettingsBackup.RecoverInterruptedImports(target);
        SettingsBackup.RecoverInterruptedImports(target);

        Assert.AreEqual("{\"Theme\":\"original\"}", File.ReadAllText(Path.Combine(target, "user-settings.json")));
        Assert.IsFalse(File.Exists(Path.Combine(target, "Calendar/new.json")));
        Assert.AreEqual("keep", File.ReadAllText(Path.Combine(target, "Calendar/unrelated.json")));
    }

    [TestMethod]
    public void Apply_StagingTamperedAfterValidation_LeavesCurrentSettingsUntouched()
    {
        var source = Write(_root, "legacy.json", "{\"Theme\":\"new\"}");
        var stage = Folder("stage");
        SettingsBackup.Prepare(source, stage);
        Write(stage, "user-settings.json", "{\"Theme\":\"tampered\"}");
        var target = Folder("target");
        Write(target, "user-settings.json", "{\"Theme\":\"keep\"}");

        Assert.ThrowsExactly<InvalidDataException>(() => SettingsBackup.Apply(stage, target));

        Assert.AreEqual("{\"Theme\":\"keep\"}", File.ReadAllText(Path.Combine(target, "user-settings.json")));
        Assert.IsFalse(Directory.Exists(Path.Combine(target, "ConfigBackups")));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Transfer_ExistingJunction_RejectsWithoutTouchingExternalTarget(bool exporting)
    {
        var source = Folder("source");
        Write(source, "user-settings.json", "{}");
        Write(source, "Calendar/reminders.json", "new");
        var external = Folder("external");
        var victim = Write(external, "reminders.json", "external data");
        var target = Folder("target");
        Write(target, "user-settings.json", "{\"Theme\":\"old\"}");
        var link = Path.Combine(target, "Calendar");
        CreateLink("/J", link, external);
        try
        {
            var zip = Path.Combine(_root, "backup.zip");
            if (exporting) Assert.Throws<IOException>(() => SettingsBackup.Export(target, zip, "test"));
            else
            {
                SettingsBackup.Export(source, zip, "test");
                var stage = Folder("stage");
                SettingsBackup.Prepare(zip, stage);
                Assert.Throws<IOException>(() => SettingsBackup.Apply(stage, target));
            }
            Assert.AreEqual("external data", File.ReadAllText(victim));
            Assert.AreEqual("{\"Theme\":\"old\"}", File.ReadAllText(Path.Combine(target, "user-settings.json")));
        }
        finally { Directory.Delete(link); }
    }

    [TestMethod]
    public void Export_HardLinkedPrivateFile_RejectsInsteadOfLeakingItsContents()
    {
        var source = Folder("source");
        Write(source, "user-settings.json", "{}");
        var external = Write(_root, "private.txt", "private outside content");
        Directory.CreateDirectory(Path.Combine(source, "Calendar"));
        CreateLink("/H", Path.Combine(source, "Calendar/shared.json"), external);
        Assert.Throws<IOException>(() => SettingsBackup.Export(source, Path.Combine(_root, "backup.zip"), "test"));
        Assert.IsFalse(File.Exists(Path.Combine(_root, "backup.zip")));
        Assert.AreEqual("private outside content", File.ReadAllText(external));
    }

    [TestMethod]
    public void BackupPaths_PinAncestors_PreventDirectoryRenameDuringTransfer()
    {
        var target = Folder("target");
        using var paths = new SettingsBackupPaths(target);
        paths.CreateParents("Calendar/nested/data.bin");
        Assert.Throws<IOException>(() => Directory.Move(Path.Combine(target, "Calendar"), Path.Combine(target, "old")));
        Assert.Throws<IOException>(() => Directory.Move(target, Path.Combine(_root, "other")));
    }

    [TestMethod]
    public void Export_AccessDenied_IsAnErrorRatherThanMissingData()
    {
        var source = Folder("source");
        Write(source, "user-settings.json", "{}");
        var file = new FileInfo(Write(source, "Calendar/private.json", "keep"));
        var acl = file.GetAccessControl();
        var deny = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ReadData, AccessControlType.Deny);
        acl.AddAccessRule(deny);
        file.SetAccessControl(acl);
        var backup = Write(_root, "backup.zip", "existing backup");
        try
        {
            Assert.ThrowsExactly<UnauthorizedAccessException>(() => SettingsBackup.Export(source, backup, "test"));
            Assert.AreEqual("existing backup", File.ReadAllText(backup));
        }
        finally { acl.RemoveAccessRuleSpecific(deny); file.SetAccessControl(acl); }
    }

    private static void CreateLink(string kind, string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "/c", "mklink", kind, Path.GetFullPath(link), Path.GetFullPath(target) }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode, output);
    }
}
