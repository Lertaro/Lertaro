using System.IO.Pipes;
using System.Security.Principal;
using Lertaro.Core.Services.Installation;

namespace Lertaro.Core.Services.Search;

/// <summary>
/// Which indexed paths one pipe caller may see. The service indexes every file on the machine as
/// LocalSystem; without this, any signed-in user could list the contents of every other user's profile.
/// </summary>
/// <remarks>
/// Captures the caller token after reading its first request, then checks actual read/list access under
/// impersonation for every returned path. This covers custom ACLs outside profile folders, deny ACEs and
/// UAC-filtered administrators. Permissions are not cached across requests or index revisions.
/// Space queries also apply this predicate to descendants before summing sizes.
/// </remarks>
internal sealed class CallerVisibility : IDisposable
{
    public static readonly CallerVisibility Everything = new([]);

    private readonly string[] _hiddenRoots;
    private WindowsIdentity? _caller;
    private bool _denyAll;

    internal CallerVisibility(IEnumerable<string> hiddenRoots) =>
        _hiddenRoots = hiddenRoots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>
    /// Whether <paramref name="path"/> is readable and outside every hidden root. Matching is on whole path components:
    /// hiding <c>C:\Users\Bob</c> hides <c>C:\Users\Bob</c> and everything under it, not <c>C:\Users\Bobby</c>.
    /// </summary>
    public bool IsVisible(string? path)
    {
        if (_denyAll) return false;
        if (string.IsNullOrEmpty(path))
            return true;

        try
        {
            if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
            path = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }

        foreach (var root in _hiddenRoots)
        {
            if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                (path.Length == root.Length || path[root.Length] is '\\' or '/'))
                return false;
        }

        if (_caller == null) return true;
        try
        {
            return WindowsIdentity.RunImpersonated(_caller.AccessToken, () =>
            {
                // Opening both files and directories for read/listing lets Windows evaluate the actual
                // DACL, group membership and deny rules. No per-SID cache: ACL changes apply immediately.
                using var handle = Win32Api.CreateFileW(path, Win32Api.GENERIC_READ,
                    Win32Api.FILE_SHARE_READ | Win32Api.FILE_SHARE_WRITE | Win32Api.FILE_SHARE_DELETE,
                    IntPtr.Zero, Win32Api.OPEN_EXISTING, Win32Api.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
                return !handle.IsInvalid;
            });
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return false; }
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
        WindowsIdentity? caller = null;
        try
        {
            pipe.RunAsClient(() =>
            {
                caller = WindowsIdentity.GetCurrent();
            });
            if (caller?.User == null) throw new UnauthorizedAccessException("The pipe client has no user SID.");
            return new CallerVisibility([]) { _caller = caller };
        }
        catch (Exception ex)
        {
            caller?.Dispose();
            Logger.Log($"[CallerVisibility] Could not identify the pipe client: {ex.Message}", LogLevel.Warn);
            return new CallerVisibility([]) { _denyAll = true };
        }
    }

    public void Dispose() => _caller?.Dispose();
}
