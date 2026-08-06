using System.Collections.ObjectModel;
using System.Windows.Input;

using Lertaro.App.Helpers;
namespace Lertaro.App.ViewModels.Settings;

/// <summary>
/// One editable list of process names: type-and-add, a row per entry, and the bulk text box for
/// pasting or exporting the lot. Extracted from <see cref="BlacklistSettingsViewModel"/> when the quick
/// panel's own list appeared beside the global one -- there is nothing about any of this that is
/// specific to which list is being edited, and the second one had no business being a bare text box
/// just because building the same thing twice was tedious.
/// </summary>
public class ProcessBlacklistEditorViewModel : ViewModelBase
{
    public ProcessBlacklistEditorViewModel(IEnumerable<string> initial)
    {
        foreach (var value in initial.Where(x => !string.IsNullOrWhiteSpace(x)))
            Items.Add(new BlacklistProcessItem(value));
        RefreshBulkText();

        AddProcessCommand = new RelayCommand(AddProcess, () => !string.IsNullOrWhiteSpace(NewProcessName));
        ApplyTextCommand = new RelayCommand(ApplyBulkText);
        ExportTextCommand = new RelayCommand(RefreshBulkText);
        RemoveProcessCommand = new RelayCommand<BlacklistProcessItem>(RemoveProcess);
        EditProcessCommand = new RelayCommand<BlacklistProcessItem>(EditProcess);
    }

    public ObservableCollection<BlacklistProcessItem> Items { get; } = new();

    public ICommand AddProcessCommand { get; }
    public ICommand ApplyTextCommand { get; }
    public ICommand ExportTextCommand { get; }
    public ICommand RemoveProcessCommand { get; }
    public ICommand EditProcessCommand { get; }

    private string _newProcessName = string.Empty;
    public string NewProcessName
    {
        get => _newProcessName;
        set
        {
            if (SetProperty(ref _newProcessName, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    private string _bulkText = string.Empty;
    public string BulkText
    {
        get => _bulkText;
        set => SetProperty(ref _bulkText, value);
    }

    /// <summary>The list as it should be stored: trimmed, blanks and repeats dropped.</summary>
    public List<string> ToSettingsList()
    {
        ApplyBulkText();
        return Items.Select(x => x.Value.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void AddProcess()
    {
        AddUnique(NewProcessName);
        NewProcessName = string.Empty;
        RefreshBulkText();
    }

    private void AddUnique(string value)
    {
        var normalized = value.Trim().Trim('"');
        if (normalized.Length == 0 || Items.Any(x => x.Value.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            return;
        Items.Add(new BlacklistProcessItem(normalized));
    }

    private void RemoveProcess(BlacklistProcessItem? item)
    {
        if (item == null)
            return;
        Items.Remove(item);
        RefreshBulkText();
    }

    // Edit is remove-and-refill: the row has no editor of its own, so the entry goes back into the box
    // it was typed in.
    private void EditProcess(BlacklistProcessItem? item)
    {
        if (item == null)
            return;
        NewProcessName = item.Value;
        Items.Remove(item);
        RefreshBulkText();
    }

    private void ApplyBulkText()
    {
        Items.Clear();
        foreach (var line in (BulkText ?? string.Empty).Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
            AddUnique(line);
        RefreshBulkText();
    }

    private void RefreshBulkText() => BulkText = string.Join(Environment.NewLine, Items.Select(x => x.Value));
}
