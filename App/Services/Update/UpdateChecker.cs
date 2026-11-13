using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lertaro.App.Services.Update;

// Polls both release lines for the newest version. Kept separate from UpdateInstaller: checking for an
// update has a different trigger (startup or a manual request from the About page) and failure domain
// (network/JSON parsing) than installing one (crypto/filesystem/service IPC) -- a caller wanting just the
// version check has no reason to pull in signature verification or elevated-process launching, and vice
// versa.
public class UpdateChecker
{
    private static readonly Lazy<UpdateChecker> _instance = new Lazy<UpdateChecker>(() => new UpdateChecker());
    public static UpdateChecker Instance => _instance.Value;

    // Asked in this order, and both are asked even once the first answers: the two pipelines throttle
    // independently (36h each, and GitLab's additionally needs a self-hosted runner that may be offline),
    // so either line can be the one holding the newer tag. A tie keeps the earlier entry, i.e. GitHub.
    // Whether the GitLab one is asked at all depends on the user's token -- see UsableSources.
    private static readonly ReleaseSource[] Sources =
    [
        new ReleaseSource("https://api.github.com/repos/Lertaro/Lertaro/releases/latest", true),
        // The project path has to be percent-encoded to address it through the API. `permalink/latest` is
        // the release published most recently, which is what the GitHub endpoint answers with.
        new ReleaseSource("https://gitlab.com/api/v4/projects/Mikachu2333%2Flertaro/releases/permalink/latest", false),
    ];

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;

    private UpdateChecker()
    {
        _httpClient = new HttpClient();
        // Required by the GitHub API; GitLab does not need it, but its edge answers requests without one
        // less predictably.
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Lertaro", "1.0.0"));
    }

    /// <summary>
    /// The newest release across both lines, or null when neither returned a tag that parses as a version.
    /// </summary>
    /// <remarks>
    /// One line being unreachable is not a failed check -- that is the whole reason there are two. Only when
    /// neither yields a usable release does the last failure propagate, with its original stack, because the
    /// About page prints that stack for the user who just pressed the button.
    /// </remarks>
    public async Task<ReleaseInfo?> CheckForUpdatesAsync()
    {
        var settings = Core.UserSettings.Load();
        var mirrors = Core.UpdateMirrorSettings.Normalize(settings.GitHubUpdateMirrors);
        var token = Core.UpdateMirrorSettings.NormalizeToken(settings.GitLabUpdateToken);
        var found = new List<ReleaseInfo>(Sources.Length);
        Exception? failure = null;

        foreach (var source in UsableSources(token))
        {
            try
            {
                // A request rather than GetStringAsync so the GitLab one can carry the user's token: it
                // belongs on that one request only, never on the shared client, or every GitHub call
                // would hand it out too.
                using var request = new HttpRequestMessage(HttpMethod.Get, source.Url);
                if (!source.IsGitHub) request.Headers.Add("PRIVATE-TOKEN", token);
                using var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var release = source.IsGitHub ? ParseGitHub(json, mirrors) : ParseGitLab(json);
                if (release != null) found.Add(release);
            }
            catch (Exception ex)
            {
                failure = ex;
                Core.Logger.Log($"[UpdateService] Check update failed ({source.Url}): {ex}", Core.LogLevel.Error);
            }
        }

        var newest = PickNewest(found);
        if (newest != null) return newest;
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        return null;
    }

    /// <summary>
    /// The lines this settings state can actually use: GitHub always, GitLab only once the user has
    /// supplied a personal access token.
    /// </summary>
    /// <remarks>
    /// The token is what GitLab's API is asked with, so with none set there is nothing to ask and -- the
    /// point of the gate -- nothing to download from there either: a user who never configured one gets
    /// the GitHub line only, which its own mirror publishes about two days later. Skipping the request
    /// rather than sending it unauthenticated is also what keeps an anonymous 429 from being logged as a
    /// failed check on every startup.
    /// </remarks>
    internal static IEnumerable<ReleaseSource> UsableSources(string token) =>
        Sources.Where(source => source.IsGitHub || token.Length > 0);

    /// <summary>
    /// The highest tag that parses as a version, keeping the earlier of two equal ones -- which is how
    /// "GitHub first" survives a check that asks both lines. An unparseable tag is skipped rather than
    /// offered, the same rule <c>UpdateCheckService.IsNewerVersion</c> then applies to the winner.
    /// </summary>
    internal static ReleaseInfo? PickNewest(IEnumerable<ReleaseInfo> releases)
    {
        ReleaseInfo? newest = null;
        Version? newestVersion = null;
        foreach (var release in releases)
        {
            if (!Version.TryParse(release.TagName.TrimStart('v', 'V'), out var version)) continue;
            if (newestVersion != null && version <= newestVersion) continue;
            newest = release;
            newestVersion = version;
        }
        return newest;
    }

    // GitHub uploads release files as a flat `assets` array; GitLab can only create assets from CI, so its
    // release carries an `assets` object holding `links` into the package registry. Both are mapped onto
    // ReleaseInfo/ReleaseAssetLink so the asset selector and the installer see one shape. The names are the
    // same on both lines (make.bat owns them), including the "<zip>.sig" the installer appends.
    internal static ReleaseInfo? ParseGitHub(string json, IReadOnlyList<string> mirrors)
    {
        var release = JsonSerializer.Deserialize<GitHubReleaseInfo>(json, JsonOptions);
        if (release == null) return null;

        return new ReleaseInfo
        {
            TagName = release.TagName,
            HtmlUrl = release.HtmlUrl,
            Mirrors = mirrors,
            Assets = new ReleaseAssets
            {
                Links = release.Assets
                    .Select(a => new ReleaseAssetLink { Name = a.Name, Url = a.BrowserDownloadUrl })
                    .ToArray(),
            },
        };
    }

    internal static ReleaseInfo? ParseGitLab(string json)
    {
        var release = JsonSerializer.Deserialize<ReleaseInfo>(json, JsonOptions);
        if (release == null) return null;

        // GitLab's release object names no page URL, unlike GitHub's html_url. Mirrors stay empty: a
        // GitLab download is never routed through a prefix the user typed for GitHub.
        release.HtmlUrl = $"https://gitlab.com/Mikachu2333/lertaro/-/releases/{Uri.EscapeDataString(release.TagName)}";
        return release;
    }

    // Internal rather than private so the tests can ask which of these a given settings state leaves
    // reachable (see UsableSources).
    internal sealed record ReleaseSource(string Url, bool IsGitHub);
}

public class ReleaseInfo
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    /// <summary>The release page, for the "open the release page" button.</summary>
    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [JsonPropertyName("assets")]
    public ReleaseAssets Assets { get; set; } = new ReleaseAssets();

    /// <summary>
    /// Mirror prefixes this release's downloads are tried through, in fallback order. Set by the checker
    /// for a GitHub release and left empty for a GitLab one. Not a wire field: a response that carried a
    /// "mirrors" key must not be able to name the host its own package is fetched from.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string> Mirrors { get; set; } = Array.Empty<string>();

    /// <summary>
    /// The addresses to try for one asset, in order: each mirror with the whole original URL concatenated
    /// onto it, then the original itself. That last entry is why mirrors whose owners all disappeared cost
    /// nothing but time. <c>UpdateMirrorSettings.Normalize</c> has already given every prefix exactly one
    /// trailing '/', so no slash guessing happens here.
    /// </summary>
    public IReadOnlyList<string> DownloadUrls(ReleaseAssetLink asset)
    {
        var urls = new List<string>(Mirrors.Count + 1);
        foreach (var mirror in Mirrors) urls.Add(mirror + asset.DownloadUrl);
        urls.Add(asset.DownloadUrl);
        return urls;
    }
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
    /// Empty when the release was created without a direct asset path, and for a GitHub asset, whose
    /// browser_download_url is mapped onto <see cref="Url"/> instead.
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

internal sealed class GitHubReleaseInfo
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [JsonPropertyName("assets")]
    public GitHubAsset[] Assets { get; set; } = Array.Empty<GitHubAsset>();
}

internal sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;
}