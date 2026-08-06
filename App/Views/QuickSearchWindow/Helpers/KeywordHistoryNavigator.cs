using Lertaro.Core;

namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

/// <summary>
/// Cycles the quick window's search box through <see cref="KeywordHistoryStore"/> entries, like a
/// shell's command-line history: "previous" steps to older keywords, "next" steps back toward whatever
/// the user was actually typing before navigation started.
/// </summary>
internal sealed class KeywordHistoryNavigator
{
    private IReadOnlyList<string>? _snapshot;
    private int _index = -1; // -1 = not currently navigating
    private string _originalQuery = string.Empty;

    /// <summary>Ends the current navigation session (new quick-window session, or the user typed).</summary>
    public void Reset()
    {
        _snapshot = null;
        _index = -1;
        _originalQuery = string.Empty;
    }

    /// <summary>Steps to an older keyword. Returns null when there's nothing further back to show.</summary>
    public string? Previous(string currentQuery)
    {
        if (_index == -1)
        {
            _snapshot = KeywordHistoryStore.GetEntries();
            _originalQuery = currentQuery;
        }

        if (_snapshot == null || _snapshot.Count == 0 || _index + 1 >= _snapshot.Count)
            return null;

        _index++;
        return _snapshot[_index];
    }

    /// <summary>Steps to a newer keyword, or back to the original query once past the newest entry.
    /// Returns null when not currently navigating.</summary>
    public string? Next()
    {
        if (_index == -1 || _snapshot == null)
            return null;

        _index--;
        return _index == -1 ? _originalQuery : _snapshot[_index];
    }

    /// <summary>Deletes the currently-shown entry from history, ends the navigation session, and
    /// clears the search box (no auto-advance to another entry, no restoring the original query).
    /// Returns null when not currently navigating -- there is nothing displayed to delete.</summary>
    public string? DeleteCurrent()
    {
        if (_index == -1 || _snapshot == null || _index >= _snapshot.Count)
            return null;

        KeywordHistoryStore.Delete(_snapshot[_index]);
        Reset();
        return string.Empty;
    }
}
