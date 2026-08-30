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
    private object? _localValueStore;

    public string PluginId { get; }
    public PluginConfigField SchemaField { get; }
    public UserSettings Settings { get; }

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
    public int MaxLength => SchemaField.MaxLength > 0 ? SchemaField.MaxLength : int.MaxValue;
    public bool IsSingleChar => SchemaField.MaxLength == 1;
    public double EditorWidth => IsSingleChar ? 48 : 180;
    public System.Windows.TextAlignment TextAlignment => IsSingleChar ? System.Windows.TextAlignment.Center : System.Windows.TextAlignment.Left;

    public void NotifyLanguageChanged()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(GroupName));
        OnPropertyChanged(nameof(Choices));
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
    public bool IsCustomControl => FieldType == ConfigFieldType.CustomControl;
    public object? CustomControl => SchemaField.CustomControl;
    public bool IsButton => FieldType == ConfigFieldType.Button;
    public ICommand ButtonClickCommand { get; }
    public bool HotkeyRequireModifier => SchemaField.RequireModifier;
    public bool IsIconField => SchemaField.Key.Equals("Icon", StringComparison.OrdinalIgnoreCase);
    public bool IsSimpleField => (IsBoolean || IsText || IsInteger || IsChoice || IsStringList || IsHotkey || IsFilePath || IsFolderPath || IsButton) && !IsCustomControl;

    public ObservableCollection<PluginConfigFieldViewModel> Children { get; } = new();
    public ObservableCollection<PluginConfigArrayItemViewModel> ArrayItems { get; } = new();

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
        AddCommand = new RelayCommand(_arraySupport.AddArrayItem);
        DuplicateCommand = new RelayCommand(_arraySupport.DuplicateArrayItem, () => SelectedArrayItem != null);
        ButtonClickCommand = new RelayCommand(() => SchemaField.OnClick?.Invoke());

        if (_onValueChanged == null)
        {
            LoadChildrenAndArrayItems();
        }
    }

    public void Commit()
    {
        // Nothing to store for a non-value row: a custom control is hosted UI, and a button runs
        // its OnClick delegate directly -- persisting either would write meaningless settings keys.
        if (IsCustomControl || IsButton) return;
        if (IsGroup)
        {
            foreach (var child in Children)
            {
                child.Commit();
            }
            return;
        }
        if (IsObject)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in Children)
            {
                child.Commit();
                dict[child.SchemaField.Key] = child.LocalValueStore;
            }
            _localValueStore = dict;
        }
        else if (IsArray)
        {
            var list = new List<object?>();
            foreach (var item in ArrayItems)
            {
                list.Add(item.GetValue());
            }
            _localValueStore = list;
        }

        if (_onValueChanged == null)
        {
            if (SchemaField.SetValue != null)
            {
                SchemaField.SetValue(LocalValueStore);
            }
            else if (IsStringList && LocalValueStore is System.Collections.IEnumerable en && !(LocalValueStore is string))
            {
                var cleaned = new List<string>();
                foreach (var item in en) { var s = item?.ToString()?.Trim(); if (!string.IsNullOrEmpty(s)) cleaned.Add(s); }
                Settings.SetPluginSetting(PluginId, SchemaField.Key, cleaned);
            }
            else
            {
                // A RequireNonEmpty field (e.g. a trigger keyword) left blank in the UI would otherwise
                // persist as "", silently making whatever depends on it unreachable rather than falling
                // back to a sane default -- force the schema's own DefaultValue back in at save time.
                var toSave = LocalValueStore;
                if (SchemaField.RequireNonEmpty && (toSave == null || (toSave is string s && string.IsNullOrWhiteSpace(s))))
                {
                    toSave = SchemaField.DefaultValue;
                    _localValueStore = toSave;
                    OnPropertyChanged(nameof(Value));
                }
                Settings.SetPluginSetting(PluginId, SchemaField.Key, toSave);
            }
        }
    }

    public void Reload()
    {
        _localValueStore = null;
        Children.Clear();
        ArrayItems.Clear();
        LoadChildrenAndArrayItems();
        OnPropertyChanged(nameof(Value));
    }

    private void LoadChildrenAndArrayItems()
    {
        if (IsGroup && SchemaField.SubFields != null)
        {
            foreach (var sf in SchemaField.SubFields)
            {
                var childVM = new PluginConfigFieldViewModel(PluginId, sf, Settings, null);
                Children.Add(childVM);
            }
        }
        else if (IsObject && SchemaField.SubFields != null)
        {
            _arraySupport.LoadObjectChildren();
        }
        else if (IsArray)
        {
            _arraySupport.LoadArrayItems();
        }
    }

    public void OnChildChanged()
    {
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
