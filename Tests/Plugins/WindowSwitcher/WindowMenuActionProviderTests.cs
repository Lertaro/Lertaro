using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.Plugins.WindowSwitcher.Tests;

// The two gates between a window row and the menu that acts on it.
//
// The host refuses the actions menu for instant results unless a provider declares it acts on them
// (IDynamicActionProvider.CanProvideForInstantResults), and these rows ARE instant results -- so if this
// plugin stopped declaring it, the whole window menu would silently become unreachable again, which is
// exactly what happened once already.
[TestClass]
public sealed class WindowMenuActionProviderTests
{
    private sealed class FakeResult : ISearchResult
    {
        public string Name => "Notepad";
        public string FullPath => string.Empty;
        public string ContextDirectory => string.Empty;
        public bool IsDir => false;
        public bool IsApplication => false;
        public string? InstantActionArgument { get; init; }
    }

    private static ISearchResult WindowRow(long hwnd) => new FakeResult { InstantActionArgument = $"activatewindow:{hwnd}" };

    [TestMethod]
    public void CanProvideForInstantResults_IsDeclared() => Assert.IsTrue(new WindowMenuActionProvider().CanProvideForInstantResults);

    // The letters the user asked for, exactly: 置顶/取消置顶 p, 隐藏/显示 h, 最大化 m, 最小化 n, 还原 r,
    // 关闭 c, 聚焦 f. The host activates a row by matching a typed letter against this hint, so this table
    // IS the mnemonic map -- a row with no hint simply cannot be reached by letter.
    [TestMethod]
    public void Build_GivesEveryRowItsMnemonicLetter()
    {
        var state = new WindowMenuOperations.WindowMenuState(IsValid: true, IsTopmost: false, IsVisible: true, IsMaximized: false, IsMinimized: false);

        var hints = WindowMenuOperations.Build(state)
            .ToDictionary(entry => (WindowMenuOperations.MenuCommand)entry.CommandId, entry => entry.ShortcutHint);

        Assert.AreEqual("p", hints[WindowMenuOperations.MenuCommand.ToggleTopmost]);
        Assert.AreEqual("m", hints[WindowMenuOperations.MenuCommand.Maximize]);
        Assert.AreEqual("n", hints[WindowMenuOperations.MenuCommand.Minimize]);
        Assert.AreEqual("r", hints[WindowMenuOperations.MenuCommand.Restore]);
        Assert.AreEqual("c", hints[WindowMenuOperations.MenuCommand.Close]);
        Assert.AreEqual("f", hints[WindowMenuOperations.MenuCommand.Focus]);
    }

    [TestMethod]
    public void CanProvide_SingleWindowRow_IsTrue()
    {
        var provider = new WindowMenuActionProvider();

        Assert.IsTrue(provider.CanProvide(new[] { WindowRow(0x1234) }));
    }

    // A multiple selection has no single window to act on, so the menu must not offer to act on "one".
    [TestMethod]
    public void CanProvide_MultipleRows_IsFalse()
    {
        var provider = new WindowMenuActionProvider();

        Assert.IsFalse(provider.CanProvide(new[] { WindowRow(0x1234), WindowRow(0x5678) }));
    }

    // Every other result kind carries no window handle: a file row still opens the shell menu, not this.
    [TestMethod]
    public void CanProvide_RowWithoutAWindowArgument_IsFalse()
    {
        var provider = new WindowMenuActionProvider();

        Assert.IsFalse(provider.CanProvide(new ISearchResult[] { new FakeResult { InstantActionArgument = null } }));
        Assert.IsFalse(provider.CanProvide(new ISearchResult[] { new FakeResult { InstantActionArgument = "activatewindow:notanumber" } }));
    }
}
