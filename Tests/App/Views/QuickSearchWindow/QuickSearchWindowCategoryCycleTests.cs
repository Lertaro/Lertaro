using System.Windows.Input;

using Lertaro.App.Views.QuickSearchWindow.Helpers;

namespace Lertaro.App.Tests.Views.QuickSearchWindow;

// Which keypress moves the quick window's category filter bar, and which way. The key is the user's to
// choose now (Settings > Hotkey), so the one behaviour that must survive is the pair it replaced: the
// recorded combination goes forward, the same key with Shift flipped goes back, and anything else --
// including a cleared setting -- leaves the key to whatever else in the window wants it.
[TestClass]
public sealed class QuickSearchWindowCategoryCycleTests
{
    [TestMethod]
    [DataRow("Tab", ModifierKeys.None, Key.Tab, 1)]
    [DataRow("Tab", ModifierKeys.Shift, Key.Tab, -1)]
    [DataRow("Tab", ModifierKeys.Control, Key.Tab, 0)] // Ctrl+Tab is completion's, and stays out of the cycle
    [DataRow("Ctrl+Tab", ModifierKeys.Control, Key.Tab, 1)]
    [DataRow("Ctrl+Tab", ModifierKeys.Control | ModifierKeys.Shift, Key.Tab, -1)]
    [DataRow("Ctrl+Tab", ModifierKeys.None, Key.Tab, 0)]
    [DataRow("Shift+Tab", ModifierKeys.Shift, Key.Tab, 1)] // recorded with Shift: bare Tab becomes the back step
    [DataRow("Shift+Tab", ModifierKeys.None, Key.Tab, -1)]
    [DataRow("F2", ModifierKeys.None, Key.F2, 1)]
    [DataRow("F2", ModifierKeys.None, Key.Tab, 0)]
    [DataRow("Tab", ModifierKeys.None, Key.G, 0)]
    [DataRow("", ModifierKeys.None, Key.Tab, 0)] // cleared on the recorder's Escape: the cycle is off
    [DataRow(null, ModifierKeys.None, Key.Tab, 0)]
    public void CategoryCycleDelta_FollowsTheRecordedKeyAndFlipsShift(
        string? hotkey, ModifierKeys current, Key key, int expected) =>
        Assert.AreEqual(expected, QuickSearchWindowInputHandler.CategoryCycleDelta(hotkey, current, key));
}
