using System.Windows.Input;
using Lertaro.App.Views.Controls;

namespace Lertaro.App.Tests.Views.Controls;

[TestClass]
public sealed class HotkeyRecorderModifierStateTests
{
    [TestMethod]
    public void Combine_TracksWindowsKeyWhenWpfReportsNoModifiers()
    {
        var tracked = HotkeyRecorderModifierState.Add(ModifierKeys.None, Key.LWin);

        var effective = HotkeyRecorderModifierState.Combine(ModifierKeys.None, tracked);

        Assert.AreEqual(ModifierKeys.Windows, effective);
    }

    [TestMethod]
    public void Remove_WindowsKeyReleased_ClearsTrackedState()
    {
        var tracked = HotkeyRecorderModifierState.Remove(ModifierKeys.Windows, Key.RWin);

        Assert.AreEqual(ModifierKeys.None, tracked);
    }
}
