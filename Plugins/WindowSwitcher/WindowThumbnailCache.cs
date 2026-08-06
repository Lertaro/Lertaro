using System.Drawing;
using System.Runtime.InteropServices;

namespace Lertaro.Plugins.WindowSwitcher;

// Caches a small in-memory Bitmap thumbnail per window handle, (re)captured on a background thread --
// GetInstantResults must stay cheap and synchronous (see SearchExecutionEngine's own "instant
// providers are cheap and synchronous" comment), so it can never block on PrintWindow itself. Mirrors
// TranslationInstantProvider's own cache+pending-set+Task.Run+SearchRefreshService pattern for async
// instant-result data: the first time a window is matched it falls back to the caller's own static
// icon, and the real thumbnail appears a keystroke or two later once the background capture lands.
internal static class WindowThumbnailCache
{
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    private sealed class Entry
    {
        public Entry(Bitmap bitmap, long capturedAtMs)
        {
            Bitmap = bitmap;
            CapturedAtMs = capturedAtMs;
        }

        public Bitmap Bitmap { get; }
        public long CapturedAtMs { get; }
    }

    private static readonly Dictionary<IntPtr, Entry> Cache = new();
    private static readonly HashSet<IntPtr> Pending = new();
    private static readonly object Lock = new();

    // Windows don't change their visual content fast enough to be worth recapturing every keystroke --
    // this bounds PrintWindow calls to at most one per window per interval, regardless of how many
    // times it's matched while the user keeps typing/narrowing their query.
    private const long StaleAfterMs = 3000;

    // Opportunistic cleanup threshold -- avoids unbounded growth over a long-running session without
    // needing a full LRU structure; a session realistically has a few dozen real windows at once.
    private const int SweepThreshold = 100;

    // Pure decision extracted from the cache-lookup orchestration below so it's unit-testable without
    // a live HWND/PrintWindow call.
    internal static bool ShouldCapture(bool hasCachedEntry, bool isPending, long ageMs) =>
        !isPending && (!hasCachedEntry || ageMs > StaleAfterMs);

    // Returns a fresh HBITMAP handle for hwnd's cached thumbnail (caller hands this to the host, which
    // takes ownership and deletes it), or IntPtr.Zero if nothing is cached yet. If nothing's cached
    // yet, or the cached entry is stale, kicks off a background (re)capture and invokes onReady once
    // a new thumbnail actually lands -- letting the caller re-trigger a search refresh so the result
    // picks up the fresh icon.
    public static IntPtr GetIconOrRefresh(IntPtr hwnd, Action onReady)
    {
        Bitmap? cachedBitmap = null;
        bool shouldCapture;

        lock (Lock)
        {
            var hasCachedEntry = Cache.TryGetValue(hwnd, out var entry);
            if (hasCachedEntry)
                cachedBitmap = entry!.Bitmap;

            var isPending = Pending.Contains(hwnd);
            var ageMs = hasCachedEntry ? Environment.TickCount64 - entry!.CapturedAtMs : long.MaxValue;

            shouldCapture = ShouldCapture(hasCachedEntry, isPending, ageMs);
            if (shouldCapture)
                Pending.Add(hwnd);

            if (!hasCachedEntry && Cache.Count >= SweepThreshold)
                SweepClosedWindows();
        }

        if (shouldCapture)
        {
            Task.Run(() =>
            {
                try
                {
                    var captured = WindowThumbnailCapture.Capture(hwnd);
                    if (captured == null)
                        return;

                    Bitmap? previous = null;
                    lock (Lock)
                    {
                        if (Cache.TryGetValue(hwnd, out var oldEntry))
                            previous = oldEntry.Bitmap;
                        Cache[hwnd] = new Entry(captured, Environment.TickCount64);
                    }
                    previous?.Dispose();
                    onReady();
                }
                finally
                {
                    lock (Lock)
                    {
                        Pending.Remove(hwnd);
                    }
                }
            });
        }

        if (cachedBitmap == null)
            return IntPtr.Zero;

        try
        {
            // GetHbitmap always creates a brand new, independent GDI handle -- safe to call repeatedly
            // on the same cached Bitmap without affecting it or any handle handed out by a previous call.
            return cachedBitmap.GetHbitmap();
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    // Caller must already hold Lock.
    private static void SweepClosedWindows()
    {
        List<IntPtr>? toRemove = null;
        foreach (var hwnd in Cache.Keys)
        {
            if (!IsWindow(hwnd))
            {
                toRemove ??= new List<IntPtr>();
                toRemove.Add(hwnd);
            }
        }
        if (toRemove == null)
            return;

        foreach (var hwnd in toRemove)
        {
            Cache[hwnd].Bitmap.Dispose();
            Cache.Remove(hwnd);
        }
    }
}
