using System.Text.Json;

namespace Lertaro.Plugins.Translator;

internal static class MicrosoftTranslationResponseParser
{
    public static bool TryParse(string json, out TranslationResponse response)
    {
        response = default;
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            return false;

        var item = root[0];
        if (!item.TryGetProperty("translations", out var translations) || translations.ValueKind != JsonValueKind.Array || translations.GetArrayLength() == 0)
            return false;

        var text = translations[0].TryGetProperty("text", out var translatedText) ? translatedText.GetString() : null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var targetLanguage = translations[0].TryGetProperty("to", out var targetLanguageElement)
            ? targetLanguageElement.GetString() ?? string.Empty
            : string.Empty;

        var language = item.TryGetProperty("detectedLanguage", out var detectedLanguage) &&
                       detectedLanguage.TryGetProperty("language", out var languageElement)
            ? languageElement.GetString() ?? string.Empty
            : string.Empty;
        response = new TranslationResponse(text, language, targetLanguage);
        return true;
    }
}

internal readonly record struct TranslationResponse(string Text, string DetectedLanguage, string TargetLanguage);
