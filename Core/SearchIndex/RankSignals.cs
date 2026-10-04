namespace Lertaro.Core.SearchIndex;

// The two per-row signals every window ranks by right after match shape: how recently the row was
// created or changed (in bands), then how deep its path sits. Both live here so the engine's sort key
// (SearchResultRelevance) and the quick/inline candidate ladder (RankedCandidateOrdering) cannot drift.
//
// Bands rather than raw time, so a cache file touched a minute ago does not beat a project folder touched
// an hour ago: within a band, the shallower path wins. Depth is what lifts "D:\dev\mcptools\code-atlas"
// above the dozens of "atlas" names buried in game, engine and package trees.
public static class RankSignals
{
    public const int BandToday = 0;
    public const int BandWeek = 1;
    public const int BandMonth = 2;
    public const int BandOlder = 3;

    private const uint Day = 24 * 60 * 60;

    // Index times are Unix seconds with 0 meaning "not recorded" (FileTimeHelper). The later of created
    // and modified counts: a copied or downloaded file keeps its old modified time but gets a new
    // creation time.
    //
    // A clock drifts and some files carry nonsense dates, so a timestamp up to a day ahead of now still
    // counts as today; one further ahead is not trusted and ranks as older.
    public static int RecencyBand(uint creationUnix, uint lastWriteUnix, uint nowUnix)
    {
        var newest = Math.Max(creationUnix, lastWriteUnix);
        if (newest == 0)
            return BandOlder;
        if (newest > nowUnix)
            return newest - nowUnix <= Day ? BandToday : BandOlder;

        var age = nowUnix - newest;
        return age < Day ? BandToday : age < 7 * Day ? BandWeek : age < 30 * Day ? BandMonth : BandOlder;
    }

    // FileMetadata carries local DateTimes, with DateTime.MinValue for "not recorded".
    public static int RecencyBand(DateTime created, DateTime modified, DateTime nowUtc) =>
        RecencyBand(ToUnix(created), ToUnix(modified), FileTimeHelper.ToUnixSeconds(nowUtc));

    // Folders between the drive root and the row: "D:\" is 0, "D:\dev" is 1, "D:\dev\mcptools\code-atlas"
    // is 3. A UNC path counts its server and share like folders, which only ever ranks it lower.
    public static int PathDepth(ReadOnlySpan<char> path)
    {
        path = path.TrimEnd(['\\', '/']);
        var depth = 0;
        foreach (var c in path)
        {
            if (c is '\\' or '/')
                depth++;
        }
        return depth;
    }

    private static uint ToUnix(DateTime time) =>
        time == DateTime.MinValue ? 0 : FileTimeHelper.ToUnixSeconds(time.ToUniversalTime());
}
