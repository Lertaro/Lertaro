namespace Lertaro.App.ViewModels.Settings.General;

// Shared shape for every "reorder list" item across the General settings page's five Order view models
// (QuickNavigation/ActionMenuGroup/ResultType/SidebarGroup/Column) -- DisplayName is a live-recomputed
// value (re-invokes whatever TranslationManager-backed expression produced it at construction) rather
// than a plain string snapshotted once, so NotifyLanguageChanged (raised by each owning ViewModel's own
// TranslationManager.Instance.PropertyChanged subscription) actually has something to refresh instead of
// leaving these rows stuck in whatever language was active when the settings page first loaded.
public abstract class OrderItemBase : ViewModelBase
{
    private readonly Func<string> _resolveDisplayName;

    protected OrderItemBase(string id, Func<string> resolveDisplayName)
    {
        Id = id;
        _resolveDisplayName = resolveDisplayName;
    }

    public string Id { get; }
    public string DisplayName => _resolveDisplayName();

    public void NotifyLanguageChanged() => OnPropertyChanged(nameof(DisplayName));
}
