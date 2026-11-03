using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lertaro.App.Services.Update;

// Polls GitLab for the latest release. Kept separate from UpdateInstaller: checking for an update has a
// different trigger (startup or a manual request from the About page) and failure domain (network/JSON
// parsing) than installing one (crypto/filesystem/service IPC) -- a caller wanting just the version check
// has no reason to pull in signature verification or elevated-process launching, and vice versa.
public class UpdateChecker
{
    private static readonly Lazy<UpdateChecker> _instance = new Lazy<UpdateChecker>(() => new UpdateChecker());
    public static UpdateChecker Instance => _instance.Value;

    // The project path has to be percent-encoded to address it through the API. `permalink/latest` is the
    // release published most recently, which is what the GitHub endpoint this replaced answered with.
    private const string GITLAB_PROJECT_PATH = "Mikachu2333%2Flertaro";
    private const string LATEST_RELEASE_URL =
        "https://gitlab.com/api/v4/projects/" + GITLAB_PROJECT_PATH + "/releases/permalink/latest";

    private readonly HttpClient _httpClient;

    private UpdateChecker()
    {
        _httpClient = new HttpClient();
        // Not required by GitLab, but its edge answers requests without one less predictably.
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Lertaro", "1.0.0"));
    }

    /// <summary>
    /// Retrieves the latest release info from GitLab.
    /// </summary>
    public async Task<ReleaseInfo?> CheckForUpdatesAsync()
    {
        try
        {
            var response = await _httpClient.GetStringAsync(LATEST_RELEASE_URL);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<ReleaseInfo>(response, options);
        }
        catch (Exception ex)
        {
            Core.Logger.Log($"[UpdateService] Check update failed: {ex}", Core.LogLevel.Error);
            throw;
        }
    }
}

public class ReleaseInfo
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("assets")]
    public ReleaseAssets Assets { get; set; } = new ReleaseAssets();

    /// <summary>The release page, for the "open the release page" button.</summary>
    public string HtmlUrl => $"https://gitlab.com/Mikachu2333/lertaro/-/releases/{Uri.EscapeDataString(TagName)}";
}

public class ReleaseAssets
{
    [JsonPropertyName("links")]
    public ReleaseAssetLink[] Links { get; set; } = Array.Empty<ReleaseAssetLink>();
}

public class ReleaseAssetLink
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Where the link points. For a CI-published asset this is the package registry URL.</summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// The release's own path for the asset, of the form <c>.../releases/&lt;tag&gt;/downloads/&lt;name&gt;</c>.
    /// Empty when the release was created without a direct asset path.
    /// </summary>
    [JsonPropertyName("direct_asset_url")]
    public string DirectAssetUrl { get; set; } = string.Empty;

    /// <summary>
    /// The URL the updater downloads from. The direct asset path is preferred because it is the stable,
    /// readable one, and because the signature is fetched as <c>this + ".sig"</c> -- which is why CI
    /// publishes the signature under exactly that name beside the zip.
    /// </summary>
    public string DownloadUrl => string.IsNullOrEmpty(DirectAssetUrl) ? Url : DirectAssetUrl;
}
