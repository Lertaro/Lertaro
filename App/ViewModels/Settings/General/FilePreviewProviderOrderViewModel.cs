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

// Lets the user reorder which IFilePreviewProvider gets first refusal when previewing a selected
// result (built-in image/text/media/PE previewers plus any third-party plugin's own, e.g. QuickLook
// Bridge) -- previously a fixed, compile-time Priority with no user-facing override at all. Edits
// stage in Items and only commit to _userSettings.FilePreviewProviderOrder when Save() runs (called
// from GeneralSettingsViewModel.Apply()).
public class FilePreviewProviderOrderViewModel : ViewModelBase
{
    private readonly System.ComponentModel.PropertyChangedEventHandler _translationHandler;

    private readonly UserSettings _userSettings;

    public FilePreviewProviderOrderViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;

        // PluginManager.Instance.FilePreviewProviders already applies both the enabled-components
        // filter and the persisted order (falling back to each provider's own Priority for anything
        // unlisted), so this list starts out showing exactly what preview dispatch would try, in the
        // same order -- disabled providers never appear here, per the "reordering them would be
        // meaningless" call every other Order view model in this file already makes.
        foreach (var provider in PluginManager.Instance.FilePreviewProviders)
        {
            Items.Add(new FilePreviewProviderOrderItem(BuildId(provider), () => provider.Name));
        }

        MoveUpCommand = new RelayCommand<FilePreviewProviderOrderItem>(MoveUp);
        MoveDownCommand = new RelayCommand<FilePreviewProviderOrderItem>(MoveDown);

        _translationHandler = (_, _) =>
        {
            foreach (var item in Items)
                item.NotifyLanguageChanged();
        };
        TranslationManager.Instance.PropertyChanged += _translationHandler;

    }

    public ObservableCollection<FilePreviewProviderOrderItem> Items { get; } = new();

    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    private static string BuildId(IFilePreviewProvider provider) =>
        PluginLoaderHelper.MakeId(ComponentFilter.GetDllName(provider), PluginComponentType.FilePreviewProvider, provider.GetType().Name);

    private void MoveUp(FilePreviewProviderOrderItem? item)
    {
        if (item == null) return;
        var idx = Items.IndexOf(item);
        if (idx > 0) Items.Move(idx, idx - 1);
    }

    private void MoveDown(FilePreviewProviderOrderItem? item)
    {
        if (item == null) return;
        var idx = Items.IndexOf(item);
        if (idx >= 0 && idx < Items.Count - 1) Items.Move(idx, idx + 1);
    }

    public void Save() => _userSettings.FilePreviewProviderOrder = Items.Select(x => x.Id).ToList();

    public void Cleanup() => TranslationManager.Instance.PropertyChanged -= _translationHandler;
}

public class FilePreviewProviderOrderItem : OrderItemBase
{
    public FilePreviewProviderOrderItem(string id, Func<string> resolveDisplayName) : base(id, resolveDisplayName) { }
}
