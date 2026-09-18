using Lertaro.Core;

namespace Lertaro.App.Services.Favorites;

/// <summary>Why one favorite's hotkey could not be made to work, for the Settings row that owns it.</summary>
public sealed record FavoriteHotkeyFailure(int OwnerIndex, string Hotkey, string Reason);

/// <summary>
/// Owns the per-favorite global hotkeys: builds the list from settings, registers it against the
/// message-only window, and on <c>WM_HOTKEY</c> asks the foreground navigator to show that favorite's
/// folder in whichever file manager is in front.
/// </summary>
/// <remarks>
/// One instance lives for the life of the process (see <see cref="Instance"/>). It must be created and
/// refreshed on the UI thread: <c>RegisterHotKey</c> posts <c>WM_HOTKEY</c> to the thread that called
/// it, so a registration made from a background thread would be delivered to a queue nobody pumps.
/// </remarks>
public sealed class FavoriteHotkeyService : IDisposable
{
    public static FavoriteHotkeyService? Instance { get; internal set; }

    // ---------------------------------------------------------------------------------------------
    // Test seams. Harmless in production (each defaults to the real thing) and deliberately not a
    // settable static: an instance owns its own registration and navigation behaviour, so a test
    // cannot leave a process-wide hook behind for the next test to trip over.
    // ---------------------------------------------------------------------------------------------

    private readonly Func<GlobalHotkeyWindow> _windowFactory;
    private readonly Func<int, uint, uint, (bool Registered, int ErrorCode)> _register;
    private readonly Action<int> _unregister;
    private readonly Action<string> _navigate;
    private readonly Func<int, FavoriteItemSetting?> _favoriteAt;

    private GlobalHotkeyWindow? _window;
    private readonly Dictionary<int, FavoriteHotkeyRegistration> _byId = new();
    private readonly HashSet<string> _unavailableCombos = new(StringComparer.OrdinalIgnoreCase);
    private int _nextId = 1;

    internal FavoriteHotkeyService(
        Func<GlobalHotkeyWindow>? windowFactory = null,
        Func<int, uint, uint, (bool Registered, int ErrorCode)>? register = null,
        Action<int>? unregister = null,
        Action<string>? navigate = null,
        Func<int, FavoriteItemSetting?>? favoriteAt = null)
    {
        _windowFactory = windowFactory ?? (() => new GlobalHotkeyWindow());
        _register = register ?? DefaultRegister;
        _unregister = unregister ?? DefaultUnregister;
        _navigate = navigate ?? new FavoriteHotkeyForegroundNavigator().Navigate;
        _favoriteAt = favoriteAt ?? GetFavoriteFromSettings;
    }

    /// <summary>
    /// Creates the process-wide instance and its message window. Called from App startup, so every
    /// later refresh happens on the UI thread that owns the registration.
    /// </summary>
    public static FavoriteHotkeyService Initialize()
    {
        var service = new FavoriteHotkeyService();
        Instance = service;
        service.Refresh(FavoriteHotkeyRegistrations.Build(UserSettings.Load().Favorites));
        return service;
    }

    /// <summary>
    /// Re-registers every favorite hotkey from the list it is given, then reports the failures to
    /// <paramref name="onFailure"/> (used to show them on the offending Settings row). Safe to call
    /// when the receiver window could not be created: nothing registers, every request is reported as
    /// unavailable, and the rest of the app is unaffected.
    /// </summary>
    public void Refresh(IReadOnlyList<FavoriteHotkeyRegistration> requests, Action<IReadOnlyList<FavoriteHotkeyFailure>>? onFailure = null)
    {
        var failures = new List<FavoriteHotkeyFailure>();
        var unavailable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        DropRegistrations();

        foreach (var request in requests)
        {
            // Favorites that produced no registration (unset, unparsable, or a duplicate of one that
            // already won) are not failures -- the Settings row shows a hint for those instead.
            if (request.SkipReason != FavoriteHotkeySkipReason.None) continue;

            var (registered, errorCode) = _register(_nextId, request.Modifiers, request.VirtualKey);

            // The id is consumed either way. Reusing a refused one would make the NEXT combination
            // register under an id the OS already rejected for a different one -- and with one refusal
            // in the list, every favorite after it inherited that id and failed too.
            var id = _nextId++;
            if (registered)
            {
                _byId[id] = request;
                continue;
            }

            // ERROR_HOTKEY_ALREADY_REGISTERED (1409) is the ordinary case: another application already
            // owns the combination. Logged once per combo, at Warning, because the user asked for this
            // hotkey and it will not work -- but only this one, the rest still register.
            unavailable.Add(request.Hotkey);
            failures.Add(new FavoriteHotkeyFailure(request.OwnerIndex, request.Hotkey, $"Win32 {errorCode}"));
            Logger.Log($"[FavoriteHotkeys] '{request.Hotkey}' could not be registered (Win32 error {errorCode}); another application may already own it.", LogLevel.Warn);
        }

        _unavailableCombos.Clear();
        foreach (var combo in unavailable) _unavailableCombos.Add(combo);

        onFailure?.Invoke(failures);
    }

    /// <summary>
    /// Whether the most recent <see cref="Refresh"/> failed to register this combination. The Settings
    /// page uses "was unavailable before this apply, and still is" to decide whether showing the error is
    /// news: a combination another application owns stays unavailable on every later apply, and
    /// re-announcing it each time would make an unrelated, successful edit look like it failed.
    /// </summary>
    public bool IsNewlyUnavailable(string? hotkey) =>
        !string.IsNullOrWhiteSpace(hotkey) && _unavailableCombos.Contains(hotkey.Trim());

    /// <summary>The favorite index a registered combination belongs to, or -1 when nothing owns it.</summary>
    public int FindOwnerIndex(int hotkeyId) =>
        _byId.TryGetValue(hotkeyId, out var registration) ? registration.OwnerIndex : -1;

    /// <summary>Maps a fired hotkey id back to its favorite and navigates the foreground file manager.</summary>
    internal void HandleHotkeyId(int hotkeyId)
    {
        if (!_byId.TryGetValue(hotkeyId, out var registration)) return;

        var favorite = _favoriteAt(registration.OwnerIndex);
        if (favorite == null) return;

        _navigate(favorite.Path);
    }

    private static FavoriteItemSetting? GetFavoriteFromSettings(int index)
    {
        var favorites = UserSettings.Load().Favorites;
        return index >= 0 && index < favorites.Count ? favorites[index] : null;
    }

    private void DropRegistrations()
    {
        foreach (var id in _byId.Keys) _unregister(id);
        _byId.Clear();

        // Ids restart from 1 on each refresh. Every id from the previous list was just unregistered, so
        // a recycled one cannot resolve to a stale entry -- _byId is empty at this point, and only
        // combinations registered below can be looked up again.
        _nextId = 1;
    }

    private (bool Registered, int ErrorCode) DefaultRegister(int id, uint modifiers, uint virtualKey)
    {
        _window ??= _windowFactory();
        if (!_window.IsAvailable)
        {
            Logger.Log("[FavoriteHotkeys] No message window available; per-favorite hotkeys are disabled.", LogLevel.Warn);
            return (false, 0);
        }

        return _window.Register(id, modifiers, virtualKey);
    }

    private void DefaultUnregister(int id)
    {
        if (_window == null) return;
        _window.Unregister(id);
    }

    /// <summary>Installs the <c>WM_HOTKEY</c> handler. Called once, from App startup.</summary>
    public void AttachHandler()
    {
        _window ??= _windowFactory();
        _window.SetHandler(HandleHotkeyId);
    }

    public void Dispose()
    {
        DropRegistrations();
        _window?.Dispose();
        _window = null;

        if (ReferenceEquals(Instance, this)) Instance = null;
    }
}
