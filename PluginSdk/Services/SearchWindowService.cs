namespace Lertaro.PluginSdk.Services;

/// <summary>
/// Allows plugins to query search window visibility or control search window display states.
/// </summary>
public static class SearchWindowService
{
    /// <summary>Delegate determining whether any search window is currently visible.</summary>
    public static Func<bool>? IsWindowVisibleFunc { get; set; }

    /// <summary>Delegate hiding any currently visible search window.</summary>
    public static Action? HideWindowFunc { get; set; }

    /// <summary>Delegate showing or bringing the search window to the front.</summary>
    public static Action<string?>? ShowWindowFunc { get; set; }

    /// <summary>Delegate focusing the query text box in the active search window.</summary>
    public static Action? FocusQueryTextBoxFunc { get; set; }

    /// <summary>Checks whether any search window is currently visible.</summary>
    public static bool IsWindowVisible()
    {
        try { return IsWindowVisibleFunc?.Invoke() ?? false; }
        catch { return false; }
    }

    /// <summary>Hides any currently visible search window.</summary>
    public static void HideWindow()
    {
        try { HideWindowFunc?.Invoke(); }
        catch { }
    }

    /// <summary>Shows the main search window, optionally pre-filling a search query.</summary>
    public static void ShowWindow(string? query = null)
    {
        try { ShowWindowFunc?.Invoke(query); }
        catch { }
    }

    /// <summary>Focuses the search query text box in the active search window.</summary>
    public static void FocusQueryTextBox()
    {
        try { FocusQueryTextBoxFunc?.Invoke(); }
        catch { }
    }
}
