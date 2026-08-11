namespace Lertaro.Core;

// Small settings-record types referenced from UserSettings, split out to keep that file under the
// project's line limit.

public class NetworkDriveSetting
{
    public string Id { get; set; } = string.Empty;
    public string RefreshMode { get; set; } = "Manual";
}

public class WslSetting
{
    public string Id { get; set; } = string.Empty; // e.g. "Ubuntu"
    public string RefreshMode { get; set; } = "Manual";
}

public class FolderIndexSetting
{
    public string Path { get; set; } = string.Empty; // the path itself is the identity, no separate Id
    public string RefreshMode { get; set; } = "Manual";
}

// Lets a user redirect "open this folder" (see FileExecutor.TryBuildDefaultFileManagerStartInfo) to an
// arbitrary third-party file manager instead of the shell's own association -- e.g. GitHub issue #180.
// Parameter is a command-line template where "%s"/"{}" expand to the folder path, already quoted --
// same placeholder convention as CustomActions.DynamicActionProvider.RunMulti. The user must not wrap
// the placeholder in their own quotes, since that would double up.
public class DefaultFileManagerSetting
{
    public bool Enabled { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Parameter { get; set; } = string.Empty;
}

/// <summary>Settings for the LocalSend protocol v2 LAN transfer engine.</summary>
public class LocalSendSettingsModel
{
    public bool Enabled { get; set; } = false;
    public string DeviceAlias { get; set; } = Environment.MachineName;
    public int Port { get; set; } = 53317;
    public bool QuickSave { get; set; } = false;
    public bool QuickSaveFromFavorites { get; set; } = true;
    public string DownloadDirectory { get; set; } = string.Empty;
    public bool EnableHttps { get; set; } = true;
    public bool CreateChecksums { get; set; } = true;
    public bool VerifyChecksums { get; set; } = true;
    public string? ReceivePin { get; set; }
    public string ShowToken { get; set; } = string.Empty;
    public int DiscoveryTimeout { get; set; } = 500;
    public string DeviceFingerprint { get; set; } = string.Empty;
}

/// <summary>Everything shown on the Hotkey Settings page, grouped under one object.</summary>
public class HotkeyPageSettings
{
    /// <summary>
    /// A bare modifier (e.g. "Ctrl") means double-tap that modifier; a combo (e.g. "Alt+Space") means a
    /// literal key combination. See <see cref="HotkeyStringFormat"/>.
    /// </summary>
    public string ToggleWindowHotkey { get; set; } = "Ctrl";

    /// <summary>
    /// By default, the global hotkeys (this one, Quick Switch, and inline-search activation) are let
    /// through untouched while the foreground window is genuinely full-screen, so they don't fight with
    /// fullscreen games -- see KeyboardHookService's shouldDisableAllHooks gate. Opting in here removes
    /// that exemption for a user whose configured combo won't collide with anything the fullscreen app
    /// itself uses (see #118).
    /// </summary>
    public bool AllowHotkeysInFullscreen { get; set; }

    /// <summary>Same flat format as <see cref="ToggleWindowHotkey"/>.</summary>
    public string QuickSwitchHotkey { get; set; } = "Ctrl+G";

    // Held with 1-9 to jump straight to that result. The quick panel reuses this same modifier to
    // switch between its workspace tabs -- one "hold this and press a number" key everywhere, rather
    // than a second setting that would only ever be set to the same thing.
    public string SelectJumpModifier { get; set; } = "Ctrl";
    public string NextItemHotkey { get; set; } = "Ctrl+N";
    public string PreviousItemHotkey { get; set; } = "Ctrl+P";
    public string ActionsMenuHotkey { get; set; } = "Ctrl+O";
    public string CompleteFromSelectionHotkey { get; set; } = "Ctrl+Tab";
    public string QuickLookHotkey { get; set; } = "Alt+P";
    public bool QuickNavTriggerOnDoubleClick { get; set; }
    public bool QuickNavTriggerOnMiddleClick { get; set; } = true;

    // Cycle back/forward through KeywordHistoryStore entries in the quick window's search box.
    public string KeywordHistoryPreviousHotkey { get; set; } = "Alt+Up";
    public string KeywordHistoryNextHotkey { get; set; } = "Alt+Down";

    // Deletes the keyword history entry currently shown in the search box (only while navigating
    // history via the two hotkeys above). A middle-click on the search box does the same thing and
    // isn't user-configurable, matching the always-on scroll-to-navigate gesture.
    public string KeywordHistoryDeleteHotkey { get; set; } = "Ctrl+Delete";

    // Opens the full SearchWindow from the Quick Window, carrying over the current query -- the same
    // action as the Quick Window's own expand ("Open More") button.
    public string OpenFullWindowHotkey { get; set; } = "Ctrl+F";

    // Opens the LocalSend send window directly from the Quick Window.
    public string LocalSendSendWindowHotkey { get; set; } = "Ctrl+S";

    // Stops the Quick Window auto-hiding when it loses focus, for the current summon only -- for
    // assembling a query out of text copied from several other windows, which otherwise means the window
    // (and with it the half-typed query) disappearing on every switch away. See #197. Scoped to the one
    // summon deliberately: it is a temporary escape from the window's whole reason for existing, not a
    // mode to leave switched on.
    public string StayOpenHotkey { get; set; } = "Ctrl+T";

    // Global, not window-level like StayOpen above: the panel docks onto whatever window is in front,
    // so it has to be reachable while that window has focus, which means the hook service detects it.
    public string QuickPanelHotkey { get; set; } = "Ctrl+F2";

    /// <summary>
    /// User overrides for plugin action hotkeys, keyed by plugin ID (the DLL file name without its
    /// extension, matching <see cref="PluginSettings"/>'s convention) then by
    /// <c>ISearchResultAction.Id</c>. An empty string value means the action's hotkey is explicitly
    /// disabled; a missing entry (either level) means "use the action's own built-in default".
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> PluginActionHotkeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class FavoriteItemSetting
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

public class SearchWindowSettings
{
    public double SearchBarWidth { get; set; } = 570;
    public double SearchBarHeight { get; set; } = 60;
    // Fraction of whichever monitor's work area the window was last dragged to (e.g. 0.5/0.22 = centered
    // horizontally, 22% down from the top of THAT monitor) rather than absolute screen pixels -- letting
    // QuickSearchWindowPositioner re-derive the equivalent spot on whatever monitor the mouse/foreground
    // window is on at the next ShowWindow, instead of always reopening on the one specific monitor the
    // window happened to be dragged on originally.
    public double? RelativeLeft { get; set; }
    public double? RelativeTop { get; set; }
    // Replaces the quick window's empty-state placeholder text with date/time/day-of-week (see #101).
    public bool ShowClock { get; set; } = false;
    // When the quick window is already open, pressing the global toggle hotkey again normally hides
    // it -- this opts into opening the full SearchWindow (carrying over the current query) instead.
    public bool ReopenAsFullWindowOnRepeatHotkey { get; set; } = false;
    // Refuses to start a drag of the quick window, so a stray press on it while reaching for the search
    // box cannot nudge it off the spot it was put on. Only the drag: right-clicking the status icon
    // still resets the position, which is the way back if it is already somewhere unwanted. Off by
    // default, since being able to move the window is the behavior everyone already has.
    public bool LockPosition { get; set; } = false;
}

public class PreviewWindowSettings
{
    // Defaults match the default search bar height (70) plus a fully-expanded 9-item results list
    // (9 * BaseSearchResultItemHeight = 459) -- see UiMetrics -- so the preview window's height is
    // predictable and doesn't change with however many results happen to be showing right now.
    public double Width { get; set; } = 400;
    public double Height { get; set; } = 529;
}

/// <summary>The full/main SearchWindow's default size -- distinct from <see cref="SearchWindowSettings"/>,
/// which is the quick window's search bar layout. Updated automatically when the user drags the main
/// window's own resize grip, in addition to being editable on the General settings page.</summary>
public class MainWindowSettings
{
    public double Width { get; set; } = 854;
    public double Height { get; set; } = 480;
}
