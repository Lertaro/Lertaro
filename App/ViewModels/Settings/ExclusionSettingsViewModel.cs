using System.Collections.ObjectModel;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.Settings;

public class ExclusionSettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;
    private string _newExcludedPath = string.Empty;
    private string _newIgnoredGlob = string.Empty;
    private string _newIgnoredRegex = string.Empty;
    private string _excludedPathsText = string.Empty;
    private string _ignoredGlobsText = string.Empty;
    private string _ignoredRegexesText = string.Empty;

    public ExclusionSettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;
        foreach (var path in _userSettings.ExcludedPaths.Where(x => !string.IsNullOrWhiteSpace(x)))
            ExcludedPaths.Add(new ExclusionRuleItem(path));

        foreach (var glob in _userSettings.IgnoredPathGlobs.Where(x => !string.IsNullOrWhiteSpace(x)))
            IgnoredGlobs.Add(new ExclusionRuleItem(glob));

        foreach (var regex in _userSettings.IgnoredPathRegexes.Where(x => !string.IsNullOrWhiteSpace(x)))
            IgnoredRegexes.Add(new ExclusionRuleItem(regex));

        RefreshBulkText();

        AddPathCommand = new RelayCommand(AddPath, CanAddPath);
        AddGlobCommand = new RelayCommand(AddGlob, CanAddGlob);
        AddRegexCommand = new RelayCommand(AddRegex, CanAddRegex);
        ApplyPathsTextCommand = new RelayCommand(() => ApplyBulkText(ExcludedPaths, ExcludedPathsText));
        ApplyGlobsTextCommand = new RelayCommand(() => ApplyBulkText(IgnoredGlobs, IgnoredGlobsText));
        ApplyRegexesTextCommand = new RelayCommand(() => ApplyBulkText(IgnoredRegexes, IgnoredRegexesText));
        ExportPathsTextCommand = new RelayCommand(() => ExcludedPathsText = JoinLines(ExcludedPaths));
        ExportGlobsTextCommand = new RelayCommand(() => IgnoredGlobsText = JoinLines(IgnoredGlobs));
        ExportRegexesTextCommand = new RelayCommand(() => IgnoredRegexesText = JoinLines(IgnoredRegexes));
        RemovePathCommand = new RelayCommand<ExclusionRuleItem>(RemovePath);
        RemoveGlobCommand = new RelayCommand<ExclusionRuleItem>(RemoveGlob);
        RemoveRegexCommand = new RelayCommand<ExclusionRuleItem>(RemoveRegex);
        EditPathCommand = new RelayCommand<ExclusionRuleItem>(EditPath);
        EditGlobCommand = new RelayCommand<ExclusionRuleItem>(EditGlob);
        EditRegexCommand = new RelayCommand<ExclusionRuleItem>(EditRegex);
        SavePathCommand = new RelayCommand<ExclusionRuleItem>(item => SaveEdit(ExcludedPaths, item), item => CanSaveEdit(ExcludedPaths, item));
        SaveGlobCommand = new RelayCommand<ExclusionRuleItem>(item => SaveEdit(IgnoredGlobs, item), item => CanSaveEdit(IgnoredGlobs, item));
        SaveRegexCommand = new RelayCommand<ExclusionRuleItem>(item => SaveEdit(IgnoredRegexes, item), item => CanSaveEdit(IgnoredRegexes, item));
        CancelPathCommand = new RelayCommand<ExclusionRuleItem>(CancelEdit);
        CancelGlobCommand = new RelayCommand<ExclusionRuleItem>(CancelEdit);
        CancelRegexCommand = new RelayCommand<ExclusionRuleItem>(CancelEdit);
        SelectSubTabCommand = new RelayCommand<string>(tab => SelectedSubTab = tab);
    }

    // Sub-tab navigation for the "Exclusions" tab nested inside the merged Index settings page.
    private string _selectedSubTab = "Path";
    public string SelectedSubTab
    {
        get => _selectedSubTab;
        set => SetProperty(ref _selectedSubTab, value);
    }
    public ICommand SelectSubTabCommand { get; }

    public ObservableCollection<ExclusionRuleItem> ExcludedPaths { get; } = new();
    public ObservableCollection<ExclusionRuleItem> IgnoredGlobs { get; } = new();
    public ObservableCollection<ExclusionRuleItem> IgnoredRegexes { get; } = new();

    public ICommand AddPathCommand { get; }
    public ICommand AddGlobCommand { get; }
    public ICommand AddRegexCommand { get; }
    public ICommand ApplyPathsTextCommand { get; }
    public ICommand ApplyGlobsTextCommand { get; }
    public ICommand ApplyRegexesTextCommand { get; }
    public ICommand ExportPathsTextCommand { get; }
    public ICommand ExportGlobsTextCommand { get; }
    public ICommand ExportRegexesTextCommand { get; }
    public ICommand RemovePathCommand { get; }
    public ICommand RemoveGlobCommand { get; }
    public ICommand RemoveRegexCommand { get; }
    public ICommand EditPathCommand { get; }
    public ICommand EditGlobCommand { get; }
    public ICommand EditRegexCommand { get; }
    public ICommand SavePathCommand { get; }
    public ICommand SaveGlobCommand { get; }
    public ICommand SaveRegexCommand { get; }
    public ICommand CancelPathCommand { get; }
    public ICommand CancelGlobCommand { get; }
    public ICommand CancelRegexCommand { get; }

    public string NewExcludedPath
    {
        get => _newExcludedPath;
        set
        {
            if (SetProperty(ref _newExcludedPath, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public string NewIgnoredGlob
    {
        get => _newIgnoredGlob;
        set
        {
            if (SetProperty(ref _newIgnoredGlob, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public string NewIgnoredRegex
    {
        get => _newIgnoredRegex;
        set
        {
            if (SetProperty(ref _newIgnoredRegex, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public string ExcludedPathsText
    {
        get => _excludedPathsText;
        set => SetProperty(ref _excludedPathsText, value);
    }

    public string IgnoredGlobsText
    {
        get => _ignoredGlobsText;
        set => SetProperty(ref _ignoredGlobsText, value);
    }

    public string IgnoredRegexesText
    {
        get => _ignoredRegexesText;
        set => SetProperty(ref _ignoredRegexesText, value);
    }

    public void Save()
    {
        ApplyBulkText(ExcludedPaths, ExcludedPathsText);
        ApplyBulkText(IgnoredGlobs, IgnoredGlobsText);
        ApplyBulkText(IgnoredRegexes, IgnoredRegexesText);

        _userSettings.ExcludedPaths = NormalizeItems(ExcludedPaths);
        _userSettings.IgnoredPathGlobs = NormalizeItems(IgnoredGlobs);
        _userSettings.IgnoredPathRegexes = NormalizeItems(IgnoredRegexes);
        RefreshBulkText();
    }

    private bool CanAddPath() => !string.IsNullOrWhiteSpace(NewExcludedPath);
    private bool CanAddGlob() => !string.IsNullOrWhiteSpace(NewIgnoredGlob);
    private bool CanAddRegex() => !string.IsNullOrWhiteSpace(NewIgnoredRegex);

    private void AddPath()
    {
        AddUnique(ExcludedPaths, NewExcludedPath);
        NewExcludedPath = string.Empty;
        RefreshBulkText();
    }

    private void AddGlob()
    {
        AddUnique(IgnoredGlobs, NewIgnoredGlob);
        NewIgnoredGlob = string.Empty;
        RefreshBulkText();
    }

    private void AddRegex()
    {
        AddUnique(IgnoredRegexes, NewIgnoredRegex);
        NewIgnoredRegex = string.Empty;
        RefreshBulkText();
    }

    private static void AddUnique(ObservableCollection<ExclusionRuleItem> items, string value)
    {
        var normalized = value.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        if (items.Any(x => x.Value.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            return;

        items.Add(new ExclusionRuleItem(normalized));
    }

    private void RemovePath(ExclusionRuleItem item)
    {
        if (item != null)
        {
            ExcludedPaths.Remove(item);
            RefreshBulkText();
        }
    }

    private void RemoveGlob(ExclusionRuleItem item)
    {
        if (item != null)
        {
            IgnoredGlobs.Remove(item);
            RefreshBulkText();
        }
    }

    private void RemoveRegex(ExclusionRuleItem item)
    {
        if (item != null)
        {
            IgnoredRegexes.Remove(item);
            RefreshBulkText();
        }
    }

    private void EditPath(ExclusionRuleItem item) => BeginEdit(ExcludedPaths, item);

    private void EditGlob(ExclusionRuleItem item) => BeginEdit(IgnoredGlobs, item);

    private void EditRegex(ExclusionRuleItem item) => BeginEdit(IgnoredRegexes, item);

    private static void BeginEdit(ObservableCollection<ExclusionRuleItem> items, ExclusionRuleItem? item)
    {
        if (item == null || !items.Contains(item))
            return;

        foreach (var other in items.Where(other => other != item))
            other.IsEditing = false;
        item.EditValue = item.Value;
        item.IsEditing = true;
    }
    private static bool CanSaveEdit(ObservableCollection<ExclusionRuleItem> items, ExclusionRuleItem? item)
    {
        if (item == null)
            return false;

        var value = NormalizeRule(item.EditValue);
        return value.Length > 0 && !items.Any(other => other != item
            && other.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
    }
    private void SaveEdit(ObservableCollection<ExclusionRuleItem> items, ExclusionRuleItem item)
    {
        if (!CanSaveEdit(items, item))
            return;

        item.Value = NormalizeRule(item.EditValue);
        item.IsEditing = false;
        RefreshBulkText();
    }
    private static void CancelEdit(ExclusionRuleItem item) => item?.IsEditing = false;
    private static string NormalizeRule(string value) => value.Trim().Trim('"');

    private void RefreshBulkText()
    {
        ExcludedPathsText = JoinLines(ExcludedPaths);
        IgnoredGlobsText = JoinLines(IgnoredGlobs);
        IgnoredRegexesText = JoinLines(IgnoredRegexes);
    }

    private static string JoinLines(ObservableCollection<ExclusionRuleItem> items) => string.Join(Environment.NewLine, items.Select(x => x.Value));

    private static List<string> NormalizeItems(ObservableCollection<ExclusionRuleItem> items) => items
            .Select(x => x.Value.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<string> ParseLines(string text) => (text ?? string.Empty)
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
            .Select(x => x.Trim().Trim('"'))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void SyncCollection(ObservableCollection<ExclusionRuleItem> target, IReadOnlyList<string> values)
    {
        target.Clear();
        foreach (var value in values)
            target.Add(new ExclusionRuleItem(value));
    }

    private void ApplyBulkText(ObservableCollection<ExclusionRuleItem> target, string text)
    {
        SyncCollection(target, ParseLines(text));
        RefreshBulkText();
    }
}
