using System.Runtime.InteropServices;

namespace Lertaro.Plugins.CoreExtensions.Preview.Handlers;

// Session-scoped cache of out-of-process preview handlers, keyed by CLSID. Keeping handlers (and their
// prevhost surrogates) alive across previews means navigating between files of a type — or back and forth
// between types — reuses the same handler instead of re-spawning prevhost every time. Idle pooled handlers
// are Unloaded (holding no file lock); everything is released together on EndPreviewSession (owner closed).
// A small LRU cap bounds how many prevhost processes stay resident. UI-thread / STA only.
internal sealed class PreviewHandlerPool
{
    private const int MaxHandlers = 4;
    private readonly Dictionary<Guid, object> _handlers = new();
    private readonly LinkedList<Guid> _lru = new(); // first = most recently used

    // Returns the COM handler for clsid (creating it out-of-process if needed), or null on failure.
    // The returned object is marked most-recently-used, so it is safe from eviction until another is taken.
    public object? Acquire(Guid clsid)
    {
        if (_handlers.TryGetValue(clsid, out var existing))
        {
            Touch(clsid);
            return existing;
        }

        var com = CoCreate(clsid);
        if (com == null) return null;

        _handlers[clsid] = com;
        _lru.AddFirst(clsid);
        EvictIfNeeded(keep: clsid);
        return com;
    }

    public void ReleaseAll()
    {
        foreach (var clsid in _handlers.Keys.ToList())
            Release(clsid);
        _lru.Clear();
    }

    private void Touch(Guid clsid)
    {
        _lru.Remove(clsid);
        _lru.AddFirst(clsid);
    }

    private void EvictIfNeeded(Guid keep)
    {
        while (_handlers.Count > MaxHandlers)
        {
            var node = _lru.Last;
            while (node != null && node.Value == keep) node = node.Previous;
            if (node == null) break; // nothing evictable but the one we must keep
            _lru.Remove(node);
            Release(node.Value);
        }
    }

    private void Release(Guid clsid)
    {
        if (!_handlers.Remove(clsid, out var com)) return;
        if (com is IPreviewHandler h)
        {
            try { h.Unload(); } catch { }
        }
        try { Marshal.FinalReleaseComObject(com); } catch { }
    }

    private static object? CoCreate(Guid clsid)
    {
        try
        {
            // Out-of-process (prevhost surrogate) so a crashing handler can't take the app down.
            var clsctx = PreviewHandlerInterop.CLSCTX.LOCAL_SERVER | PreviewHandlerInterop.CLSCTX.NO_CODE_DOWNLOAD;
            var iid = PreviewHandlerInterop.IID_IPreviewHandler;
            if (PreviewHandlerInterop.CoCreateInstance(clsid, IntPtr.Zero, clsctx, iid, out var pUnk) != 0 || pUnk == IntPtr.Zero)
                return null;
            var com = Marshal.GetObjectForIUnknown(pUnk);
            Marshal.Release(pUnk);
            return com;
        }
        catch
        {
            return null;
        }
    }
}
