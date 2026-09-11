using System.IO;

namespace Lertaro.App.Tests.Views.InlineSearchWindow.Helpers;

// Enter on an EMPTY inline search box must leave the search, not run whatever child row the empty box
// happens to be listing.
//
// With nothing typed the inline list shows the current folder's contents (a browsing list, not a match
// set), so there is no "top result" for Enter to be confirming -- opening whichever child is selected is
// not what pressing Enter on an empty box means. Escape and empty-Enter are the same user intent, so both
// must go through one exit path rather than two that can drift.
[TestClass]
public sealed class InlineSearchWindowEnterOnEmptyTests
{
    [TestMethod]
    public void EnterOnAnEmptyBoxExitsInsteadOfRunningARow()
    {
        var handler = Source("App/Views/InlineSearchWindow/Helpers/InlineSearchWindowInputHandler.cs");
        var enterBranch = Between(handler, "if (actualKey == Key.Enter)", "// Up arrow");

        // The emptiness check must come before the row resolver: resolving first is exactly the bug
        // (it would pick a row out of the browsing list).
        var exit = enterBranch.IndexOf("ExitSearch()", StringComparison.Ordinal);
        var resolve = enterBranch.IndexOf("ResolveEnterTarget", StringComparison.Ordinal);

        Assert.IsGreaterThan(-1, exit, "empty Enter must exit the search");
        Assert.IsGreaterThan(-1, resolve, "non-empty Enter still acts on a row");
        Assert.IsLessThan(resolve, exit, "the empty-box check must run BEFORE a row is resolved and run");
        Assert.Contains("IsNullOrWhiteSpace", enterBranch,
            "emptiness must use the same whitespace-inclusive test as the search dispatch");
    }

    [TestMethod]
    public void EscapeEmptyEnterAndEmptyBackspaceShareOneExitPath()
    {
        var handler = Source("App/Views/InlineSearchWindow/Helpers/InlineSearchWindowInputHandler.cs");

        // One named exit used by all three keys, so a change to "what leaving means" cannot apply to only one.
        var escape = Between(handler, "if (e.Key == Key.Escape && noModifiers)", "// Backspace in an already-empty box");
        Assert.Contains("ExitSearch()", escape, "Escape must leave through the shared exit");

        // Backspace on an empty box has nothing to delete, so it means "leave" -- same as Escape. The
        // emptiness test must be IsNullOrEmpty (not IsNullOrWhiteSpace): a lone space is real, editable
        // content, so backspace on it must still delete the space rather than exit.
        var backspace = Between(handler, "if (e.Key == Key.Back && noModifiers", "// Enter key");
        Assert.Contains("ExitSearch()", backspace, "backspace on an empty box must exit");
        Assert.Contains("string.IsNullOrEmpty(_window.SearchTextBox.Text)", backspace,
            "the guard must be IsNullOrEmpty, so backspace still deletes a lone space");

        var exitMethod = Between(handler, "private void ExitSearch()", "public void UpdateShortcutHints");
        Assert.Contains("IsActiveWindowDialog", exitMethod,
            "inside an Explorer file dialog the window stays up and focus returns to the dialog");
        Assert.Contains("ResetInlineSearchAndFocusDialog()", exitMethod,
            "the dialog case restores focus rather than closing the window");
        Assert.Contains("HideWindow()", exitMethod,
            "outside a dialog the inline window closes");
    }

    private static string Between(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, start, $"could not find '{from}'");
        var end = source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, end, $"could not find '{to}' after '{from}'");
        return source.Substring(start, end - start);
    }

    private static string Source(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "could not locate the repository root");
        return dir!.FullName;
    }
}
