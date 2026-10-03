using Lertaro.Core.Services.HookLaunch;

namespace Lertaro.Core.Tests.Services.HookLaunch;

[TestClass]
public sealed class SessionProcessLauncherTests
{
    private static readonly Dictionary<string, string> NoPins = new(StringComparer.OrdinalIgnoreCase);

    private static string Block(params string[] entries) => string.Join('\0', entries) + "\0\0";

    private static string[] Entries(string block) => block.Split('\0', StringSplitOptions.RemoveEmptyEntries);

    [TestMethod]
    [DataRow("DOTNET_STARTUP_HOOKS=C:\\Users\\testuser\\hook.dll")]
    [DataRow("dotnet_startup_hooks=C:\\Users\\testuser\\hook.dll")]
    [DataRow("DOTNET_ROOT=C:\\Users\\testuser\\runtime")]
    [DataRow("COMPlus_EnableDiagnostics=1")]
    [DataRow("CORECLR_PROFILER={00000000-0000-0000-0000-000000000000}")]
    [DataRow("CORECLR_ENABLE_PROFILING=1")]
    [DataRow("COR_PROFILER_PATH=C:\\Users\\testuser\\profiler.dll")]
    public void FilterEnvironment_DropsRuntimeHookVariables(string entry)
    {
        var filtered = Entries(SessionProcessLauncher.FilterEnvironment(Block("PATH=C:\\Windows", entry, "TEMP=C:\\Temp"), NoPins));

        CollectionAssert.DoesNotContain(filtered, entry);
        CollectionAssert.AreEquivalent(new[] { "PATH=C:\\Windows", "TEMP=C:\\Temp" }, filtered);
    }

    [TestMethod]
    public void FilterEnvironment_KeepsOrdinaryVariables()
    {
        // A name that merely contains a hook prefix later on is not one of them.
        string[] ordinary = ["=C:=C:\\Users\\testuser", "COMPUTERNAME=TESTPC", "MY_DOTNET_TOOL=1", "Path=C:\\Windows", "TEMP=C:\\Temp", "USERNAME=testuser"];

        var filtered = Entries(SessionProcessLauncher.FilterEnvironment(Block(ordinary), NoPins));

        CollectionAssert.AreEquivalent(ordinary, filtered);
    }

    [TestMethod]
    public void FilterEnvironment_PinnedVariablesReplaceTheUsersValues()
    {
        var pins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SystemRoot"] = "C:\\Windows",
            ["PATHEXT"] = ".COM;.EXE;.BAT;.CMD",
        };

        var filtered = Entries(SessionProcessLauncher.FilterEnvironment(
            Block("SYSTEMROOT=C:\\Users\\testuser\\fake", "PathExt=.JS", "TEMP=C:\\Temp"), pins));

        CollectionAssert.AreEquivalent(new[] { "SystemRoot=C:\\Windows", "PATHEXT=.COM;.EXE;.BAT;.CMD", "TEMP=C:\\Temp" }, filtered);
    }

    [TestMethod]
    public void FilterEnvironment_IsDoubleNulTerminatedAndSortedByName()
    {
        var filtered = SessionProcessLauncher.FilterEnvironment(Block("b=2", "=C:=C:\\", "A=1", "DOTNET_X=1"), NoPins);

        Assert.EndsWith("\0\0", filtered);
        Assert.IsFalse(filtered.EndsWith("\0\0\0", StringComparison.Ordinal));
        CollectionAssert.AreEqual(new[] { "=C:=C:\\", "A=1", "b=2" }, Entries(filtered));
    }
}
