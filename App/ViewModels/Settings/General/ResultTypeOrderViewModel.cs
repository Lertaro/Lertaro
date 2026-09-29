using System.Collections.ObjectModel;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.App.ViewModels.Search;
using Lertaro.App.ViewModels.Search.Dispatch;
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
                _ => RefreshTriggerWarnings())
        };

        foreach (var provider in PluginManager.Instance.SearchableItemProviders)
        {
            var id = SearchResultTypePriority.GetProviderTypeId(provider);
            candidates.Add(new ResultTypeOrderItem(id, () => provider.Name, triggers.GetValueOrDefault(id, string.Empty),
                _ => RefreshTriggerWarnings()));
        }

        foreach (var item in candidates.OrderBy(c => SearchResultTypePriority.Rank(c.Id, order)))
        {
            Items.Add(item);
        }

        // A saved table can already hold a duplicate (nothing stopped it before this warned): name it on
        // the rows it affects rather than waiting for the user to touch one of them again.
        RefreshTriggerWarnings();

        MoveUpCommand = new RelayCommand<ResultTypeOrderItem>(MoveUp);
        MoveDownCommand = new RelayCommand<ResultTypeOrderItem>(MoveDown);

        _translationHandler = (_, _) =>
        {
            foreach (var item in Items)
                item.NotifyLanguageChanged();
            // The warning names the other row/feature, so it has to follow the language too.
            RefreshTriggerWarnings();
        };
        TranslationManager.Instance.PropertyChanged += _translationHandler;

    }

    /// <summary>
    /// The type whose trigger character the given row duplicates, ignoring case -- the rule the runtime
    /// applies after <see cref="SearchResultTypePriority.ResolveTrigger"/>, which returns whichever entry
    /// the table happens to enumerate first and leaves the other type's character silently dead. Pure, so
    /// the collision rule is testable without the settings graph or the plugin registry.
    /// </summary>
    internal static ResultTypeOrderItem? FindDuplicateTrigger(IReadOnlyList<ResultTypeOrderItem> items, ResultTypeOrderItem item)
    {
        if (item.TriggerChar.Length != 1)
            return null;

        foreach (var other in items)
        {
            if (ReferenceEquals(other, item) || other.TriggerChar.Length != 1)
                continue;

            if (char.ToUpperInvariant(other.TriggerChar[0]) == char.ToUpperInvariant(item.TriggerChar[0]))
                return other;
        }

        return null;
    }

    // Recomputed for every row whenever one of them changes: which row owns a character is only knowable
    // by looking at the whole table. A plugin's trigger word counts too -- a single-character word and a
    // type trigger on the same character is a real clash (the character wins the file search while the
    // plugin still answers), and PluginTriggerQuery already knows about every word.
    private void RefreshTriggerWarnings()
    {
        foreach (var item in Items)
        {
            if (item.TriggerChar.Length == 0)
            {
                item.ConflictWarning = string.Empty;
                continue;
            }

            var other = FindDuplicateTrigger(Items, item)?.DisplayName
                ?? PluginTriggerCollisionReport.FindOtherOwnerForHostTrigger(item.TriggerChar);
            item.ConflictWarning = other == null
                ? string.Empty
                : string.Format(TranslationManager.Instance["General_ResultTypeTriggerTaken"], other);
        }
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
        var visible = Items.Select(x => x.Id).ToList();
        // Items is seeded from the ENABLED SearchableItemProviders, so it is not the whole table: a provider
        // the user merely switched off on the plugin page has its type id still live in the saved settings
        // but no row here. Writing Items out wholesale therefore deleted that type's trigger character and
        // its rank -- silently, permanently, and it returned as "no trigger configured" once the provider
        // was re-enabled. An id that is neither visible nor loaded is an uninstalled plugin's leftover,
        // which is exactly the garbage the wholesale replace usefully cleaned, so those stay dropped.
        var hidden = PluginManager.Instance.AllSearchableItemProviders
            .Select(SearchResultTypePriority.GetProviderTypeId)
            .ToHashSet();
        hidden.ExceptWith(visible);

        _userSettings.ResultTypeOrder = MergeOrder(visible, _userSettings.ResultTypeOrder, hidden);
        _userSettings.ResultTypeTriggers = MergeTriggers(
            Items.Select(x => (x.Id, x.TriggerChar)).ToList(), _userSettings.ResultTypeTriggers, hidden);
    }

    // The two merge rules, pure and static for the same reason FindDuplicateTrigger is: the settings graph
    // and the plugin registry are both process-wide singletons, and this is the part worth pinning down.

    // Visible types in the order the dialog now shows them, then the loaded-but-disabled ones in whatever
    // relative order they had. An unlisted id already sorts to the end (see SearchResultTypePriority.Rank),
    // so trailing them costs nothing a query-time sort would not have done anyway.
    internal static List<string> MergeOrder(IReadOnlyList<string> visible, IReadOnlyList<string> stored, IReadOnlyCollection<string> hidden)
    {
        var merged = new List<string>(visible.Count + hidden.Count);
        merged.AddRange(visible);
        merged.AddRange(stored.Where(hidden.Contains));
        return merged;
    }

    // A row this dialog shows owns its trigger outright: clearing it must really remove the entry, which is
    // why the visible ids are rewritten rather than merged over. Only a hidden id's stored value survives
    // untouched.
    internal static Dictionary<string, string> MergeTriggers(
        IReadOnlyList<(string Id, string TriggerChar)> visible,
        IReadOnlyDictionary<string, string> stored,
        IReadOnlyCollection<string> hidden)
    {
        var merged = stored.Where(entry => hidden.Contains(entry.Key))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        foreach (var (id, triggerChar) in visible)
        {
            if (triggerChar.Length > 0)
                merged[id] = triggerChar;
        }

        return merged;
    }

    public void Cleanup() => TranslationManager.Instance.PropertyChanged -= _translationHandler;
}

public class ResultTypeOrderItem : OrderItemBase
{
    private readonly Action<ResultTypeOrderItem>? _onTriggerCharChanged;
    private string _triggerChar = string.Empty;
    private string _conflictWarning = string.Empty;

    public ResultTypeOrderItem(string id, Func<string> resolveDisplayName, string triggerChar,
        Action<ResultTypeOrderItem>? onTriggerCharChanged = null)
        : base(id, resolveDisplayName)
    {
        _onTriggerCharChanged = onTriggerCharChanged;
        _triggerChar = Normalize(triggerChar);
    }

    // Empty = no trigger configured. When this is the first character typed in the quick window,
    // only this type's results show (see SearchResultMapper.BuildQuickResults' triggeredTypeId).
    public string TriggerChar
    {
        get => _triggerChar;
        set
        {
            var normalized = Normalize(value);
            if (string.Equals(_triggerChar, normalized, StringComparison.Ordinal))
                return;

            _triggerChar = normalized;
            OnPropertyChanged(nameof(TriggerChar));
            // Every other row's warning is about THIS character, so the owner recomputes them all -- a
            // duplicate the user just created (or just removed) has to show up without a page reload.
            _onTriggerCharChanged?.Invoke(this);
        }
    }

    /// <summary>
    /// Shown in amber under the row when another type -- or another feature's trigger word -- already
    /// answers to this character. Assigned by <see cref="ResultTypeOrderViewModel"/>, the only thing that
    /// can see the other rows.
    /// </summary>
    public string ConflictWarning
    {
        get => _conflictWarning;
        internal set
        {
            if (string.Equals(_conflictWarning, value, StringComparison.Ordinal))
                return;

            _conflictWarning = value;
            OnPropertyChanged(nameof(ConflictWarning));
        }
    }

    // One character IS the rule at the runtime end (SearchResultTypePriority.ResolveTrigger only ever
    // looks at a length-1 value), so anything longer the editor lets through is trimmed here instead of
    // being saved as a trigger that can never fire.
    private static string Normalize(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length > 1 ? trimmed[..1] : trimmed;
    }
}
