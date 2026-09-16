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

    internal string Description => ResolveText(_field.SchemaField.DescriptionKey);

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
