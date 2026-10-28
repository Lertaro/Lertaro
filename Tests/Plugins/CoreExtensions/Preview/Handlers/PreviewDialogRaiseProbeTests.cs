using System.Runtime.InteropServices;
using Lertaro.Plugins.CoreExtensions.Preview.Handlers;

namespace Lertaro.Plugins.CoreExtensions.Tests.Preview.Handlers;

// PreviewFocusGuard raises a handler's own dialog with SetForegroundWindow, which sends to that dialog's thread from
// this app's UI thread. It is guarded by one bounded WM_NULL probe, so the declaration itself is what this pins: a
// mistyped DllImport dies silently (or throws at the call site on the UI thread), and the only visible symptom would
// be a prompt that is never raised -- issue #133 arriving again as a quiet regression.
[TestClass]
public sealed class PreviewDialogRaiseProbeTests
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [TestMethod]
    public void SendMessageTimeoutAnswersAnAnsweringWindowAndSurvivesABogusOne()
    {
        var answered = PreviewHandlerInterop.SendMessageTimeout(GetDesktopWindow(), PreviewHandlerInterop.WM_NULL,
            IntPtr.Zero, IntPtr.Zero, PreviewHandlerInterop.SMTO_ABORTIFHUNG, 200, out _);
        Assert.AreNotEqual(IntPtr.Zero, answered);

        // The guard skips on zero, so the failure path has to be a value rather than an exception.
        var bogus = PreviewHandlerInterop.SendMessageTimeout(new IntPtr(0x1234), PreviewHandlerInterop.WM_NULL,
            IntPtr.Zero, IntPtr.Zero, PreviewHandlerInterop.SMTO_ABORTIFHUNG, 200, out _);
        Assert.AreEqual(IntPtr.Zero, bogus);
    }
}
