using System.Security.Cryptography;
using System.Text;

namespace Lertaro.Core.SearchIndex;

/// <summary>
/// Builds the in-memory identity and hashes carried by a ranking explanation. Nothing here is
/// persisted: the export a user attaches to a bug report is the only way trace data leaves the app.
/// </summary>
internal static class RankingTrace
{
    internal const int SchemaVersion = 4;

    private static readonly string ProcessSalt = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
    private static long _sessionSequence;
    private static long _pageSequence;
    private static long _querySequence;

    internal static RankingLogSession BeginSession(
        string? rawQuery,
        string? canonicalQuery,
        RankingTraceWindow window = RankingTraceWindow.Quick,
        int queryVariantFlags = 0) =>
        new(
            $"s-{ProcessSalt}-{Interlocked.Increment(ref _sessionSequence):x}",
            Interlocked.Increment(ref _querySequence),
            window,
            HashKey(rawQuery ?? string.Empty),
            HashKey(canonicalQuery ?? string.Empty),
            queryVariantFlags);

    internal static RankingPageStamp ComposePage(RankingPageTrace trace) =>
        new(
            trace.Session.SearchSessionId,
            $"p-{ProcessSalt}-{Interlocked.Increment(ref _pageSequence):x}",
            trace.Session.Window,
            trace.Session.QuerySequence);

    internal static string HashKey(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToUpperInvariant())))[..12];

    internal static string HashExact(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
