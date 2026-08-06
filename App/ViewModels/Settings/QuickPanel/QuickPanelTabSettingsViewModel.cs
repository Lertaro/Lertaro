using System.Collections.ObjectModel;
using System.Windows.Input;

using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.Core;
namespace Lertaro.App.ViewModels.Settings.QuickPanel;

/// <summary>
/// One workspace being edited: its name and its source list, in the order the panel will show them.
/// The list is the single place every source appears -- folders the user added and the two built-in
/// ones alike -- so reordering, hiding and renaming are one gesture each rather than three lists to
/// keep in sync.
/// </summary>
public class QuickPanelTabSettingsViewModel : ViewModelBase
{
    private readonly QuickPanelTab _model;

    public QuickPanelTabSettingsViewModel(QuickPanelTab model)
    {
        _model = model;
        _name = model.Name;
        _enabled = model.Enabled;
        Processes = new ProcessBlacklistEditorViewModel(model.Processes);

        foreach (var id in QuickPanelGroupOrdering.Resolve(AvailableIds(model), model.GroupOrder, disabled: null))
            Sources.Add(BuildRow(model, id));

        AddFolderCommand = new RelayCommand(AddFolder);
        RemoveSourceCommand = new RelayCommand<QuickPanelSourceRowViewModel>(RemoveSource);
        MoveUpCommand = new RelayCommand<QuickPanelSourceRowViewModel>(row => Move(row, -1));
        MoveDownCommand = new RelayCommand<QuickPanelSourceRowViewModel>(row => Move(row, +1));
    }

    public string Id => _model.Id;

    private string _name;

    /// <summary>Empty falls back to a translated name, so an untouched tab follows the UI language.</summary>
    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
                OnPropertyChanged(nameof(EffectiveName));
        }
    }

    public string EffectiveName => string.IsNullOrWhiteSpace(Name)
        ? TranslationManager.Instance["QuickPanel_DefaultTabName"]
        : Name.Trim();

    /// <summary>Re-reads the fallback name after a language switch, for a tab that has no name of its own.</summary>
    public void NotifyLanguageChanged() => OnPropertyChanged(nameof(EffectiveName));

    private bool _enabled;

    /// <summary>Off keeps the workspace configured but gives it no tab in the panel.</summary>
    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public ICommand MoveUpSelfCommand { get; private set; } = null!;
    public ICommand MoveDownSelfCommand { get; private set; } = null!;
    public ICommand RemoveSelfCommand { get; private set; } = null!;

    /// <summary>
    /// Lets a workspace row carry its own reorder/delete buttons, the way the plugin array editor's
    /// master list does, instead of a toolbar under the list acting on whatever is selected. Wired by
    /// the page, which owns the list these operate on.
    /// </summary>
    internal void BindRowCommands(ICommand moveUp, ICommand moveDown, ICommand remove)
    {
        MoveUpSelfCommand = moveUp;
        MoveDownSelfCommand = moveDown;
        RemoveSelfCommand = remove;
    }

    /// <summary>
    /// The apps this workspace belongs to. The same editor the hotkey blacklist uses -- it is the same
    /// job (a list of process names), so it gets the same type-and-add list rather than a bare box.
    /// </summary>
    public ProcessBlacklistEditorViewModel Processes { get; }

    public ObservableCollection<QuickPanelSourceRowViewModel> Sources { get; } = new();

    public ICommand AddFolderCommand { get; }

    public ICommand RemoveSourceCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    private bool HasSource(string id) => Sources.Any(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Stages everything back into the settings object this was built from.</summary>
    public void Save()
    {
        _model.Name = Name.Trim();
        _model.Enabled = Enabled;
        _model.Processes = Processes.ToSettingsList();

        foreach (var row in Sources)
            row.SaveFolderFields();

        // Rebuilt from the rows rather than patched: the list IS the order, and a folder removed here has
        // to leave the list it came from too.
        _model.Folders = Sources.Select(r => _model.Folders.First(f => f.Id == r.Id)).ToList();
        _model.GroupOrder = Sources.Select(r => r.Id).ToList();
        _model.DisabledGroupIds = Sources.Where(r => !r.IsVisible).Select(r => r.Id).ToList();

        // Only what the user actually overrode: a row left at its defaults gets no entry at all. What is
        // left of a preference (sort, expanded) is the panel's own to write, so an entry that already
        // exists is patched rather than replaced.
        foreach (var row in Sources)
        {
            var custom = row.DisplayName.Trim();
            if (!_model.GroupPreferences.TryGetValue(row.Id, out var preference))
            {
                if (custom.Length == 0 && !row.ShowAsList)
                    continue;
                _model.GroupPreferences[row.Id] = preference = new QuickPanelGroupPreference();
            }
            preference.DisplayName = custom;
            preference.ThumbnailView = !row.ShowAsList;
        }

        // A source deleted here leaves nothing behind to accumulate in the settings file.
        var live = new HashSet<string>(Sources.Select(r => r.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var stale in _model.GroupPreferences.Keys.Where(k => !live.Contains(k)).ToList())
            _model.GroupPreferences.Remove(stale);
    }

    private void AddFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Multiselect = true };
        if (dialog.ShowDialog() != true)
            return;

        AddFolders(dialog.FolderNames);
    }

    /// <summary>Adds folders as sources, skipping any this workspace already has.</summary>
    /// <remarks>
    /// A picked folder starts with files only: folders make a panel harder to scan and are one dropdown
    /// away when they are wanted. Recently changed files remains an explicit alternative because it can
    /// leave a folder full of files showing nothing when none of them changed recently.
    ///
    /// The workspace a fresh install starts with is not this: Desktop, Downloads and Documents are
    /// recent-files there deliberately, being places things arrive rather than places things are kept.
    /// </remarks>
    internal void AddFolders(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Sources.Any(r => r.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                continue;
            var folder = QuickPanelFolderSource.For(path, QuickPanelSourceKind.FilesOnly);
            _model.Folders.Add(folder);
            Sources.Add(QuickPanelSourceRowViewModel.ForFolder(folder));
        }
    }

    private void RemoveSource(QuickPanelSourceRowViewModel? row)
    {
        if (row == null)
            return;
        Sources.Remove(row);
        _model.Folders.RemoveAll(f => f.Id == row.Id);
    }

    private void Move(QuickPanelSourceRowViewModel? row, int delta)
    {
        if (row == null)
            return;
        var from = Sources.IndexOf(row);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= Sources.Count)
            return;
        Sources.Move(from, to);
    }

    private static IEnumerable<string> AvailableIds(QuickPanelTab model) => model.Folders.Select(f => f.Id);

    private static QuickPanelSourceRowViewModel BuildRow(QuickPanelTab model, string id)
    {
        var row = QuickPanelSourceRowViewModel.ForFolder(model.Folders.First(f => f.Id == id));
        row.IsVisible = !model.DisabledGroupIds.Contains(id, StringComparer.OrdinalIgnoreCase);
        if (model.GroupPreferences.TryGetValue(id, out var preference))
        {
            row.DisplayName = preference.DisplayName;
            row.ShowAsList = !preference.ThumbnailView;
        }
        return row;
    }
}
