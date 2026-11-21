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
        Assert.HasCount(succeeds ? 2 : 1, result.Calls);
        var call = result.Calls[0];
        Assert.AreEqual("C:\\Scoop metadata\\setup.exe", call.GetProperty("Path").GetString());
        Assert.IsTrue(call.GetProperty("RunAs").GetBoolean());
        var arguments = call.GetProperty("Arguments").EnumerateArray().Select(value => value.GetString()).ToArray();
        Assert.Contains("/VERYSILENT", arguments);
        Assert.Contains("/NORESTART", arguments);
        Assert.Contains("/TASKS=startmenuicon,addtopath", arguments);
        Assert.Contains(@"/LOG=C:\Scoop metadata\install.log", arguments);
        if (!succeeds) Assert.Contains("Lertaro installation failed.", result.Output);
    }

    [TestMethod]
    [DataRow("Lertaro.App.exe")]
    [DataRow("Lertaro.App.dll")]
    [DataRow("Lertaro.App.runtimeconfig.json")]
    [DataRow("Lertaro.Service.exe")]
    [DataRow("Lertaro.Service.dll")]
    [DataRow("Lertaro.Service.runtimeconfig.json")]
    [DataRow("Lertaro.Service.deps.json")]
    [DataRow("lff.exe")]
    [DataRow("lff.dll")]
    [DataRow("lff.runtimeconfig.json")]
    [DataRow("Lertaro.Core.dll")]
    [DataRow("Lertaro.PluginSdk.dll")]
    [DataRow("System.ServiceProcess.ServiceController.dll")]
    public async Task Install_SuccessfulExitWithMissingPayload_FailsWithTheMissingPath(string missingFile)
    {
        var result = await RunHookAsync("install", missingFile: missingFile);

        Assert.AreNotEqual(0, result.ExitCode, result.Output);
        Assert.Contains(Path.Combine(_root, "Installed 中文 app", missingFile), result.Output);
        Assert.Contains("install.log", result.Output);
    }

    [TestMethod]
    public async Task Install_SuccessfulExitWithoutRegistration_IsRejected()
    {
        var result = await RunHookAsync("install", missingRegistration: true);

        Assert.AreNotEqual(0, result.ExitCode, result.Output);
        Assert.Contains("Registration missing", result.Output);
    }

    [TestMethod]
    public async Task Install_DirectoryNamedLikeApplication_IsRejected()
    {
        var result = await RunHookAsync("install", missingFile: "Lertaro.App.exe", directoryInsteadOfFile: true);

        Assert.AreNotEqual(0, result.ExitCode, result.Output);
        Assert.Contains("Lertaro.App.exe is missing", result.Output);
    }

    [TestMethod]
    [DataRow("install")]
    [DataRow("uninstall")]
    public async Task Hook_RegistrationAccessDenied_PropagatesError(string operation)
    {
        var result = await RunHookAsync(operation, registrationAccessDenied: true);

        Assert.AreNotEqual(0, result.ExitCode, result.Output);
        Assert.Contains("Registration access denied", result.Output);
        if (operation == "uninstall") Assert.HasCount(1, result.Calls);
    }

    [TestMethod]
    [DataRow("install")]
    [DataRow("uninstall")]
    public async Task Hook_EmptyInstallLocation_FailsWithoutUsingTheWorkingDirectory(string operation)
    {
        var result = await RunHookAsync(operation, emptyLocation: true);

        Assert.AreNotEqual(0, result.ExitCode, result.Output);
        Assert.Contains("InstallLocation", result.Output);
        if (operation == "uninstall") Assert.HasCount(1, result.Calls);
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
        Assert.AreEqual(Path.Combine(_root, "Installed 中文 app", "unins000.exe"), call.GetProperty("Path").GetString());
        Assert.IsTrue(call.GetProperty("RunAs").GetBoolean());
        Assert.Contains("/VERYSILENT", call.GetProperty("Arguments").EnumerateArray().Select(value => value.GetString()).ToArray());
        if (!succeeds) Assert.Contains("Lertaro uninstallation failed.", result.Output);
    }

    [TestMethod]
    public async Task Uninstall_MissingRegistration_AllowsCleanupOfFailedInstallation()
    {
        var result = await RunHookAsync("uninstall", missingRegistration: true);

        Assert.AreEqual(0, result.ExitCode, result.Output);
        var call = Assert.ContainsSingle(result.Calls);
        Assert.IsTrue(call.TryGetProperty("Registry", out _));
    }

    private async Task<(int ExitCode, string Output, JsonElement[] Calls)> RunHookAsync(string operation,
        string command = "uninstall", bool global = true, bool admin = true, bool succeeds = true, bool missingRegistration = false,
        string? missingFile = null, bool emptyLocation = false, bool directoryInsteadOfFile = false,
        bool registrationAccessDenied = false)
    {
        var installPath = Directory.CreateDirectory(Path.Combine(_root, "Installed 中文 app")).FullName;
        string[] payload = ["Lertaro.App.exe", "Lertaro.App.dll", "Lertaro.App.runtimeconfig.json",
            "Lertaro.Service.exe", "Lertaro.Service.dll", "Lertaro.Service.runtimeconfig.json", "Lertaro.Service.deps.json",
            "lff.exe", "lff.dll", "lff.runtimeconfig.json", "Lertaro.Core.dll", "Lertaro.PluginSdk.dll",
            "System.ServiceProcess.ServiceController.dll"];
        foreach (var file in payload)
            if (file != missingFile) File.WriteAllText(Path.Combine(installPath, file), "fixture");
        if (directoryInsteadOfFile) Directory.CreateDirectory(Path.Combine(installPath, missingFile!));
        var script = Path.Combine(_root, "run-hook.ps1");
        var trace = Path.Combine(_root, "calls.json");
        File.WriteAllText(script, """
            param($ManifestPath, $TracePath, $Operation, $Command, $GlobalInstall, $Admin, $Succeeds, $MissingRegistration, $InstallPath, $EmptyLocation, $RegistrationAccessDenied)
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
                if ($RegistrationAccessDenied -eq 'true') { throw [UnauthorizedAccessException]::new('Registration access denied') }
                if ($MissingRegistration -eq 'true') { throw [System.Management.Automation.ItemNotFoundException]::new('Registration missing') }
                return [pscustomobject]@{ InstallLocation = $(if ($EmptyLocation -eq 'true') { '' } else { $InstallPath }) }
            }
            $code = 0
            try {
                $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
                if ($Operation -eq 'install') {
                    Invoke-Command ([scriptblock]::Create($manifest.pre_install -join "`n"))
                    Invoke-Command ([scriptblock]::Create($manifest.installer.script -join "`n"))
                    Invoke-Command ([scriptblock]::Create($manifest.post_install -join "`n"))
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
            "-Succeeds", succeeds.ToString().ToLowerInvariant(), "-MissingRegistration", missingRegistration.ToString().ToLowerInvariant(),
            "-InstallPath", installPath, "-EmptyLocation", emptyLocation.ToString().ToLowerInvariant(),
            "-RegistrationAccessDenied", registrationAccessDenied.ToString().ToLowerInvariant()];
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
