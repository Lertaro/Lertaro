using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using Lertaro.Core.Services.Installation;

namespace Lertaro.Core.Tests.Settings;

[TestClass]
public sealed class PermissionCompatibilityTests
{
    private static readonly SecurityIdentifier User = WindowsIdentity.GetCurrent().User!;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LertaroPermissions_" + Guid.NewGuid().ToString("N"));

    public PermissionCompatibilityTests()
    {
        var directory = Directory.CreateDirectory(_root);
        var acl = directory.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(User, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(acl);
    }

    [TestCleanup]
    public void Cleanup()
    {
        var junction = Path.Combine(_root, "user", "plugin");
        if (Directory.Exists(junction)) Directory.Delete(junction);
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }

    [TestMethod]
    public void SharedLogs_NewAndExistingFiles_DoNotInheritPublicReadAccess()
    {
        var logs = Directory.CreateDirectory(Path.Combine(_root, "logs")).FullName;
        var previous = Path.Combine(logs, "service.log");
        File.WriteAllText(previous, "Private search path: C:\\Users\\Private\\document.txt");
        var shared = new InstallDirectoryLock.Zone(User,
            [InstallDirectoryLock.Allow(User, FileSystemRights.FullControl, AceFlags.ObjectInherit | AceFlags.ContainerInherit),
             InstallDirectoryLock.Allow(InstallDirectoryLock.Users, FileSystemRights.ReadAndExecute, AceFlags.ObjectInherit | AceFlags.ContainerInherit)]);
        InstallDirectoryLock.LockSharedDataDirectory(_root, shared, PortableDirectoryLock.OwnedBy(User));
        var next = Path.Combine(logs, "next.log");
        File.WriteAllText(next, "next private path");
        foreach (var path in new[] { previous, next })
            Assert.IsFalse(new FileInfo(path).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>().Any(rule => rule.IdentityReference == InstallDirectoryLock.Users && rule.AccessControlType == AccessControlType.Allow));
    }

    [TestMethod]
    public void Migration_IntoPrivateDirectory_DoesNotImportOldReadGrants()
    {
        var old = Path.Combine(Directory.CreateDirectory(Path.Combine(_root, "SwiftList", "indexes")).FullName, "private.idx");
        File.WriteAllText(old, "private metadata");
        var acl = new FileInfo(old).GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(InstallDirectoryLock.Users, FileSystemRights.Read, AccessControlType.Allow));
        new FileInfo(old).SetAccessControl(acl);
        var current = Directory.CreateDirectory(Path.Combine(_root, "Lertaro")).FullName;
        InstallDirectoryLock.Lock(current, PortableDirectoryLock.OwnedBy(User), _ => null);

        SettingsDataDirectoryMigrator.Migrate(current, false);

        var copied = Path.Combine(current, "indexes", "private.idx");
        Assert.AreEqual("private metadata", File.ReadAllText(copied));
        Assert.IsFalse(new FileInfo(copied).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().Any(rule => rule.IdentityReference == InstallDirectoryLock.Users && rule.AccessControlType == AccessControlType.Allow));
    }

    [TestMethod]
    public void GrantsAtLeast_DeniedRead_IsNotHealthy()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "settings")).FullName;
        var file = Path.Combine(folder, "settings.json");
        File.WriteAllText(file, "{}");
        var zone = PortableDirectoryLock.OwnedBy(User);
        InstallDirectoryLock.Lock(folder, zone, _ => null);
        var acl = new DirectoryInfo(folder).GetAccessControl();
        var deny = new FileSystemAccessRule(User, FileSystemRights.ReadData,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Deny);
        acl.AddAccessRule(deny);
        new DirectoryInfo(folder).SetAccessControl(acl);
        try
        {
            Assert.ThrowsExactly<UnauthorizedAccessException>(() => File.ReadAllText(file));
            Assert.IsFalse(InstallDirectoryLock.GrantsAtLeast(folder, zone));
        }
        finally
        {
            acl.RemoveAccessRuleSpecific(deny);
            new DirectoryInfo(folder).SetAccessControl(acl);
        }
    }

    [TestMethod]
    public void UserDataRepair_PreservesLegitimateLinksAndTargets()
    {
        var target = Directory.CreateDirectory(Path.Combine(_root, "plugin-data")).FullName;
        var victim = Path.Combine(target, "config.json");
        File.WriteAllText(victim, "plugin preferences");
        var userFolder = Directory.CreateDirectory(Path.Combine(_root, "user")).FullName;
        var link = Path.Combine(userFolder, "plugin");
        Mklink("/J", link, target);
        Mklink("/H", Path.Combine(userFolder, "shared.json"), victim);

        var report = InstallDirectoryLock.Lock(userFolder, PortableDirectoryLock.OwnedBy(User), _ => null);

        Assert.IsEmpty(report.Failed);
        Assert.IsTrue(Directory.Exists(link));
        Assert.AreEqual("plugin preferences", File.ReadAllText(Path.Combine(userFolder, "shared.json")));
        Assert.IsFalse(new DirectoryInfo(target).GetAccessControl().AreAccessRulesProtected);
    }

    [TestMethod]
    public void AtomicSave_DoesNotFollowPredictableTemporaryHardlink()
    {
        var path = Path.Combine(_root, "settings.json");
        var victim = Path.Combine(_root, "other.json");
        File.WriteAllText(victim, "keep");
        Mklink("/H", $"{path}.{Environment.ProcessId}.tmp", victim);

        AtomicFileStore.Write(path, "new settings");

        Assert.AreEqual("keep", File.ReadAllText(victim));
        Assert.AreEqual("new settings", File.ReadAllText(path));
    }

    [TestMethod]
    public void Save_ReadOnlyDestination_PreservesOldContentAndCleansTemporaryFile()
    {
        var path = Path.Combine(_root, "settings.json");
        File.WriteAllText(path, "original");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            Assert.IsFalse(UserSettingsPersistence.TryPersist("changed", path));
            Assert.AreEqual("original", File.ReadAllText(path));
            Assert.IsEmpty(Directory.GetFiles(_root, "*.tmp"));
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
    }

    [TestMethod]
    public void Import_NullDocument_DoesNotReplaceExistingSettings()
    {
        var path = Path.Combine(_root, "settings.json");
        var source = Path.Combine(_root, "import.json");
        File.WriteAllText(path, "{\"Language\":\"zh-CN\"}");
        File.WriteAllText(source, "null");

        Assert.ThrowsExactly<InvalidDataException>(() => UserSettingsPersistence.WriteRestored(source, path, 5, out _));
        Assert.AreEqual("{\"Language\":\"zh-CN\"}", File.ReadAllText(path));
    }

    [TestMethod]
    public void MachineRead_AccessDenied_IsNotTreatedAsMissingSettings()
    {
        var path = Path.Combine(_root, "machine-settings.json");
        File.WriteAllText(path, "{\"LocalDrives\":[\"my-volume\"]}");
        var acl = new FileInfo(path).GetAccessControl();
        var deny = new FileSystemAccessRule(User, FileSystemRights.ReadData, AccessControlType.Deny);
        acl.AddAccessRule(deny);
        new FileInfo(path).SetAccessControl(acl);
        try
        {
            Assert.ThrowsExactly<UnauthorizedAccessException>(() => File.ReadAllText(path));
            Assert.ThrowsExactly<UnauthorizedAccessException>(() => MachineSettings.TryLoadFromFile(path));
        }
        finally
        {
            acl.RemoveAccessRuleSpecific(deny);
            new FileInfo(path).SetAccessControl(acl);
        }
    }

    [TestMethod]
    public void UserDataRepair_RenamedDirectory_DoesNotChangeOutsideTargetPermissions()
    {
        var outside = Directory.CreateDirectory(Path.Combine(_root, "outside")).FullName;
        var outsideFile = Path.Combine(outside, "settings.json");
        File.WriteAllText(outsideFile, "outside");
        var acl = new FileInfo(outsideFile).GetAccessControl();
        var marker = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        acl.AddAccessRule(new FileSystemAccessRule(marker, FileSystemRights.ReadData, AccessControlType.Allow));
        new FileInfo(outsideFile).SetAccessControl(acl);
        var userFolder = Directory.CreateDirectory(Path.Combine(_root, "user")).FullName;
        var nested = Directory.CreateDirectory(Path.Combine(userFolder, "nested")).FullName;
        File.WriteAllText(Path.Combine(nested, "settings.json"), "inside");
        var swapped = false;
        try
        {
            InstallDirectoryLock.Lock(userFolder, PortableDirectoryLock.OwnedBy(User), path =>
            {
                if (!swapped && path == Path.Combine(nested, "settings.json"))
                {
                    swapped = true;
                    Directory.Move(nested, Path.Combine(userFolder, "saved"));
                    Mklink("/J", nested, outside);
                }
                return null;
            });
            Assert.IsTrue(swapped);
            Assert.IsTrue(new FileInfo(outsideFile).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>().Any(rule => rule.IdentityReference == marker && !rule.IsInherited));
        }
        finally { if (swapped) Directory.Delete(nested); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Write_LockedBackup_DoesNotOverwriteCurrentSettings(bool import)
    {
        var path = Path.Combine(_root, "settings.json");
        var source = Path.Combine(_root, "import.json");
        File.WriteAllText(path, "{\"LogLevel\":\"Debug\"}");
        File.WriteAllText(source, "{\"LogLevel\":\"Error\"}");
        using var locked = new FileStream(path + ".bak.1", FileMode.CreateNew, FileAccess.Write, FileShare.None);

        if (import)
            Assert.Throws<IOException>(() => UserSettingsPersistence.WriteRestored(source, path, 5, out _));
        else
            Assert.IsFalse(UserSettingsPersistence.TryPersist(File.ReadAllText(source), path));
        Assert.AreEqual("{\"LogLevel\":\"Debug\"}", File.ReadAllText(path));
    }

    [TestMethod]
    public void Load_MissingPrimaryWithCorruptBackup_RefusesFreshDefaults()
    {
        var path = Path.Combine(_root, "settings.json");
        File.WriteAllText(path + ".bak.1", "{truncated");
        Assert.ThrowsExactly<InvalidDataException>(() => UserSettingsPersistence.LoadFromPath(path, out _));
    }

    [TestMethod]
    public void Rotation_ReadOnlySource_DoesNotPoisonFutureBackups()
    {
        var path = Path.Combine(_root, "settings.json");
        File.WriteAllText(path, "old");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        UserSettingsBackupStore.Rotate(path, 2);
        Assert.IsFalse(File.GetAttributes(path + ".bak.1").HasFlag(FileAttributes.ReadOnly));
    }

    [TestMethod]
    public void SettingsRoundTrip_PreservesFutureFieldsAndDisabledPluginConfiguration()
    {
        var path = Path.Combine(_root, "user-settings.json");
        File.WriteAllText(path, """{"FutureOption":{"Version":9},"DisabledPlugins":["inactive"],"PluginSettings":{"inactive":{"secret":"preserve"}}} """);
        var userSettings = UserSettingsPersistence.LoadFromPath(path, out _);
        userSettings.LogLevel = "Warn";
        Assert.IsTrue(UserSettingsPersistence.TryPersist(System.Text.Json.JsonSerializer.Serialize(userSettings), path));
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        Assert.AreEqual(9, json.RootElement.GetProperty("FutureOption").GetProperty("Version").GetInt32());
        Assert.AreEqual("preserve", json.RootElement.GetProperty("PluginSettings").GetProperty("inactive").GetProperty("secret").GetString());
        Assert.AreEqual("inactive", json.RootElement.GetProperty("DisabledPlugins")[0].GetString());

        var machinePath = Path.Combine(_root, "machine-settings.json");
        File.WriteAllText(machinePath, """{"LocalDrives":["old"],"LocalDriveSelectionConfigured":true,"ServiceLogLevel":"Debug","FutureMachineOption":9}""");
        var machine = MachineSettings.Load(machinePath);
        machine.LocalDrives = ["new"];
        machine.Save(machinePath);
        using var machineJson = System.Text.Json.JsonDocument.Parse(File.ReadAllText(machinePath));
        Assert.AreEqual(9, machineJson.RootElement.GetProperty("FutureMachineOption").GetInt32());
        Assert.AreEqual("Debug", machineJson.RootElement.GetProperty("ServiceLogLevel").GetString());
    }

    [TestMethod]
    public void ProfileRepair_RejectsLinkedAncestorWithoutChangingOutsideData()
    {
        var profile = Directory.CreateDirectory(Path.Combine(_root, "profile")).FullName;
        var outside = Directory.CreateDirectory(Path.Combine(_root, "outside", "Local", "Lertaro")).FullName;
        var file = Path.Combine(outside, "settings.json");
        File.WriteAllText(file, "keep");
        var before = new FileInfo(file).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        var link = Path.Combine(profile, "AppData");
        Mklink("/J", link, Path.Combine(_root, "outside"));
        try
        {
            Assert.Throws<IOException>(() => PortableDirectoryLock.RepairProfileUserDirectory(profile, User));
            Assert.AreEqual("keep", File.ReadAllText(file));
            Assert.AreEqual(before, new FileInfo(file).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access));
        }
        finally { Directory.Delete(link); }
    }

    [TestMethod]
    public void LockedPersonalFile_DoesNotPreventSecuringServiceFiles()
    {
        var personal = Directory.CreateDirectory(Path.Combine(_root, "user")).FullName;
        var config = Path.Combine(personal, "busy-plugin.json");
        File.WriteAllText(config, "keep");
        var serviceFile = Path.Combine(_root, "service.txt");
        File.WriteAllText(serviceFile, "service");
        using var locked = new FileStream(config, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var zone = new InstallDirectoryLock.Zone(User, PortableDirectoryLock.OwnedBy(User).Aces);
        var report = InstallDirectoryLock.Lock(_root, zone, path => path == personal ? PortableDirectoryLock.OwnedBy(User) : null);
        Assert.IsEmpty(report.Failed, "A busy personal plugin file is not a failure of the service's trust boundary.");
        Assert.HasCount(1, report.UserDataFailed);
        Assert.AreEqual("service", File.ReadAllText(serviceFile));
    }

    private static void Mklink(string kind, string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c mklink {kind} \"{link}\" \"{target}\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode);
    }
}
