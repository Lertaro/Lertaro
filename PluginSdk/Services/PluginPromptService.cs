using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.PluginSdk.Services;

/// <summary>
/// Lets a plugin collect a few values from the user at runtime (e.g. "name this before adding it")
/// using the same field schema/rendering the Settings UI's own Configure dialog uses for a plugin's
/// persistent config -- without those values ever being read from or written to that plugin's actual
/// settings. Reuses <see cref="PluginConfigField"/> as the field schema so a plugin doesn't need a
/// second, parallel way to describe "one Text field" versus "one Choice field", etc.
/// </summary>
public static class PluginPromptService
{
    /// <summary>
    /// Delegate function set by the host application to show the prompt. Parameters: (title, fields,
    /// initialValues). Returns the entered values keyed by each field's <see cref="PluginConfigField.Key"/>,
    /// or null if the user cancelled (or the host hasn't wired this up).
    /// </summary>
    public static Func<string, IReadOnlyList<PluginConfigField>, IReadOnlyDictionary<string, object?>?, IReadOnlyDictionary<string, object?>?>? PromptFunc { get; set; }

    /// <summary>
    /// Shows a small modal window asking for the given fields' values, pre-filled from
    /// <paramref name="initialValues"/> (matched by <see cref="PluginConfigField.Key"/>) where present,
    /// otherwise each field's own <see cref="PluginConfigField.DefaultValue"/>.
    /// </summary>
    /// <returns>The entered values keyed by field Key, or null if cancelled.</returns>
    public static IReadOnlyDictionary<string, object?>? Prompt(
        string title,
        IReadOnlyList<PluginConfigField> fields,
        IReadOnlyDictionary<string, object?>? initialValues = null)
        => PromptFunc?.Invoke(title, fields, initialValues);
}
