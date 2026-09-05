using System.Collections.ObjectModel;
using System.Windows.Input;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.App.Helpers;

namespace Lertaro.App.ViewModels.Settings.Plugins;

public class PluginConfigArrayItemViewModel : ViewModelBase
{
    private readonly PluginConfigFieldViewModel _parent;

    public ObservableCollection<PluginConfigFieldViewModel> Children { get; } = new();

    public PluginConfigFieldViewModel? SimpleValueViewModel { get; }

    public ICommand DeleteCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    // Master-list summary: best-effort picks from the item's own sub-fields, since the schema
    // varies per plugin (there's no dedicated "name"/"keyword" concept in PluginConfigField).
    public PluginConfigFieldViewModel? TitleField => Children.FirstOrDefault(c => c.FieldType == ConfigFieldType.Text);
    public PluginConfigFieldViewModel? BadgeField => Children
        .Where(c => c.FieldType == ConfigFieldType.Text && !c.IsIconField)
        .Skip(1)
        .FirstOrDefault();
    public PluginConfigFieldViewModel? IconField => Children.FirstOrDefault(c => c.IsIconField);

    public PluginConfigArrayItemViewModel(PluginConfigFieldViewModel parent, object? initialValue, Action onDelete, Action? onMoveUp = null, Action? onMoveDown = null)
    {
        _parent = parent;
        DeleteCommand = new RelayCommand(onDelete);
        MoveUpCommand = new RelayCommand(onMoveUp ?? (() => { }));
        MoveDownCommand = new RelayCommand(onMoveDown ?? (() => { }));

        var subFields = parent.SchemaField.SubFields;
        if (subFields != null && subFields.Count > 0)
        {
            var rawVal = ConfigValueHelper.UnpackValue(initialValue);
            var dict = rawVal as Dictionary<string, object>
                       ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            foreach (var sf in subFields)
            {
                dict.TryGetValue(sf.Key, out var val);
                var valToUse = ConfigValueHelper.UnpackValue(val ?? sf.DefaultValue);

                var subFieldVM = new PluginConfigFieldViewModel(parent.PluginId, sf, parent.Settings, () => parent.OnChildChanged())
                {
                    LocalValueStore = valToUse
                };
                Children.Add(subFieldVM);
            }
        }
        else
        {
            var sf = new PluginConfigField
            {
                Key = "value",
                LabelKey = string.Empty,
                DescriptionKey = string.Empty,
                FieldType = MapTypeToFieldType(parent.SchemaField.DefaultValue),
                DefaultValue = string.Empty
            };

            SimpleValueViewModel = new PluginConfigFieldViewModel(parent.PluginId, sf, parent.Settings, () => parent.OnChildChanged())
            {
                LocalValueStore = ConfigValueHelper.UnpackValue(initialValue) ?? string.Empty
            };
        }
    }

    private static ConfigFieldType MapTypeToFieldType(object defaultValue)
    {
        if (defaultValue is bool) return ConfigFieldType.Boolean;
        if (defaultValue is int || defaultValue is long || defaultValue is double) return ConfigFieldType.Integer;
        return ConfigFieldType.Text;
    }

    public object? GetValue()
    {
        if (SimpleValueViewModel != null)
        {
            return SimpleValueViewModel.LocalValueStore;
        }

        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in Children)
        {
            dict[child.SchemaField.Key] = child.LocalValueStore;
        }
        return dict;
    }
}
