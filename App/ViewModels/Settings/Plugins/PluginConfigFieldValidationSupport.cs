using Lertaro.App.Helpers;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.ViewModels.Settings.Plugins;

// Owns one field's trigger-collision reporting: whether the single character (or keyword) it holds can
// actually fire, and the same question for its loaded rows.
//
// Split out of PluginConfigFieldViewModel to keep that file within the repo's per-file line limit, and
// because this is the half of the field that is asked from OUTSIDE its own page -- the Settings window
// asks every built page for its errors before letting Apply save (see SettingsViewModel.ValidationErrors).
internal sealed class PluginConfigFieldValidationSupport
{
    private readonly PluginConfigFieldViewModel _field;

    internal PluginConfigFieldValidationSupport(PluginConfigFieldViewModel field) => _field = field;

    /// <summary>
    /// Why this single-character trigger cannot be used, or null when it is fine -- a prefix collision
    /// otherwise resolves silently by whichever provider is asked first, leaving the other one's tokens
    /// simply not working. See QueryTokenPrefixRules for the collisions that count.
    /// </summary>
    internal string? PrefixError => QueryTokenPrefixRules.IsPrefixField(_field.SchemaField)
        ? QueryTokenPrefixRules.PluginPrefixConflict(_field.PluginId, _field.SchemaField, _field.Value as string, _field.Settings)
        : null;

    /// <summary>
    /// Why this instant-answer trigger keyword cannot be used, or null when it is fine. The keyword is a
    /// prefix of the whole query, so one starting with a character the search syntax consumes is stripped
    /// before the provider is ever asked and the trigger silently stops working. Only fields that declare
    /// <see cref="ConfigFieldValidation.TriggerKeyword"/> are checked.
    /// </summary>
    internal string? TriggerKeywordError => _field.SchemaField.Validation == ConfigFieldValidation.TriggerKeyword
        ? QueryTokenPrefixRules.TriggerKeywordConflict(_field.Value as string)
        : null;

    /// <summary>
    /// This field's own errors, then its loaded rows'. A container that was never shown holds no staged
    /// edit and so cannot hold an error -- and materializing it here would make every Apply walk every
    /// visited plugin's whole schema, the exact cost the lazy rows exist to avoid. Same rule, and the same
    /// reason, as PluginConfigFieldLoadSupport.IsDirty.
    ///
    /// Each message is prefixed with the plugin id and field key rather than the label: this is read by
    /// the Apply guard, which only logs it, and a translated label would make the log line depend on the
    /// active language.
    /// </summary>
    internal IEnumerable<string> Errors
    {
        get
        {
            if (PrefixError is { Length: > 0 } prefix)
                yield return Describe(prefix);
            if (TriggerKeywordError is { Length: > 0 } keyword)
                yield return Describe(keyword);

            // Guarded before the getters, which load on demand: a tree that was never materialized cannot
            // have been edited, and asking for it would build it just to find nothing.
            if (_field.HasLoadedChildren)
            {
                foreach (var error in _field.Children.SelectMany(child => child.Validation.Errors))
                    yield return error;
            }

            if (_field.HasLoadedArrayItems)
            {
                foreach (var error in _field.ArrayItems.SelectMany(item => item.ValidationErrors))
                    yield return error;
            }
        }
    }

    private string Describe(string error) => $"{_field.PluginId}.{_field.SchemaField.Key}: {error}";
}
