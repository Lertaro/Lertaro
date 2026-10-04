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
    FolderPath,
    CustomControl,
    Button
}

/// <summary>
/// A validation rule the host applies to a config field beyond its declared type, for values that are a
/// prefix of the search query and so compete with the search syntax for the same leading characters.
/// Declaring it here is what lets the host identify the field: the eight instant-answer trigger keywords
/// are otherwise ordinary non-empty text fields, scattered across plugins under inconsistent key names
/// ("TriggerKeyword", "SearchSettingsTrigger", "BookmarkTriggerKeyword", ...).
/// </summary>
public enum ConfigFieldValidation
{
    /// <summary>No extra rule beyond the field type.</summary>
    None,

    /// <summary>
    /// An instant-answer trigger keyword: the word typed at the start of a query to invoke a provider.
    /// Rejected when it starts with a character the search syntax consumes (see the host's
    /// SearchSyntaxReserved), because that character is stripped before the provider ever sees the query
    /// and the trigger would silently never fire.
    /// </summary>
    TriggerKeyword,

    /// <summary>
    /// A query-token keyword: the word a provider recognizes after the token prefix, as in the "audio" of
    /// "\audio". No rule is validated against it -- it is matched inside a token, so a leading character
    /// the syntax owns is harmless here -- but declaring it lets the host SHOW the user the whole token to
    /// type, prefix included. Without that the settings page can only describe the field in the abstract,
    /// because the prefix is not this plugin's to know (see the host's SearchSyntaxService).
    /// </summary>
    TokenKeyword
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
    /// <summary>Choice values with separate persisted values and localized display labels.</summary>
    public List<PluginConfigChoice>? ChoiceOptions { get; set; }
    public List<PluginConfigField>? SubFields { get; set; }
    /// <summary>Opt in to single-entry JSON import/export for host-managed Array fields with flat
    /// Boolean/Text/Hotkey/FilePath/FolderPath children, including Icon Path Data. Attachments are not bundled.</summary>
    public bool AllowEntryTransfer { get; set; }
    /// <summary>For Hotkey fields: when true, single keys without modifier keys (Ctrl/Alt/Shift/Win) are rejected.</summary>
    public bool RequireModifier { get; set; }
    /// <summary>When true, saving this field with an empty/whitespace value falls back to <see cref="DefaultValue"/>
    /// instead of persisting the empty value -- for a field like a trigger keyword, where an empty value would
    /// silently make the depending feature unreachable rather than just "no value set".</summary>
    public bool RequireNonEmpty { get; set; }
    /// <summary>The extra rule the host validates this field's value against; see <see cref="ConfigFieldValidation"/>.
    /// This is also how the host recognizes a trigger keyword: <see cref="ConfigFieldValidation.TriggerKeyword"/>
    /// fields are the ones checked against the reserved leading characters, migrated when the syntax gains a
    /// new one, and warned about when another feature already answers to the same word.</summary>
    public ConfigFieldValidation Validation { get; set; }
    /// <summary>Superseded by <see cref="Validation"/> equal to
    /// <see cref="ConfigFieldValidation.TriggerKeyword"/>.</summary>
    /// <remarks>
    /// Exists only for binary compatibility, and must not be used by new code. This was replaced by
    /// <see cref="Validation"/> while the assembly version stayed at 2.0.0, and plugins reference
    /// Lertaro.PluginSdk.dll from an installed copy with <c>&lt;Private&gt;false&lt;/Private&gt;</c> -- so a
    /// plugin built against the earlier 2.0.0 still binds, then throws MissingMethodException on its own
    /// <c>set_IsTriggerWord</c> call the first time the host asks it for a schema. The host swallows that
    /// exception, which takes the plugin's entire settings page and its default-value fallback down with it
    /// and shows the user nothing. Forwarding the getter and setter costs no behaviour of its own and makes
    /// those plugins work again.
    /// <para>
    /// Setting false is deliberately a no-op rather than a reset to <see cref="ConfigFieldValidation.None"/>:
    /// None is what an unset field already holds, and resetting here would let an initializer that writes
    /// <c>Validation = TokenKeyword</c> before <c>IsTriggerWord = false</c> silently lose the first.
    /// </para>
    /// </remarks>
    [Obsolete("Use Validation = ConfigFieldValidation.TriggerKeyword. Kept only so plugins compiled against an earlier 2.0.0 PluginSdk still load.")]
    public bool IsTriggerWord
    {
        get => Validation == ConfigFieldValidation.TriggerKeyword;
        set
        {
            if (value)
                Validation = ConfigFieldValidation.TriggerKeyword;
        }
    }
    /// <summary>For Text fields: maximum character length (0 or unset means no length restriction).</summary>
    public int MaxLength { get; set; }
    /// <summary>For Text fields: zero-based initial selection start in the prompt editor.</summary>
    public int SelectionStart { get; set; }
    /// <summary>For Text fields: initial selection length in the prompt editor.</summary>
    public int SelectionLength { get; set; }
    /// <summary>For CustomControl fields: custom UI element/control hosted directly by the application.</summary>
    public object? CustomControl { get; set; }
    /// <summary>For Button fields: invoked when the button is clicked. A Button field stores no value;
    /// the click runs this delegate directly (e.g. a rebuild or clear action).</summary>
    public Action? OnClick { get; set; }
    /// <summary>Custom getter delegate for external plugin settings.</summary>
    public Func<object?>? GetValue { get; set; }
    /// <summary>Custom setter delegate for external plugin settings.</summary>
    public Action<object?>? SetValue { get; set; }
}

public class PluginConfigChoice
{
    public string Value { get; set; } = string.Empty;
    public string LabelKey { get; set; } = string.Empty;
}

public class PluginConfigSchema
{
    public List<PluginConfigField> Fields { get; set; } = new();
    public Action? OnSave { get; set; }
    public Action? OnRollback { get; set; }
}

public interface IConfigurable
{
    PluginConfigSchema GetConfigSchema();
}
