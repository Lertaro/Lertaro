using Lertaro.App.Helpers;

namespace Lertaro.App.Tests.Helpers;

// The horizontal scroll range of the settings log view. It used to be a constant 20000, so the view
// scrolled thousands of pixels past the end of the text and did it on an empty log too.
[TestClass]
public sealed class LogDocumentWidthTests
{
    private const double Viewport = 600;

    [TestMethod]
    public void NoLines_DoesNotScroll() =>
        // An empty log has to come out at exactly the viewport: anything wider is scrollable, and there
        // is nothing there to scroll to.
        Assert.AreEqual(Viewport, LogDocumentWidth.Compute([], Viewport));

    [TestMethod]
    public void LinesNarrowerThanTheViewport_DoNotScroll() => Assert.AreEqual(Viewport, LogDocumentWidth.Compute([100, 250, 80], Viewport));

    [TestMethod]
    public void TheWidestLineSetsTheRange_NotTheLastOrTheFirst() => Assert.AreEqual(900 + LogDocumentWidth.TrailingMargin, LogDocumentWidth.Compute([200, 900, 400], Viewport));

    [TestMethod]
    public void TheWidestLineGetsRoomPastItsLastGlyph() =>
        // Exactly-viewport-width text still needs the margin, or the caret at end of line sits on the
        // edge with nothing to scroll into.
        Assert.AreEqual(Viewport + LogDocumentWidth.TrailingMargin, LogDocumentWidth.Compute([Viewport], Viewport));

    [TestMethod]
    public void BeforeTheFirstLayout_TheViewportIsUnknownAndTheTextStillDecides() =>
        // ViewportWidth reads 0 until the control has been laid out once, which is exactly when the
        // first log document gets built.
        Assert.AreEqual(700 + LogDocumentWidth.TrailingMargin, LogDocumentWidth.Compute([700], 0));
}
