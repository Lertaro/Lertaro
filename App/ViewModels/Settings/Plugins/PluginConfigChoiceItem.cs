namespace Lertaro.App.ViewModels.Settings.Plugins;

/// <summary>One choice presented by the settings UI while preserving its stable stored value.</summary>
public sealed class PluginConfigChoiceItem
{
    public string Value { get; }
    public string Label { get; }

    public PluginConfigChoiceItem(string value, string label)
    {
        Value = value;
        Label = label;
    }
}
