using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.PluginSdk.Tests.Helpers;

[TestClass]
public sealed class ShellCommandLauncherTests
{
    [TestMethod]
    [DataRow(" cmd ", "cmd", false)]
    [DataRow("POWERSHELL", "powershell", true)]
    [DataRow("pwsh", "pwsh", false)]
    [DataRow("pwsh", "pwsh", true)]
    public void Launch_ExactShellName_StartsThatProgramDirectly(string command, string expected, bool admin)
    {
        var starts = new List<ProcessStartInfo>();
        ShellCommandLauncher.LaunchCore(command, "cmd", admin, null, shell => shell + ".exe", starts.Add);
        var info = Assert.ContainsSingle(starts);
        Assert.AreEqual(expected + ".exe", info.FileName);
        Assert.AreEqual(admin ? "runas" : "", info.Verb);
        if (expected != "cmd") Assert.AreEqual("-NoExit", Assert.ContainsSingle(info.ArgumentList));
        else Assert.AreEqual("/d /v:off /k ", info.Arguments);
    }

    [TestMethod]
    [DataRow("pwsh -NoProfile")]
    [DataRow("powershell.exe")]
    [DataRow("echo pwsh")]
    public void Launch_NonExactInput_UsesConfiguredInterpreter(string command)
    {
        ProcessStartInfo? info = null;
        ShellCommandLauncher.LaunchCore(command, "powershell", false, null, shell => shell + ".exe", value => info = value);
        Assert.IsNotNull(info);
        Assert.AreEqual("powershell.exe", info.FileName);
        Assert.AreEqual(command, DecodeScript(info));
    }

    [TestMethod]
    public void Launch_MissingExactShell_FallsBackToCmdWithOriginalCommandAndElevation()
    {
        ProcessStartInfo? info = null;
        ShellCommandLauncher.LaunchCore("pwsh", "powershell", true, @"C:\work", shell => shell == "cmd" ? "cmd.exe" : null, value => info = value);
        Assert.IsNotNull(info);
        Assert.AreEqual("cmd.exe", info.FileName);
        Assert.AreEqual("runas", info.Verb);
        Assert.AreEqual(@"C:\work", info.WorkingDirectory);
        Assert.EndsWith(" || exit & pwsh", info.Arguments);
    }

    [TestMethod]
    public void Launch_MissingSelectedShell_DoesNotInterpretItsScriptAsCmd()
    {
        var starts = 0;
        Assert.ThrowsExactly<FileNotFoundException>(() => ShellCommandLauncher.LaunchCore("Get-ChildItem", "pwsh", false, null,
            shell => shell == "cmd" ? "cmd.exe" : null, _ => starts++));
        Assert.AreEqual(0, starts);
    }

    [TestMethod]
    [DataRow(5)]
    [DataRow(1223)]
    [DataRow(3)]
    public void Launch_ExistingExecutableFails_DoesNotRetry(int code)
    {
        var starts = 0;
        var failure = new Win32Exception(code);
        var actual = Assert.ThrowsExactly<Win32Exception>(() => ShellCommandLauncher.LaunchCore("pwsh", "cmd", true, null,
            shell => shell + ".exe", _ => { starts++; throw failure; }));
        Assert.AreSame(failure, actual);
        Assert.AreEqual(1, starts);
    }

    [TestMethod]
    public void Launch_ExecutableDisappears_FallsBackOnce()
    {
        var starts = new List<string>();
        ShellCommandLauncher.LaunchCore("pwsh", "cmd", false, null,
            shell => shell == "pwsh" && starts.Count > 0 ? null : shell + ".exe",
            info => { starts.Add(info.FileName); if (starts.Count == 1) throw new Win32Exception(2); });
        CollectionAssert.AreEqual(new[] { "pwsh.exe", "cmd.exe" }, starts);
    }

    [TestMethod]
    public void ResolveWorkingDirectory_CurrentOverridesFixedAndPreservesLiteralPercent()
    {
        const string current = @"D:\项目 %PATH% ! folder";
        string? checkedPath = null;
        var directory = ShellCommandLauncher.ResolveWorkingDirectory(@"C:\fixed", true, current, path => { checkedPath = path; return true; });
        Assert.AreEqual(current, checkedPath);
        Assert.AreEqual(current, directory);
    }

    [TestMethod]
    public void ResolveWorkingDirectory_Unconfigured_DoesNotProbeOrUseContext()
    {
        var probes = 0;
        var directory = ShellCommandLauncher.ResolveWorkingDirectory("", false, @"D:\context", _ => { probes++; return false; });
        Assert.IsNull(directory);
        Assert.AreEqual(0, probes);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("shell:AppsFolder")]
    [DataRow("relative")]
    [DataRow(@"D:\vanished")]
    public void ResolveWorkingDirectory_InvalidCurrent_StopsInsteadOfUsingFixedDirectory(string? current) =>
        Assert.ThrowsExactly<DirectoryNotFoundException>(() =>
            ShellCommandLauncher.ResolveWorkingDirectory(@"C:\fixed", true, current, _ => false));

    [TestMethod]
    [DataRow("powershell")]
    [DataRow("pwsh")]
    public void BuildStartInfo_PowerShell_EncodesDirectoryAndCommandWithoutChangingScript(string shell)
    {
        const string command = "$x = '中文'; Write-Output \"$x & %PATH%\"";
        var info = ShellCommandLauncher.BuildStartInfo(shell + ".exe", shell, command, true, @"D:\O'Brien [x] %PATH%");
        Assert.AreEqual("Set-Location -LiteralPath 'D:\\O''Brien [x] %PATH%' -ErrorAction Stop;\n" + command, DecodeScript(info));
        Assert.AreEqual("runas", info.Verb);
    }

    [TestMethod]
    public void BuildStartInfo_Cmd_InitializesUncDirectoryAndProtectsLiteralPercent()
    {
        var info = ShellCommandLauncher.BuildStartInfo("cmd.exe", "cmd", "dir", false, @"\\server\share\%PATH% ! &");
        Assert.AreEqual("/d /v:off /k pushd \"\\\\server\\share\\%\"\"PATH%\"\" ! &\" || exit & dir", info.Arguments);
    }

    private static string DecodeScript(ProcessStartInfo info) => Encoding.Unicode.GetString(Convert.FromBase64String(info.ArgumentList[^1]));
}
