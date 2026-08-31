using System.Collections.ObjectModel;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.App.Services.PluginManagerCore;
using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

using Lertaro.App.Services.Plugin;
namespace Lertaro.App.ViewModels.Settings.General;

// Lets the user reorder which IQuickNavigationProvider's entries appear first/last in the quick
// navigation menu's root level -- a global preference, not per-plugin config, so it lives here rather
// than in any plugin's own PluginConfigSchema. Edits stage in Items and only commit to
// _userSettings.QuickNavigationProviderOrder when Save() runs (called from GeneralSettingsViewModel.Apply()).
public class QuickNavigationOrderViewModel : ViewModelBase
{
    private readonly System.ComponentModel.PropertyChangedEventHandler _translationHandler;

    private readonly UserSettings _userSettings;

    public QuickNavigationOrderViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;

        // PluginManager.Instance.QuickNavigationProviders already applies both the enabled-components
        // filter and the persisted order (falling back to discovery order for anything unlisted), so
        // this list starts out showing exactly what the menu itself would show, in the same order --
        // disabled providers never appear here, per the "hiding them would be meaningless" call.
        foreach (var provider in PluginManager.Instance.QuickNavigationProviders)
        {
            Items.Add(new QuickNavProviderOrderItem(BuildId(provider), () => provider.GroupName));
        }

        MoveUpCommand = new RelayCommand<QuickNavProviderOrderItem>(MoveUp);
        MoveDownCommand = new RelayCommand<QuickNavProviderOrderItem>(MoveDown);

        _translationHandler = (_, _) =>
        {
            foreach (var item in Items)
                item.NotifyLanguageChanged();
        };
        TranslationManager.Instance.PropertyChanged += _translationHandler;

    }

    public ObservableCollection<QuickNavProviderOrderItem> Items { get; } = new();

    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    private static string BuildId(IQuickNavigationProvider provider) =>
        PluginLoaderHelper.MakeId(ComponentFilter.GetDllName(provider), PluginComponentType.QuickNavigationProvider, provider.GetType().Name);

    private void MoveUp(QuickNavProviderOrderItem? item)
    {
        if (item == null) return;
        var idx = Items.IndexOf(item);
        if (idx > 0) Items.Move(idx, idx - 1);
    }

    private void MoveDown(QuickNavProviderOrderItem? item)
    {
        if (item == null) return;
        var idx = Items.IndexOf(item);
        if (idx >= 0 && idx < Items.Count - 1) Items.Move(idx, idx + 1);
    }

    public void Save() => _userSettings.QuickNavigationProviderOrder = Items.Select(x => x.Id).ToList();

    public void Cleanup() => TranslationManager.Instance.PropertyChanged -= _translationHandler;
}

public class QuickNavProviderOrderItem : OrderItemBase
{
    public QuickNavProviderOrderItem(string id, Func<string> resolveDisplayName) : base(id, resolveDisplayName) { }
}
