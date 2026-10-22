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
            foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-NonInteractive", "-File", runner,
                Path.Combine(AppContext.BaseDirectory, "portable-updater.ps1"),
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

    [TestMethod]
    public async Task StopService_Transient1061_RetriesUntilTheServiceIsStopped()
    {
        var root = Directory.CreateTempSubdirectory("LertaroUpdaterStop_").FullName;
        try
        {
            var sc = WriteFakeSc(root, 1061, 1062);
            var runner = WriteUpdaterRunner(root);

            var run = await RunUpdaterActionAsync(runner, "StopTransient", sc);

            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual(2, ReadFakeScCalls(sc));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task StopService_Persistent1061_StillFailsAfterItsRetryBudget()
    {
        var root = Directory.CreateTempSubdirectory("LertaroUpdaterStop_").FullName;
        try
        {
            var sc = WriteFakeSc(root, 1061);
            var runner = WriteUpdaterRunner(root);

            var run = await RunUpdaterActionAsync(runner, "StopPersistent", sc);

            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.IsGreaterThanOrEqualTo(2, ReadFakeScCalls(sc));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task StartService_Transient1061_RetriesUntilStartIsAccepted()
    {
        var root = Directory.CreateTempSubdirectory("LertaroUpdaterStart_").FullName;
        try
        {
            var sc = WriteFakeSc(root, 1061, 0);
            var runner = WriteUpdaterRunner(root);

            var run = await RunUpdaterActionAsync(runner, "StartTransient", sc);

            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual(2, ReadFakeScCalls(sc));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task CompleteUpdate_RestartFailure_IsRecordedAndStillReleasesTheLease()
    {
        var root = Directory.CreateTempSubdirectory("LertaroUpdaterRestart_").FullName;
        try
        {
            var target = Directory.CreateDirectory(Path.Combine(root, "app")).FullName;
            var sc = WriteFakeSc(root, 1058);
            var runner = WriteUpdaterRunner(root);

            var run = await RunUpdaterActionAsync(runner, "CompleteRestartFailure", sc,
                target, Path.Combine(root, "update.lock"));

            Assert.AreEqual(0, run.ExitCode, run.Error);
            using var verdict = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(Path.Combine(target, "update-result.json")));
            Assert.AreEqual("failed", verdict.RootElement.GetProperty("Status").GetString());
            var message = verdict.RootElement.GetProperty("Message").GetString();
            StringAssert.Contains(message, "copy succeeded");
            StringAssert.Contains(message, "Could not restart service: 1058");
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task WaitProcessExit_LingeringProcess_FailsNamingItInsteadOfKillingIt()
    {
        var root = Directory.CreateTempSubdirectory("LertaroUpdaterWait_").FullName;
        try
        {
            var runner = WriteUpdaterRunner(root);

            var run = await RunUpdaterActionAsync(runner, "WaitLingering", "");

            Assert.AreEqual(0, run.ExitCode, run.Error + run.Output);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task WaitProcessExit_AbsentProcess_ReturnsWithoutWaiting()
    {
        var root = Directory.CreateTempSubdirectory("LertaroUpdaterWait_").FullName;
        try
        {
            var runner = WriteUpdaterRunner(root);

            var run = await RunUpdaterActionAsync(runner, "WaitAbsent", "");

            Assert.AreEqual(0, run.ExitCode, run.Error + run.Output);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task ProcessLookup_SeveralNames_LooksUpEachName()
    {
        var root = Directory.CreateTempSubdirectory("LertaroUpdaterNames_").FullName;
        try
        {
            var run = await RunUpdaterActionAsync(WriteUpdaterRunner(root), "ProcessHereArray", "");

            Assert.AreEqual(0, run.ExitCode, run.Error + run.Output);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task ManualPayload_StagesUnderThePayloadRoot_AndRefusesAnythingElse()
    {
        var root = Directory.CreateTempSubdirectory("LertaroUpdaterManual_").FullName;
        try
        {
            var run = await RunUpdaterActionAsync(WriteUpdaterRunner(root), "ManualPayload", "", root);

            Assert.AreEqual(0, run.ExitCode, run.Error + run.Output);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task ClearStalePayload_DeletesOldRuns_AndKeepsTheCurrentOne()
    {
        var root = Directory.CreateTempSubdirectory("LertaroUpdaterStale_").FullName;
        try
        {
            var run = await RunUpdaterActionAsync(WriteUpdaterRunner(root), "ClearStalePayload", "", root);

            Assert.AreEqual(0, run.ExitCode, run.Error + run.Output);
        }
        finally { Directory.Delete(root, true); }
    }

    // The fake returns scripted sc.exe exit codes while recording how many times the helper called it.
    private static string WriteFakeSc(string root, params int[] exitCodes)
    {
        var path = Path.Combine(root, "fake-sc.ps1");
        File.WriteAllText(path, $$"""
            $exits = @({{string.Join(", ", exitCodes)}})
            $state = "${PSCommandPath}.count"
            $count = 0
            if (Test-Path -LiteralPath $state) { $count = [int](Get-Content -LiteralPath $state -Raw) }
            $count++
            Set-Content -LiteralPath $state -Value $count
            $index = [Math]::Min($count - 1, $exits.Length - 1)
            $code = $exits[$index]
            exit $code
            """);
        return path;
    }

    private static string WriteUpdaterRunner(string root)
    {
        var path = Path.Combine(root, "run-action.ps1");
        File.WriteAllText(path, """
            param($Updater, $Action, $Sc, $Target, $Lease)
            $ErrorActionPreference = 'Stop'
            . $Updater
            switch ($Action) {
                'StopTransient' {
                    Stop-LertaroService $Sc 2000 10
                    exit 0
                }
                'StopPersistent' {
                    try {
                        Stop-LertaroService $Sc 1000 10
                        exit 90
                    } catch {
                        if ($_.Exception.Message -eq 'Could not stop service: 1061') { exit 0 }
                        throw
                    }
                }
                'StartTransient' {
                    Start-LertaroService $Sc 2000 10
                    exit 0
                }
                'WaitLingering' {
                    # The runner's own host is a process that cannot leave on its own, so the wait has to time
                    # out and name it -- an attempt to terminate it would only say "Access is denied".
                    $deadline = [Diagnostics.Stopwatch]::StartNew()
                    $message = ''
                    try { Wait-LertaroProcessExit 'powershell' $PSHOME 400 10 } catch { $message = $_.Exception.Message }
                    if ($message -notlike 'powershell (PID *) did not exit.') {
                        throw "Unexpected result after $($deadline.ElapsedMilliseconds)ms: '$message'"
                    }
                    if ($deadline.Elapsed.TotalMilliseconds -lt 300) { throw 'The wait gave up before its deadline.' }
                    exit 0
                }
                'WaitAbsent' {
                    $deadline = [Diagnostics.Stopwatch]::StartNew()
                    Wait-LertaroProcessExit 'NoSuchLertaroProcess' $PSHOME 5000 10
                    if ($deadline.Elapsed.TotalMilliseconds -ge 1000) {
                        throw "The wait polled for $($deadline.ElapsedMilliseconds)ms for an absent process."
                    }
                    exit 0
                }
                'CompleteRestartFailure' {
                    $lease = [IO.File]::Open($Lease, 'OpenOrCreate', 'ReadWrite', 'None')
                    $result = @{ Status = 'success'; Time = [DateTime]::UtcNow.ToString('O')
                                 Message = 'copy succeeded'; RecoveryDirectory = '' }
                    Complete-LertaroUpdate -Result $result -RestartSafe $true -Sc $Sc -TargetRoot $Target -Lease $lease
                    if ($result.Status -ne 'failed' -or $result.Message -notlike '*Could not restart service: 1058*') {
                        throw "Unexpected verdict: $($result.Status) / $($result.Message)"
                    }
                    $disposed = $false
                    try { $lease.WriteByte(0) } catch [ObjectDisposedException] { $disposed = $true }
                    if (!$disposed) { throw 'The update lease was not released.' }
                    exit 0
                }
                'ProcessHereArray' {
                    # One name is a pattern, so several names have to be looked up one at a time. A [string]
                    # parameter would join them into "powershell NoSuchLertaroProcess" and match nothing,
                    # which is how the App and the CLI stopped being stopped before a copy.
                    $found = @(Get-LertaroProcessHere @('powershell', 'NoSuchLertaroProcess') $PSHOME)
                    if ($found.Count -lt 1) { throw 'The array of names matched nothing: the names were joined.' }
                    $absent = @(Get-LertaroProcessHere @('NoSuchLertaroProcess') $PSHOME)
                    if ($absent.Count -ne 0) { throw "An absent process matched $($absent.Count) entries." }
                    exit 0
                }
                'ManualPayload' {
                    $source = Join-Path $Target 'download'
                    $install = Join-Path $Target 'install'
                    [IO.Directory]::CreateDirectory($source) | Out-Null
                    [IO.Directory]::CreateDirectory($install) | Out-Null
                    Set-Content -LiteralPath (Join-Path $install 'portable-updater.ps1') -Value '# marker'
                    Set-Content -LiteralPath (Join-Path $source 'Lertaro.App.exe') -Value 'new'
                    [IO.Directory]::CreateDirectory((Join-Path $source 'sub')) | Out-Null
                    Set-Content -LiteralPath (Join-Path $source 'sub\hidden.txt') -Value 'x'
                    (Get-Item -LiteralPath (Join-Path $source 'sub\hidden.txt')).Attributes = 'Hidden'

                    $staged = Copy-LertaroManualPayload $source $install
                    $payloadRoot = Join-Path $install 'update-payload'
                    if (!$staged.StartsWith($payloadRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
                        throw "The manual payload was staged outside $payloadRoot`: $staged"
                    }
                    if (!(Test-Path -LiteralPath (Join-Path $staged 'Lertaro.App.exe'))) { throw 'The payload file was not staged.' }
                    if (!(Test-Path -LiteralPath (Join-Path $staged 'sub\hidden.txt'))) { throw 'The hidden payload file was not staged.' }

                    $missing = Join-Path $Target 'missing'
                    try { Copy-LertaroManualPayload $missing $install; throw 'A missing source was accepted.' }
                    catch { if ($_.Exception.Message -notlike 'Update source not found:*') { throw } }
                    try { Copy-LertaroManualPayload $source $Target; throw 'A non-installation target was accepted.' }
                    catch { if ($_.Exception.Message -notlike 'The destination is not a Lertaro installation.*') { throw } }
                    exit 0
                }
                'ClearStalePayload' {
                    $root = Join-Path $Target 'update-payload'
                    $old = Join-Path $root 'old-run'
                    $recent = Join-Path $root 'recent-run'
                    $current = Join-Path $root 'this-run'
                    foreach ($dir in @($old, $recent, $current)) { [IO.Directory]::CreateDirectory($dir) | Out-Null }
                    (Get-Item -LiteralPath $old).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-30)

                    Clear-LertaroStalePayload $root $current

                    if (Test-Path -LiteralPath $old) { throw 'A stale run was left behind.' }
                    if (!(Test-Path -LiteralPath $recent)) { throw 'A recent run was deleted.' }
                    if (!(Test-Path -LiteralPath $current)) { throw 'The current run was deleted.' }
                    Clear-LertaroStalePayload (Join-Path $Target 'absent') ''
                    exit 0
                }
            }
            throw "Unknown action: $Action"
            """);
        return path;
    }

    private static int ReadFakeScCalls(string fakeSc)
        => int.Parse(File.ReadAllText(fakeSc + ".count"));

    private static async Task<(int ExitCode, string Output, string Error)> RunUpdaterActionAsync(
        string runner, string action, string sc, string? target = null, string? lease = null)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-NonInteractive", "-File", runner,
            Path.Combine(AppContext.BaseDirectory, "portable-updater.ps1"), action, sc, target ?? "", lease ?? "" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The updater helper did not finish within 20 seconds.");
        }
        return (process.ExitCode, await output, await error);
    }
}
