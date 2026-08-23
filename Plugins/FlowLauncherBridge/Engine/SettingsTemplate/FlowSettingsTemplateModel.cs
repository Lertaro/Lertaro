namespace Lertaro.Plugins.FlowLauncherBridge.Engine.SettingsTemplate;

/// <summary>
/// Represents a single UI element definition parsed from a Flow.Launcher SettingsTemplate.yaml/json file.
/// </summary>
public sealed class FlowSettingsTemplateElement
{
    public string Type { get; set; } = string.Empty;
    public Dictionary<string, string> Attributes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Options { get; set; } = [];

    public string Name => Attributes.GetValueOrDefault("name", string.Empty);
    public string Label => Attributes.GetValueOrDefault("label", string.Empty);
    public string Description => Attributes.GetValueOrDefault("description", string.Empty);
    public string DefaultValue => Attributes.GetValueOrDefault("defaultValue", string.Empty);
    public string Url => Attributes.GetValueOrDefault("url", string.Empty);
}

/// <summary>
/// Root document containing all UI element definitions for a plugin's settings template.
/// </summary>
public sealed class FlowSettingsTemplateDoc
{
    public List<FlowSettingsTemplateElement> Elements { get; set; } = [];
}
