using Lertaro.Core;

namespace Lertaro.App.Services;

public static class UiMetrics
{
    // ── Base (design) metrics, calibrated for the default search bar height ──
    public const double DefaultSearchBarHeight = 70;

    // Reference search bar height at which the Flow-pinned row metrics (icon/font/row-height, see
    // FlowResultIconSize etc.) render at their exact literal values -- matches
    // SearchWindowSettings.SearchBarHeight's own default (60), NOT DefaultSearchBarHeight (70, used by
    // Scale for list items/section headers): the two constants diverged when the row metrics were
    // pinned to Flow's literals, and reusing DefaultSearchBarHeight here would either break Flow pixel
    // parity at the actual default settings or require also changing how list items/section headers
    // scale, which is a separate concern.
    public const double FlowRowReferenceSearchBarHeight = 60;
    public const double BaseSearchResultItemHeight = 51;
    public const double BaseListItemHeight = 34;
    public const double BaseSearchSectionHeaderHeight = 28;

    // Floor for the action-menu section header row so its title font never gets clipped.
    public const double MinSectionHeaderHeight = 18;

    // Shared by every actions-menu surface (in-window list, quick window, flyout) so a compacted row
    // and a separator row come out the same relative size no matter which surface renders them, instead
    // of each surface picking its own ratio and drifting out of sync with the others.
    public const double ActionMenuCompactRowScale = 0.8;
    public const double ActionMenuSeparatorRowScale = 0.3;

    // The result row's ItemBorder (ResultItemStyle/ActionItemStyle in ListBox.xaml) has
    // Margin="6,2,6,2" -- since that Border is the template root, its 2px top + 2px bottom margin adds
    // to the row's own measured/desired size. Row-height math needs to budget for it, or a row whose
    // icon drives it past the base height still comes out a few pixels short of what actually renders.
    public const double ResultRowVerticalMargin = 4;

    // Extra breathing room around the icon once it's what drives the row's height (icon larger than
    // BaseSearchResultItemHeight - ResultRowVerticalMargin) -- at the DEFAULT icon size the row already
    // has this much slack "for free" (51 - 42 = 9 = ResultRowVerticalMargin + this), but that slack was
    // only ever a byproduct of the base height being a fixed constant, so it silently vanished to zero
    // the moment a configured icon grew past the point where Math.Max's icon branch started winning --
    // a bigger icon then had only the bare structural margin around it, reading as visibly cramped (#91
    // follow-up). Baking the same slack in explicitly keeps it constant across every icon size instead
    // of only at the default.
    public const double IconRowBreathingRoom = 5;

    // Base font/icon metrics used by the search result item template. Name:Path is weighted 10:8 (~56:44)
    // when both lines show, tilting more toward the name than an even split while keeping the path line
    // (the smaller of the two) comfortably legible.
    public const double BaseResultNameFontSize = 16;
    public const double BaseResultPathFontSize = 12.8;
    public const double BaseResultIconSize = 42; // fixed size for the main window

    // Flow Launcher's own literal result-row metrics (32px icon, 16px title, 13px subtitle, 58px row) --
    // the quick window's Scaled* properties below apply _flowRowScale to these (see its own comment)
    // instead of deriving them from a separately configurable icon-size setting, so the row always keeps
    // Flow's exact proportions at any search-bar height instead of the icon and text drifting out of
    // ratio the way they could under the old (pre-#132) independent icon-size/Scale formulas.
    public const double FlowResultIconSize = 32;
    public const double FlowResultNameFontSize = 16;
    public const double FlowResultPathFontSize = 13;
    public const double FlowResultItemHeight = 58;

    // Retained only as IconRelativeFontScale's reference point (see FlowResultIconSize above).
    public const double IconFontRatioReferenceSize = 32;

    // Floor for the quick window's Flow-pinned row text (see ScaledResultNameFontSize etc.) -- at the
    // smallest configurable search bar height, straight proportional scaling would shrink text well
    // past legible.
    public const double MinScaledResultNameFontSize = 14;
    public const double MinScaledResultPathFontSize = 9;

    // Range for the user-configurable QuickLook preview window size (General settings page).
    public const double MinPreviewWindowWidth = 250;
    public const double MaxPreviewWindowWidth = 900;
    public const double MinPreviewWindowHeight = 250;
    public const double MaxPreviewWindowHeight = 1200;

    // Range for the user-configurable main SearchWindow default size (General settings page).
    // Min matches SearchWindow.xaml's own MinWidth/MinHeight resize floor.
    public const double DefaultMainWindowWidth = 854;
    public const double DefaultMainWindowHeight = 480;
    public const double MinMainWindowWidth = 640;
    public const double MaxMainWindowWidth = 2000;
    public const double MinMainWindowHeight = 400;
    public const double MaxMainWindowHeight = 1400;

    private static double _scale = 1.0;
    private static double _flowRowScale = 1.0;
    private static double _previewWindowWidth = 400;
    private static double _previewWindowHeight = 529;
    private static double _mainWindowWidth = DefaultMainWindowWidth;
    private static double _mainWindowHeight = DefaultMainWindowHeight;

    /// <summary>
    /// Fires whenever Scale actually changes value -- lets an already-open window (whose bound rows
    /// were built from a snapshot of the old scale, e.g. AppSearchResult's own
    /// Scaled* properties) refresh those bindings live instead of only picking up the new scale the
    /// next time it happens to rebuild its content (e.g. the next search, or the next time the window
    /// is shown).
    /// </summary>
    public static event Action? ScaleChanged;

    /// <summary>
    /// Global UI scale factor. Result rows, fonts and icons multiply their
    /// base metrics by this value so they grow/shrink together with the
    /// user-configured search box height.
    /// </summary>
    public static double Scale
    {
        get => _scale;
        set
        {
            var clamped = Math.Clamp(value, 0.6, 1.8);
            if (clamped == _scale) return;
            _scale = clamped;
            ScaleChanged?.Invoke();
        }
    }

    /// <summary>
    /// Derives the scale factor from the configured search bar height so the
    /// result list scales proportionally (e.g. 70px -> 1.0, 105px -> 1.5).
    /// </summary>
    public static void UpdateScaleFromSearchBarHeight(double searchBarHeight)
    {
        if (searchBarHeight > 0)
        {
            Scale = searchBarHeight / DefaultSearchBarHeight;
            _flowRowScale = searchBarHeight / FlowRowReferenceSearchBarHeight;
        }
    }

    /// <summary>QuickLook preview window size. User-configurable (General settings page); fixed rather
    /// than derived from the owner window's current height so it doesn't change with however many
    /// results happen to be showing right now.</summary>
    public static double PreviewWindowWidth
    {
        get => _previewWindowWidth;
        set => _previewWindowWidth = Math.Clamp(value, MinPreviewWindowWidth, MaxPreviewWindowWidth);
    }

    public static double PreviewWindowHeight
    {
        get => _previewWindowHeight;
        set => _previewWindowHeight = Math.Clamp(value, MinPreviewWindowHeight, MaxPreviewWindowHeight);
    }

    /// <summary>Main SearchWindow default size. User-configurable (General settings page) and also
    /// updated automatically when the user drags the window's own resize grip, so re-opening it (or
    /// opening a new instance) remembers the last size either way.</summary>
    public static double MainWindowWidth
    {
        get => _mainWindowWidth;
        set => _mainWindowWidth = Math.Clamp(value, MinMainWindowWidth, MaxMainWindowWidth);
    }

    public static double MainWindowHeight
    {
        get => _mainWindowHeight;
        set => _mainWindowHeight = Math.Clamp(value, MinMainWindowHeight, MaxMainWindowHeight);
    }

    /// <summary>Loads the current search bar height, quick-window icon size, preview window size, and
    /// main window size from settings and applies them.</summary>
    public static void ApplyScaleFromSettings()
    {
        var settings = UserSettings.Load();
        try { UpdateScaleFromSearchBarHeight(settings.SearchWindow.SearchBarHeight); }
        catch { /* fall back to current scale */ }
        try { PreviewWindowWidth = settings.PreviewWindow.Width; }
        catch { /* fall back to current preview width */ }
        try { PreviewWindowHeight = settings.PreviewWindow.Height; }
        catch { /* fall back to current preview height */ }
        try { MainWindowWidth = settings.MainWindow.Width; }
        catch { /* fall back to current main window width */ }
        try { MainWindowHeight = settings.MainWindow.Height; }
        catch { /* fall back to current main window height */ }
    }

    // ── Base metrics (used everywhere by default: inline window, full window, action menu) ──
    public static double SearchResultItemHeight => BaseSearchResultItemHeight;
    public static double ListItemHeight => BaseListItemHeight;
    public static double SearchSectionHeaderHeight => BaseSearchSectionHeaderHeight;
    public static double MenuItemHeight => ListItemHeight * 0.8;

    // A row with no path subtitle (applications, blank ParentDir) gives the whole name/path line-
    // height budget to the name alone instead of splitting it with an empty second line -- shares
    // BaseResultNameFontSize's value structurally (not just numerically) so a single-line row and a
    // dual-line row's name can never drift apart again the way they did across #65 and #91.
    public static double ResultNameFontSize => BaseResultNameFontSize;
    public static double ResultNameFontSizeSingleLine => BaseResultNameFontSize;
    public static double ResultPathFontSize => BaseResultPathFontSize;
    public static double ResultIconSize => BaseResultIconSize;

    // The inline window's own row metrics -- literal design values, NOT derived by scaling
    // BaseSearchResultItemHeight/BaseResultIconSize by a ratio (that used to be Math.Round(51 * 0.7),
    // which quietly coupled inline's row design to the main window's own numbers: changing one could
    // silently drag the other along). Tuned independently so inline can be re-designed on its own.
    // InlineRowHeight must stay >= InlineIconSize + ResultRowVerticalMargin for the icon to actually
    // fit (see AppSearchResult.InlineItemHeight, which assumes this and no longer needs its own
    // Math.Max to enforce it).
    public const double InlineRowHeight = 36;
    public const double InlineIconSize = 27;

    // ── Scaled metrics — consumed ONLY by the quick window (opted in via window title),
    //    so the inline/full windows never scale with the search-bar height. ──
    //    Icon/font/row-height below all multiply Flow Launcher's own literal values (see
    //    FlowResultIconSize etc.'s own comment) by _flowRowScale, so the whole row grows/shrinks
    //    together as one unit -- keeping Flow's exact proportions at every search-bar height instead of
    //    just at the default -- rather than each metric scaling off its own independent setting.
    //    ScaledListItemHeight/ScaledSearchSectionHeaderHeight are unrelated rows (list items, section
    //    headers) and still scale off the separate Scale factor.
    public static double ScaledSearchResultItemHeight => Math.Round(FlowResultItemHeight * _flowRowScale);
    public static double ScaledListItemHeight => Math.Round(BaseListItemHeight * _scale);
    public static double ScaledSearchSectionHeaderHeight => Math.Round(BaseSearchSectionHeaderHeight * _scale);

    public static double ScaledResultIconSize => Math.Round(FlowResultIconSize * _flowRowScale);

    // The actual rendered height of a normal (icon+text) row once the icon-size-driven floor is
    // applied -- shared by AppSearchResult (results list) and ActionMenuItem (actions list) so their
    // rows come out pixel-identical instead of one accounting for icon overflow and the other not.
    public static double ScaledNormalRowHeight => Math.Max(ScaledSearchResultItemHeight, ScaledResultIconSize + ResultRowVerticalMargin + IconRowBreathingRoom);

    // Kept so ActionMenuItem's own icon/font scaling (ScaledIconSize etc., which multiplies its OWN
    // base sizes by this) tracks the same _flowRowScale-derived factor as the results row, while both
    // share one formula instead of ActionMenuItem needing its own separate scaling logic.
    public static double IconRelativeFontScale => ScaledResultIconSize / IconFontRatioReferenceSize;

    public static double ScaledResultNameFontSize => Math.Max(MinScaledResultNameFontSize, FlowResultNameFontSize * _flowRowScale);
    public static double ScaledResultNameFontSizeSingleLine => ScaledResultNameFontSize;
    public static double ScaledResultPathFontSize => Math.Max(MinScaledResultPathFontSize, FlowResultPathFontSize * _flowRowScale);
}
