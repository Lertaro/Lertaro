namespace Lertaro.App.Helpers;

/// <summary>
/// The page width to give a FlowDocument that must not wrap its lines.
/// </summary>
/// <remarks>
/// FlowDocument has no TextWrapping="NoWrap": the usual way to stop it wrapping is to hand it a page
/// far wider than any line could be. That width is also what the horizontal scrollbar scrolls over
/// though, so a fixed one (this was 20000) lets the view scroll thousands of pixels past the end of the
/// text, and does so even when the document is empty. Measuring the lines instead costs one text
/// measurement pass and makes the scroll range mean something.
/// </remarks>
public static class LogDocumentWidth
{
    /// <summary>Room past the last glyph, so the caret at end-of-line is not flush against the edge.</summary>
    public const double TrailingMargin = 24;

    public static double Compute(IEnumerable<double> lineWidths, double viewportWidth)
    {
        var widest = 0.0;
        foreach (var width in lineWidths)
        {
            if (width > widest) widest = width;
        }

        // Never narrower than the viewport: a page narrower than what is on screen would scroll the
        // other way, and a document with nothing in it should simply not scroll.
        return Math.Max(widest + TrailingMargin, viewportWidth);
    }
}
