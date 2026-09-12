using System.Collections.ObjectModel;
using System.Windows.Input;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;
using Lertaro.App.Helpers;

namespace Lertaro.App.ViewModels.Settings.Plugins;

public class PluginConfigFieldViewModel : ViewModelBase
{
    private readonly Action? _onValueChanged;
    private readonly PluginConfigArrayFieldSupport _arraySupport;
    private readonly PluginConfigFieldLoadSupport _loadSupport;
    private object? _localValueStore;

    public string PluginId { get; }
    public PluginConfigField SchemaField { get; }
    public UserSettings Settings { get; }

    internal bool HasValueChangedCallback => _onValueChanged != null;
    internal PluginConfigArrayFieldSupport ArraySupport => _arraySupport;

    private static string ResolveText(string? keyOrText)
    {
        if (string.IsNullOrEmpty(keyOrText)) return string.Empty;
        if (TranslationService.TryGet(keyOrText, out var translated))
            return translated;
        return keyOrText;
    }

    public string Label => ResolveText(SchemaField.LabelKey);
    public string Description => ResolveText(SchemaField.DescriptionKey);
    public string GroupKey => SchemaField.GroupKey;
    public string GroupName => ResolveText(GroupKey);
    public ConfigFieldType FieldType => SchemaField.FieldType;
    public List<string>? Choices => SchemaField.Choices?.Select(ResolveText).ToList();
    public IReadOnlyList<PluginConfigChoiceItem> ChoiceItems => SchemaField.ChoiceOptions != null
        ? SchemaField.ChoiceOptions.Select(choice => new PluginConfigChoiceItem(choice.Value, ResolveText(choice.LabelKey))).ToList()
        : SchemaField.Choices?.Select(choice => new PluginConfigChoiceItem(choice, ResolveText(choice))).ToList() ?? [];
    public int MaxLength => SchemaField.MaxLength > 0 ? SchemaField.MaxLength : int.MaxValue;
    public int SelectionStart => SchemaField.SelectionStart;
    public int SelectionLength => SchemaField.SelectionLength;
    public bool HasLengthLimit => SchemaField.MaxLength > 0;
    public bool IsSingleChar => SchemaField.MaxLength == 1;
    public double EditorWidth => IsSingleChar ? 48 : 180;
    public System.Windows.TextAlignment TextAlignment => IsSingleChar ? System.Windows.TextAlignment.Center : System.Windows.TextAlignment.Left;

    public void NotifyLanguageChanged()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(GroupName));
        OnPropertyChanged(nameof(Choices));
        OnPropertyChanged(nameof(ChoiceItems));
    }

    public bool IsBoolean => FieldType == ConfigFieldType.Boolean;
    public bool IsText => FieldType == ConfigFieldType.Text;
    public bool IsInteger => FieldType == ConfigFieldType.Integer;
    public bool IsChoice => FieldType == ConfigFieldType.Choice;
    public bool IsArray => FieldType == ConfigFieldType.Array;
    // Object arrays (SubFields present) render as a master/detail list; scalar arrays (a plain
    // list of strings/numbers/bools) render as a single-column compact list -- neither currently
    // occurs without the other, but a plugin's schema decides which at declaration time.
    public bool IsObjectArray => IsArray && SchemaField.SubFields is { Count: > 0 };
    public bool IsScalarArray => IsArray && !IsObjectArray;
    public bool IsObject => FieldType == ConfigFieldType.Object;
    public bool IsGroup => FieldType == ConfigFieldType.Group;
    public bool IsStringList => FieldType == ConfigFieldType.StringList;
    public bool IsHotkey => FieldType == ConfigFieldType.Hotkey;
    public bool IsFilePath => FieldType == ConfigFieldType.FilePath;
    public bool IsFolderPath => FieldType == ConfigFieldType.FolderPath;
    public bool SupportsInlineEditing => IsText || IsInteger || IsStringList || IsFilePath || IsFolderPath;
    public bool IsCustomControl => FieldType == ConfigFieldType.CustomControl;
    public object? CustomControl => SchemaField.CustomControl;
    public bool IsButton => FieldType == ConfigFieldType.Button;
    public ICommand ButtonClickCommand { get; }
    public bool HotkeyRequireModifier => SchemaField.RequireModifier;
    public bool IsIconField => SchemaField.Key.Equals("Icon", StringComparison.OrdinalIgnoreCase);
    public bool IsSimpleField => (IsBoolean || IsText || IsInteger || IsChoice || IsStringList || IsHotkey || IsFilePath || IsFolderPath || IsButton) && !IsCustomControl;

    // Children/ArrayItems are populated on first access rather than in the constructor -- see
    // PluginConfigFieldLoadSupport for why, and for the load itself. The collections it hands back are
    // the same instances throughout, so XAML bindings stay valid across the load.
    public ObservableCollection<PluginConfigFieldViewModel> Children => _loadSupport.Children;
    public ObservableCollection<PluginConfigArrayItemViewModel> ArrayItems => _loadSupport.ArrayItems;

    /// <summary>
    /// Whether this field (or, for a container, any of its rows) holds an edit the user made since the
    /// last commit. Sorting out "was it ever shown" is the load support's job -- see its own docs for why
    /// this must not materialize an unbuilt tree.
    /// </summary>
    internal bool IsDirty => _loadSupport.IsDirty;

    /// <summary>Clears this field's own staged flag without touching its value.</summary>
    internal void ClearDirty() => _loadSupport.ClearDirty();

    /// <summary>Drops staged child/array state so the next access re-reads it from settings.</summary>
    public void ResetChildrenAndArrayItems() => _loadSupport.Reset();

    /// <summary>
    /// Whether this field's child rows are currently materialized, checked WITHOUT building them (see
    /// PluginConfigFieldLoadSupport.HasLoadedChildren for why the Children getter cannot answer this).
    /// </summary>
    internal bool HasLoadedChildren => _loadSupport.HasLoadedChildren;

    /// <summary>
    /// Discards staged edits and drops the rows. Used by Cancel (and any rollback), where the staged
    /// values must not survive; a later access rebuilds the rows lazily from settings.
    /// </summary>
    internal void Discard()
    {
        _localValueStore = null;
        ResetChildrenAndArrayItems();
    }

    /// <summary>
    /// Drops the rows when this field holds no staged edit, keeping them otherwise.
    /// </summary>
    /// <remarks>
    /// Used when the config tab is left. The rows are the only place staged edits live, so a field the
    /// user actually edited must keep its tree -- dropping it there would lose the edit that this whole
    /// change exists to preserve. A clean field's tree carries nothing the lazy getters cannot rebuild
    /// identically, so it is dropped to keep the "visited but untouched" plugins cheap.
    /// </remarks>
    internal void DiscardRowsIfClean()
    {
        if (IsDirty) return;
        Discard();
    }

    /// <summary>
    /// Re-materializes this field's rows when they were dropped and no edit is staged, for when the
    /// config tab is shown again.
    /// </summary>
    /// <remarks>
    /// A dropped field's bound control does not re-read the (stable) collection property, so the rows
    /// have to be rebuilt before they are shown again. A field that IS dirty is skipped: its rows were
    /// deliberately kept (see <see cref="DiscardRowsIfClean"/>), and rebuilding would recreate them from
    /// settings and throw the staged edit away -- the exact loss this change exists to prevent.
    /// </remarks>
    internal void RebuildRowsIfClean()
    {
        if (IsDirty) return;
        Reload();
    }

    /// <summary>
    /// Discards staged state AND rebuilds whatever had been loaded, for when the fields are about to be
    /// shown again (and for a rollback of a still-visible tab). <see cref="Discard"/> alone leaves the
    /// (stable) collections empty, and a control already bound to them does not re-read the property, so
    /// the rebuild has to happen before showing.
    /// </summary>
    public void Reload()
    {
        _localValueStore = null;
        _loadSupport.Reload();
        OnPropertyChanged(nameof(Value));
    }

    // The array item shown in the master/detail editor's right-hand panel.
    private PluginConfigArrayItemViewModel? _selectedArrayItem;
    public PluginConfigArrayItemViewModel? SelectedArrayItem
    {
        get => _selectedArrayItem;
        set => SetProperty(ref _selectedArrayItem, value);
    }

    public ICommand AddCommand { get; }

    /// <summary>Copies the selected array item, for entries that differ in one field.</summary>
    public ICommand DuplicateCommand { get; }

    public object? LocalValueStore
    {
        get
        {
            if (_localValueStore == null)
            {
                if (IsGroup || IsCustomControl)
                {
                    _localValueStore = null;
                }
                else if (_onValueChanged != null)
                {
                    _localValueStore = SchemaField.DefaultValue;
                }
                else if (SchemaField.GetValue != null)
                {
                    _localValueStore = ConfigValueHelper.UnpackValue(SchemaField.GetValue() ?? SchemaField.DefaultValue);
                }
                else
                {
                    _localValueStore = ConfigValueHelper.UnpackValue(Settings.GetPluginSetting(PluginId, SchemaField.Key, SchemaField.DefaultValue));
                }
            }
            return _localValueStore;
        }
        set
        {
            _localValueStore = ConfigValueHelper.UnpackValue(value);
            OnPropertyChanged(nameof(Value));
            _onValueChanged?.Invoke();
        }
    }

    public object? Value
    {
        get
        {
            if (IsObject || IsArray || IsGroup) return this;
            if (IsStringList)
            {
                if (LocalValueStore is System.Collections.IEnumerable en && !(LocalValueStore is string))
                {
                    var items = new List<string>();
                    foreach (var item in en) items.Add(item?.ToString() ?? string.Empty);
                    return string.Join("\r\n", items);
                }
                return LocalValueStore?.ToString() ?? string.Empty;
            }
            return LocalValueStore;
        }
        set
        {
            if (IsStringList && value is string strVal)
                LocalValueStore = strVal.Split('\n').Select(s => s.TrimEnd('\r').Trim()).ToList();
            else
                LocalValueStore = ConfigValueHelper.ConvertValue(value, FieldType);
            // Only the public Value setter is the user-edit path (XAML two-way bindings and the clear
            // buttons); the load paths write LocalValueStore directly, so staging a value this way is
            // what marks the field (and therefore its plugin) as having something to save.
            _loadSupport.MarkDirty();
            if (_onValueChanged == null) OnPropertyChanged();
        }
    }

    public PluginConfigFieldViewModel(string pluginId, PluginConfigField field, UserSettings settings, Action? onValueChanged = null)
    {
        PluginId = pluginId;
        SchemaField = field;
        Settings = settings;
        _onValueChanged = onValueChanged;
        _arraySupport = new PluginConfigArrayFieldSupport(this);
        _loadSupport = new PluginConfigFieldLoadSupport(this);
        AddCommand = new RelayCommand(_arraySupport.AddArrayItem);
        DuplicateCommand = new RelayCommand(_arraySupport.DuplicateArrayItem, () => SelectedArrayItem != null);
        ButtonClickCommand = new RelayCommand(() => SchemaField.OnClick?.Invoke());
        // No eager child/array build here -- see Children/ArrayItems. The constructor is now cheap, which
        // is what lets the whole plugin list be built without paying for every plugin's config tree.
    }

    public void Commit() => PluginConfigFieldCommitSupport.Commit(this);

    /// <summary>
    /// Sets the staged value without the Value setter's change notification or dirty-marking, for
    /// PluginConfigFieldCommitSupport re-serializing an Object/Array field's rows back into it.
    /// </summary>
    internal void SetRawLocalValue(object? value) => _localValueStore = value;

    /// <summary>Raises the Value notification for PluginConfigFieldCommitSupport's RequireNonEmpty fixup.</summary>
    internal void NotifyValueChanged() => OnPropertyChanged(nameof(Value));

    public void OnChildChanged()
    {
        // A child row edited inside an Object/Array container is an edit to this container, which the
        // parent plugin tracks through this field's IsDirty.
        _loadSupport.MarkDirty();
        if (IsArray) _arraySupport.SaveArrayFromChildren();
        else if (IsObject) _arraySupport.SaveObjectFromChildren();
        else _onValueChanged?.Invoke();
    }

    // Lets PluginConfigArrayFieldSupport re-serialize Children/ArrayItems back into this field's
    // stored value without exposing the raw backing field or the protected change-notification API.
    internal void CommitLocalValue(object? rawValue)
    {
        _localValueStore = rawValue;
        OnPropertyChanged(nameof(Value));
        _onValueChanged?.Invoke();
    }
}
