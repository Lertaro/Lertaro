namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

/// <summary>
/// Owns the search box's keyword-history navigation session: wires the TextChanged (manual-edit reset),
/// PreviewMouseWheel (scroll-to-navigate) and middle-click (delete-current) hookups, and applies
/// Previous/Next/Delete results from the underlying <see cref="KeywordHistoryNavigator"/>.
/// </summary>
internal sealed class QuickSearchKeywordHistoryController
{
    private readonly Lertaro.App.QuickSearchWindow _window;
    private readonly KeywordHistoryNavigator _navigator = new();
    private bool _isApplyingHistory;

    public QuickSearchKeywordHistoryController(Lertaro.App.QuickSearchWindow window)
    {
        _window = window;
        _window.TxtSearch.TextChanged += (s, e) =>
        {
            if (!_isApplyingHistory)
                _navigator.Reset();
        };
        _window.TxtSearch.PreviewMouseWheel += (s, e) =>
        {
            Navigate(previous: e.Delta > 0);
            e.Handled = true;
        };
        // Middle-click deletes the entry currently shown, mirroring the always-on scroll-to-navigate
        // gesture above -- not user-configurable, unlike the keyboard hotkey.
        _window.TxtSearch.PreviewMouseDown += (s, e) =>
        {
            if (e.ChangedButton != System.Windows.Input.MouseButton.Middle) return;
            DeleteCurrent();
            e.Handled = true;
        };
    }

    /// <summary>Ends the current navigation session (call when the quick window hides).</summary>
    public void Reset() => _navigator.Reset();

    /// <summary>Steps the search box through keyword history (hotkey or mouse-wheel driven).</summary>
    public void Navigate(bool previous) => ApplyValue(previous ? _navigator.Previous(_window.ViewModel.SearchQuery) : _navigator.Next());

    /// <summary>Deletes the currently-shown history entry (hotkey or middle-click driven). No-op when
    /// not currently navigating history.</summary>
    public void DeleteCurrent() => ApplyValue(_navigator.DeleteCurrent());

    private void ApplyValue(string? value)
    {
        if (value == null) return;

        _isApplyingHistory = true;
        try
        {
            _window.ViewModel.SearchQuery = value;
            _window.TxtSearch.CaretIndex = _window.TxtSearch.Text.Length;
        }
        finally
        {
            _isApplyingHistory = false;
        }
    }
}
