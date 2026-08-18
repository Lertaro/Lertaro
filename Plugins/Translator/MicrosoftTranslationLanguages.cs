namespace Lertaro.Plugins.Translator;

internal static class MicrosoftTranslationLanguages
{
    private static readonly HashSet<string> SupportedCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "af", "sq", "am", "ar", "hy", "as", "az", "bn", "ba", "eu", "bho", "brx", "bs", "bg", "yue",
        "ca", "hne", "lzh", "zh-hans", "zh-hant", "sn", "hr", "cs", "da", "prs", "dv", "doi", "nl", "en",
        "et", "fo", "fj", "fil", "fi", "fr", "fr-ca", "gl", "ka", "de", "el", "gu", "ht", "ha", "he", "hi",
        "mww", "hu", "is", "ig", "id", "ikt", "iu", "iu-latn", "ga", "it", "ja", "kn", "ks", "kk", "km", "rw",
        "tlh-latn", "tlh-piqd", "gom", "ko", "ku", "kmr", "ky", "lo", "lv", "lt", "ln", "dsb", "lug", "mk",
        "mai", "mg", "ms", "ml", "mt", "mni", "mi", "mr", "mn-cyrl", "mn-mong", "my", "ne", "nb", "nya", "or",
        "ps", "fa", "pl", "pt", "pt-pt", "pa", "otq", "ro", "run", "ru", "sm", "sr-cyrl", "sr-latn", "st", "nso",
        "tn", "sd", "si", "sk", "sl", "so", "es", "sw", "sv", "ty", "ta", "tt", "te", "th", "bo", "ti", "to", "tr", "tk",
        "uk", "hsb", "ur", "ug", "uz", "vi", "cy", "xh", "yo", "yua", "zu"
    };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zh-cn"] = "zh-hans",
        ["zh-sg"] = "zh-hans",
        ["zh-tw"] = "zh-hant",
        ["zh-hk"] = "zh-hant",
        ["zh-mo"] = "zh-hant",
        ["en-us"] = "en",
        ["en-gb"] = "en",
        ["es-es"] = "es",
        ["ja-jp"] = "ja",
        ["ko-kr"] = "ko"
    };

    public static bool TryNormalize(string value, out string language)
    {
        var normalized = value.Replace('_', '-').Trim();
        if (Aliases.TryGetValue(normalized, out language!))
            return true;

        if (SupportedCodes.Contains(normalized))
        {
            language = normalized.ToLowerInvariant();
            return true;
        }

        language = string.Empty;
        return false;
    }
}
