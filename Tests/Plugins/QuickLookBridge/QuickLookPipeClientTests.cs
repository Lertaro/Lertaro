namespace Lertaro.Plugins.QuickLookBridge.Tests;

// Whether QuickLook is actually installed/running can't be controlled from a unit test, so these only
// assert the invariant that holds either way: the calls never throw. No caching -- every IsAvailable()
// call does a fresh probe.
[TestClass]
public sealed class QuickLookPipeClientTests
{
    [TestMethod]
    public void IsAvailable_DoesNotThrow() => QuickLookPipeClient.IsAvailable();

    [TestMethod]
    public void TryInvokePreview_DoesNotThrow() =>
        QuickLookPipeClient.TryInvokePreview(@"Z:\definitely-not-a-real-lertaro-path.txt");
}
