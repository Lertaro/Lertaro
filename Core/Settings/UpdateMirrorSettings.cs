namespace Lertaro.Core;

/// <summary>
/// The user's GitHub download mirrors: up to three URL prefixes tried in order ahead of the release's own
/// address. Lives in Core because settings persistence and the updater have to agree on what a usable
/// prefix is. Only the GitHub source carries them -- a GitLab release is always fetched from GitLab.
/// </summary>
public static class UpdateMirrorSettings
{
    public const int MaxMirrors = 3;

    /// <summary>
    /// Whether this is a prefix the updater may request: an absolute http(s) URL naming a host.
    /// user-settings.json is hand-editable and the value ends up in an HttpClient request, so a scheme
    /// that is not http(s) is refused rather than passed on. The host check is what rejects "https://",
    /// which Uri accepts as absolute while naming nothing to connect to.
    /// </summary>
    public static bool IsValid(string? mirror) =>
        !string.IsNullOrWhiteSpace(mirror) &&
        Uri.TryCreate(mirror.Trim(), UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
        uri.Host.Length > 0;

    /// <summary>
    /// Trims a typed token. Null and whitespace both become the empty string, which is the one value that
    /// means "not set" -- the updater decides whether GitLab is asked at all from it.
    /// </summary>
    public static string NormalizeToken(string? token) => token?.Trim() ?? string.Empty;

    /// <summary>
    /// Trims, drops what <see cref="IsValid"/> refuses and duplicates, and keeps at most
    /// <see cref="MaxMirrors"/> entries in order. Every surviving prefix ends in exactly one '/', which is
    /// what lets the updater concatenate a whole original URL onto it without having to ask whether the
    /// user typed a trailing slash -- or three.
    /// </summary>
    public static List<string> Normalize(IEnumerable<string?>? mirrors)
    {
        var result = new List<string>(MaxMirrors);
        if (mirrors == null) return result;
        foreach (var mirror in mirrors)
        {
            if (!IsValid(mirror)) continue;
            var prefix = mirror!.Trim().TrimEnd('/') + "/";
            if (result.Contains(prefix, StringComparer.OrdinalIgnoreCase)) continue;
            result.Add(prefix);
            if (result.Count == MaxMirrors) break;
        }
        return result;
    }
}