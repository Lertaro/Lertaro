using System.Windows.Interop;

namespace Lertaro.App.Services.Favorites;

/// <summary>
/// The <c>WM_HOTKEY</c> receiver: a message-only window owned by a specific thread, plus the
/// registration table for the hotkeys registered against it.
/// </summary>
/// <remarks>
/// Must be created on the thread that owns the App's message pump (the Dispatcher). Windows posts
/// <c>WM_HOTKEY</c> to the <em>thread</em> that called <c>RegisterHotKey</c>, and only a thread that
/// pumps messages -- meaning only the UI thread here -- will ever see it.
/// </remarks>
internal class GlobalHotkeyWindow : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly HwndSource? _source;

    /// <summary>Hotkey ids already accepted by the OS, so they are unregistered with the same hwnd.</summary>
    private readonly HashSet<int> _registeredIds = new();

    public GlobalHotkeyWindow()
    {
        _hwnd = FavoriteHotkeyNativeMethods.CreateMessageWindow();
        if (_hwnd != IntPtr.Zero)
            _source = HwndSource.FromHwnd(_hwnd);
    }

    /// <summary>
    /// False when the OS refused the receiver window, in which case no hotkey can be registered.
    /// Overridable so the registration policy can be tested without an HWND -- the Win32 half is not
    /// what a unit test should be exercising.
    /// </summary>
    public virtual bool IsAvailable => _hwnd != IntPtr.Zero;

    /// <summary>
    /// Registers one combination. Existing registrations for the same id are dropped first so a
    /// re-entrant call (settings applied twice, a refresh after a failed one) cannot leave a stale
    /// registration behind under a different combination.
    /// </summary>
    public virtual (bool Registered, int ErrorCode) Register(int id, uint modifiers, uint virtualKey)
    {
        if (!IsAvailable) return (false, 0);

        Unregister(id);

        try
        {
            if (FavoriteHotkeyNativeMethods.RegisterHotKey(_hwnd, id, modifiers, virtualKey))
            {
                _registeredIds.Add(id);
                return (true, 0);
            }

            return (false, FavoriteHotkeyNativeMethods.LastError());
        }
        catch (Exception ex)
        {
            // Never let a P/Invoke failure escape into the caller's refresh loop, which still has the
            // remaining favorites to register.
            Core.Logger.Log($"[FavoriteHotkeys] RegisterHotKey threw for id {id}: {ex.Message}", Core.LogLevel.Error);
            return (false, 0);
        }
    }

    public virtual void Unregister(int id)
    {
        if (!IsAvailable || !_registeredIds.Remove(id)) return;

        try { FavoriteHotkeyNativeMethods.UnregisterHotKey(_hwnd, id); }
        catch (Exception ex)
        {
            Core.Logger.Log($"[FavoriteHotkeys] UnregisterHotKey threw for id {id}: {ex.Message}", Core.LogLevel.Error);
        }
    }

    /// <summary>
    /// Installs the <c>WM_HOTKEY</c> handler. The message is left unmarked as handled: it belongs to a
    /// message-only window nobody else looks at, and marking it would only hide it from a future
    /// low-level hook.
    /// </summary>
    public virtual void SetHandler(Action<int> onHotkeyId)
    {
        if (_source == null) return;

        _source.AddHook((IntPtr _, int message, IntPtr wParam, IntPtr _, ref bool _) =>
        {
            if (message != FavoriteHotkeyNativeMethods.WmHotKey) return IntPtr.Zero;

            // The whole body is inside the try: a throw out of a window procedure would tear down the
            // message pump, which is the one thing this feature must never do.
            try
            {
                onHotkeyId(wParam.ToInt32());
            }
            catch (Exception ex)
            {
                Core.Logger.Log($"[FavoriteHotkeys] Hotkey handler threw: {ex}", Core.LogLevel.Error);
            }

            return IntPtr.Zero;
        });
    }

    public virtual void Dispose()
    {
        if (_source != null) _source.Dispose();

        if (_hwnd != IntPtr.Zero)
        {
            try { FavoriteHotkeyNativeMethods.DestroyWindow(_hwnd); }
            catch { /* teardown only: the process is exiting either way */ }
        }

        _registeredIds.Clear();
    }
}
