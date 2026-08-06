using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace Lertaro.App;

public class ActionMenuItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    // Driven explicitly by LstActions.SelectionChanged (see ShellMenuPresenter) rather than the
    // RelativeSource-AncestorType-ListBoxItem DataTrigger the results list's own badge uses -- that
    // pattern renders every row's badge as permanently "selected" in this ListBox specifically (not
    // reproducible in the results list's own ListBox), root cause not pinned down; a plain
    // INotifyPropertyChanged-backed flag sidesteps whatever ancestor-lookup quirk causes it entirely.
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public string Text { get; set; } = string.Empty;
    public string SearchQuery { get; set; } = string.Empty;
    public uint CommandId { get; set; }
    public bool IsSeparator { get; set; }
    public bool IsSectionHeader { get; set; }
    public string SectionTitle { get; set; } = string.Empty;
    // Stable, non-localized id for a section header (see ActionMenuBuilder.BuildStaticGroupId/
    // BuildDynamicGroupId) -- SectionTitle alone can't be persisted as a user-chosen order key, since
    // it's already-translated display text that changes with the UI language.
    public string SectionGroupId { get; set; } = string.Empty;
    public bool HasSubMenu { get; set; }
    public IntPtr SubMenuHandle { get; set; }
    public bool IsDisabled { get; set; }
    public ImageSource? Icon { get; set; }
    private string _shortcutHint = string.Empty;
    public string ShortcutHint
    {
        get => _shortcutHint;
        set => _shortcutHint = Core.HotkeyStringFormat.ToDisplayText(value);
    }
    public Action? OnExecute { get; set; }

    public double ItemHeight { get; set; } = Services.UiMetrics.ListItemHeight;

    // Set for the quick-nav flyout so it renders at the compact shell-menu size (smaller font + shorter
    // rows) instead of the roomy full-window list size, while keeping the same layout and colors.
    public bool IsCompact { get; set; }

    // Base content sizes (match ActionMenuItem.xaml) and their scaled variants, used only by
    // the quick window so its action list scales with the configured search box height.
    private const double BaseIconSize = 16;
    private const double BaseTextFontSize = 13;
    private const double BaseSectionFontSize = 12;
    private const double BaseShortcutFontSize = 11;

    // Was UiMetrics.ScaledNormalRowHeight as-is (matching the results list's own row height, so the two
    // lists read as the same size when flipping between them) -- deliberately broken by request: a
    // normal-sized action row wastes far more space than an action needs, and the actions list has no
    // reason to stay visually locked to the results list's own sizing at the cost of fitting fewer
    // actions on screen. Both scales are the same UiMetrics constants ActionMenuBuilder uses for the
    // flyout/full-window action list, so all three actions-menu surfaces land on the same relative row
    // and separator sizing rather than each picking its own ratio. A separator used to render as a full
    // row (the quick window's ActionItemStyle binds MinHeight to this same property for every row
    // regardless of IsSeparator), which is far more visual weight than a thin divider needs.
    // QuickSearchWindowLayoutManager sums this same property to size the actions panel, so shrinking it
    // here shrinks the panel to match -- no separate bookkeeping, and no leftover blank space at the
    // bottom of the list.
    private double CompactScaledNormalRowHeight => Math.Round(Services.UiMetrics.ScaledNormalRowHeight * Services.UiMetrics.ActionMenuCompactRowScale);
    public double ScaledItemHeight => IsSeparator
        ? Math.Round(CompactScaledNormalRowHeight * Services.UiMetrics.ActionMenuSeparatorRowScale)
        : CompactScaledNormalRowHeight;

    // Scaled off IconRelativeFontScale (tracks the actual configured icon size), not raw Scale (tracks
    // only the search-bar height) -- the two only coincide when the configured result icon size equals
    // UiMetrics.BaseResultIconSize, so using Scale here left action text/icons visibly smaller than
    // the result row text sitting right next to them once the two diverged.
    public double ScaledIconSize => Math.Round(BaseIconSize * Services.UiMetrics.IconRelativeFontScale);
    public double ScaledTextFontSize => Math.Round(BaseTextFontSize * Services.UiMetrics.IconRelativeFontScale);
    public double ScaledSectionFontSize => Math.Round(BaseSectionFontSize * Services.UiMetrics.IconRelativeFontScale);
    public double ScaledShortcutFontSize => Math.Round(BaseShortcutFontSize * Services.UiMetrics.IconRelativeFontScale);

    public bool IsNormalItem => !IsSeparator && !IsSectionHeader;

    public Visibility IconVisibility => Icon != null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PlaceholderVisibility => (Icon == null && !IsSeparator && !IsSectionHeader) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SectionHeaderVisibility => IsSectionHeader ? Visibility.Visible : Visibility.Collapsed;
}
