using System.IO;
using System.Media;
using Lertaro.PluginSdk;

namespace Lertaro.Plugins.Calendar.Reminders;

/// <summary>
/// The one place a reminder turns into something the user can hear.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SoundPlayer"/> is the whole implementation: it is the framework's own WAV player, so
/// nothing extra ships with the plugin. It is also why the format is checked here rather than trusted
/// from the settings page -- that page's file picker offers every file there is, and the schema has no
/// way to narrow it, while <see cref="SoundPlayer"/> understands WAV and nothing else.
/// </para>
/// <para>
/// A configured file that is missing or not a WAV falls back to the default chime instead of going
/// quiet. Silence is indistinguishable from the feature being broken, and this is the one notification
/// the user is relying on to not miss an appointment.
/// </para>
/// </remarks>
internal static class ReminderSound
{
    /// <summary>Windows' own messaging chime: present on every install, so a fresh profile has a sound.</summary>
    internal static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Windows Notify Messaging.wav");

    private const string WavExtension = ".wav";

    /// <summary>
    /// Reminders due on one tick share a single chime.
    /// </summary>
    /// <remarks>
    /// <see cref="SoundPlayer"/> plays through the process-wide <c>PlaySound</c>, so a second call cuts
    /// the first off: without this, five reminders for the same minute would restart the chime five
    /// times in a row and sound like a glitch. Reminder times are whole minutes, so anything closer
    /// together than this window is the same burst by construction, never two reminders of its own.
    /// </remarks>
    private const long BurstWindowMs = 5_000;

    private static long _lastPlayedAt;

    /// <summary>Plays the reminder sound for the settings as they stand, if any.</summary>
    internal static void Play()
    {
        var path = Resolve(CalendarPlugin.ReminderSoundEnabled(), CalendarPlugin.ReminderSoundPath());
        if (path == null) return;

        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastPlayedAt);
        if (now - last < BurstWindowMs) return;
        // Losing the race means another reminder of this burst got there first, which is the outcome
        // this guard is for anyway.
        if (Interlocked.CompareExchange(ref _lastPlayedAt, now, last) != last) return;

        // Off the caller's thread: the player reads the file, and the caller is either the reminder tick
        // or the settings page's test button, neither of which should wait on the disk for a sound.
        _ = Task.Run(() =>
        {
            try
            {
                // A fresh player per play rather than one kept alive: a reused one would still be
                // holding whatever it loaded first.
                new SoundPlayer(path).Play();
            }
            catch (Exception ex)
            {
                Logger.Log($"[Calendar] reminder sound could not play: {ex.Message}", LogLevel.Warn);
            }
        });
    }

    /// <summary>The file to play for these settings, or <see langword="null"/> when nothing should be.</summary>
    /// <remarks>Testable as is: the only I/O is the existence check <see cref="IsPlayable"/> makes.</remarks>
    internal static string? Resolve(bool enabled, string? configured)
    {
        if (!enabled) return null;

        var candidate = Normalize(configured);
        if (IsPlayable(candidate)) return candidate;
        return IsPlayable(DefaultPath) ? DefaultPath : null;
    }

    /// <summary>Whether this is a file <see cref="SoundPlayer"/> can play: an existing WAV.</summary>
    internal static bool IsPlayable(string? path)
    {
        var normalized = Normalize(path);
        return normalized.EndsWith(WavExtension, StringComparison.OrdinalIgnoreCase) && File.Exists(normalized);
    }

    // Typed-in paths arrive with stray whitespace, and %WINDIR%-style ones are legal input for a text box.
    private static string Normalize(string? path) =>
        string.IsNullOrWhiteSpace(path) ? string.Empty : Environment.ExpandEnvironmentVariables(path.Trim());
}
