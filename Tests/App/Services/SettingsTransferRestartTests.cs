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
                Assert.Throws<IOException>(() => SettingsTransferRestart.StartSession(root, arguments, out _));
                Assert.AreEqual("{\"Theme\":\"old-memory\"}", File.ReadAllText(main));
                // Simulates an old plugin's exit save, after import validation but before restart.
                File.WriteAllText(main, "{\"Theme\":\"saved-on-exit\"}");
            }
            File.WriteAllText(source, "changed after validation");

            using var restarted = SettingsTransferRestart.StartSession(root, arguments, out var message);

            Assert.AreEqual("{\"Theme\":\"imported\"}", File.ReadAllText(main));
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
}
