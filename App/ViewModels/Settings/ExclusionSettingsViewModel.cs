using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;

using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.App.ViewModels.Settings.General;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.Settings;

/// <summary>
/// One list for paths, globs and regexes, each carrying a low/normal/high/exclude effect. The kind is not
/// a user choice: it is derived from the spelling so typing <c>build\</c> or <c>/^\./</c> is enough.
/// </summary>
public class ExclusionSettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;
    private readonly ListCollectionView _view;

    private string _newRuleText = string.Empty;
    private string _newRuleEffect = nameof(IndexRuleEffect.Normal);
    private string _newRuleDetection = string.Empty;
    private string _newRuleResolvedPath = string.Empty;
    private string _newRuleError = string.Empty;
    private string _conflictSummary = string.Empty;
    private string _conflictDetails = string.Empty;
    private string _statusMessage = string.Empty;
    private string _bulkText = string.Empty;
    private string _filter = "All";
    private string _countSummary = string.Empty;
    private bool _hasConflicts;

    public ExclusionSettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;

        foreach (var rule in IndexRuleMigration.Ensure(_userSettings))
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern))
                continue;
            Rules.Add(new ExclusionRuleItem(IndexRuleSyntax.Clean(rule.Pattern), rule.Effect, rule.AlwaysTop));
        }

        _view = new ListCollectionView(Rules)
        {
            CustomSort = new IndexRuleSortComparer(),
            Filter = FilterRule,
        };

        EffectOptions =
        [
            new LabeledOption(nameof(IndexRuleEffect.Exclude), T("Exclusions_EffectExclude")),
            new LabeledOption(nameof(IndexRuleEffect.Low), T("Exclusions_EffectLow")),
            new LabeledOption(nameof(IndexRuleEffect.Normal), T("Exclusions_EffectNormal")),
            new LabeledOption(nameof(IndexRuleEffect.High), T("Exclusions_EffectHigh")),
        ];

        FilterOptions =
        [
            new LabeledOption("All", T("Exclusions_FilterAll")),
            new LabeledOption(nameof(IndexRuleEffect.Exclude), T("Exclusions_EffectExclude")),
            new LabeledOption(nameof(IndexRuleEffect.Low), T("Exclusions_EffectLow")),
            new LabeledOption(nameof(IndexRuleEffect.Normal), T("Exclusions_EffectNormal")),
            new LabeledOption(nameof(IndexRuleEffect.High), T("Exclusions_EffectHigh")),
        ];

        AddRuleCommand = new RelayCommand(AddRule, CanAddRule);
        SelectFilterCommand = new RelayCommand<string>(value => Filter = string.IsNullOrEmpty(value) ? "All" : value);
        RemoveRuleCommand = new RelayCommand<ExclusionRuleItem>(RemoveRule);
        EditRuleCommand = new RelayCommand<ExclusionRuleItem>(BeginEdit);
        SaveRuleCommand = new RelayCommand<ExclusionRuleItem>(SaveEdit);
        CancelRuleCommand = new RelayCommand<ExclusionRuleItem>(item => item.IsEditing = false);
        ApplyBulkTextCommand = new RelayCommand(() => ApplyBulkText());
        ExportBulkTextCommand = new RelayCommand(RefreshBulkText);

        RefreshBulkText();
        RecomputeConflicts();
        RefreshCounts();
    }

    public ObservableCollection<ExclusionRuleItem> Rules { get; } = new();

    public ICollectionView RulesView => _view;

    public IReadOnlyList<LabeledOption> EffectOptions { get; }

    public IReadOnlyList<LabeledOption> FilterOptions { get; }

    public ICommand AddRuleCommand { get; }
    public ICommand SelectFilterCommand { get; }
    public ICommand RemoveRuleCommand { get; }
    public ICommand EditRuleCommand { get; }
    public ICommand SaveRuleCommand { get; }
    public ICommand CancelRuleCommand { get; }
    public ICommand ApplyBulkTextCommand { get; }
    public ICommand ExportBulkTextCommand { get; }

    public string NewRuleText
    {
        get => _newRuleText;
        set
        {
            if (!SetProperty(ref _newRuleText, value))
                return;
            UpdatePreview();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string NewRuleEffect
    {
        get => _newRuleEffect;
        set
        {
            if (!SetProperty(ref _newRuleEffect, value))
                return;
            UpdatePreview();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string NewRuleDetection
    {
        get => _newRuleDetection;
        private set => SetProperty(ref _newRuleDetection, value);
    }

    public string NewRuleResolvedPath
    {
        get => _newRuleResolvedPath;
        private set
        {
            if (SetProperty(ref _newRuleResolvedPath, value))
                OnPropertyChanged(nameof(HasNewRuleResolvedPath));
        }
    }

    public bool HasNewRuleResolvedPath => NewRuleResolvedPath.Length > 0;

    public string NewRuleError
    {
        get => _newRuleError;
        private set
        {
            if (SetProperty(ref _newRuleError, value))
                OnPropertyChanged(nameof(HasNewRuleError));
        }
    }

    public bool HasNewRuleError => NewRuleError.Length > 0;

    public bool HasConflicts
    {
        get => _hasConflicts;
        private set => SetProperty(ref _hasConflicts, value);
    }

    public string ConflictSummary
    {
        get => _conflictSummary;
        private set => SetProperty(ref _conflictSummary, value);
    }

    public string ConflictDetails
    {
        get => _conflictDetails;
        private set => SetProperty(ref _conflictDetails, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
                OnPropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => StatusMessage.Length > 0;

    public string BulkText
    {
        get => _bulkText;
        set => SetProperty(ref _bulkText, value);
    }

    public string CountSummary
    {
        get => _countSummary;
        private set => SetProperty(ref _countSummary, value);
    }

    public string Filter
    {
        get => _filter;
        set
        {
            if (!SetProperty(ref _filter, value))
                return;
            StatusMessage = string.Empty;
            _view.Refresh();
        }
    }

    public void Save()
    {
        // Bulk edits are explicit: when a line cannot be parsed nothing is applied and the text stays in the
        // box, so a typo can never silently drop rules from the saved configuration.
        var bulkApplied = ApplyBulkText();
        _userSettings.IndexRules = Rules
            .Select(ToSetting)
            .ToList();
        // Keep the in-memory legacy lists in step with the rules so an immediate settings snapshot and a
        // downgrade both see the excluding subset.
        IndexRuleMigration.MirrorLegacy(_userSettings);
        if (bulkApplied)
            RefreshBulkText();
    }

    private bool FilterRule(object candidate) => candidate is ExclusionRuleItem item
        && (Filter == "All" || item.Effect.ToString().Equals(Filter, StringComparison.OrdinalIgnoreCase));

    private bool CanAddRule()
    {
        if (!TryReadNewRule(out var parsed, out _))
            return false;
        return !Rules.Any(rule => rule.Value.Equals(parsed.Text, StringComparison.OrdinalIgnoreCase));
    }

    private bool TryReadNewRule(out ParsedIndexRule parsed, out IndexRuleError error)
    {
        parsed = default;
        var (text, effect) = SplitEffectPrefix(NewRuleText, ParseEffect(NewRuleEffect));
        if (!IndexRuleSyntax.TryParse(text, effect, out parsed, out error))
            return false;
        if (parsed.Kind == IndexRuleKind.Path
            && !IndexRuleSyntax.TryResolvePath(parsed.Pattern, out _, out error))
        {
            return false;
        }
        return true;
    }

    private void UpdatePreview()
    {
        NewRuleResolvedPath = string.Empty;

        if (IndexRuleSyntax.Clean(NewRuleText).Length == 0)
        {
            NewRuleDetection = string.Empty;
            NewRuleError = string.Empty;
            return;
        }

        if (!TryReadNewRule(out var parsed, out var error))
        {
            NewRuleDetection = string.Empty;
            NewRuleError = ErrorText(error);
            return;
        }

        NewRuleDetection = string.Format(T("Exclusions_DetectedTemplate"), KindText(parsed.Kind));
        NewRuleError = string.Empty;

        if (parsed.Kind == IndexRuleKind.Path
            && IndexRuleSyntax.TryResolvePath(parsed.Pattern, out var resolved, out _))
        {
            NewRuleResolvedPath = string.Format(T("Exclusions_ResolvedTemplate"), resolved);
        }
    }

    private void AddRule()
    {
        if (!TryReadNewRule(out var parsed, out _))
            return;

        var text = parsed.Text;

        if (Rules.Any(rule => rule.Value.Equals(text, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = T("Exclusions_Duplicate");
            return;
        }

        StatusMessage = string.Empty;
        Rules.Add(new ExclusionRuleItem(text, parsed.Effect));
        NewRuleText = string.Empty;
        NewRuleDetection = string.Empty;
        NewRuleResolvedPath = string.Empty;
        NewRuleError = string.Empty;

        _view.Refresh();
        RefreshBulkText();
        RecomputeConflicts();
        RefreshCounts();

        if (Filter != "All" && !parsed.Effect.ToString().Equals(Filter, StringComparison.OrdinalIgnoreCase))
            StatusMessage = string.Format(T("Exclusions_MovedTemplate"), EffectText(parsed.Effect));
    }

    private void RemoveRule(ExclusionRuleItem? item)
    {
        if (item == null)
            return;

        Rules.Remove(item);
        _view.Refresh();
        RefreshBulkText();
        RecomputeConflicts();
        RefreshCounts();
    }

    private void BeginEdit(ExclusionRuleItem? item)
    {
        if (item == null || !Rules.Contains(item))
            return;

        foreach (var other in Rules.Where(other => other != item))
            other.IsEditing = false;

        item.EditValue = item.Value;
        item.EditEffect = item.Effect;
        item.IsEditing = true;
    }

    private void SaveEdit(ExclusionRuleItem? item)
    {
        if (item == null || !item.IsEditing)
            return;

        if (!IndexRuleSyntax.TryParse(item.EditValue, item.EditEffect, out var parsed, out var error))
        {
            StatusMessage = ErrorText(error);
            return;
        }

        if (parsed.Kind == IndexRuleKind.Path && !IndexRuleSyntax.TryResolvePath(parsed.Pattern, out _, out error))
        {
            StatusMessage = ErrorText(error);
            return;
        }

        if (Rules.Any(other => other != item && other.Value.Equals(parsed.Text, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = T("Exclusions_Duplicate");
            return;
        }

        var effectChanged = item.Effect != parsed.Effect;
        item.Value = parsed.Text;
        item.Effect = parsed.Effect;
        item.RefreshKind();
        item.IsEditing = false;
        StatusMessage = string.Empty;

        _view.Refresh();
        RefreshBulkText();
        RecomputeConflicts();
        RefreshCounts();

        if (effectChanged && Filter != "All" && !parsed.Effect.ToString().Equals(Filter, StringComparison.OrdinalIgnoreCase))
            StatusMessage = string.Format(T("Exclusions_MovedTemplate"), EffectText(parsed.Effect));
    }

    // One row-to-setting conversion, so the saved list and the conflict scan can never disagree about
    // which fields a rule carries.
    private static IndexRuleSetting ToSetting(ExclusionRuleItem rule) =>
        new() { Pattern = rule.Value, Effect = rule.Effect, AlwaysTop = rule.AlwaysTop };

    private void RecomputeConflicts()
    {
        var settings = Rules
            .Select(ToSetting)
            .ToList();
        var conflicts = IndexRuleConflictDetector.Detect(settings);

        foreach (var rule in Rules)
        {
            rule.IsConflict = false;
            rule.ConflictTooltip = string.Empty;
        }

        var details = new List<string>();
        foreach (var conflict in conflicts)
        {
            var priority = Rules[conflict.PriorityIndex];
            priority.IsConflict = true;
            priority.ConflictTooltip = string.Format(T("Exclusions_ConflictTooltip"), conflict.ExcludePattern);
            details.Add($"• {conflict.PriorityPattern}  ←  {conflict.ExcludePattern}");
        }

        HasConflicts = conflicts.Count > 0;
        ConflictSummary = conflicts.Count == 0
            ? string.Empty
            : string.Format(T("Exclusions_ConflictSummary"), conflicts.Count);
        ConflictDetails = string.Join(Environment.NewLine, details);
    }

    private void RefreshCounts()
    {
        var excluded = Rules.Count(rule => rule.Effect == IndexRuleEffect.Exclude);
        var low = Rules.Count(rule => rule.Effect == IndexRuleEffect.Low);
        var high = Rules.Count(rule => rule.Effect == IndexRuleEffect.High);
        var normal = Rules.Count - excluded - low - high;
        CountSummary = string.Format(T("Exclusions_CountSummary"), Rules.Count, excluded, low, normal, high);
    }

    private void RefreshBulkText() => BulkText = string.Join(Environment.NewLine, Rules.Select(FormatBulkLine));

    private bool ApplyBulkText()
    {
        var parsed = new List<ExclusionRuleItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var invalidLines = new List<int>();
        var lineNumber = 0;

        // The bulk box spells the level only -- no prefix exists for a pin -- so re-applying the text must
        // not silently unpin the high rules that are still there. A line whose text changed loses its pin,
        // which is the same outcome as deleting and re-adding the rule.
        var storedPins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in Rules)
            if (rule.AlwaysTop)
                storedPins.Add(rule.Value);

        foreach (var line in (BulkText ?? string.Empty).Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
        {
            lineNumber++;
            var (text, effect) = ParseBulkLine(line);
            if (text.Length == 0)
                continue;
            if (!IndexRuleSyntax.TryParse(text, effect, out var rule, out _))
            {
                invalidLines.Add(lineNumber);
                continue;
            }

            if (rule.Kind == IndexRuleKind.Path && !IndexRuleSyntax.TryResolvePath(rule.Pattern, out _, out _))
            {
                invalidLines.Add(lineNumber);
                continue;
            }

            if (!seen.Add(rule.Text))
                continue;

            parsed.Add(new ExclusionRuleItem(
                rule.Text,
                effect,
                effect == IndexRuleEffect.High && storedPins.Contains(rule.Text)));
        }

        // All or nothing: a half-applied paste is harder to notice than a refused one.
        if (invalidLines.Count > 0)
        {
            StatusMessage = string.Format(T("Exclusions_BulkInvalid"), string.Join(", ", invalidLines));
            return false;
        }

        Rules.Clear();
        foreach (var rule in parsed)
            Rules.Add(rule);

        StatusMessage = string.Empty;
        _view.Refresh();
        RecomputeConflicts();
        RefreshCounts();
        RefreshBulkText();
        return true;
    }

    private static (string Text, IndexRuleEffect Effect) ParseBulkLine(string line) =>
        SplitEffectPrefix(line, IndexRuleEffect.Normal);

    /// <summary>
    /// Splits the optional effect prefix used by both the bulk box and the input box: `!` exclude, `-` low,
    /// `+` high; a leading `\` escapes the prefix character. A fallback is used when no prefix is present.
    /// </summary>
    private static (string Text, IndexRuleEffect Effect) SplitEffectPrefix(string? line, IndexRuleEffect fallback)
    {
        var text = (line ?? string.Empty).Trim();
        var effect = fallback;

        if (text.StartsWith('!'))
        {
            effect = IndexRuleEffect.Exclude;
            text = text[1..];
        }
        else if (text.StartsWith('-'))
        {
            effect = IndexRuleEffect.Low;
            text = text[1..];
        }
        else if (text.StartsWith('+'))
        {
            effect = IndexRuleEffect.High;
            text = text[1..];
        }

        // Only strip the escape when it actually escapes a prefix character, so UNC paths such as
        // `\\server\share` keep both leading separators.
        if (text.Length >= 2 && text[0] == '\\' && text[1] is '!' or '-' or '+' or '\\')
            text = text[1..];

        return (text.Trim(), effect);
    }

    private static string FormatBulkLine(ExclusionRuleItem rule)
    {
        var prefix = rule.Effect switch
        {
            IndexRuleEffect.Exclude => "!",
            IndexRuleEffect.Low => "-",
            IndexRuleEffect.High => "+",
            _ => string.Empty,
        };

        var text = rule.Value.StartsWith('!') || rule.Value.StartsWith('-')
            || rule.Value.StartsWith('+') || rule.Value.StartsWith('\\')
            ? "\\" + rule.Value
            : rule.Value;

        return prefix + text;
    }

    private static IndexRuleEffect ParseEffect(string? value) => Enum.TryParse<IndexRuleEffect>(value, ignoreCase: true, out var effect)
        ? effect
        : IndexRuleEffect.Normal;

    private static string KindText(IndexRuleKind kind) => T(kind switch
    {
        IndexRuleKind.Path => "Exclusions_KindPath",
        IndexRuleKind.Regex => "Exclusions_KindRegex",
        _ => "Exclusions_KindGlob",
    });

    private static string EffectText(IndexRuleEffect effect) => T(effect switch
    {
        IndexRuleEffect.Exclude => "Exclusions_EffectExclude",
        IndexRuleEffect.Low => "Exclusions_EffectLow",
        IndexRuleEffect.High => "Exclusions_EffectHigh",
        _ => "Exclusions_EffectNormal",
    });

    private static string ErrorText(IndexRuleError error) => T(error switch
    {
        IndexRuleError.Empty => "Exclusions_ErrorEmpty",
        IndexRuleError.EmptyRegex => "Exclusions_ErrorEmptyRegex",
        IndexRuleError.InvalidRegex => "Exclusions_ErrorInvalidRegex",
        IndexRuleError.UnresolvedEnvironmentVariable => "Exclusions_ErrorUnresolvedEnvironment",
        IndexRuleError.VirtualPathWithoutFileSystem => "Exclusions_ErrorVirtualPath",
        IndexRuleError.InvalidPath => "Exclusions_ErrorInvalidPath",
        _ => "Exclusions_ErrorEmpty",
    });

    private static string T(string key) => TranslationManager.Instance[key];
}
