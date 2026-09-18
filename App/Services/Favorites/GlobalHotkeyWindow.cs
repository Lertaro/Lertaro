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
///
/// The window is one WPF created (<see cref="HwndSource"/>), not one made with a raw
/// <c>CreateWindowEx</c>: seeing <c>WM_HOTKEY</c> from managed code needs a hook on a WPF-owned window,
/// and <c>HwndSource.FromHwnd</c> answers null for a window WPF did not create. That is how this feature
/// first shipped -- the registration succeeded and the message was posted, but the window's own procedure
/// discarded it, so every hotkey did nothing and logged nothing at all. A message-only parent
/// (<see cref="FavoriteHotkeyNativeMethods.HwndMessage"/>) keeps the window off the desktop, out of window
/// enumeration and out of the taskbar, which is everything a message sink needs.
/// </remarks>
internal class GlobalHotkeyWindow : IDisposable
{
    private const string MessageWindowName = "LertaroFavoriteHotkeys";

    private IntPtr _hwnd;
    private HwndSource? _source;

    /// <summary>Hotkey ids already accepted by the OS, so they are unregistered with the same hwnd.</summary>
    private readonly HashSet<int> _registeredIds = new();

    /// <summary>
    /// False when the OS refused the receiver window, in which case no hotkey can be registered.
    /// Overridable so the registration policy can be tested without an HWND -- the Win32 half is not
    /// what a unit test should be exercising.
    /// </summary>
    public virtual bool IsAvailable
    {
        get
        {
            EnsureMessageWindow();
            return _hwnd != IntPtr.Zero;
        }
    }

    /// <summary>
    /// Creates the receiver window on first use rather than in the constructor: a user who has configured
    /// no per-favorite hotkeys should not get one at all, and it keeps this type constructible without
    /// touching the OS.
    /// </summary>
    private void EnsureMessageWindow()
    {
        if (_source != null || _hwnd != IntPtr.Zero) return;

        try
        {
            _source = new HwndSource(new HwndSourceParameters(MessageWindowName)
            {
                ParentWindow = FavoriteHotkeyNativeMethods.HwndMessage,
                WindowStyle = 0,
                Width = 0,
                Height = 0
            });
            _hwnd = _source.Handle;
        }
        catch (Exception ex)
        {
            // Degrade to "no per-favorite hotkeys" with one line instead of taking startup down: how the
            // OS treats window creation is the one thing here that is outside this process's control.
            // _source is left null, which is also what Unregister and SetHandler test for.
            _source = null;
            _hwnd = IntPtr.Zero;
            Core.Logger.Log($"[FavoriteHotkeys] Could not create the hotkey message window: {ex.Message}", Core.LogLevel.Warn);
        }
    }

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
        EnsureMessageWindow();
        if (_source == null)
        {
            // Only reachable when the window could not be created at all, which EnsureMessageWindow has
            // already logged. Said out loud here because the silent version of this return is exactly how
            // a registered-but-dead hotkey went unnoticed.
            Core.Logger.Log("[FavoriteHotkeys] No message window, so the hotkey handler is not installed.", Core.LogLevel.Warn);
            return;
        }

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
        // Disposing the HwndSource destroys the window it owns; there is nothing separate to destroy.
        _source?.Dispose();
        _source = null;
        _hwnd = IntPtr.Zero;

        _registeredIds.Clear();
    }
}
