using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lertaro.App.Services.Update;

// Polls GitHub for the latest release. Kept separate from UpdateInstaller: checking for an update has a
// different trigger (startup or a manual request from the About page) and failure domain (network/JSON
// parsing) than installing one (crypto/filesystem/service IPC) -- a caller wanting just the version check
// has no
// reason to pull in signature verification or elevated-process launching, and vice versa.
public class UpdateChecker
{
    private static readonly Lazy<UpdateChecker> _instance = new Lazy<UpdateChecker>(() => new UpdateChecker());
    public static UpdateChecker Instance => _instance.Value;

    private readonly HttpClient _httpClient;
    // This fork's own releases, deliberately not upstream's. Upstream ships fixes to main without
    // publishing them (the permission check that slowed every search down was one of those), and an
    // auto-update pointed at upstream would hand such a build to a portable install that cannot roll back.
    // The fork builds the same asset names and signatures, so everything downstream is unchanged.
    private const string GITHUB_API_URL = "https://api.github.com/repos/bobo198504/Lertaro/releases/latest";

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
