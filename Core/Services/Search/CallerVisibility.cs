using System.IO.Pipes;
using System.Security.Principal;
using Lertaro.Core.Services.Installation;

namespace Lertaro.Core.Services.Search;

/// <summary>
/// Which indexed paths one pipe caller may see. The service indexes every file on the machine as
/// LocalSystem; without this, any signed-in user could list the contents of every other user's profile.
/// </summary>
/// <remarks>
/// Decided once per connection from the caller's own token. An elevated administrator sees everything. Any
/// other caller, including an administrator running without elevation (a filtered token, the same one
/// Explorer would stop at another user's profile with a UAC prompt), does not see the profile folders of
/// the other accounts on this machine, taken from ProfileList in the registry.
///
/// ponytail: only other users' profile folders are hidden. A folder elsewhere that its owner locked down with
/// its own permissions stays visible to searches, and GetSpaceEntries' folder sizes above a profile (C:\Users)
/// still count the files inside other profiles. The upgrade is a per-directory AccessCheck against the
/// caller's token, cached per SID.
///
/// The profile map is read from the registry once per connection too, and it is the dearer half of the round
/// trip <see cref="ServicePipe"/> counts: measured at 203 us median, 357 us p95 with five accounts on this
/// machine, one key opened per account, so it grows with the profile count. That still fits inside one
/// keystroke's budget. Caching it would need an invalidation story ProfileList does not offer -- a profile
/// appears at a new account's first logon and nothing announces it -- so the map is read fresh.
/// </remarks>
internal sealed class CallerVisibility
{
    public static readonly CallerVisibility Everything = new([]);

    private readonly string[] _hiddenRoots;

    internal CallerVisibility(IEnumerable<string> hiddenRoots) =>
        _hiddenRoots = hiddenRoots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => Path.TrimEndingDirectorySeparator(root))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>
    /// Whether <paramref name="path"/> is outside every hidden root. Matching is on whole path components:
    /// hiding <c>C:\Users\Bob</c> hides <c>C:\Users\Bob</c> and everything under it, not <c>C:\Users\Bobby</c>.
    /// </summary>
    public bool IsVisible(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return true;

        foreach (var root in _hiddenRoots)
        {
            if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                (path.Length == root.Length || path[root.Length] is '\\' or '/'))
                return false;
        }

        return true;
    }

    /// <summary>
    /// The visibility for <paramref name="caller"/> given every profile on the machine (SID to profile
    /// folder). An unknown caller (null) is treated as nobody's profile owner and sees none of them.
    /// </summary>
    internal static CallerVisibility For(SecurityIdentifier? caller, bool isElevatedAdmin, IReadOnlyDictionary<string, string> profiles) =>
        isElevatedAdmin
            ? Everything
            : new CallerVisibility(profiles
                .Where(profile => caller is null || !string.Equals(profile.Key, caller.Value, StringComparison.OrdinalIgnoreCase))
                .Select(profile => profile.Value));

    /// <summary>
    /// Identifies the client on the other end of <paramref name="pipe"/> by impersonating it. Needs a
    /// request to have been read from the pipe first. Fails closed: when the caller cannot be identified,
    /// it sees no one's profile.
    /// </summary>
    public static CallerVisibility ForClient(NamedPipeServerStream pipe)
    {
        SecurityIdentifier? user = null;
        var isElevatedAdmin = false;
        try
        {
            pipe.RunAsClient(() =>
            {
                using var identity = WindowsIdentity.GetCurrent();
                user = identity.User;
                isElevatedAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"[CallerVisibility] Could not identify the pipe client, hiding every profile: {ex.Message}", LogLevel.Warn);
            user = null;
            isElevatedAdmin = false;
        }

        return For(user, isElevatedAdmin, UserProfiles.Read());
    }
}
