using System.Collections.ObjectModel;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.App.Services.PluginManagerCore;
using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions.Plugins.Preview;

using Lertaro.App.Services.Plugin;
namespace Lertaro.App.ViewModels.Settings.General;

// Lets the user reorder which IThumbnailProvider gets first refusal when generating a result's
// icon/thumbnail (the built-in shell thumbnail provider plus any third-party plugin's own). Save()
// commits _userSettings.ThumbnailProviderOrder.
public class ThumbnailProviderOrderViewModel : ViewModelBase
{
    private readonly System.ComponentModel.PropertyChangedEventHandler _translationHandler;

    private readonly UserSettings _userSettings;

    public ThumbnailProviderOrderViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;

        // Same filter/persisted-order behaviour as QuickNavigationOrderViewModel's ctor comment, except
        // unlisted providers fall back to their own Priority, not to discovery order.
        foreach (var provider in PluginManager.Instance.ThumbnailProviders)
        {
            Items.Add(new ThumbnailProviderOrderItem(BuildId(provider), () => provider.Name));
        }

        MoveUpCommand = new RelayCommand<ThumbnailProviderOrderItem>(MoveUp);
        MoveDownCommand = new RelayCommand<ThumbnailProviderOrderItem>(MoveDown);

        _translationHandler = (_, _) =>
        {
            foreach (var item in Items)
                item.NotifyLanguageChanged();
        };
        TranslationManager.Instance.PropertyChanged += _translationHandler;

    }

    public ObservableCollection<ThumbnailProviderOrderItem> Items { get; } = new();

    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    private static string BuildId(IThumbnailProvider provider) =>
        PluginLoaderHelper.MakeId(ComponentFilter.GetDllName(provider), PluginComponentType.ThumbnailProvider, provider.GetType().Name);

    private void MoveUp(ThumbnailProviderOrderItem? item)
    {
        if (item == null) return;
        var idx = Items.IndexOf(item);
        if (idx > 0) Items.Move(idx, idx - 1);
    }

    private void MoveDown(ThumbnailProviderOrderItem? item)
    {
        if (item == null) return;
        var idx = Items.IndexOf(item);
        if (idx >= 0 && idx < Items.Count - 1) Items.Move(idx, idx + 1);
    }

    public void Save() => _userSettings.ThumbnailProviderOrder = Items.Select(x => x.Id).ToList();

    public void Cleanup() => TranslationManager.Instance.PropertyChanged -= _translationHandler;
}

public class ThumbnailProviderOrderItem : OrderItemBase
{
    public ThumbnailProviderOrderItem(string id, Func<string> resolveDisplayName) : base(id, resolveDisplayName) { }
}
