using Lertaro.App.Helpers;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;

namespace Lertaro.App.ViewModels.Settings.Plugins;

// Owns how one config field is presented: resolving its label/description keys through TranslationService
// and projecting the schema's choice list into the shape the editors bind.
//
// Split out of PluginConfigFieldViewModel to keep that file within the repo's per-file line limit. Every
// name below is re-exposed on the field itself, because the field editors and Templates.xaml bind them
// there -- only the lookup behind them lives here.
internal sealed class PluginConfigFieldDisplaySupport
{
    private readonly PluginConfigFieldViewModel _field;

    internal PluginConfigFieldDisplaySupport(PluginConfigFieldViewModel field) => _field = field;

    // A schema string is a translation KEY when it resolves, and literal text when it does not, which is
    // how a plugin can put a plain label next to its localizable ones.
    private static string ResolveText(string? keyOrText)
    {
        if (string.IsNullOrEmpty(keyOrText)) return string.Empty;
        if (TranslationService.TryGet(keyOrText, out var translated))
            return translated;
        return keyOrText;
    }

    internal string Label => ResolveText(_field.SchemaField.LabelKey);

    /// <summary>
    /// The field's help text, plus the one sentence the host can add that a plugin cannot write for itself.
    /// </summary>
    /// <remarks>
    /// A TOKEN KEYWORD field (see <see cref="ConfigFieldValidation.TokenKeyword"/>) is where this applies:
    /// the user has to be told which WORD to type in the search box, and that word is the configured prefix
    /// plus what they are typing right now -- neither of which the plugin assembly can see. Describing the
    /// field in the abstract left them to assemble it themselves from a prefix the settings page never
    /// showed them.
    /// </remarks>
    internal string Description
    {
        get
        {
            var text = ResolveText(_field.SchemaField.DescriptionKey);

            // Read lazily: only a token keyword field ever needs it, and only while it has a value.
            if (_field.SchemaField.Validation == ConfigFieldValidation.TokenKeyword
                && TranslationService.TryGet(TokenKeywordHintKey, out var template))
            {
                return TokenHint(text, template, GlobalTokenPrefix.Current, _field.Value as string) ?? text;
            }

            return text;
        }
    }

    // The one App-owned string a plugin's schema reaches for by name. Named here rather than in the plugin so
    // the sentence stays the host's to word.
    private const string TokenKeywordHintKey = "Plugins_TokenKeywordHint";

    /// <summary>
    /// The field's help text with the "type this to filter" sentence appended, or null when there is nothing
    /// to put in that sentence.
    /// </summary>
    /// <remarks>
    /// An empty keyword would render the sentence as a bare "\", which explains less than saying nothing, so
    /// the base text is returned alone until the user has typed something. Pure and static so the wording
    /// rules are testable without a loaded translation file -- this project's test assemblies have none, so
    /// a property that resolves a key first is a property whose interesting half cannot be pinned.
    /// </remarks>
    internal static string? TokenHint(string? baseText, string? template, char prefix, string? keyword)
    {
        if (string.IsNullOrEmpty(template) || string.IsNullOrWhiteSpace(keyword))
            return null;

        var hint = FillPlaceholders(template, prefix + keyword.Trim());
        return string.IsNullOrEmpty(baseText) ? hint : baseText + " " + hint;
    }

    // A translated format string is not a programmer's string: a locale that mistypes a placeholder ("{O}")
    // would otherwise throw out of a bound property and leave the row with no help text at all plus a
    // binding error in the log, which is strictly worse than showing the text with the placeholder unfilled.
    // This project has already shipped broken translation resources twice.
    private static string FillPlaceholders(string text, params object?[] args)
    {
        try
        {
            return string.Format(text, args);
        }
        catch (FormatException)
        {
            return text;
        }
    }

    internal string GroupName => ResolveText(_field.GroupKey);

    internal List<string>? Choices => _field.SchemaField.Choices?.Select(ResolveText).ToList();

    internal IReadOnlyList<PluginConfigChoiceItem> ChoiceItems => _field.SchemaField.ChoiceOptions != null
        ? _field.SchemaField.ChoiceOptions.Select(choice => new PluginConfigChoiceItem(choice.Value, ResolveText(choice.LabelKey))).ToList()
        : _field.SchemaField.Choices?.Select(choice => new PluginConfigChoiceItem(choice, ResolveText(choice))).ToList() ?? [];

    /// <summary>
    /// A StringList field's value as the multi-line text its editor shows: one entry per line, CRLF
    /// between them. Anything that is not an enumerable (or is the raw string an unset field holds) is
    /// already the text to show.
    /// </summary>
    internal static string SerializeStringList(object? value)
    {
        if (value is System.Collections.IEnumerable items && value is not string)
        {
            var lines = new List<string>();
            foreach (var item in items)
                lines.Add(item?.ToString() ?? string.Empty);
            return string.Join("\r\n", lines);
        }

        return value?.ToString() ?? string.Empty;
    }
}
