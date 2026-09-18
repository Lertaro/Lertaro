namespace Lertaro.App.Services.Favorites;

/// <summary>What pressing a favorite's hotkey should do with the window that is currently in front.</summary>
public enum FavoriteHotkeyNavigationAction
{
    /// <summary>The foreground window is not a file manager this app knows how to navigate. Do nothing.</summary>
    NotAFileManager,

    /// <summary>Hand the folder to the file manager that owns the foreground window, in place.</summary>
    NavigateInPlace,

    /// <summary>No existing window to navigate, so let the normal open-a-folder path run.</summary>
    OpenThroughDefaultRoute,

    /// <summary>The favorite has no folder a file manager could navigate to (a web address, or a
    /// virtual folder the shell cannot turn into a real path).</summary>
    UnusableTarget
}

/// <summary>
/// The decision half of "navigate the foreground file manager to this favorite": given what the window
/// in front is and what the favorite resolves to, what should happen. Kept pure and free of window
/// handles so it holds no state a test would have to fake.
/// </summary>
public static class FavoriteHotkeyNavigation
{
    /// <summary>
    /// Decides the action. <paramref name="managerCanNavigateInPlace"/> is true when the window in front is
    /// something this app can move: a file manager a registered inline-search adapter recognizes, or
    /// another application's Open/Save dialog a registered file-dialog adapter recognizes. Those registries
    /// are the repository's own list of windows it can drive, so this deliberately does not enumerate
    /// Directory Opus / Total Commander / Explorer itself. False is the "some other application has the
    /// foreground" case, which does nothing at all rather than opening a second window over the user's work.
    /// </summary>
    public static FavoriteHotkeyNavigationAction Decide(
        bool isWebUrl,
        bool isVirtualPath,
        bool targetIsFolder,
        bool managerCanNavigateInPlace)
    {
        // A web address has no folder for a file manager to navigate to, and a virtual folder the shell
        // could not resolve has no real path either -- both would otherwise be handed to a manager as a
        // path it cannot use.
        if (isWebUrl || isVirtualPath) return FavoriteHotkeyNavigationAction.UnusableTarget;

        if (!managerCanNavigateInPlace) return FavoriteHotkeyNavigationAction.NotAFileManager;

        // Navigating to a FILE is what "open containing folder" means, and that route is the one already
        // wired to select the item in the window it lands in. A folder navigates in place.
        return targetIsFolder
            ? FavoriteHotkeyNavigationAction.NavigateInPlace
            : FavoriteHotkeyNavigationAction.OpenThroughDefaultRoute;
    }

    /// <summary>
    /// The window the user is working in. Read through the same native helper the rest of the app uses,
    /// so there is one P/Invoke declaration for it rather than a new one here.
    /// </summary>
    public static IntPtr GetForegroundWindow() => Core.Hook.ExplorerNativeHooks.GetForegroundWindow();
}
