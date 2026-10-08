using System.Diagnostics;
using System.IO;
using Lertaro.App.Services;
using Lertaro.Core;
using System.Text.Json;
using System.IO.Compression;

namespace Lertaro.App.Tests.Services;

[TestClass]
public sealed class SettingsTransferRestartTests
{
    [TestMethod]
    public void StartSession_SyncClientHoldsLegacyLock_AppStillStarts()
    {
        var root = Directory.CreateTempSubdirectory("LertaroSyncLock-").FullName;
        try
        {
            using var sync = new FileStream(Path.Combine(root, ".settings-session.lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            using var session = SettingsTransferRestart.StartSession(root, [], out var message);
            Assert.IsNull(message);
            Assert.Throws<IOException>(() => SettingsBackup.OpenSession(root, true));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void StartSession_LockedExportDestination_KeepsSettingsAndRequestAndResumesSession()
    {
        var root = Directory.CreateTempSubdirectory("LertaroFailedExport-").FullName;
        try
        {
            var data = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
            var main = Path.Combine(data, "user-settings.json");
            File.WriteAllText(main, "{}");
            File.WriteAllText(Path.Combine(data, "plugin-settings.json"), "{}");
            var id = Guid.NewGuid().ToString("N");
            var requestDirectory = Directory.CreateDirectory(Path.Combine(data, "ConfigTransfers", id)).FullName;
            var destination = Path.Combine(root, "backup.zip");
            File.WriteAllText(destination, "previous backup");
            var requestPath = Path.Combine(requestDirectory, "request.json");
            File.WriteAllText(requestPath, JsonSerializer.Serialize(new SettingsTransferRestart.Request(true, destination, false, "Saved {0}")));
            using (var sync = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var session = SettingsTransferRestart.StartSession(data, [SettingsTransferRestart.ArgumentPrefix + id], out var message))
            {
                Assert.IsNotNull(message);
                Assert.AreEqual("{}", File.ReadAllText(main));
                Assert.IsTrue(File.Exists(requestPath));
                Assert.Throws<IOException>(() => SettingsBackup.OpenSession(data, true));
            }
            Assert.AreEqual("previous backup", File.ReadAllText(destination));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void StartSession_DataDirectoryBehindScoopJunctions_StartsWithoutATransferFailure(bool throughCurrent)
    {
        var root = Directory.CreateTempSubdirectory("LertaroScoopData-").FullName;
        var links = new List<string>();
        try
        {
            // scoop keeps Data in persist\<app>\Data and links it into the version folder, and links
            // `current` to the version folder: launching either the shortcut (through `current`) or the
            // version folder itself puts the whole data path behind links.
            var version = Directory.CreateDirectory(Path.Combine(root, "apps", "lertaro", "5.9.1")).FullName;
            var persist = Directory.CreateDirectory(Path.Combine(root, "persist", "lertaro", "Data")).FullName;
            links.Add(CreateLink(Path.Combine(version, "Data"), persist));
            var current = Path.Combine(root, "apps", "lertaro", "current");
            links.Add(CreateLink(current, version));
            var data = Directory.CreateDirectory(Path.Combine(throughCurrent ? current : version, "Data", "Users", "hash")).FullName;
            File.WriteAllText(Path.Combine(data, "user-settings.json"), "{}");
            File.WriteAllText(Path.Combine(data, "plugin-settings.json"), "{}");

            using var session = SettingsTransferRestart.StartSession(data, [], out var message);

            Assert.IsNull(message);
            Assert.AreEqual("{}", File.ReadAllText(Path.Combine(data, "user-settings.json")));
        }
        finally
        {
            // Directory.Delete(root, recursive: true) gives up on a tree that still holds a junction.
            foreach (var link in links) Directory.Delete(link);
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ParseRequestId_MissingRequest_DoesNotScheduleTransfer() =>
        Assert.IsNull(SettingsTransferRestart.ParseRequestId(["--ordinary-launch"]));

    [TestMethod]
    public void ParseRequestId_ValidRequest_ReturnsCanonicalIdentifier()
    {
        var id = Guid.NewGuid();
        Assert.AreEqual(id.ToString("N"), SettingsTransferRestart.ParseRequestId([SettingsTransferRestart.ArgumentPrefix + id.ToString("N").ToUpperInvariant()]));
    }

    [TestMethod]
    [DataRow("../somebody-else")]
    [DataRow("C:\\external")]
    [DataRow("")]
    public void ParseRequestId_UntrustedPath_Rejects(string path) =>
        Assert.ThrowsExactly<InvalidDataException>(() => SettingsTransferRestart.ParseRequestId([SettingsTransferRestart.ArgumentPrefix + path]));

    [TestMethod]
    public void StartSession_ImportWaitsForOldSessionAndUsesStagedSnapshot()
    {
        var root = Directory.CreateTempSubdirectory("LertaroTransfer-").FullName;
        try
        {
            var main = Path.Combine(root, "user-settings.json");
            File.WriteAllText(main, "{\"Theme\":\"old-memory\"}");
            var id = Guid.NewGuid().ToString("N");
            var requestDirectory = Path.Combine(root, "ConfigTransfers", id);
            var stage = Directory.CreateDirectory(Path.Combine(requestDirectory, "payload")).FullName;
            var source = Path.Combine(root, "source.json");
            File.WriteAllText(source, "{\"Theme\":\"imported\"}");
            SettingsBackup.Prepare(source, stage);
            File.WriteAllText(Path.Combine(requestDirectory, "request.json"), JsonSerializer.Serialize(new SettingsTransferRestart.Request(false, null, false, "Restored; originals: {0}")));
            var arguments = new[] { SettingsTransferRestart.ArgumentPrefix + id };
            using (var oldProcess = SettingsTransferRestart.StartSession(root, [], out _))
            {
                using var failedTransfer = SettingsTransferRestart.StartSession(root, arguments, out var failure);
                Assert.IsNotNull(failure);
                Assert.AreEqual("{\"Theme\":\"old-memory\"}", File.ReadAllText(main));
                // Simulates an old plugin's exit save, after import validation but before restart.
                File.WriteAllText(main, "{\"Theme\":\"saved-on-exit\"}");
            }
            File.WriteAllText(source, "changed after validation");

            using var restarted = SettingsTransferRestart.StartSession(root, arguments, out var message);

            using var imported = JsonDocument.Parse(File.ReadAllText(main));
            Assert.AreEqual("imported", imported.RootElement.GetProperty("Theme").GetString());
            Assert.AreEqual("{\"Theme\":\"saved-on-exit\"}", File.ReadAllText(main + ".bak.1"));
            Assert.IsNotNull(message);
            Assert.Contains("ConfigBackups", message);
            Assert.IsFalse(File.Exists(Path.Combine(requestDirectory, "request.json")));
            Assert.IsEmpty(Directory.GetFiles(stage, "*", SearchOption.AllDirectories));
            Assert.Throws<IOException>(() => SettingsBackup.OpenSession(root, true));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void StartSession_ExportReadsLastSavedFilesWithoutLoadingPlugins()
    {
        var root = Directory.CreateTempSubdirectory("LertaroExportRestart-").FullName;
        try
        {
            var data = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
            var main = Path.Combine(data, "user-settings.json");
            File.WriteAllText(main, "{}");
            File.WriteAllText(Path.Combine(data, "plugin-settings.json"), "{}");
            var id = Guid.NewGuid().ToString("N");
            var requestDirectory = Directory.CreateDirectory(Path.Combine(data, "ConfigTransfers", id)).FullName;
            var zipPath = Path.Combine(root, "backup.zip");
            File.WriteAllText(Path.Combine(requestDirectory, "request.json"), JsonSerializer.Serialize(new SettingsTransferRestart.Request(true, zipPath, false, "Saved {0}")));
            File.WriteAllText(main, "{\"Theme\":\"last-save\"}");

            using var session = SettingsTransferRestart.StartSession(data, [SettingsTransferRestart.ArgumentPrefix + id], out var message);

            Assert.AreEqual("Saved " + zipPath, message);
            using var archive = ZipFile.OpenRead(zipPath);
            using var reader = new StreamReader(archive.GetEntry("user-settings.json")!.Open());
            Assert.AreEqual("{\"Theme\":\"last-save\"}", reader.ReadToEnd());
        }
        finally { Directory.Delete(root, true); }
    }

    private static string CreateLink(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "/c", "mklink", "/J", Path.GetFullPath(link), Path.GetFullPath(target) }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode, output);
        return link;
    }
}
