using System.Collections.ObjectModel;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.Core;

using Lertaro.App.Services.Plugin;
namespace Lertaro.App.ViewModels.Settings.General;

// Lets the user reorder the full SearchWindow's results grid columns (built-in Name/Path/DateModified
// plus any third-party IResultColumnProvider's own columns) -- purely which columns show in which
// left-to-right position, NOT which column the rows are sorted by (that's runtime-only, see
// SearchResultSortMemory). Edits stage in Items and only commit to _userSettings.ColumnOrder when
// Save() runs (called from GeneralSettingsViewModel.Apply()).
public class ColumnOrderViewModel : ViewModelBase
{
    private readonly System.ComponentModel.PropertyChangedEventHandler _translationHandler;

    private readonly UserSettings _userSettings;

    public ColumnOrderViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;
        var order = userSettings.ColumnOrder;

        var candidates = new List<ColumnOrderItem>
        {
            new("Name", () => TranslationManager.Instance["Search_HeaderName"]),
            new("Path", () => TranslationManager.Instance["Search_HeaderPath"]),
            new("DateModified", () => TranslationManager.Instance["Search_HeaderDateModified"]),
        };

        foreach (var provider in PluginManager.Instance.ResultColumnProviders)
            foreach (var col in provider.GetColumns())
            {
                var columnId = col.ColumnId;
                // Re-invokes GetColumns() rather than closing over this one ResultColumnDefinition
                // instance's own HeaderText: a plugin builds a fresh HeaderText (typically via its own
                // TranslationService.Get call) each time GetColumns() runs, but the DEFINITION OBJECT
                // itself is frozen at whatever language was active the moment this loop ran, so reading
                // straight off col.HeaderText later would still show the stale string.
                candidates.Add(new ColumnOrderItem(columnId, () => provider.GetColumns().FirstOrDefault(c => c.ColumnId == columnId)?.HeaderText ?? string.Empty));
            }

        foreach (var item in candidates.OrderBy(c => Rank(c.Id, order)))
            Items.Add(item);

        MoveUpCommand = new RelayCommand<ColumnOrderItem>(MoveUp);
        MoveDownCommand = new RelayCommand<ColumnOrderItem>(MoveDown);

        _translationHandler = (_, _) =>
        {
            foreach (var item in Items)
                item.NotifyLanguageChanged();
        };
        TranslationManager.Instance.PropertyChanged += _translationHandler;

    }

    public ObservableCollection<ColumnOrderItem> Items { get; } = new();

    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    // Position in the user's saved order (most-preferred first); an id that isn't listed yet falls back
    // to int.MaxValue, which -- since the caller's sort is stable -- lands it after every listed column
    // while preserving its original relative order against any OTHER unlisted column, same convention
    // SearchResultTypePriority.Rank/PluginManager.QuickNavigationProviders already use.
    private static int Rank(string columnId, List<string> order)
    {
        var idx = order.IndexOf(columnId);
        return idx >= 0 ? idx : int.MaxValue;
    }

    private void MoveUp(ColumnOrderItem? item)
    {
        if (item == null) return;
        var idx = Items.IndexOf(item);
        if (idx > 0) Items.Move(idx, idx - 1);
    }

    private void MoveDown(ColumnOrderItem? item)
    {
        if (item == null) return;
        var idx = Items.IndexOf(item);
        if (idx >= 0 && idx < Items.Count - 1) Items.Move(idx, idx + 1);
    }

    public void Save() => _userSettings.ColumnOrder = Items.Select(x => x.Id).ToList();

    public void Cleanup() => TranslationManager.Instance.PropertyChanged -= _translationHandler;
}

public class ColumnOrderItem : OrderItemBase
{
    public ColumnOrderItem(string id, Func<string> resolveDisplayName) : base(id, resolveDisplayName) { }
}
