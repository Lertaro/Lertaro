using System.Runtime.InteropServices;
using Lertaro.App.Services.Favorites;

namespace Lertaro.App.Services;

/// <summary>
/// Answers one question about a global hotkey combination before the settings save it: does the OS say
/// it is already taken? The hook hotkeys are not <c>RegisterHotKey</c> registrations -- the low-level
/// hook sees every key -- but a combination the system or another program has registered is a
/// competition the hook only sometimes wins: it sees the key first and usually consumes it, yet every
/// path that stands down (blacklisted foreground, a fullscreen app, a text input that wants the key)
/// hands the keystroke to whoever registered it, so the feature fires only when it feels like it.
/// Probing at save time turns that half-broken state into an honest refusal instead.
/// </summary>
/// <remarks>
/// Probed with <c>RegisterHotKey</c> itself, against no window: the API accepts a null one and posts
/// <c>WM_HOTKEY</c> to the calling thread's queue, where nothing waits for it -- the registration is
/// undone before returning, and no key is ever pressed in between. Anything the OS refuses counts as
/// taken; the ordinary refusal is ERROR_HOTKEY_ALREADY_REGISTERED, and the rarer ones (a reserved
/// combination, a secure-desktop owned one) are the same news for the user's purposes.
/// </remarks>
internal static class HotkeyConflictProbe
{
    // Distinct from the favorites' ids: those live on their own window, this probe registers against
    // none, and the two never meet -- the separation is so a probe can never collide with a real id.
    private const int ProbeId = 0x4C52544B;

    /// <summary>Whether the combination cannot be registered right now because someone else owns it.</summary>
    public static bool IsTaken(uint modifiers, uint virtualKey)
    {
        var registered = false;
        try
        {
            registered = FavoriteHotkeyNativeMethods.RegisterHotKey(IntPtr.Zero, ProbeId, modifiers, virtualKey);
            if (registered)
                return false;

            Core.Logger.Log(
                $"[HotkeyConflictProbe] Combination refused by RegisterHotKey (Win32 error {FavoriteHotkeyNativeMethods.LastError()}).",
                Core.LogLevel.Debug);
            return true;
        }
        catch (Exception ex)
        {
            // The probe must never be the thing that breaks a settings save; an unreachable OS answers
            // "not taken" and the hotkey saves as configured, exactly as it did before this existed.
            Core.Logger.Log($"[HotkeyConflictProbe] Probe failed: {ex.Message}", Core.LogLevel.Warn);
            return false;
        }
        finally
        {
            if (registered)
                FavoriteHotkeyNativeMethods.UnregisterHotKey(IntPtr.Zero, ProbeId);
        }
    }
}
