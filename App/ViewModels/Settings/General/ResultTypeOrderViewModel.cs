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
                triggers.GetValueOrDefault(SearchResultTypePriority.FilesTypeId, string.Empty),
                OnTriggerCharChanged)
        };

        foreach (var provider in PluginManager.Instance.SearchableItemProviders)
        {
            var id = SearchResultTypePriority.GetProviderTypeId(provider);
            candidates.Add(new ResultTypeOrderItem(id, () => provider.Name, triggers.GetValueOrDefault(id, string.Empty), OnTriggerCharChanged));
        }

        foreach (var item in candidates.OrderBy(c => SearchResultTypePriority.Rank(c.Id, order)))
        {
            Items.Add(item);
        }

        // A value saved by an older build may already collide with the syntax or with another type; that
        // has to be visible as soon as the page opens, not only after the user happens to retype it.
        ValidateTriggers();

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
        // Keyed by type id, NOT by trigger character: that is the shape UserSettings.ResultTypeTriggers
        // already had and the shape SearchResultTypePriority.ResolveTrigger reads.
        //
        // A trigger character is a dictionary VALUE here, so two types claiming the same character cannot
        // both survive -- ToDictionary used to throw outright on the duplicate and take Save/Apply down
        // with it, and a duplicate is entirely legal to type. The collision is reported per row instead
        // (see ValidateTriggers), and the first claimant keeps the character.
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        _userSettings.ResultTypeTriggers = Items
            .Where(x => !string.IsNullOrEmpty(x.TriggerChar) && claimed.Add(x.TriggerChar))
            .ToDictionary(x => x.Id, x => x.TriggerChar);
    }

    // Reports, per row, whether its trigger character can actually work:
    //   * a reserved character is consumed by the search syntax before any trigger is read, so the
    //     trigger would silently never fire (see SearchSyntaxReserved);
    //   * a duplicate would be discarded by the dictionary in Save.
    // Called on every keystroke in the trigger box and once on load, so a value saved by an older build
    // is flagged rather than silently kept -- the value itself is deliberately NOT rewritten, so the
    // user's setting keeps working until they change it.
    private void ValidateTriggers()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in Items)
        {
            if (string.IsNullOrEmpty(item.TriggerChar))
                continue;
            counts[item.TriggerChar] = counts.GetValueOrDefault(item.TriggerChar) + 1;
        }

        foreach (var item in Items)
        {
            item.Error = string.IsNullOrEmpty(item.TriggerChar)
                ? null
                : SearchSyntaxReserved.ValidateLeadingCharacter(item.TriggerChar)
                    ?? (counts.GetValueOrDefault(item.TriggerChar) > 1
                        ? TranslationManager.Instance["General_ResultTypeTriggerDuplicate"]
                        : null);
        }
    }

    // Any row's trigger change re-validates every row, because a duplicate is a property of the set and
    // not of the row that was just typed in.
    private void OnTriggerCharChanged() => ValidateTriggers();

    public void Cleanup() => TranslationManager.Instance.PropertyChanged -= _translationHandler;
}

public class ResultTypeOrderItem : OrderItemBase
{
    private readonly Action? _onTriggerChanged;
    private string _triggerChar;

    public ResultTypeOrderItem(string id, Func<string> resolveDisplayName, string triggerChar, Action? onTriggerChanged = null)
        : base(id, resolveDisplayName)
    {
        _triggerChar = triggerChar;
        _onTriggerChanged = onTriggerChanged;
    }

    // Empty = no trigger configured. When this is the first character typed in the quick window,
    // only this type's results show (see SearchResultMapper.BuildQuickResults' triggeredTypeId).
    public string TriggerChar
    {
        get => _triggerChar;
        set
        {
            SetProperty(ref _triggerChar, value);
            _onTriggerChanged?.Invoke();
        }
    }

    private string? _error;

    /// <summary>
    /// Why this trigger character cannot work, or null. Shown under the box: a trigger the search syntax
    /// consumes, or one a second type also claims, would otherwise just silently never fire.
    /// </summary>
    public string? Error
    {
        get => _error;
        set
        {
            SetProperty(ref _error, value);
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_error);
}
