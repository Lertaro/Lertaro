using System.Diagnostics;
using System.Text.Json;

namespace Lertaro.Core.Tests.Services.Update;

[TestClass]
public sealed class PackageManagerManifestTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("LertaroPackage_中文_").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(false, false)]
    public async Task Install_WithoutGlobalAdministrator_RejectsBeforeStartingInstaller(bool global, bool admin)
    {
        var result = await RunHookAsync("install", global: global, admin: admin);

        Assert.AreNotEqual(0, result.ExitCode, result.Output);
        Assert.Contains("administrator shell", result.Output);
        Assert.IsEmpty(result.Calls);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Install_RunsOfficialInstallerAndPropagatesFailure(bool succeeds)
    {
        var result = await RunHookAsync("install", succeeds: succeeds);

        Assert.AreEqual(succeeds ? 0 : 1, result.ExitCode, result.Output);
        var call = Assert.ContainsSingle(result.Calls);
        Assert.AreEqual("C:\\Scoop metadata\\setup.exe", call.GetProperty("Path").GetString());
        Assert.IsTrue(call.GetProperty("RunAs").GetBoolean());
        var arguments = call.GetProperty("Arguments").EnumerateArray().Select(value => value.GetString()).ToArray();
        Assert.Contains("/VERYSILENT", arguments);
        Assert.Contains("/NORESTART", arguments);
        Assert.Contains("/TASKS=startmenuicon,addtopath", arguments);
        if (!succeeds) Assert.Contains("Lertaro installation failed.", result.Output);
    }

    [TestMethod]
    public async Task Uninstall_DuringUpgrade_LeavesSharedApplicationAndServiceUntouched()
    {
        // A failing external command and missing registration must remain irrelevant during upgrade.
        var result = await RunHookAsync("uninstall", command: "update", succeeds: false, missingRegistration: true);

        Assert.AreEqual(0, result.ExitCode, result.Output);
        Assert.IsEmpty(result.Calls);
    }

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(false, false)]
    public async Task Uninstall_WithoutGlobalAdministrator_RejectsBeforeReadingRegistration(bool global, bool admin)
    {
        var result = await RunHookAsync("uninstall", global: global, admin: admin);

        Assert.AreNotEqual(0, result.ExitCode, result.Output);
        Assert.Contains("administrator shell", result.Output);
        Assert.IsEmpty(result.Calls);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Uninstall_UsesRegisteredDirectoryAndPropagatesFailure(bool succeeds)
    {
        var result = await RunHookAsync("uninstall", succeeds: succeeds);

        Assert.AreEqual(succeeds ? 0 : 1, result.ExitCode, result.Output);
        Assert.HasCount(2, result.Calls);
        Assert.AreEqual(@"HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{D37D0B75-B5E3-40D9-92EE-429C7D4D7F2A}_is1",
            result.Calls[0].GetProperty("Registry").GetString());
        var call = result.Calls[1];
        Assert.AreEqual("C:\\Program Files\\Lertaro custom\\unins000.exe", call.GetProperty("Path").GetString());
        Assert.IsTrue(call.GetProperty("RunAs").GetBoolean());
        Assert.Contains("/VERYSILENT", call.GetProperty("Arguments").EnumerateArray().Select(value => value.GetString()).ToArray());
        if (!succeeds) Assert.Contains("Lertaro uninstallation failed.", result.Output);
    }

    [TestMethod]
    public async Task Uninstall_MissingRegistration_FailsWithoutGuessingAnExecutablePath()
    {
        var result = await RunHookAsync("uninstall", missingRegistration: true);

        Assert.AreNotEqual(0, result.ExitCode, result.Output);
        Assert.Contains("Registration missing", result.Output);
        var call = Assert.ContainsSingle(result.Calls);
        Assert.IsTrue(call.TryGetProperty("Registry", out _));
    }

    private async Task<(int ExitCode, string Output, JsonElement[] Calls)> RunHookAsync(string operation,
        string command = "uninstall", bool global = true, bool admin = true, bool succeeds = true, bool missingRegistration = false)
    {
        var script = Path.Combine(_root, "run-hook.ps1");
        var trace = Path.Combine(_root, "calls.json");
        File.WriteAllText(script, """
            param($ManifestPath, $TracePath, $Operation, $Command, $GlobalInstall, $Admin, $Succeeds, $MissingRegistration)
            $ErrorActionPreference = 'Stop'
            $global = $GlobalInstall -eq 'true'
            $cmd = $Command
            $dir = 'C:\Scoop metadata'
            $fname = 'setup.exe'
            $calls = [System.Collections.Generic.List[object]]::new()
            function is_admin { $Admin -eq 'true' }
            function Invoke-ExternalCommand {
                param($FilePath, [string[]]$ArgumentList, [switch]$RunAs)
                $calls.Add(@{ Path = $FilePath; Arguments = $ArgumentList; RunAs = $RunAs.IsPresent })
                return $Succeeds -eq 'true'
            }
            function Get-ItemProperty {
                [CmdletBinding()]
                param($LiteralPath)
                $calls.Add(@{ Registry = $LiteralPath })
                if ($MissingRegistration -eq 'true') { throw 'Registration missing' }
                return [pscustomobject]@{ InstallLocation = 'C:\Program Files\Lertaro custom' }
            }
            $code = 0
            try {
                $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
                if ($Operation -eq 'install') {
                    Invoke-Command ([scriptblock]::Create($manifest.pre_install -join "`n"))
                    Invoke-Command ([scriptblock]::Create($manifest.installer.script -join "`n"))
                } else {
                    Invoke-Command ([scriptblock]::Create($manifest.uninstaller.script -join "`n"))
                }
            } catch {
                Write-Output $_.Exception.Message
                $code = 1
            } finally {
                ConvertTo-Json -InputObject @($calls.ToArray()) -Depth 5 | Set-Content -LiteralPath $TracePath -Encoding utf8
            }
            exit $code
            """);
        var start = new ProcessStartInfo("pwsh.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        string[] arguments = ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", script,
            "-ManifestPath", FindManifest(), "-TracePath", trace, "-Operation", operation, "-Command", command,
            "-GlobalInstall", global.ToString().ToLowerInvariant(), "-Admin", admin.ToString().ToLowerInvariant(),
            "-Succeeds", succeeds.ToString().ToLowerInvariant(), "-MissingRegistration", missingRegistration.ToString().ToLowerInvariant()];
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        catch { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output + await error, JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(trace))!);
    }

    private static string FindManifest()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "bucket", "lertaro-np.json");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Could not locate bucket/lertaro-np.json.");
    }
}
