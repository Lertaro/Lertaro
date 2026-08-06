using System.Text;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.SpanishAlias;

public sealed class SpanishAliasProvider : IAliasProvider, ITranslationProvider
{
    public string Name => TranslationService.Get("Plugins_SpanishAliasPluginName");
    public string Description => TranslationService.Get("Plugin_Comp_Desc_SpanishAliasProvider");
    public int Version => 1;

    public IReadOnlyList<string> SupportedCultures => TranslationService.GetSupportedCultures(System.Reflection.Assembly.GetExecutingAssembly());

    public IReadOnlyList<(char Start, char End)> InputRanges { get; } = new[] { ('\u00C0', '\u00FF') };
    public IReadOnlyList<(char Start, char End)> OutputRanges { get; } = new[] { ('a', 'z') };

    public bool CanHandle(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        foreach (var c in text)
        {
            if (SpanishDiacritics.IsSpanishDiacritic(c))
                return true;
        }
        return false;
    }

    public IEnumerable<string> GetAliases(string text)
    {
        if (!CanHandle(text))
            return Array.Empty<string>();

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            sb.Append(SpanishDiacritics.RemoveDiacritic(c));
        }
        return new[] { sb.ToString() };
    }

    public int[]? MapAliasToSourceIndices(string text, string alias)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(alias) || alias.Length != text.Length)
            return null;

        var map = new int[text.Length];
        for (var i = 0; i < text.Length; i++)
            map[i] = i;
        return map;
    }

    public void GetAliasesUtf8(string text, AliasByteSink dest)
    {
        if (!CanHandle(text))
            return;

        var chars = text.Length <= 256 ? stackalloc char[text.Length] : new char[text.Length];
        for (var i = 0; i < text.Length; i++)
            chars[i] = SpanishDiacritics.RemoveDiacritic(text[i]);

        dest.AddString(chars.ToString());
    }

    public IReadOnlyDictionary<string, string> GetTranslations(string cultureName) => TranslationService.LoadEmbeddedTranslations(System.Reflection.Assembly.GetExecutingAssembly(), cultureName, "Plugin");
}
