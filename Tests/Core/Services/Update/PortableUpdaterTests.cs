using System.Diagnostics;
using System.Text.Json;

namespace Lertaro.Core.Tests.Services.Update;

[TestClass]
public sealed class PortableUpdaterTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BatchLauncher_PreservesSpecialCharacterPaths_AndDefaultsToItsOwnFolder(bool omitDestination)
    {
        var root = Directory.CreateTempSubdirectory("LertaroPortable_").FullName;
        try
        {
            var launchDirectory = Directory.CreateDirectory(Path.Combine(root, "launcher space ! &' (中文)")).FullName;
            var source = Directory.CreateDirectory(Path.Combine(root, "source space ! &' (中文)")).FullName;
            var destination = Directory.CreateDirectory(Path.Combine(root, "target space ! &' (中文)")).FullName;
            var launcher = Path.Combine(launchDirectory, "portable-updater.bat");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "portable-updater.bat"), launcher);
            WriteRecorder(Path.Combine(launchDirectory, "portable-updater.ps1"), 0);
            var command = $"\"{launcher}\" \"{source}\"";
            if (!omitDestination) command += $" \"{destination}\"";
            // cmd /s /c needs an outer quote around the whole command, in addition to each path's quotes.
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"))
            {
                Arguments = $"/d /s /c \"{command}\"",
                WorkingDirectory = launchDirectory,
                RedirectStandardInput = true
            };
            start.Environment["TEMP"] = Directory.CreateDirectory(Path.Combine(root, "temp with spaces")).FullName;

            var run = await RunAsync(start);

            Assert.AreEqual(0, run.ExitCode, run.Output + run.Error);
            using var recorded = JsonDocument.Parse(File.ReadAllText(Path.Combine(launchDirectory, "arguments.json")));
            Assert.AreEqual(source, recorded.RootElement.GetProperty("Source").GetString());
            Assert.AreEqual(omitDestination ? launchDirectory + Path.DirectorySeparatorChar : destination,
                recorded.RootElement.GetProperty("Destination").GetString());
            Assert.IsTrue(recorded.RootElement.GetProperty("Manual").GetBoolean());
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task ManualElevation_QuotesPathsAsData_AndReturnsTheChildExitCode()
    {
        var root = Directory.CreateTempSubdirectory("LertaroPortable_").FullName;
        try
        {
            var directory = Directory.CreateDirectory(Path.Combine(root, "elevation space ! &' (中文)")).FullName;
            var source = Path.Combine(directory, "source ! &' (中文)");
            var destination = Path.Combine(directory, "target ! &' (中文)");
            var recorder = Path.Combine(directory, "record arguments.ps1");
            WriteRecorder(recorder, 17);

            var run = await RunActionAsync(root, "Elevate", directory, recorder, source, destination);

            Assert.AreEqual(17, run.ExitCode, run.Output + run.Error);
            using var recorded = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "arguments.json")));
            Assert.AreEqual(source, recorded.RootElement.GetProperty("Source").GetString());
            Assert.AreEqual(destination, recorded.RootElement.GetProperty("Destination").GetString());
            Assert.IsTrue(recorded.RootElement.GetProperty("Manual").GetBoolean());
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task CompleteUpdate_WithoutTheLease_PreservesTheOwnersResultAndDoesNotRestartService()
    {
        var root = Directory.CreateTempSubdirectory("LertaroPortable_").FullName;
        try
        {
            var resultFile = Path.Combine(root, "update-result.json");
            File.WriteAllText(resultFile, "the lock owner's result");
            using var owner = new FileStream(Path.Combine(root, "update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

            var run = await RunActionAsync(root, "CompleteWithoutLease", root);

            Assert.AreEqual(0, run.ExitCode, run.Output + run.Error);
            Assert.AreEqual("the lock owner's result", File.ReadAllText(resultFile));
            Assert.IsFalse(File.Exists(Path.Combine(root, "service-started")));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task Copy_MissingSource_ReportsTheSourcePath()
    {
        var root = Directory.CreateTempSubdirectory("LertaroPortable_").FullName;
        try
        {
            var missing = Path.Combine(root, "missing");

            var run = await RunActionAsync(root, "MissingSource", root, testSource: missing);

            Assert.AreEqual(1, run.ExitCode, run.Output + run.Error);
            Assert.Contains(missing, run.Error);
            Assert.DoesNotContain("Update failed at ''", run.Error);
        }
        finally { Directory.Delete(root, true); }
    }

    private static void WriteRecorder(string path, int exitCode) => File.WriteAllText(path, $$"""
        param([string]$Source = $env:LERTARO_MANUAL_UPDATE_SOURCE,
              [string]$Destination = $env:LERTARO_MANUAL_UPDATE_DESTINATION, [switch]$Manual)
        @{ Source = $Source; Destination = $Destination; Manual = [bool]$Manual } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'arguments.json') -Encoding UTF8
        exit {{exitCode}}
        """);

    private static async Task<(int ExitCode, string Output, string Error)> RunActionAsync(
        string root, string action, string target, string recorder = "", string testSource = "", string testDestination = "")
    {
        var runner = Path.Combine(root, "run-action.ps1");
        File.WriteAllText(runner, """
            param($Updater, $Action, $Target, $Recorder, $TestSource, $TestDestination)
            $ErrorActionPreference = 'Stop'
            . $Updater
            switch ($Action) {
                'Elevate' {
                    # Record a real child process's arguments while substituting only the UAC request.
                    function Start-Process {
                        param($FilePath, $ArgumentList, $Verb, $WindowStyle, [switch]$Wait, [switch]$PassThru, $ErrorAction)
                        if ($Verb -ne 'RunAs' -or $WindowStyle -ne 'Hidden' -or !$Wait -or !$PassThru) {
                            throw 'The elevated launch did not request the expected process options.'
                        }
                        Microsoft.PowerShell.Management\Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -WindowStyle Hidden -Wait -PassThru
                    }
                    exit (Start-LertaroManualUpdater $Recorder $TestSource $TestDestination)
                }
                'CompleteWithoutLease' {
                    function Start-LertaroService {
                        Set-Content -LiteralPath (Join-Path $Target 'service-started') -Value 'started'
                    }
                    $result = @{ Status = 'failed'; Message = 'Another update is running.' }
                    Complete-LertaroUpdate -Result $result -RestartSafe $true -Sc '' -TargetRoot $Target -Lease $null
                    exit 0
                }
                'MissingSource' {
                    try { Invoke-LertaroUpdateCopy $TestSource (Join-Path $Target 'app') (Join-Path $Target 'backup') }
                    catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
                    exit 0
                }
            }
            throw "Unknown action: $Action"
            """);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe"));
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", runner,
            Path.Combine(AppContext.BaseDirectory, "portable-updater.ps1"), action, target, recorder, testSource, testDestination })
            start.ArgumentList.Add(argument);
        return await RunAsync(start);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(ProcessStartInfo start)
    {
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (start.RedirectStandardInput)
        {
            await process.StandardInput.WriteLineAsync();
            process.StandardInput.Close();
        }
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The portable updater test process did not finish within 20 seconds.");
        }
        return (process.ExitCode, await output, await error);
    }
}
