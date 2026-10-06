using System.Diagnostics;
using Lertaro.Core.Services.Update;

namespace Lertaro.Core.Tests.Services.Update;

[TestClass]
public sealed class UpdaterRollbackTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Copy_ReplacementFailureRestoresPreviousFiles_AndPreservesUserData(bool blockLastFile)
    {
        var root = Directory.CreateTempSubdirectory("LertaroUpdate_中文_").FullName;
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root, "payload")).FullName;
            var target = Directory.CreateDirectory(Path.Combine(root, "app")).FullName;
            foreach (var name in new[] { "a.txt", "z.txt" })
            {
                File.WriteAllText(Path.Combine(source, name), "new-" + name);
                File.WriteAllText(Path.Combine(target, name), "old-" + name);
            }
            Directory.CreateDirectory(Path.Combine(source, "Data"));
            Directory.CreateDirectory(Path.Combine(target, "Data"));
            File.WriteAllText(Path.Combine(source, "Data", "settings.json"), "payload defaults");
            File.WriteAllText(Path.Combine(target, "Data", "settings.json"), "my preferences");
            File.WriteAllText(Path.Combine(target, "unrelated.txt"), "keep");
            var runner = Path.Combine(root, "run.ps1");
            File.WriteAllText(runner, """
                param($Updater, $TestSource, $TestDestination, $TestBackup)
                $ErrorActionPreference = 'Stop'
                . $Updater
                Invoke-LertaroUpdateCopy $TestSource $TestDestination $TestBackup
                """);
            using var blocker = blockLastFile
                ? new FileStream(Path.Combine(target, "z.txt"), FileMode.Open, FileAccess.Read, FileShare.Read) : null;
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", runner, Path.Combine(AppContext.BaseDirectory, "portable-updater.ps1"),
                source, target, Path.Combine(root, "rollback") }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await process.WaitForExitAsync(timeout.Token);
            var errors = await error;
            Assert.AreEqual(blockLastFile ? 1 : 0, process.ExitCode, errors + await output);
            if (blockLastFile) Assert.Contains("z.txt", errors);
            foreach (var name in new[] { "a.txt", "z.txt" })
                Assert.AreEqual((blockLastFile ? "old-" : "new-") + name, File.ReadAllText(Path.Combine(target, name)));
            Assert.AreEqual("my preferences", File.ReadAllText(Path.Combine(target, "Data", "settings.json")));
            Assert.AreEqual("keep", File.ReadAllText(Path.Combine(target, "unrelated.txt")));
            Assert.IsEmpty(Directory.GetFiles(target, "*.tmp", SearchOption.AllDirectories));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void UpdateLease_RejectsConcurrentWriter_AndReleasesAfterFailure()
    {
        var root = Directory.CreateTempSubdirectory("LertaroUpdateLease_").FullName;
        try
        {
            using (UpdateApplyRequestHandler.AcquireUpdateLock(root))
                Assert.Throws<IOException>(() => UpdateApplyRequestHandler.AcquireUpdateLock(root).Dispose());
            using var retry = UpdateApplyRequestHandler.AcquireUpdateLock(root);
            Assert.IsTrue(retry.CanWrite);
        }
        finally { Directory.Delete(root, true); }
    }
}
