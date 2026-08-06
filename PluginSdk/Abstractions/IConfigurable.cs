namespace Lertaro.PluginSdk.Abstractions;

public enum ConfigFieldType
{
    Boolean,
    Text,
    Integer,
    Choice,
    Array,
    Object,
    Group,
    StringList,
    Hotkey,
    FilePath,
    FolderPath
}

public class PluginConfigField
{
    public string Key { get; set; } = string.Empty;
    public string GroupKey { get; set; } = string.Empty;
    public string LabelKey { get; set; } = string.Empty;
    public string DescriptionKey { get; set; } = string.Empty;
    public ConfigFieldType FieldType { get; set; }
    public object DefaultValue { get; set; } = null!;
    public List<string>? Choices { get; set; }
    public List<PluginConfigField>? SubFields { get; set; }
    /// <summary>For Hotkey fields: when true, single keys without modifier keys (Ctrl/Alt/Shift/Win) are rejected.</summary>
    public bool RequireModifier { get; set; }
    /// <summary>When true, saving this field with an empty/whitespace value falls back to <see cref="DefaultValue"/>
    /// instead of persisting the empty value -- for a field like a trigger keyword, where an empty value would
    /// silently make the depending feature unreachable rather than just "no value set".</summary>
    public bool RequireNonEmpty { get; set; }
    /// <summary>For Text fields: maximum character length (0 or unset means no length restriction).</summary>
    public int MaxLength { get; set; }
}

public class PluginConfigSchema
{
    public List<PluginConfigField> Fields { get; set; } = new();
}

public interface IConfigurable
{
    PluginConfigSchema GetConfigSchema();
}
