namespace Lertaro.Plugins.Translator;

internal readonly record struct TranslationQuery(string TargetLanguage, string Text);

internal static class TranslationQueryParser
{
    public static TranslationQuery Parse(string remainder, string defaultTargetLanguage)
    {
        var input = remainder.Trim();
        if (input.Length == 0)
            return new(defaultTargetLanguage, string.Empty);

        var separator = FindWhitespace(input);
        if (separator < 0 || !TryNormalizeLanguage(input[..separator], out var targetLanguage))
            return new(defaultTargetLanguage, input);

        return new(targetLanguage, input[(separator + 1)..].TrimStart());
    }

    private static int FindWhitespace(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsWhiteSpace(value[index]))
                return index;
        }

        return -1;
    }

    private static bool TryNormalizeLanguage(string value, out string language) => MicrosoftTranslationLanguages.TryNormalize(value, out language);
}
