using Lertaro.Core.Hook;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

/// <summary>
/// The two rectangles a dialog's adapter reports about itself -- its target field and its file list --
/// measured away from the thread that places the card.
/// </summary>
/// <remarks>
/// A Qt-drawn dialog can only answer either question through UI Automation, which is a synchronous call into
/// the other process's UI thread. Asked straight from the WPF thread, that froze the whole application when
/// WPS stopped answering while its dialog was being destroyed (the user had just clicked Cancel), so the
/// placing thread now takes whatever was answered last and the measurement happens here instead.
///
/// The key is the dialog window and the rect it answered for, position included. These are screen rectangles,
/// and an answer carried across a move is not "the dialog's inner layout", it is where the dialog used to be:
/// the card then anchors to a point the dialog has left behind, which is exactly a card that looks stuck while
/// the window is dragged. Re-asking on a move is cheap, because the adapter translates its own offsets and
/// only reaches for UI Automation again when the dialog changed size.
/// </remarks>
internal sealed class InlineDialogGeometryProbe
{
    /// <summary>
    /// How often one layout is asked before the card settles for placing itself without an answer. A dialog
    /// can answer for its window before it has laid out the widget inside it, so one attempt is not enough;
    /// unbounded attempts would be a probe fired on every placement pass forever.
    /// </summary>
    internal const int MaxAttemptsPerLayout = 3;

    internal readonly record struct Answer(ExplorerTracker.RECT? Anchor, ExplorerTracker.RECT? FileList);

    private readonly record struct Key(IntPtr Hwnd, int Left, int Top, int Width, int Height);

    private readonly Func<IntPtr, Answer> _measure;
    private readonly Action _placementChanged;
    private readonly object _gate = new();

    private Key? _asked;
    private Key? _beingMeasured;
    private Key? _answeredFor;
    private Answer _answer;
    private int _attempts;

    /// <param name="measure">Reads the dialog's rectangles; may block, so it never runs on the placing thread.</param>
    /// <param name="placementChanged">Asks the host to place the card again, once an answer has landed.</param>
    public InlineDialogGeometryProbe(Func<IntPtr, Answer> measure, Action placementChanged) =>
        (_measure, _placementChanged) = (measure, placementChanged);

    /// <summary>
    /// What is known about this dialog's inner layout right now, starting a measurement if none is running.
    /// </summary>
    /// <remarks>
    /// A measurement that arrives after the dialog moved or resized is dropped rather than applied -- the card
    /// is placed by the answer for the rect it is at now -- and it asks for a fresh placement either way, which
    /// is what starts the measurement the current rect needs.
    /// </remarks>
    public Answer Request(IntPtr hwnd, int left, int top, int width, int height)
    {
        var key = new Key(hwnd, left, top, width, height);
        lock (_gate)
        {
            if (_asked != key)
            {
                _asked = key;
                _attempts = 0;
                // Windows hands out the same handle again, so the window and size agreeing is not proof the
                // answer still applies. The answer itself is kept while this rect is being measured: it is the
                // same dialog's inner layout offset by where the dialog used to be, and dropping it would move
                // the card by a hundred-odd pixels for the frame before the fresh one lands.
                _answeredFor = null;
            }

            if (_answeredFor == key || _beingMeasured != null || _attempts >= MaxAttemptsPerLayout)
                return _answer;

            _attempts++;
            _beingMeasured = key;
            Task.Run(() => Measure(key));
            return _answer;
        }
    }

    /// <summary>
    /// Drops the credit for the layout being asked about, so the next request measures it again.
    /// </summary>
    /// <remarks>
    /// For a dialog whose adapter this process has only just matched: every answer before that was empty, and
    /// <see cref="MaxAttemptsPerLayout"/> would otherwise keep the card on the anchorless placement until the
    /// dialog happened to move or resize. The answer itself is kept, and a measurement already in flight
    /// still lands and is still judged against the layout it was asked for.
    /// </remarks>
    public void Invalidate()
    {
        lock (_gate)
        {
            _answeredFor = null;
            _attempts = 0;
        }
    }

    private void Measure(Key key)
    {
        var answer = _measure(key.Hwnd);
        if (answer.Anchor is null && answer.FileList is null)
        {
            // Nothing to apply, and nothing said to the host either: the next placement pass asks again,
            // bounded by MaxAttemptsPerLayout.
            lock (_gate) _beingMeasured = null;
            return;
        }

        lock (_gate)
        {
            _beingMeasured = null;
            if (_asked == key)
            {
                _answer = answer;
                _answeredFor = key;
            }
        }

        // Even an answer that arrived for a layout the card has already left asks for a placement: that is
        // what gets the layout the card is in now measured, rather than waiting on some unrelated event to
        // happen to come along.
        _placementChanged();
    }
}
