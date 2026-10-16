using System.Collections.ObjectModel;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.App.Services.PluginManagerCore;
using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions.Plugins;

using Lertaro.App.Services.Plugin;
namespace Lertaro.App.ViewModels.Settings.General;

// Lets the user reorder the full SearchWindow's sidebar filter groups (Type/Date/Size/any third-party
// ISidebarFilterProvider) -- one entry per PROVIDER rather than per group, matching how
// PluginManager.SidebarFilterProviders already orders at provider granularity. Save() commits
// _userSettings.SidebarGroupOrder.
public class SidebarGroupOrderViewModel : ViewModelBase
{
    private readonly System.ComponentModel.PropertyChangedEventHandler _translationHandler;

    private readonly UserSettings _userSettings;

    public SidebarGroupOrderViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;

        // Same filter/persisted-order behaviour as QuickNavigationOrderViewModel's ctor comment; the
        // filter here comes from the FilteredSidebarFilterProvider wrapper. A provider whose every group
        // is disabled contributes none -- skipped, since reordering an empty entry would be meaningless.
        foreach (var provider in PluginManager.Instance.SidebarFilterProviders)
        {
            var groups = provider.GetFilterGroups().ToList();
            if (groups.Count == 0) continue;

            // BuildId must match PluginManager.SidebarFilterProviders' own ordering exactly, which
            // computes its id off the RAW plugin-defined provider -- provider here is always a
            // FilteredSidebarFilterProvider wrapper (see PluginManager.SidebarFilterProviders), so
            // unwrap it first or the id would come from the wrapper's own type/assembly instead
            // and never match anything the user's saved order could reference.
            var id = BuildId(provider is FilteredSidebarFilterProvider filtered ? filtered.Inner : provider);
            Items.Add(new SidebarGroupOrderItem(id, () => provider.GetFilterGroups().FirstOrDefault()?.Header ?? string.Empty));
        }

        MoveUpCommand = new RelayCommand<SidebarGroupOrderItem>(MoveUp);
        MoveDownCommand = new RelayCommand<SidebarGroupOrderItem>(MoveDown);

        _translationHandler = (_, _) =>
        {
            foreach (var item in Items)
                item.NotifyLanguageChanged();
        };
        TranslationManager.Instance.PropertyChanged += _translationHandler;

    }

    public ObservableCollection<SidebarGroupOrderItem> Items { get; } = new();

    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    internal static string BuildId(ISidebarFilterProvider provider) =>
        PluginLoaderHelper.MakeId(ComponentFilter.GetDllName(provider), PluginComponentType.FilterProvider, provider.GetType().Name);

    private void MoveUp(SidebarGroupOrderItem? item)
    {
        if (item == null) return;
        var idx = Items.IndexOf(item);
        if (idx > 0) Items.Move(idx, idx - 1);
    }

    private void MoveDown(SidebarGroupOrderItem? item)
    {
        if (item == null) return;
        var idx = Items.IndexOf(item);
        if (idx >= 0 && idx < Items.Count - 1) Items.Move(idx, idx + 1);
    }

    public void Save() => _userSettings.SidebarGroupOrder = Items.Select(x => x.Id).ToList();

    public void Cleanup() => TranslationManager.Instance.PropertyChanged -= _translationHandler;
}

public class SidebarGroupOrderItem : OrderItemBase
{
    public SidebarGroupOrderItem(string id, Func<string> resolveDisplayName) : base(id, resolveDisplayName) { }
}
