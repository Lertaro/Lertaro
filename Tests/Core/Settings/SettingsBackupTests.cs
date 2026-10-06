using System.IO.Compression;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Lertaro.Core.Tests.Settings;

[TestClass]
public sealed class SettingsBackupTests
{
    private readonly string _root = Directory.CreateTempSubdirectory("LertaroBackup-").FullName;
    private string Folder(string name) => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
    private string Write(string folder, string relative, byte[] bytes)
    {
        var path = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }
    private string Write(string folder, string relative, string text) => Write(folder, relative, Encoding.UTF8.GetBytes(text));

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }

    [TestMethod]
    public void ExportAndImport_PreservesOpaqueDisabledAndOrphanData_WithoutCachesOrMachineSettings()
    {
        var source = Folder("source");
        const string settings = """{"PluginSettings":{"Disabled_Native":{"Token":"private","future":{"x":1}}},"DisabledPluginComponents":["Bridge.dll::Provider::X"],"Future":{"nested":true}}""";
        Write(source, "user-settings.json", settings);
        Write(source, "FlowData/Settings/Plugins.json", """{"中文_Name":{"Disabled":true,"ActionKeyword":"词","Future":123},"Uninstalled":{"Disabled":true}}""");
        Write(source, "FlowData/Settings/Plugins/中文_Name/Typed.json", """{"unknown":{"中文":true}}""");
        var bytes = new byte[] { 0, 255, 0x81, 0x40, 77 };
        Write(source, "FlowData/Settings/Plugins/中文_Name/nested/二进制.bin", bytes);
        Write(source, "FlowData/Settings/Plugins/中文/Settings.json", "{}");
        Write(source, "FlowData/Settings/Plugins/Uninstalled/config.dat", bytes);
        Write(source, "FlowData/Plugins/not-loadable/plugin.json", """{"ID":"stable-id","Name":"中文_Name","Version":"4.2","Language":"python","Website":"https://example.test"}""");
        Write(source, "Calendar/reminders.json", """{"Reminders":[{"Text":"提醒"}]}""");
        Write(source, "machine-settings.json", "private machine volumes");
        Write(source, "FlowData/Caches/private.dat", "cache");
        Write(source, "FlowData/WebView/profile", "cookies");
        Write(source, "ContentIndex/content_index.db", "database");
        var zipPath = Path.Combine(_root, "设置.zip");

        var manifest = SettingsBackup.Export(source, zipPath, "5.8.5");
        var stage = Folder("stage");
        SettingsBackup.Prepare(zipPath, stage);
        var target = Folder("different-user-root");
        Write(target, "machine-settings.json", "keep current machine");
        var recovery = SettingsBackup.Apply(stage, target);

        Assert.AreEqual(settings, File.ReadAllText(Path.Combine(target, "user-settings.json")));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(Path.Combine(target, "FlowData/Settings/Plugins/中文_Name/nested/二进制.bin")));
        foreach (var file in manifest.Files)
            CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(source, file.Path)), File.ReadAllBytes(Path.Combine(target, file.Path)));
        Assert.AreEqual("keep current machine", File.ReadAllText(Path.Combine(target, "machine-settings.json")));
        Assert.IsFalse(Directory.Exists(Path.Combine(target, "ContentIndex")));
        Assert.IsFalse(Directory.Exists(Path.Combine(target, "FlowData/Caches")));
        Assert.IsFalse(Directory.Exists(Path.Combine(target, "FlowData/Plugins")));
        Assert.ContainsSingle(manifest.Plugins.Where(p => p.Id == "stable-id" && p.Disabled == true && p.Version == "4.2"));
        Assert.ContainsSingle(manifest.Plugins.Where(p => p.Name == "Uninstalled" && p.Kind == "unattributed-flow"));
        Assert.IsTrue(File.Exists(Path.Combine(recovery, "journal.json")));
    }

    [TestMethod]
    [DataRow("utf-8")]
    [DataRow("gbk")]
    public void Import_Utf8FlagOrLegacyGbkNames_DecodesChineseIndependentlyOfSystemCodePage(string encoding)
    {
        var nameEncoding = encoding == "gbk" ? SettingsBackupFormat.Gbk : SettingsBackupFormat.Utf8;
        var path = CreateArchive(nameEncoding, "FlowData/Settings/Plugins/中文_插件/设置.bin", [0x81, 0x40, 0, 255]);
        var stage = Folder("stage");

        SettingsBackup.Prepare(path, stage);
        var target = Folder("target");
        SettingsBackup.Apply(stage, target);

        CollectionAssert.AreEqual(new byte[] { 0x81, 0x40, 0, 255 }, File.ReadAllBytes(Path.Combine(target, "FlowData/Settings/Plugins/中文_插件/设置.bin")));
    }

    [TestMethod]
    public void Export_UsesStoredEntriesAndUtf8Flag_PreservesReadOnlySourceBytes()
    {
        var source = Folder("source");
        var main = Write(source, "user-settings.json", "{}");
        File.SetAttributes(main, FileAttributes.ReadOnly);
        Write(source, "Calendar/提醒_é_😀.bin", [0, 0xff, 19]);
        var zipPath = Path.Combine(_root, "export.zip");

        SettingsBackup.Export(source, zipPath, "test");

        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries) Assert.AreEqual(entry.Length, entry.CompressedLength);
        using var file = File.OpenRead(zipPath);
        using var reader = new BinaryReader(file);
        Assert.AreEqual(0x04034b50u, reader.ReadUInt32());
        reader.ReadUInt16();
        Assert.AreNotEqual(0, reader.ReadUInt16() & 0x800, "The UTF-8 filename flag must not depend on Windows ACP.");
        Assert.AreEqual((ushort)0, reader.ReadUInt16(), "ZIP method must be Store, not Deflate.");
        Assert.IsFalse(File.GetAttributes(zipPath).HasFlag(FileAttributes.ReadOnly));
        Assert.IsNotNull(zip.GetEntry("Calendar/提醒_é_😀.bin"));
    }

    [TestMethod]
    [DataRow("utf-8")]
    [DataRow("gbk")]
    public void Import_LegacyJson_RestoresOnlyMainSettingsAndPreservesUnknownFields(string encoding)
    {
        var bytes = (encoding == "gbk" ? SettingsBackupFormat.Gbk : SettingsBackupFormat.Utf8).GetBytes("""{"Theme":"中文主题","Future":{"嵌套":true}}""");
        var legacy = Write(_root, "old.json", bytes);
        var target = Folder("target");
        var plugin = Write(target, "FlowData/Settings/Plugins/keep/data.bin", [7, 8, 9]);
        var stage = Folder("stage");

        var manifest = SettingsBackup.Prepare(legacy, stage);
        SettingsBackup.Apply(stage, target);

        Assert.AreEqual("legacy-json", manifest.ApplicationVersion);
        Assert.AreEqual("""{"Theme":"中文主题","Future":{"嵌套":true}}""", File.ReadAllText(Path.Combine(target, "user-settings.json")));
        CollectionAssert.AreEqual(new byte[] { 7, 8, 9 }, File.ReadAllBytes(plugin));
    }

    [TestMethod]
    [DataRow("../escape")]
    [DataRow("/rooted")]
    [DataRow("C:/absolute")]
    [DataRow("Calendar/../escape")]
    [DataRow("Calendar\\escape")]
    [DataRow("Calendar/x:stream")]
    [DataRow("Calendar/CON.txt")]
    [DataRow("Calendar/LPT1")]
    [DataRow("Calendar/trailing.")]
    [DataRow("Calendar/trailing ")]
    [DataRow("machine-settings.json")]
    [DataRow("FlowData/WebView/cookies")]
    public void Import_UnsafeOrOutOfScopePath_RejectsBeforeTouchingLiveData(string entry)
    {
        var zip = CreateArchive(SettingsBackupFormat.Utf8, entry, [1]);
        Assert.ThrowsExactly<InvalidDataException>(() => SettingsBackup.Prepare(zip, Folder("stage")));
        Assert.IsFalse(File.Exists(Path.Combine(_root, "escape")));
    }

    [TestMethod]
    [DataRow("future")]
    [DataRow("hash")]
    [DataRow("extra")]
    [DataRow("duplicate")]
    [DataRow("link")]
    [DataRow("missing")]
    public void Import_InvalidArchive_Rejects(string fault)
    {
        var zipPath = CreateArchive(SettingsBackupFormat.Utf8, "Calendar/a.bin", [1, 2]);
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update))
        {
            if (fault is "future" or "hash")
            {
                var entry = zip.GetEntry("manifest.json")!;
                SettingsBackupManifest manifest;
                using (var stream = entry.Open()) manifest = JsonSerializer.Deserialize<SettingsBackupManifest>(stream, SettingsBackupFormat.JsonOptions)!;
                if (fault == "future") manifest.FormatVersion = 999;
                else manifest.Files[1] = manifest.Files[1] with { Sha256 = new string('0', 64) };
                entry.Delete();
                using var output = zip.CreateEntry("manifest.json").Open();
                JsonSerializer.Serialize(output, manifest, SettingsBackupFormat.JsonOptions);
            }
            if (fault == "extra") zip.CreateEntry("Calendar/extra");
            if (fault == "duplicate") zip.CreateEntry("CALENDAR/A.BIN");
            if (fault == "link") zip.GetEntry("Calendar/a.bin")!.ExternalAttributes = unchecked((int)0xa0000000);
            if (fault == "missing") zip.GetEntry("Calendar/a.bin")!.Delete();
        }
        Assert.ThrowsExactly<InvalidDataException>(() => SettingsBackup.Prepare(zipPath, Folder("stage")));
    }

    [TestMethod]
    public void Import_PluginDirectoryFiles_RequiresExplicitConsentAtBothPrepareAndApply()
    {
        var source = Folder("source");
        Write(source, "user-settings.json", "{}");
        Write(source, "FlowData/Plugins/Custom/custom.dat", [255, 8]);
        var zip = Path.Combine(_root, "plugins.zip");
        SettingsBackup.Export(source, zip, "test", includePluginFiles: true);
        Assert.ThrowsExactly<InvalidDataException>(() => SettingsBackup.Prepare(zip, Folder("refused")));
        var stage = Folder("stage");
        SettingsBackup.Prepare(zip, stage, allowPluginFiles: true);
        var target = Folder("target");
        Assert.ThrowsExactly<InvalidDataException>(() => SettingsBackup.Apply(stage, target));
        SettingsBackup.Apply(stage, target, allowPluginFiles: true);
        CollectionAssert.AreEqual(new byte[] { 255, 8 }, File.ReadAllBytes(Path.Combine(target, "FlowData/Plugins/Custom/custom.dat")));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Apply_ReadOnlyLaterFile_RollsBackEarlierReplacementsAndNewFiles(bool hadMainFile)
    {
        var zip = CreateArchive(SettingsBackupFormat.Utf8, "Calendar/blocked.bin", [9]);
        var stage = Folder("stage");
        SettingsBackup.Prepare(zip, stage);
        var target = Folder("target");
        if (hadMainFile) Write(target, "user-settings.json", "{\"Theme\":\"old\"}");
        var blocked = Write(target, "Calendar/blocked.bin", [1]);
        File.SetAttributes(blocked, FileAttributes.ReadOnly);

        Assert.ThrowsExactly<UnauthorizedAccessException>(() => SettingsBackup.Apply(stage, target));

        if (hadMainFile) Assert.AreEqual("{\"Theme\":\"old\"}", File.ReadAllText(Path.Combine(target, "user-settings.json")));
        else Assert.IsFalse(File.Exists(Path.Combine(target, "user-settings.json")));
        CollectionAssert.AreEqual(new byte[] { 1 }, File.ReadAllBytes(blocked));
        if (hadMainFile)
        {
            var original = Directory.GetFiles(Path.Combine(target, "ConfigBackups"), "user-settings.json", SearchOption.AllDirectories).Single();
            Assert.AreEqual("{\"Theme\":\"old\"}", File.ReadAllText(original));
        }
    }

    [TestMethod]
    public void Export_LockedSource_KeepsPreviousArchiveAndReportsFile()
    {
        var source = Folder("source");
        Write(source, "user-settings.json", "{}");
        var locked = Write(source, "Calendar/locked.bin", [1]);
        var zip = Write(_root, "previous.zip", "previous backup");
        using var held = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var error = Assert.Throws<IOException>(() => SettingsBackup.Export(source, zip, "test"));

        Assert.Contains("locked.bin", error.Message);
        Assert.AreEqual("previous backup", File.ReadAllText(zip));
        Assert.IsEmpty(Directory.GetFiles(_root, "*.tmp"));
    }

    [TestMethod]
    public void Apply_PreservesCurrentUserAcl_DoesNotCopyReadOnlyOrSourceGrants()
    {
        var source = Folder("source");
        var main = Write(source, "user-settings.json", "{}");
        var acl = new FileInfo(main).GetAccessControl();
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        acl.AddAccessRule(new FileSystemAccessRule(everyone, FileSystemRights.Read, AccessControlType.Allow));
        new FileInfo(main).SetAccessControl(acl);
        File.SetAttributes(main, FileAttributes.ReadOnly);
        var zip = Path.Combine(_root, "acl.zip");
        SettingsBackup.Export(source, zip, "test");
        var stage = Folder("stage");
        SettingsBackup.Prepare(zip, stage);
        var target = Folder("target");

        SettingsBackup.Apply(stage, target);

        var restored = new FileInfo(Path.Combine(target, "user-settings.json"));
        Assert.IsFalse(restored.IsReadOnly);
        Assert.AreEqual(WindowsIdentity.GetCurrent().User, restored.GetAccessControl().GetOwner(typeof(SecurityIdentifier)));
        Assert.IsFalse(restored.GetAccessControl().GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().Any(r => r.IdentityReference == everyone));
        File.AppendAllText(restored.FullName, " ");
    }

    [TestMethod]
    public void SessionLease_RejectsTransferWhileAnyAppSessionIsRunning()
    {
        var root = Folder("user");
        using (var first = SettingsBackup.OpenSession(root, false))
        using (var second = SettingsBackup.OpenSession(root, false))
            Assert.Throws<IOException>(() => SettingsBackup.OpenSession(root, true));
        using var exclusive = SettingsBackup.OpenSession(root, true);
        Assert.Throws<IOException>(() => SettingsBackup.OpenSession(root, false));
    }

    [TestMethod]
    public void Import_GbkMainJsonInsideZip_NormalizesForTheActualSettingsReader()
    {
        var source = Folder("source");
        Write(source, "user-settings.json", SettingsBackupFormat.Gbk.GetBytes("{\"Theme\":\"中文主题\"}"));
        var zip = Path.Combine(_root, "gbk.zip");
        SettingsBackup.Export(source, zip, "test");
        var stage = Folder("stage");
        SettingsBackup.Prepare(zip, stage);
        var target = Folder("target");

        SettingsBackup.Apply(stage, target);

        var settings = UserSettingsPersistence.LoadFromPath(Path.Combine(target, "user-settings.json"), out _);
        Assert.AreEqual("中文主题", settings.Theme);
    }

    [TestMethod]
    public void Import_ZipWithExplicitDirectoryEntries_AcceptsWithoutRestoringTheirAttributes()
    {
        var zipPath = CreateArchive(SettingsBackupFormat.Gbk, "Calendar/中文.bin", [1]);
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update)) zip.CreateEntry("Calendar/");
        var stage = Folder("stage");
        var manifest = SettingsBackup.Prepare(zipPath, stage);
        Assert.HasCount(2, manifest.Files);
        CollectionAssert.AreEqual(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(stage, "Calendar/中文.bin")));
    }

    [TestMethod]
    public void Apply_RotatesExistingLegacyBackups_AndFailureToRotatePreservesMainSettings()
    {
        var legacy = Write(_root, "old.json", "{\"Theme\":\"imported\"}");
        var stage = Folder("stage");
        SettingsBackup.Prepare(legacy, stage);
        var target = Folder("target");
        var current = Write(target, "user-settings.json", "{\"Theme\":\"current\"}");
        var old = Write(target, "user-settings.json.bak.1", "{\"Theme\":\"previous\"}");
        File.SetAttributes(old, FileAttributes.ReadOnly);
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => SettingsBackup.Apply(stage, target));
        Assert.AreEqual("{\"Theme\":\"current\"}", File.ReadAllText(current));
        File.SetAttributes(old, FileAttributes.Normal);

        SettingsBackup.Apply(stage, target);

        Assert.AreEqual("{\"Theme\":\"current\"}", File.ReadAllText(old));
        Assert.AreEqual("{\"Theme\":\"previous\"}", File.ReadAllText(Path.Combine(target, "user-settings.json.bak.2")));
    }

    private string CreateArchive(Encoding encoding, string path, byte[] content)
    {
        var destination = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using var output = File.Create(destination);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, false, encoding);
        var manifest = new SettingsBackupManifest();
        foreach (var (name, data) in new[] { ("user-settings.json", Encoding.UTF8.GetBytes("{}")), (path, content) })
        {
            using var entry = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
            entry.Write(data);
            manifest.Files.Add(new(name, data.Length, Convert.ToHexString(SHA256.HashData(data)), name == "user-settings.json" ? "user-settings" : "calendar"));
        }
        if (path.StartsWith("FlowData/Settings/", StringComparison.Ordinal)) manifest.Files[1] = manifest.Files[1] with { Category = "flow-settings" };
        using var metadata = zip.CreateEntry("manifest.json", CompressionLevel.NoCompression).Open();
        JsonSerializer.Serialize(metadata, manifest, SettingsBackupFormat.JsonOptions);
        return destination;
    }
}
