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
/// The key is the dialog window plus its size, because that is what the answer describes: a dialog that only
/// moved keeps its widgets where they were relative to its own edges, and a dialog that changed size has new
/// ones. Adapters cache the same way, so a repeated request costs nothing on either side.
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

    private readonly record struct Key(IntPtr Hwnd, int Width, int Height);

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
    /// A measurement that arrives after the dialog changed size or moved on is dropped rather than applied --
    /// the card is placed by the answer for the layout it is actually in -- and it asks for a fresh placement,
    /// which is what starts the measurement the current layout needs.
    /// </remarks>
    public Answer Request(IntPtr hwnd, int width, int height)
    {
        var key = new Key(hwnd, width, height);
        lock (_gate)
        {
            if (_asked != key)
            {
                _asked = key;
                _attempts = 0;
                // Whatever was answered describes a layout the card is no longer in -- another dialog, or this
                // one at another size -- so it is not carried over. Windows hands out the same window handle
                // again, so the size and handle agreeing is not proof the answer still applies.
                _answeredFor = null;
                _answer = default;
            }

            if (_answeredFor == key || _beingMeasured != null || _attempts >= MaxAttemptsPerLayout)
                return _answer;

            _attempts++;
            _beingMeasured = key;
            Task.Run(() => Measure(key));
            return _answer;
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
