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
            var capturedName = pluginName;
            var yamlPath = Path.Combine(pair.Metadata.PluginDirectory, "SettingsTemplate.yaml");
            var jsonPath = Path.Combine(pair.Metadata.PluginDirectory, "SettingsTemplate.json");
            var templatePath = File.Exists(yamlPath) ? yamlPath : (File.Exists(jsonPath) ? jsonPath : null);

            var pluginFields = new List<PluginConfigField>
            {
                new PluginConfigField
                {
                    Key = $"{pluginName}.Enabled", GroupKey = pluginName,
                    LabelKey = PluginSdk.Services.TranslationService.Get("FlowLauncherBridge_PluginEnabledLabel"),
                    DescriptionKey = PluginSdk.Services.TranslationService.Get("FlowLauncherBridge_PluginEnabledDesc"),
                    FieldType = ConfigFieldType.Boolean, DefaultValue = !pair.Metadata.Disabled,
                    GetValue = () => host.IsPluginEnabled(capturedName),
                    SetValue = val => host.SetPluginEnabled(capturedName, val is bool b ? b : bool.TryParse(val?.ToString(), out var p) && p)
                }
            };

            if (!pair.Metadata.HideActionKeywordPanel && pair.Metadata.ActionKeyword != "*")
            {
                pluginFields.Add(new PluginConfigField
                {
                    Key = $"{pluginName}.ActionKeyword", GroupKey = pluginName,
                    LabelKey = PluginSdk.Services.TranslationService.Get("FlowLauncherBridge_PluginActionKeywordLabel"),
                    DescriptionKey = string.Format(PluginSdk.Services.TranslationService.Get("FlowLauncherBridge_PluginActionKeywordDesc"), pair.Metadata.ActionKeyword),
                    FieldType = ConfigFieldType.Text, DefaultValue = pair.Metadata.ActionKeyword ?? string.Empty,
                    RequireNonEmpty = true, MaxLength = 16,
                    GetValue = () => host.GetPluginActionKeyword(capturedName),
                    SetValue = val => { var kw = val?.ToString()?.Trim(); if (!string.IsNullOrEmpty(kw)) host.UpdatePluginActionKeyword(capturedName, kw); }
                });
            }

            if (templatePath != null)
            {
                var baseDir = PluginSdk.Services.UserDataService.GetUserDataDirectory()
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lertaro");
                var settingsPath = FlowSettingsTemplateStorage.GetSettingsPath(baseDir, pluginName);

                var doc = FlowSettingsTemplateParser.ParseFile(templatePath);
                foreach (var elem in doc.Elements)
                {
                    if (string.Equals(elem.Name, "triggerKeyword", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(elem.Name, "ActionKeyword", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!pair.Metadata.HideActionKeywordPanel && pair.Metadata.ActionKeyword != "*")
                            continue;
                    }

                    var field = ConvertElementToField(pluginName, elem, settingsPath, host);
                    if (field != null)
                    {
                        pluginFields.Add(field);
                    }
                }
            }
            else if (pair.Plugin is ISettingProvider)
            {
                var panel = CreatePanelSafe(pair);
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
        string settingsPath,
        FlowPluginHost host)
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
            "textarea" => new PluginConfigField
            {
                Key = key,
                GroupKey = groupName,
                LabelKey = label,
                DescriptionKey = elem.Description,
                FieldType = ConfigFieldType.StringList,
                DefaultValue = elem.DefaultValue ?? string.Empty
            },
            "number" or "integer" or "numeric" => new PluginConfigField
            {
                Key = key,
                GroupKey = groupName,
                LabelKey = label,
                DescriptionKey = elem.Description,
                FieldType = ConfigFieldType.Integer,
                DefaultValue = int.TryParse(elem.DefaultValue, out var n) ? n : 0
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
            "textblock" or "text" or "label" or "separator" or "hyperlink" or "link" or "url" => new PluginConfigField
            {
                Key = key,
                GroupKey = groupName,
                LabelKey = label,
                DescriptionKey = !string.IsNullOrEmpty(elem.Url) ? elem.Url : elem.Description,
                FieldType = ConfigFieldType.Text,
                DefaultValue = string.Empty
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

    private static Control? CreatePanelSafe(PluginPair pair)
    {
        if (pair.Plugin is not ISettingProvider settingProvider) return null;

        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => CreatePanelSafe(pair));
        }

        try
        {
            EnsureAppResourcesLoaded();
            if (Application.Current != null)
            {
                FlowPluginLanguageHelper.LoadPluginLanguage(pair.Metadata.PluginDirectory, Application.Current.Resources);
            }

            var panel = settingProvider.CreateSettingPanel();
            if (panel == null || (panel is UserControl uc && uc.Content == null)) return null;

            try
            {
                var uri = new Uri("pack://application:,,,/Lertaro.App;component/Views/Settings/Plugins/CustomControlStyles.xaml", UriKind.Absolute);
                panel.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
                FlowPluginLanguageHelper.LoadPluginLanguage(pair.Metadata.PluginDirectory, panel.Resources);
            }
            catch { }

            return panel;
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureAppResourcesLoaded()
    {
        if (Application.Current == null) return;

        try
        {
            var uri = new Uri("pack://application:,,,/Lertaro.App;component/Views/Settings/Plugins/FlowCompatibilityStyles.xaml", UriKind.Absolute);
            if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == uri))
            {
                Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
            }
        }
        catch { }
    }
}
