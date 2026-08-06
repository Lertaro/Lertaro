using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

namespace Lertaro.Plugins.WPS.Tests;

// The adapter's behaviour that does not need a live WPS dialog: the guards that decide whether to touch
// UI Automation at all, and the two members whose answers are fixed.
[TestClass]
public sealed class WPSFileDialogAdapterTests
{
    private static WPSFileDialogAdapter Adapter() => new();

    [TestMethod]
    public void AWindowFromAnotherProcessIsRejectedWithoutTouchingAutomation()
    {
        // The ordering this pins is the point: the process-name test is a string compare, the class-name
        // test behind it is a cross-process call that can block until UI Automation's own timeout. If they
        // were the other way round, every foreground change on the machine would pay for it.
        Assert.IsFalse(Adapter().CanHandle(IntPtr.Zero, "#32770", "explorer"));
        Assert.IsFalse(Adapter().CanHandle(IntPtr.Zero, "#32770", "WINWORD"));
    }

    [TestMethod]
    public void AWPSProcessWithNoLiveWindowIsRejected() =>
        // Right process, dead handle. Has to come back false rather than throwing out of the Hook process.
        Assert.IsFalse(Adapter().CanHandle(IntPtr.Zero, "", "wps"));

    [TestMethod]
    public void TheCurrentPathIsAlwaysUnknown()
    {
        // Deliberate, and load-bearing: ExplorerActivePathPoller calls this on every tick while the dialog
        // is active. Returning null keeps SearchScope at its last value instead of feeding it a guess, and
        // keeps a cross-process call off a timer. See the remarks on the member itself.
        Assert.IsNull(Adapter().GetCurrentPath(IntPtr.Zero));
        Assert.IsNull(Adapter().GetCurrentPath(new IntPtr(0x1234)));
    }

    [TestMethod]
    public void ThePickedPathIsPassedThroughAsIs() =>
        // Unlike the archive-tool adapters, whose destination field can only hold a folder, this one is an
        // Open/Save file-name box: a picked file must arrive here as that file, not as its parent folder.
        Assert.IsFalse(Adapter().TargetIsFolderOnly);

    [TestMethod]
    public void AnEmptyTargetIsRefusedBeforeAnyWindowWork()
    {
        Assert.IsFalse(Adapter().NavigateTo(IntPtr.Zero, ""));
        Assert.IsFalse(Adapter().NavigateTo(IntPtr.Zero, "   "));
        Assert.IsFalse(Adapter().NavigateTo(IntPtr.Zero, null!));
    }

    [TestMethod]
    public void DeadWindowsAreHandledRatherThanThrowing()
    {
        // Every one of these can be reached by the user closing the dialog mid-operation. This adapter
        // runs inside the Hook process, which serves every other window integration too.
        Assert.IsFalse(Adapter().NavigateTo(IntPtr.Zero, @"D:\Projects"));
        Assert.IsFalse(Adapter().RestoreFocus(IntPtr.Zero));
        Assert.IsFalse(Adapter().GetDockBounds(IntPtr.Zero, out var rect));
        Assert.AreEqual(default(AdapterRect), rect);
    }

    [TestMethod]
    public void TheComponentIsNamedForTheApplicationItIntegratesWith() =>
        // Shown in Settings -> Plugins, and it is what the user looks for when deciding whether to turn
        // this off.
        Assert.AreEqual("WPS", Adapter().Name);

    [TestMethod]
    public void TheAdapterIsDiscoverableAsAFileDialogAdapter() =>
        // How it reaches FileDialogAdapterRegistry at all: the loaders scan for the interface rather than
        // taking any registration from the plugin itself.
        Assert.IsInstanceOfType<IFileDialogAdapter>(Adapter());
}
