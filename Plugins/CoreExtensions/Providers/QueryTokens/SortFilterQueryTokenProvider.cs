using System.Globalization;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.QueryTokens;

// Built-in implementation of the "<key>" / ">key" query suffix tokens: sort by a property, and
// optionally keep only results on one side of a threshold.
//
//   <s          sort by size, smallest first
//   >s          sort by size, largest first
//   <s>20m      sort by size, and keep only files larger than 20 MB
//   >c>2008.8.3 sort by creation time newest first, and keep only items created after 2008-08-03
//
// The trigger characters are '<' and '>' because neither is legal in a Windows file name, so a token
// can never be confused with text the user wants to search for.
public class SortFilterQueryTokenProvider : IQueryTokenProvider
{
    public const string PluginId = "Lertaro.Plugins.CoreExtensions";
    public const string SettingKey = "SortFilterPrefix";

    // Keys: size, created, modified, accessed, and folder/file. An unknown key is not claimed.
    private static readonly Dictionary<string, SortKey> KeysByLetter = new(StringComparer.OrdinalIgnoreCase)
    {
        ["s"] = SortKey.Size,
        ["c"] = SortKey.Created,
        ["m"] = SortKey.Modified,
        ["a"] = SortKey.Accessed,
        ["f"] = SortKey.IsDirectory
    };

    public string Name => TranslationService.Get("CoreExtensions_QueryTokenProvider_Name");

    // A token is "<" or ">" followed by a key letter, and optionally a second "<"/">" plus a threshold
    // ("<s>20m", ">c>2008.8.3"). The prefix character is configurable, so callers can move the whole
    // family off '<'/'>' if they ever need to.
    //
    // The second trigger must be present only as part of a threshold: a lone "<s>" is not a sort token,
    // it is an unparseable query, and claiming it would swallow the user's text and return nothing.
    public bool CanHandle(string token)
    {
        if (token.Length < 2 || !IsTrigger(token[0]))
            return false;

        if (!KeysByLetter.ContainsKey(token[1].ToString()))
            return false;

        // A bare "<s" (no threshold) is complete. Anything longer must be a well-formed threshold, i.e.
        // a trigger followed by something -- "<s>" has a dangling trigger and is rejected.
        return token.Length == 2 || (IsTrigger(token[2]) && token.Length > 3);
    }

    private static bool IsTrigger(char c) => c == GetConfiguredPrefix() || c == GetDescendingPrefix(GetConfiguredPrefix());

    public Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results)
    {
        if (results == null || results.Count == 0)
            return Task.FromResult<IReadOnlyList<ISearchResult>>(Array.Empty<ISearchResult>());

        var ascending = token[0] == GetConfiguredPrefix();
        var key = KeysByLetter[token[1].ToString()];
        var body = token.Length > 2 ? token[2..] : string.Empty;
        var (hasThreshold, thresholdAscending, rawThreshold) = SplitThreshold(body);

        IEnumerable<ISearchResult> query = results;

        if (hasThreshold)
        {
            var predicate = BuildThresholdPredicate(key, thresholdAscending, rawThreshold);
            if (predicate != null)
                query = query.Where(predicate);
        }

        // Materialized before ordering: the source is the caller's list, and this provider must not
        // hand back a deferred query that re-reads it after the caller has moved on.
        var materialized = query as ICollection<ISearchResult> ?? query.ToList();

        var ordered = ascending
            ? materialized.OrderBy(SelectorFor(key))
            : materialized.OrderByDescending(SelectorFor(key));

        return Task.FromResult<IReadOnlyList<ISearchResult>>(ordered.ToList());
    }

    public string? GetHighlightText(string token) => null;

    // The second trigger names the comparison directly, independent of the sort direction it follows:
    // "<s>20m" keeps files whose size is GREATER than 20 MB, and "<s<20m" keeps files smaller than it.
    // That is why ">" here means a lower bound and "<" an upper bound -- this trigger is a comparison
    // operator, not a mirror of the sort arrow it sits next to.
    private static (bool HasThreshold, bool IsLowerBound, string Raw) SplitThreshold(string body)
    {
        if (body.Length < 2)
            return (false, false, string.Empty);

        if (body[0] == GetDescendingPrefix(GetConfiguredPrefix()))
            return (true, true, body[1..]);
        if (body[0] == GetConfiguredPrefix())
            return (true, false, body[1..]);

        return (false, false, string.Empty);
    }

    // Returns null when the threshold text can't be parsed, so an unparseable threshold narrows nothing
    // rather than silently dropping every result.
    private static Func<ISearchResult, bool>? BuildThresholdPredicate(SortKey key, bool isLowerBound, string raw)
    {
        var threshold = raw.Trim();
        if (threshold.Length == 0)
            return null;

        if (key == SortKey.IsDirectory)
        {
            // Folders are not ordered, so the only meaningful threshold is membership: "<f>f/folder"
            // keeps directories and "<f<f" keeps everything else, mirroring ">20m" = "on this side".
            var wantsDirectory = isLowerBound && threshold.StartsWith('f');
            return r => r.IsDir == wantsDirectory;
        }

        if (key == SortKey.Size)
        {
            if (!TryParseSize(threshold, out var bytes))
                return null;
            return isLowerBound ? r => r.Metadata.Size > bytes : r => r.Metadata.Size < bytes;
        }

        if (!TryParseDate(threshold, out var date))
            return null;

        Func<ISearchResult, DateTime> getter = key switch
        {
            SortKey.Created => r => r.Metadata.Created,
            SortKey.Modified => r => r.Metadata.Modified,
            _ => r => r.Metadata.Accessed
        };

        return isLowerBound ? r => getter(r) > date : r => getter(r) < date;
    }

    private static Func<ISearchResult, IComparable> SelectorFor(SortKey key) => key switch
    {
        SortKey.Size => r => r.Metadata.Size,
        SortKey.Created => r => r.Metadata.Created,
        SortKey.Modified => r => r.Metadata.Modified,
        SortKey.Accessed => r => r.Metadata.Accessed,
        _ => r => r.IsDir
    };

    // "20m" / "1.5g" / "512k" / a plain byte count. Binary units, matching how Explorer reports sizes.
    internal static bool TryParseSize(string text, out long bytes)
    {
        bytes = 0;
        if (text.Length == 0)
            return false;

        var multiplier = 1L;
        var last = char.ToLowerInvariant(text[^1]);
        var digits = text;
        switch (last)
        {
            case 'k': multiplier = 1024L; digits = text[..^1]; break;
            case 'm': multiplier = 1024L * 1024; digits = text[..^1]; break;
            case 'g': multiplier = 1024L * 1024 * 1024; digits = text[..^1]; break;
            case 't': multiplier = 1024L * 1024 * 1024 * 1024; digits = text[..^1]; break;
        }

        if (!double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return false;

        bytes = (long)(value * multiplier);
        return true;
    }

    // Only these shapes are accepted -- deliberately not DateTime.TryParse's guessing, which reads
    // "20030803" differently depending on the machine's culture and would make the same query mean
    // different things on different machines.
    //
    // The set is 3 separators x 2 year widths x 2 month/day widths, plus the compact 8-digit form:
    // every accepted string is YEAR-MONTH-DAY. A two-digit year is read as 20xx ("03" -> 2003), which
    // is the only sensible reading for a file-date filter. Mixing separators ("2003-08.03") is not
    // accepted, and neither is a month-first reading ("03/08/2003") -- the order is fixed, so one query
    // cannot mean two different days on two machines.
    //
    // The year-only and year-month forms ("2008", "2008.8") are kept: a threshold only needs day
    // precision, and "<c>2008" is a natural way to say "created during 2008".
    private static readonly string[] DateFormats =
    {
        // '-'
        "yyyy-M-d", "yyyy-MM-dd", "yy-M-d", "yy-MM-dd",
        "yyyy-M", "yy-M",
        // '.'
        "yyyy.M.d", "yyyy.MM.dd", "yy.M.d", "yy.MM.dd",
        "yyyy.M", "yy.M",
        // '/'
        "yyyy/M/d", "yyyy/MM/dd", "yy/M/d", "yy/MM/dd",
        "yyyy/M", "yy/M",
        // compact
        "yyyyMMdd",
        "yyyy"
    };

    internal static bool TryParseDate(string text, out DateTime value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Separators may not be mixed: "2003-08.03" is not a date, it is a typo, and accepting it would
        // reintroduce the culture guessing the whitelist exists to prevent.
        if (HasMixedSeparators(text.Trim()))
            return false;

        return DateTime.TryParseExact(text.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    private static bool HasMixedSeparators(string text)
    {
        var seen = '\0';
        foreach (var c in text)
        {
            if (c is not ('-' or '.' or '/'))
                continue;
            if (seen == '\0')
                seen = c;
            else if (seen != c)
                return true;
        }

        return false;
    }

    private static char GetConfiguredPrefix()
    {
        var prefix = PluginSettingsService.GetSetting(PluginId, SettingKey, "<");
        return string.IsNullOrEmpty(prefix) ? '<' : prefix[0];
    }

    private static char GetDescendingPrefix(char ascending) => ascending switch
    {
        '<' => '>',
        '>' => '<',
        _ => ascending == '>' ? '<' : '>'
    };

    private enum SortKey
    {
        Size,
        Created,
        Modified,
        Accessed,
        IsDirectory
    }
}
