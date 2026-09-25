namespace Lertaro.Core.Services.Update;

/// <summary>
/// One-line note in the shared data directory saying "when this service comes back up, start the App in
/// session N".
/// </summary>
/// <remarks>
/// An update has to stop this service to unlock the files it replaces, so whatever has to happen after the
/// copy cannot live in the stopped service's memory, and it cannot be done by the copier either: the copier
/// runs elevated, and an elevated process handing a launch to the user's non-elevated shell is dropped by
/// UI Privilege Isolation (measured: the App simply never came back). The service restarting is the only
/// process that both outlives the copy and holds the privilege to start something at the session's own
/// integrity level, so the handover goes through a file.
///
/// ProgramData rather than the install directory because a portable copy's install directory is the user's
/// own data directory, and <see cref="Logger.SharedDataDir"/> resolves to the right place for both.
/// </remarks>
public static class UpdateRelaunchMarker
{
    /// <summary>
    /// How old a note may be and still be acted on. A leftover from an update that died would otherwise
    /// launch the App at some unrelated service start much later.
    /// </summary>
    internal static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(5);

    private static string FilePath => Path.Combine(Logger.SharedDataDir, "update-relaunch");

    /// <summary>
    /// Records the request. Best effort: when this fails the App will not come back on its own after the
    /// update, so it says so at Error rather than swallowing it.
    /// </summary>
    public static void Write(int sessionId, string appExePath, DateTimeOffset now)
    {
        try
        {
            Directory.CreateDirectory(Logger.SharedDataDir);
            File.WriteAllText(FilePath, $"{now.UtcTicks}\t{sessionId}\t{appExePath}");
        }
        catch (Exception ex)
        {
            Logger.Log($"[UpdateRelaunch] Could not record the pending relaunch: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>
    /// Drops the note without acting on it, for the path where the copier never started.
    /// </summary>
    public static void Clear()
    {
        try
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }
        catch (Exception ex)
        {
            Logger.Log($"[UpdateRelaunch] Could not discard the pending relaunch: {ex.Message}", LogLevel.Warn);
        }
    }

    /// <summary>
    /// Reads and consumes the note, whether or not it is acted on -- a note is only ever for one start-up.
    /// </summary>
    public static bool TryTake(out int sessionId, out string appExePath, DateTimeOffset now)
    {
        sessionId = 0;
        appExePath = string.Empty;

        string content;
        try
        {
            if (!File.Exists(FilePath))
                return false;

            content = File.ReadAllText(FilePath);
            File.Delete(FilePath);
        }
        catch (Exception ex)
        {
            Logger.Log($"[UpdateRelaunch] Could not read the pending relaunch: {ex.Message}", LogLevel.Warn);
            return false;
        }

        if (!TryParse(content, now, out sessionId, out appExePath))
            return false;

        // An App that has since been uninstalled or moved is not worth a failing launch over.
        return File.Exists(appExePath);
    }

    /// <summary>
    /// Splits the note's <c>ticks TAB session TAB path</c> and applies the freshness window. Split out of
    /// the file read because the value being parsed came from another process's write, and that is the part
    /// worth pinning down. On any refusal both outputs are left at their zero values, so a caller cannot
    /// read a session id out of a note that was just rejected.
    /// </summary>
    internal static bool TryParse(string content, DateTimeOffset now, out int sessionId, out string appExePath)
    {
        sessionId = 0;
        appExePath = string.Empty;

        var parts = content.Split('\t', 3);
        if (parts.Length != 3 || parts[2].Length == 0)
            return false;

        if (!long.TryParse(parts[0], out var writtenTicks) ||
            writtenTicks <= 0 || writtenTicks > DateTime.MaxValue.Ticks)
            return false;

        if (!int.TryParse(parts[1], out var session) || session <= 0)
            return false;

        var age = now - new DateTimeOffset(writtenTicks, TimeSpan.Zero);

        // A note from the future is as untrustworthy as an old one: a clock that moved backwards (an NTP
        // correction, a dead CMOS battery) must not make some much later start of the service launch an App.
        if (age < TimeSpan.Zero || age > FreshFor)
        {
            Logger.Log($"[UpdateRelaunch] Discarded a relaunch note (age {age}).", LogLevel.Warn);
            return false;
        }

        sessionId = session;
        appExePath = parts[2];
        return true;
    }
}
