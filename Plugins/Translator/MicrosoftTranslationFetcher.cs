using System.Text;
using System.Text.Json;

namespace Lertaro.Plugins.Translator;

internal static class MicrosoftTranslationFetcher
{
    private const string TranslateUrl = "https://edge.microsoft.com/translate/translatetext?isEnterpriseClient=false&to=";
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    static MicrosoftTranslationFetcher() =>
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36 Edg/125.0.0.0");

    public static async Task<TranslationResponse> TranslateAsync(string text, string targetLanguage)
    {
        var url = TranslateUrl + Uri.EscapeDataString(targetLanguage);
        using var content = new StringContent(JsonSerializer.Serialize(new[] { text }), Encoding.UTF8, "application/json");
        using var response = await HttpClient.PostAsync(url, content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        if (!MicrosoftTranslationResponseParser.TryParse(json, out var translation))
            throw new InvalidOperationException("Microsoft Translator returned no translation.");

        return translation;
    }
}
