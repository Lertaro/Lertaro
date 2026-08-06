using System.IO;
using Lertaro.App.ViewModels.Settings;
using Lertaro.App.ViewModels.Settings.NetworkDrive;

namespace Lertaro.App.Tests.ViewModels.Settings.NetworkDrive;

// A row's enabled state is a persisted user decision. Reachability is a transient probe. Letting the
// second overwrite the first silently deleted a user's folder-index configuration:
//
// UpdateRowsInPlace cleared IsEnabled for any row that was momentarily unreachable. That raised
// PropertyChanged, which set HasPendingEdits -- an edit nobody made -- which pins the refresh
// coordinator to UpdateRowsInPlace and locks out RebuildRows, the one path that recomputes the tick
// from saved settings. Then the next Apply of anything filters rows by IsEnabled and replaces the
// settings list wholesale, so the entry was gone. Coming back, the row read as never configured, and
// re-ticking it was a fresh add: a full re-scan from zero.
//
// UpdateRowsInPlace itself needs a live SearchService (it opens pipes), so it is not constructible
// here. The assignment is what regressed and the assignment is what is pinned, in the same
// source-scanning spirit as Tests/Core/RepositoryHygieneTests -- remembering did not work the first
// time either.
[TestClass]
public sealed class NetworkDriveRowSyncHelperTests
{
    [TestMethod]
    public void UpdateRowsInPlace_NeverAssignsIsEnabled()
    {
        var body = MethodBody("UpdateRowsInPlace");

        // Any form of assignment, not just the exact line that was there before.
        var offenders = body
            .Split('\n')
            .Select((line, i) => (Text: line.Trim(), Number: i + 1))
            .Where(l => !l.Text.StartsWith("//"))
            .Where(l => System.Text.RegularExpressions.Regex.IsMatch(l.Text, @"\bIsEnabled\s*=[^=]"))
            .Select(l => $"line {l.Number}: {l.Text}")
            .ToList();

        Assert.IsEmpty(offenders,
            "UpdateRowsInPlace must not write IsEnabled: it runs on every refresh, so it would be " +
            "overwriting a persisted user decision from a transient reachability probe. Reachability " +
            "belongs in IsPresent, the state text, and CanEditEnabled. Found: " + string.Join("; ", offenders));
    }

    [TestMethod]
    public void RebuildRows_MayAssignIsEnabled_SinceItBuildsFreshRowsFromSavedSettings()
    {
        // The counterpart, asserted so the test above cannot be "satisfied" by gutting both paths: a
        // brand-new row has no prior state to preserve, so deriving it from the saved settings there is
        // correct and must stay.
        var body = MethodBody("RebuildRows");

        Assert.Contains("IsEnabled", body,
            "RebuildRows builds rows from scratch and is where the enabled state is legitimately derived");
    }

    [TestMethod]
    public void ReachabilityAndEnabledAreIndependentOnEveryRowType()
    {
        // The model contract the fix relies on: turning a row unreachable must not disturb the tick.
        // Covers all three categories, because the bug existed as three copies of the same line.
        var drive = new NetworkDriveSettingsItem { IsEnabled = true, IsPresent = true };
        var wsl = new WslSettingsItem { IsEnabled = true, IsPresent = true };
        var folder = new FolderIndexSettingsItem { IsEnabled = true, IsPresent = true };

        drive.IsPresent = false;
        wsl.IsPresent = false;
        folder.IsPresent = false;

        Assert.IsTrue(drive.IsEnabled, "a network drive going unreachable must not untick it");
        Assert.IsTrue(wsl.IsEnabled, "a WSL distro going unreachable must not untick it");
        Assert.IsTrue(folder.IsEnabled, "a folder going unreachable must not untick it");
    }

    // Text of one method of NetworkDriveRowSyncHelper, from the source in the working tree, so the
    // assertions above are about what is actually compiled rather than what was remembered.
    private static string MethodBody(string methodName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "could not locate the repository root");

        var path = Path.Combine(dir!.FullName, "App", "ViewModels", "Settings", "NetworkDrive", "NetworkDriveRowSyncHelper.cs");
        Assert.IsTrue(File.Exists(path), $"expected the helper at {path}");
        var source = File.ReadAllText(path);

        var start = source.IndexOf($"void {methodName}(", StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, start, $"could not find {methodName} in the helper");

        // Runs to the next method signature, or to the end of the file for the last one.
        var next = source.IndexOf("    public static void ", start + 1, StringComparison.Ordinal);
        return next > start ? source.Substring(start, next - start) : source.Substring(start);
    }
}
