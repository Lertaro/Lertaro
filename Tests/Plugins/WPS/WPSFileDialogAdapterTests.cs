using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

namespace Lertaro.Plugins.WPS.Tests;

// The adapter's behaviour that does not need a live WPS dialog: the guards that decide whether to touch
// UI Automation at all, and the one member whose answer is fixed.
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
    public void AnAdapterThatHasNotMatchedADialogGivesTheWideAnswer() =>
        // The folder-only verdict comes out of CanHandle, which is the only thing that has seen the dialog's
        // bottom row. Before that the safe answer is the one that hides nothing: an unfiltered result list
        // rather than a list with every file silently dropped from it.
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

    [TestMethod]
    public void ADeadDialogOffersNoFieldToAnchorTo()
    {
        // Asked on the positioning path, which keeps running while the dialog is going away. The cheap Win32
        // liveness check has to answer before anything reaches UI Automation, and "no anchor" has to be an
        // answer rather than an exception -- the host reads false as "keep the placement you had".
        Assert.IsFalse(Adapter().TryGetTargetFieldBounds(IntPtr.Zero, out var bounds));
        Assert.AreEqual(default(AdapterRect), bounds);
    }

    [TestMethod]
    public void SameSizeIgnoresWhereTheDialogIsButNotHowBigItGot()
    {
        var here = Rect(480, 272, 1440, 920);
        Assert.IsTrue(WPSFileDialogAdapter.SameSize(here, Rect(900, 400, 1860, 1048)), "a dragged dialog is the same dialog");
        Assert.IsFalse(WPSFileDialogAdapter.SameSize(here, Rect(480, 272, 1440, 1200)), "a taller dialog relayouts its file-name row");
        Assert.IsFalse(WPSFileDialogAdapter.SameSize(here, Rect(600, 272, 1440, 920)), "a narrower dialog moves it");
    }

    [TestMethod]
    public void TranslateCarriesTheFieldAlongWithTheDialog()
    {
        // Measured against the dialog at 480,272; the dialog has since been dragged to 1000,400, so the
        // file-name box went with it, offset for offset.
        var field = Rect(783, 799, 1405, 821);
        var moved = WPSFileDialogAdapter.Translate(field, Rect(480, 272, 1440, 920), Rect(1000, 400, 1960, 1048));
        Assert.AreEqual(new AdapterRect { Left = 1303, Top = 927, Right = 1925, Bottom = 949 }, moved);
    }

    private static AdapterRect Rect(int left, int top, int right, int bottom) =>
        new() { Left = left, Top = top, Right = right, Bottom = bottom };
}
