using System.Collections.ObjectModel;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.App.ViewModels.Search;
using Lertaro.Core;

using Lertaro.App.Services.Plugin;
namespace Lertaro.App.ViewModels.Settings.General;

// Lets the user reorder the quick window's search-result "types" -- each enabled
// ISearchableItemProvider (Applications, Settings, File Filters, any third-party plugin) plus one
// synthetic "Files" entry for raw file-index results -- as a hard tier above match-quality weight
// (see SearchResultMapper.RankedCandidate.TypeRank), and optionally give each type a single-character
// trigger that exclusively filters to just that type (see BuildQuickResults' triggeredTypeId).
// History/Favorites stay hardcoded top-priority and are deliberately NOT part of this list. Edits
// stage in Items and only commit to _userSettings.ResultTypeOrder/ResultTypeTriggers when Save() runs
// (called from GeneralSettingsViewModel.Apply()).
public class ResultTypeOrderViewModel : ViewModelBase
{
    private readonly System.ComponentModel.PropertyChangedEventHandler _translationHandler;

    private readonly UserSettings _userSettings;

    public ResultTypeOrderViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;

        var order = userSettings.ResultTypeOrder;
        var triggers = userSettings.ResultTypeTriggers;
        var candidates = new List<ResultTypeOrderItem>
        {
            new(
                SearchResultTypePriority.FilesTypeId,
                () => TranslationManager.Instance["General_ResultTypeFiles"],
                triggers.GetValueOrDefault(SearchResultTypePriority.FilesTypeId, string.Empty))
        };

        foreach (var provider in PluginManager.Instance.SearchableItemProviders)
        {
            var id = SearchResultTypePriority.GetProviderTypeId(provider);
            candidates.Add(new ResultTypeOrderItem(id, () => provider.Name, triggers.GetValueOrDefault(id, string.Empty)));
        }

        foreach (var item in candidates.OrderBy(c => SearchResultTypePriority.Rank(c.Id, order)))
        {
            Items.Add(item);
        }

        MoveUpCommand = new RelayCommand<ResultTypeOrderItem>(MoveUp);
        MoveDownCommand = new RelayCommand<ResultTypeOrderItem>(MoveDown);

        _translationHandler = (_, _) =>
        {
            foreach (var item in Items)
                item.NotifyLanguageChanged();
        };
        TranslationManager.Instance.PropertyChanged += _translationHandler;

    }

    public ObservableCollection<ResultTypeOrderItem> Items { get; } = new();

    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    private void MoveUp(ResultTypeOrderItem? item)
    {
        if (item == null) return;
        var idx = Items.IndexOf(item);
        if (idx > 0) Items.Move(idx, idx - 1);
    }

    private void MoveDown(ResultTypeOrderItem? item)
    {
        if (item == null) return;
        var idx = Items.IndexOf(item);
        if (idx >= 0 && idx < Items.Count - 1) Items.Move(idx, idx + 1);
    }

    public void Save()
    {
        _userSettings.ResultTypeOrder = Items.Select(x => x.Id).ToList();
        _userSettings.ResultTypeTriggers = Items
            .Where(x => !string.IsNullOrEmpty(x.TriggerChar))
            .ToDictionary(x => x.Id, x => x.TriggerChar);
    }

    public void Cleanup() => TranslationManager.Instance.PropertyChanged -= _translationHandler;
}

public class ResultTypeOrderItem : OrderItemBase
{
    public ResultTypeOrderItem(string id, Func<string> resolveDisplayName, string triggerChar) : base(id, resolveDisplayName) => TriggerChar = triggerChar;

    // Empty = no trigger configured. When this is the first character typed in the quick window,
    // only this type's results show (see SearchResultMapper.BuildQuickResults' triggeredTypeId).
    public string TriggerChar { get; set; }
}
