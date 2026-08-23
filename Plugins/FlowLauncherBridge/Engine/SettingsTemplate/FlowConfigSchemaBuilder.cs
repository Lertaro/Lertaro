using System.IO;
using System.Windows;
using System.Windows.Controls;
using Flow.Launcher.Plugin;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.SettingsTemplate;

/// <summary>
/// Converts Flow.Launcher plugin setting definitions (from SettingsTemplate.yaml or ISettingProvider)
/// into standard Lertaro PluginConfigSchema data models.
/// </summary>
public static class FlowConfigSchemaBuilder
{
    public static PluginConfigSchema BuildSchema(FlowPluginHost host)
    {
        var schema = new PluginConfigSchema();

        // Top-level global trigger keyword setting
        schema.Fields.Add(new PluginConfigField
        {
            Key = "TriggerKeyword",
            GroupKey = string.Empty,
            LabelKey = "FlowLauncherBridge_Config_TriggerKeywordLabel",
            DescriptionKey = "FlowLauncherBridge_Config_TriggerKeywordDesc",
            FieldType = ConfigFieldType.Text,
            DefaultValue = "flow",
            RequireNonEmpty = true,
            MaxLength = 10
        });

        foreach (var pair in host.GetAllPlugins())
        {
            var pluginName = !string.IsNullOrEmpty(pair.Metadata.Name) ? pair.Metadata.Name : (pair.Metadata.ID ?? string.Empty);
            var yamlPath = Path.Combine(pair.Metadata.PluginDirectory, "SettingsTemplate.yaml");
            var jsonPath = Path.Combine(pair.Metadata.PluginDirectory, "SettingsTemplate.json");
            var templatePath = File.Exists(yamlPath) ? yamlPath : (File.Exists(jsonPath) ? jsonPath : null);

            var pluginFields = new List<PluginConfigField>();

            if (templatePath != null)
            {
                var baseDir = PluginSdk.Services.UserDataService.GetUserDataDirectory()
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lertaro");
                var settingsPath = Path.Combine(baseDir, "FlowData", "Settings", pluginName, "Settings.json");

                var doc = FlowSettingsTemplateParser.ParseFile(templatePath);
                foreach (var elem in doc.Elements)
                {
                    var field = ConvertElementToField(pluginName, elem, settingsPath);
                    if (field != null)
                    {
                        pluginFields.Add(field);
                    }
                }
            }
            else if (pair.Plugin is ISettingProvider settingProvider)
            {
                var panel = CreatePanelSafe(settingProvider);
                if (panel != null)
                {
                    pluginFields.Add(new PluginConfigField
                    {
                        Key = $"{pluginName}.CustomPanel",
                        GroupKey = pluginName,
                        LabelKey = pluginName,
                        FieldType = ConfigFieldType.CustomControl,
                        CustomControl = panel
                    });
                }
            }

            if (pluginFields.Count > 0)
            {
                schema.Fields.Add(new PluginConfigField
                {
                    Key = $"{pluginName}Group",
                    LabelKey = pluginName,
                    GroupKey = pluginName,
                    FieldType = ConfigFieldType.Group,
                    SubFields = pluginFields
                });
            }
        }

        schema.OnSave = () => host.SaveAll();
        schema.OnRollback = () => host.RollbackAll();

        return schema;
    }

    private static PluginConfigField? ConvertElementToField(
        string groupName,
        FlowSettingsTemplateElement elem,
        string settingsPath)
    {
        var type = elem.Type.ToLowerInvariant();
        var key = $"{groupName}.{elem.Name}";
        var label = !string.IsNullOrEmpty(elem.Label) ? elem.Label : elem.Name;

        var field = type switch
        {
            "checkbox" => new PluginConfigField
            {
                Key = key,
                GroupKey = groupName,
                LabelKey = label,
                DescriptionKey = elem.Description,
                FieldType = ConfigFieldType.Boolean,
                DefaultValue = bool.TryParse(elem.DefaultValue, out var b) && b
            },
            "input" or "txtbox" or "textbox" => new PluginConfigField
            {
                Key = key,
                GroupKey = groupName,
                LabelKey = label,
                DescriptionKey = elem.Description,
                FieldType = ConfigFieldType.Text,
                DefaultValue = elem.DefaultValue ?? string.Empty
            },
            "select" or "dropdown" => new PluginConfigField
            {
                Key = key,
                GroupKey = groupName,
                LabelKey = label,
                DescriptionKey = elem.Description,
                FieldType = ConfigFieldType.Choice,
                Choices = elem.Options,
                DefaultValue = !string.IsNullOrEmpty(elem.DefaultValue) ? elem.DefaultValue : (elem.Options.FirstOrDefault() ?? string.Empty)
            },
            "keybind" or "hotkey" => new PluginConfigField
            {
                Key = key,
                GroupKey = groupName,
                LabelKey = label,
                DescriptionKey = elem.Description,
                FieldType = ConfigFieldType.Hotkey,
                DefaultValue = elem.DefaultValue ?? string.Empty
            },
            "folderpicker" => new PluginConfigField
            {
                Key = key,
                GroupKey = groupName,
                LabelKey = label,
                DescriptionKey = elem.Description,
                FieldType = ConfigFieldType.FolderPath,
                DefaultValue = elem.DefaultValue ?? string.Empty
            },
            "filepicker" => new PluginConfigField
            {
                Key = key,
                GroupKey = groupName,
                LabelKey = label,
                DescriptionKey = elem.Description,
                FieldType = ConfigFieldType.FilePath,
                DefaultValue = elem.DefaultValue ?? string.Empty
            },
            _ => null
        };

        if (field != null)
        {
            var elemName = elem.Name;
            field.GetValue = () => FlowSettingsTemplateStorage.GetSettingValue(settingsPath, elemName)
                                ?? field.DefaultValue;
            field.SetValue = val => FlowSettingsTemplateStorage.SaveSettingValue(settingsPath, elemName, val);
        }

        return field;
    }

    private static Control? CreatePanelSafe(ISettingProvider settingProvider)
    {
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => CreatePanelSafe(settingProvider));
        }

        try
        {
            var panel = settingProvider.CreateSettingPanel();
            if (panel == null || (panel is UserControl uc && uc.Content == null)) return null;
            return panel;
        }
        catch
        {
            return null;
        }
    }
}
