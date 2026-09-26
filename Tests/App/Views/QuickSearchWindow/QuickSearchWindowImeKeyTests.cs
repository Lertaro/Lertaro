using System.IO;

namespace Lertaro.App.Tests.Views.QuickSearchWindow;

// An active IME (Chinese mode) does not only intercept the keys it composes with: WPF reports the plain
// arrow and Enter keystrokes around it as Key.ImeProcessed, with the real key moved to ImeProcessedKey.
// GetActualKey unwraps that, so a handler comparing against the raw e.Key simply never sees the key --
// which is how arrow navigation died in the quick window while the search box was in Chinese mode.
//
// The handler needs a live window, so these pin the source shape, in the same spirit as StayOpenGateTests.
[TestClass]
public sealed class QuickSearchWindowImeKeyTests
{
    [TestMethod]
    public void TheBareArrowsAreReadFromTheUnwrappedKey()
    {
        var handler = Source("App/Views/QuickSearchWindow/Helpers/QuickSearchWindowInputHandler.cs");

        Assert.Contains("if (actualKey == Key.Down && Keyboard.Modifiers == ModifierKeys.None)", handler,
            "Down must be read through GetActualKey, or it never arrives while an IME is active; the "
            + "no-modifier guard keeps Ctrl/Alt+arrow falling through to the hotkey dispatch as before");
        Assert.Contains("if (actualKey == Key.Up && Keyboard.Modifiers == ModifierKeys.None)", handler,
            "and the same for Up");
        Assert.DoesNotContain("if (e.Key == Key.Down", handler,
            "the raw comparison is the bug: it silently drops the IME-processed form");
        Assert.DoesNotContain("if (e.Key == Key.Up", handler, "the same, upwards");
    }

    [TestMethod]
    public void TheKeysAComposingImeStillOwnsStayOnTheRawKey()
    {
        // Deliberately NOT unwrapped: Escape cancels an in-flight composition and Enter commits one, so
        // both must keep bowing out when the IME is the one holding the key (#125). Blanket-converting
        // these to actualKey would execute a result instead of committing the text being typed.
        var handler = Source("App/Views/QuickSearchWindow/Helpers/QuickSearchWindowInputHandler.cs");

        Assert.Contains("if (e.Key == Key.ImeProcessed)", handler,
            "Enter must still bail out on the IME-processed form, or it steals the composition commit");
        Assert.Contains("if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)", handler,
            "Escape must stay raw for the same reason");
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
