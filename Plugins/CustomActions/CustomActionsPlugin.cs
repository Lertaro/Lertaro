using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CustomActions;

public class CustomActionsPlugin : IPlugin, IActionProvider, IConfigurable
{
    public string Name => TranslationService.Get("CustomActions_PluginName");
    public string Description => TranslationService.Get("CustomActions_PluginDesc");

    public IEnumerable<ISearchResultAction> GetActions() => Array.Empty<ISearchResultAction>();

    public IEnumerable<IDynamicActionProvider> GetDynamicActionProviders() => new[] { new DynamicActionProvider() };

    public PluginConfigSchema GetConfigSchema() => new PluginConfigSchema
    {
        Fields = new List<PluginConfigField>
        {
            new PluginConfigField
            {
                Key = "Actions",
                LabelKey = "CustomActions_Config_ActionsLabel",
                DescriptionKey = "CustomActions_Config_ActionsDesc",
                FieldType = ConfigFieldType.Array,
                DefaultValue = new List<object>(),
                SubFields = new List<PluginConfigField>
                {
                    new PluginConfigField { Key = "Enabled",     LabelKey = "CustomActions_Config_EnabledLabel",    FieldType = ConfigFieldType.Boolean, DefaultValue = true },
                    new PluginConfigField { Key = "Title",       LabelKey = "CustomActions_Config_TitleLabel",      FieldType = ConfigFieldType.Text,    DefaultValue = "" },
                    new PluginConfigField { Key = "Path",        LabelKey = "CustomActions_Config_PathLabel",       FieldType = ConfigFieldType.FilePath,   DefaultValue = "" },
                    new PluginConfigField { Key = "Parameter",   LabelKey = "CustomActions_Config_ParameterLabel",  FieldType = ConfigFieldType.Text,    DefaultValue = "" },
                    new PluginConfigField { Key = "Icon",        LabelKey = "CustomActions_Config_IconLabel",       FieldType = ConfigFieldType.Text,    DefaultValue = "" },
                    new PluginConfigField { Key = "WorkingDir",  LabelKey = "CustomActions_Config_WorkingDirLabel", FieldType = ConfigFieldType.FolderPath, DefaultValue = "" },
                    new PluginConfigField { Key = "RunSilently", LabelKey = "CustomActions_Config_RunSilentlyLabel",FieldType = ConfigFieldType.Boolean, DefaultValue = false },
                    new PluginConfigField { Key = "RunAsAdmin",  LabelKey = "CustomActions_Config_RunAsAdminLabel", FieldType = ConfigFieldType.Boolean, DefaultValue = false },
                    new PluginConfigField { Key = "Hotkey",      LabelKey = "CustomActions_Config_HotkeyLabel",     FieldType = ConfigFieldType.Hotkey,  DefaultValue = "", RequireModifier = true },
                    new PluginConfigField { Key = "FolderOnly",  LabelKey = "CustomActions_Config_FolderOnlyLabel", FieldType = ConfigFieldType.Boolean, DefaultValue = false },
                    new PluginConfigField { Key = "MultiSelect", LabelKey = "CustomActions_Config_MultiSelectLabel", FieldType = ConfigFieldType.Boolean, DefaultValue = false },
                    new PluginConfigField { Key = "Extensions",  LabelKey = "CustomActions_Config_ExtensionsLabel", FieldType = ConfigFieldType.Text,    DefaultValue = "" }
                }
            }
        }
    };
}
