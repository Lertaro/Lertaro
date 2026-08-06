using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lertaro.App.Services.Update;

// Polls GitHub for the latest release. Kept separate from UpdateInstaller: checking for an update has a
// different trigger (periodic/startup) and failure domain (network/JSON parsing) than installing one
// (user-consented, crypto/filesystem/process elevation) -- a caller wanting just the version check has no
// reason to pull in signature verification or elevated-process launching, and vice versa.
public class UpdateChecker
{
    private static readonly Lazy<UpdateChecker> _instance = new Lazy<UpdateChecker>(() => new UpdateChecker());
    public static UpdateChecker Instance => _instance.Value;

    private readonly HttpClient _httpClient;
    private const string GITHUB_API_URL = "https://api.github.com/repos/Lertaro/Lertaro/releases/latest";

    private UpdateChecker()
    {
        _httpClient = new HttpClient();
        // User-Agent header is strictly required by GitHub API
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Lertaro", "1.0.0"));
    }

    /// <summary>
    /// Retrieves the latest release info from GitHub.
    /// </summary>
    public async Task<GitHubReleaseInfo?> CheckForUpdatesAsync()
    {
        try
        {
            var response = await _httpClient.GetStringAsync(GITHUB_API_URL);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<GitHubReleaseInfo>(response, options);
        }
        catch (Exception ex)
        {
            Core.Logger.Log($"[UpdateService] Check update failed: {ex}", Core.LogLevel.Error);
            throw;
        }
    }
}

public class GitHubReleaseInfo
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("assets")]
    public GitHubAsset[] Assets { get; set; } = Array.Empty<GitHubAsset>();
}

public class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;
}
