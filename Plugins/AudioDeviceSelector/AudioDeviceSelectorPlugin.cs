using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.AudioDeviceSelector.CoreAudio;

namespace Lertaro.Plugins.AudioDeviceSelector;

public sealed class AudioDeviceSelectorPlugin : IPlugin, IConfigurable
{
    public string Name => TranslationService.Get("AudioDeviceSelector_PluginName");
    public string Description => TranslationService.Get("AudioDeviceSelector_PluginDesc");

    public PluginConfigSchema GetConfigSchema() => new()
    {
        Fields =
        [
            new PluginConfigField
            {
                Key = "TriggerKeyword",
                LabelKey = "AudioDeviceSelector_Config_TriggerKeywordLabel",
                DescriptionKey = "AudioDeviceSelector_Config_TriggerKeywordDesc",
                FieldType = ConfigFieldType.Text,
                DefaultValue = "ad",
                RequireNonEmpty = true
            },
            new PluginConfigField
            {
                Key = "DisplayMode",
                LabelKey = "AudioDeviceSelector_Config_DisplayModeLabel",
                DescriptionKey = "AudioDeviceSelector_Config_DisplayModeDesc",
                FieldType = ConfigFieldType.Choice,
                DefaultValue = "FriendlyName",
                ChoiceOptions =
                [
                    new PluginConfigChoice
                    {
                        Value = nameof(AudioDeviceDisplayMode.FriendlyName),
                        LabelKey = "AudioDeviceSelector_Config_DisplayModeFriendlyName"
                    },
                    new PluginConfigChoice
                    {
                        Value = nameof(AudioDeviceDisplayMode.DeviceName),
                        LabelKey = "AudioDeviceSelector_Config_DisplayModeDeviceName"
                    },
                    new PluginConfigChoice
                    {
                        Value = nameof(AudioDeviceDisplayMode.DeviceDescription),
                        LabelKey = "AudioDeviceSelector_Config_DisplayModeDeviceDescription"
                    }
                ]
            }
        ]
    };
}
