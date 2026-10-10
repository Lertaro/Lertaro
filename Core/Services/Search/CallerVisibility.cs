using System.IO.Pipes;
using System.Security.Principal;
using Lertaro.Core.Services.Installation;

namespace Lertaro.Core.Services.Search;

/// <summary>
/// Which indexed paths one pipe caller may see. The service indexes every file on the machine as
/// LocalSystem; without this, any signed-in user could list the contents of every other user's profile.
/// </summary>
/// <remarks>
/// Search results use the caller's profile-directory exclusions, captured once per connection. Direct
/// directory/metadata requests still check current read/list access under the caller's identity.
/// ponytail: indexed search can list names with custom deny ACLs outside excluded profiles. Per-file
/// opens cost seconds for broad queries; finer search visibility needs permissions in the index.
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
        if (_caller == null) return IsVisibleCore(path);
        try
        {
            return WindowsIdentity.RunImpersonated(_caller.AccessToken, () => IsVisibleCore(path));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return false; }
    }

    // Only for canonical paths produced by the index, never caller-supplied request paths. No file I/O:
    // even GetFullPath can probe the filesystem to expand '~' components on Windows.
    internal bool IsIndexedPathVisible(string? path)
    {
        if (_denyAll || string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path) ||
            (path[0] is '\\' or '/') || path.Contains('\0'))
            return false;

        path = path.Replace('/', '\\');
        // An index never emits dot segments. Reject them rather than allowing traversal around a
        // profile prefix if a future caller accidentally passes an unnormalized path here.
        if (path.Contains(@"\..\", StringComparison.Ordinal) || path.EndsWith(@"\..", StringComparison.Ordinal) ||
            path.Contains(@"\.\", StringComparison.Ordinal) || path.EndsWith(@"\.", StringComparison.Ordinal))
            return false;

        return IsOutsideHiddenRoots(path);
    }

    // With a captured caller, IsVisible runs this under that caller's identity.
    private bool IsVisibleCore(string? path)
    {
        if (_denyAll) return false;
        if (string.IsNullOrEmpty(path))
            return true;

        try
        {
            if (!Path.IsPathFullyQualified(path) || (path[0] is '\\' or '/') || path.Contains('\0')) return false;
            // Canonical spelling is needed for the lexical hidden-root check. The real caller check
            // opens the path itself, so Windows resolves it and checks the actual target's ACL already.
            // GetFullPath would additionally probe the filesystem to expand every '~' path component.
            if (_hiddenRoots.Length > 0)
                path = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }

        if (!IsOutsideHiddenRoots(path)) return false;

        if (_caller == null) return true;
        try
        {
            // Opening both files and directories for read/listing lets Windows evaluate the actual
            // DACL, group membership and deny rules. No per-SID cache: ACL changes apply immediately.
            using var handle = Win32Api.CreateFileW(path, Win32Api.GENERIC_READ,
                Win32Api.FILE_SHARE_READ | Win32Api.FILE_SHARE_WRITE | Win32Api.FILE_SHARE_DELETE,
                IntPtr.Zero, Win32Api.OPEN_EXISTING, Win32Api.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
            return !handle.IsInvalid;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return false; }
    }

    private bool IsOutsideHiddenRoots(string path)
    {
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
        WindowsIdentity? caller = null;
        try
        {
            pipe.RunAsClient(() =>
            {
                caller = WindowsIdentity.GetCurrent();
            });
            if (caller?.User == null) throw new UnauthorizedAccessException("The pipe client has no user SID.");
            var profileVisibility = For(caller.User,
                new WindowsPrincipal(caller).IsInRole(WindowsBuiltInRole.Administrator), UserProfiles.Read());
            // Everything is shared; keep the disposable caller token on a connection-owned instance.
            return new CallerVisibility(profileVisibility._hiddenRoots) { _caller = caller };
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
