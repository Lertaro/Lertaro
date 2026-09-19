namespace Lertaro.Plugins.WindowSwitcher.Tests;

[TestClass]
public sealed class WindowMenuOperationsTests
{
    [TestMethod]
    public void Build_NormalVisibleWindow_EnablesEverythingExceptRestore()
    {
        var entries = WindowMenuOperations.Build(new WindowMenuOperations.WindowMenuState(
            IsValid: true, IsTopmost: false, IsVisible: true, IsMaximized: false, IsMinimized: false));

        Assert.HasCount(6, entries);
        Assert.IsTrue(Find(entries, WindowMenuOperations.MenuCommand.ToggleTopmost).Enabled);
        Assert.IsTrue(Find(entries, WindowMenuOperations.MenuCommand.Maximize).Enabled);
        Assert.IsTrue(Find(entries, WindowMenuOperations.MenuCommand.Minimize).Enabled);
        Assert.IsTrue(Find(entries, WindowMenuOperations.MenuCommand.Close).Enabled);
        Assert.IsTrue(Find(entries, WindowMenuOperations.MenuCommand.Focus).Enabled);
        // A normal window is neither maximized nor minimized, so there is nothing to restore.
        Assert.IsFalse(Find(entries, WindowMenuOperations.MenuCommand.Restore).Enabled);
    }

    [TestMethod]
    public void Build_NormalVisibleWindow_UsesUnflippedLabels()
    {
        var entries = WindowMenuOperations.Build(new WindowMenuOperations.WindowMenuState(
            IsValid: true, IsTopmost: false, IsVisible: true, IsMaximized: false, IsMinimized: false));

        Assert.AreEqual("WindowSwitcher_MenuAlwaysOnTop", Find(entries, WindowMenuOperations.MenuCommand.ToggleTopmost).LabelKey);
        Assert.AreEqual("WindowSwitcher_MenuMaximize", Find(entries, WindowMenuOperations.MenuCommand.Maximize).LabelKey);
        Assert.AreEqual("WindowSwitcher_MenuMinimize", Find(entries, WindowMenuOperations.MenuCommand.Minimize).LabelKey);
        Assert.AreEqual("WindowSwitcher_MenuRestore", Find(entries, WindowMenuOperations.MenuCommand.Restore).LabelKey);
        Assert.AreEqual("WindowSwitcher_MenuClose", Find(entries, WindowMenuOperations.MenuCommand.Close).LabelKey);
        Assert.AreEqual("WindowSwitcher_MenuFocus", Find(entries, WindowMenuOperations.MenuCommand.Focus).LabelKey);
    }

    [TestMethod]
    public void Build_MaximizedWindow_DisablesMaximizeAndEnablesRestore()
    {
        var entries = WindowMenuOperations.Build(new WindowMenuOperations.WindowMenuState(
            IsValid: true, IsTopmost: false, IsVisible: true, IsMaximized: true, IsMinimized: false));

        Assert.IsFalse(Find(entries, WindowMenuOperations.MenuCommand.Maximize).Enabled);
        Assert.IsTrue(Find(entries, WindowMenuOperations.MenuCommand.Restore).Enabled);
        // Minimizing a maximized window is still a real state change, so it stays available.
        Assert.IsTrue(Find(entries, WindowMenuOperations.MenuCommand.Minimize).Enabled);
    }

    [TestMethod]
    public void Build_MinimizedWindow_DisablesMinimizeAndEnablesRestore()
    {
        var entries = WindowMenuOperations.Build(new WindowMenuOperations.WindowMenuState(
            IsValid: true, IsTopmost: false, IsVisible: true, IsMaximized: false, IsMinimized: true));

        Assert.IsFalse(Find(entries, WindowMenuOperations.MenuCommand.Minimize).Enabled);
        Assert.IsTrue(Find(entries, WindowMenuOperations.MenuCommand.Restore).Enabled);
        Assert.IsTrue(Find(entries, WindowMenuOperations.MenuCommand.Maximize).Enabled);
    }

    [TestMethod]
    public void Build_TopmostWindow_FlipsItsLabel()
    {
        var entries = WindowMenuOperations.Build(new WindowMenuOperations.WindowMenuState(
            IsValid: true, IsTopmost: true, IsVisible: false, IsMaximized: false, IsMinimized: false));

        // Topmost is not about visibility: a hidden window can still be (and here is) WS_EX_TOPMOST.
        Assert.AreEqual("WindowSwitcher_MenuCancelAlwaysOnTop", Find(entries, WindowMenuOperations.MenuCommand.ToggleTopmost).LabelKey);
        Assert.IsTrue(Find(entries, WindowMenuOperations.MenuCommand.ToggleTopmost).Enabled);
        Assert.IsFalse(Find(entries, WindowMenuOperations.MenuCommand.Restore).Enabled);
    }

    [TestMethod]
    public void Build_EveryEntry_CarriesOneOfThisPluginsTranslationKeys()
    {
        // Pins the label keys against typos: an unrecognized key renders as a visible "[Key]"
        // placeholder in the menu, so every one of them must be a WindowSwitcher_Menu* key.
        var entries = WindowMenuOperations.Build(default);

        Assert.IsNotEmpty(entries);
        Assert.IsTrue(entries.All(e => e.LabelKey.StartsWith("WindowSwitcher_Menu", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Build_CommandIds_AreDistinct()
    {
        var entries = WindowMenuOperations.Build(default);

        Assert.HasCount(entries.Count, entries.Select(e => e.CommandId).Distinct());
    }

    [TestMethod]
    public void ParseWindowHandle_ProviderArgumentShape_ReturnsHandle()
        => Assert.AreEqual(new IntPtr(1234), WindowMenuOperations.ParseWindowHandle("activatewindow:1234"));

    [TestMethod]
    public void ParseWindowHandle_RealProviderArgument_ReturnsSameHandle()
    {
        // Exactly the shape WindowSwitcherInstantProvider emits: $"activatewindow:{window.Handle.ToInt64()}".
        var hwnd = new IntPtr(0x0000_0000_0012_3456);
        var argument = $"activatewindow:{hwnd.ToInt64()}";

        Assert.AreEqual(hwnd, WindowMenuOperations.ParseWindowHandle(argument));
    }

    [TestMethod]
    public void ParseWindowHandle_UppercasePrefix_ReturnsHandle()
        // PluginActionExecutor matches the same prefix case-insensitively; both consumers must agree.
        => Assert.AreEqual(new IntPtr(1234), WindowMenuOperations.ParseWindowHandle("ActivateWindow:1234"));

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("activatewindow:")]
    [DataRow("activatewindow:abc")]
    [DataRow("activatewindow:0")]
    [DataRow("activatewindow:-5")]
    [DataRow("activatewindow:1234x")]
    [DataRow("kill:1234")]
    [DataRow("cc_exec:{}")]
    [DataRow("C:\\Windows\\notepad.exe")]
    public void ParseWindowHandle_NotAWindowArgument_ReturnsZero(string? actionArgument)
        => Assert.AreEqual(IntPtr.Zero, WindowMenuOperations.ParseWindowHandle(actionArgument));

    private static WindowMenuOperations.WindowMenuEntry Find(
        IReadOnlyList<WindowMenuOperations.WindowMenuEntry> entries,
        WindowMenuOperations.MenuCommand command) =>
        entries.Single(e => e.CommandId == (uint)command);
}
