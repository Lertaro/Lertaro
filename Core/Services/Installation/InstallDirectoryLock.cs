using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Lertaro.Core.Services.Installation;

/// <summary>
/// Takes a directory tree that a privileged process writes or runs from and makes it writable by SYSTEM and
/// Administrators only: owner Administrators, a protected DACL at the top, and every entry below reset to
/// what it inherits from there.
/// </summary>
/// <remarks>
/// The directories this is pointed at start out writable by ordinary users (%ProgramData% lets anyone create
/// files in a subfolder; a portable copy sits wherever it was unzipped), so anything already inside may have
/// been planted by one of them: a junction that sends a SYSTEM write somewhere else, a hard link to a file
/// elsewhere, an entry whose owner can re-grant itself access after the reset.
///
/// So the walk never trusts a path twice. Each entry is opened once, without following reparse points, and
/// everything after that goes through the handle: what the entry is, its new owner and DACL, or its removal.
/// Links are removed rather than followed (a junction or symlink here was never written by this product, and
/// the name of a hard link is dropped without touching the file it shares). It runs top-down, so by the time
/// a directory is listed it and every parent already belong to Administrators, and nobody else can swap a
/// name inside it any more. SetKernelObjectSecurity rather than SetNamedSecurityInfo because the latter
/// resolves paths and walks children by itself, following the same links this is trying to strip.
///
/// ponytail: an attacker holding a handle opened before the reset keeps the access that handle was granted,
/// so a determined local user could still race the walk from a pre-opened directory handle. Closing that
/// needs the enumeration itself done by handle (NtQueryDirectoryFile relative to the parent handle).
/// </remarks>
public static class InstallDirectoryLock
{
    internal static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);
    internal static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    internal static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);

    private const AceFlags Inheritable = AceFlags.ObjectInherit | AceFlags.ContainerInherit;

    /// <summary>
    /// The protected DACL at the top of a locked tree, and the owner of every entry that inherits it.
    /// </summary>
    internal sealed record Zone(SecurityIdentifier Owner, IReadOnlyList<CommonAce> Aces);

    /// <summary>SYSTEM and Administrators full control, Users read and execute, applied to everything below.</summary>
    internal static Zone ReadOnlyForUsers { get; } = new(Administrators,
    [
        Allow(LocalSystem, FileSystemRights.FullControl, Inheritable),
        Allow(Administrators, FileSystemRights.FullControl, Inheritable),
        Allow(Users, FileSystemRights.ReadAndExecute, Inheritable),
    ]);

    /// <summary>
    /// SYSTEM and Administrators only. For the drive indexes: they list every file on the disk, including
    /// other users' profiles, which the service filters before answering a caller.
    /// </summary>
    internal static Zone PrivateToService { get; } = new(Administrators,
    [
        Allow(LocalSystem, FileSystemRights.FullControl, Inheritable),
        Allow(Administrators, FileSystemRights.FullControl, Inheritable),
    ]);

    /// <summary>What a lock removed (planted links) and what it could not reset; the caller logs both.</summary>
    public sealed class Report
    {
        public List<string> Removed { get; } = [];
        public List<string> Failed { get; } = [];
    }

    /// <summary>
    /// Locks the machine-wide data directory the service writes as LocalSystem (logs, the relaunch note,
    /// machine settings, indexes). Users keep read access to all of it but the indexes. The report is for
    /// the caller to log: this runs before the service's log is opened, because opening it is one of the
    /// writes a planted link would redirect.
    /// </summary>
    public static Report LockSharedDataDirectory(string directory)
    {
        // A pre-planted junction in place of the directory itself: remove the link, not what it points at
        // (Directory.Delete without recursion on a reparse point deletes only the link).
        if (Directory.Exists(directory) && File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
            Directory.Delete(directory);
        Directory.CreateDirectory(directory);

        // LocalDriveCacheLocator.DefaultCacheDir, relative to the directory being locked.
        var indexes = Path.Combine(directory, "indexes");
        return Lock(directory, ReadOnlyForUsers,
            path => string.Equals(path, indexes, StringComparison.OrdinalIgnoreCase) ? PrivateToService : null);
    }

    internal static void Merge(Report into, Report from)
    {
        into.Removed.AddRange(from.Removed);
        into.Failed.AddRange(from.Failed);
    }

    /// <summary>
    /// Applies <paramref name="zone"/> to <paramref name="root"/> and resets everything below it.
    /// <paramref name="zoneFor"/> may start a different zone at any descendant (it gets the full path and
    /// returns null to keep inheriting). Throws when the root itself is a link or the process lacks the
    /// rights; a descendant that cannot be fixed is reported and skipped so one bad entry does not leave the
    /// rest of the tree open.
    /// </summary>
    internal static Report Lock(string root, Zone zone, Func<string, Zone?> zoneFor)
    {
        var report = new Report();
        using var privileges = BackupRestorePrivileges.Enable();
        if (!Apply(Path.GetFullPath(root), zone, isZoneRoot: true, zoneFor, report, isTop: true))
            throw new UnauthorizedAccessException($"'{root}' is a link, not a directory; refusing to lock it.");
        return report;
    }

    /// <summary>
    /// Returns false when the entry was a link or an extra hard-link name: removed, except at the top of the
    /// walk, where the caller named it and gets to decide.
    /// </summary>
    private static bool Apply(string path, Zone zone, bool isZoneRoot, Func<string, Zone?> zoneFor, Report report,
        bool isTop = false)
    {
        using var handle = DirectoryLockNativeMethods.OpenWithoutFollowing(path);
        var info = DirectoryLockNativeMethods.GetInfo(handle);
        var attributes = (FileAttributes)info.dwFileAttributes;
        var isDirectory = attributes.HasFlag(FileAttributes.Directory);

        if (attributes.HasFlag(FileAttributes.ReparsePoint) || (!isDirectory && info.nNumberOfLinks > 1))
        {
            if (isTop)
                return false;
            DirectoryLockNativeMethods.Delete(handle);
            report.Removed.Add(path);
            return false;
        }

        DirectoryLockNativeMethods.SetSecurity(handle, Describe(zone, isZoneRoot, isDirectory), isZoneRoot);
        if (!isDirectory)
            return true;

        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false };
        foreach (var child in Directory.EnumerateFileSystemEntries(path, "*", options))
        {
            try
            {
                var childZone = zoneFor(child);
                Apply(child, childZone ?? zone, childZone is not null, zoneFor, report);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                report.Failed.Add($"{child}: {ex.Message}");
            }
        }

        return true;
    }

    /// <summary>
    /// The security descriptor an entry gets: the zone's own ACEs, protected, at the top of the zone;
    /// below it, the ACEs that inheritance would hand down to a directory or a file, marked inherited.
    /// </summary>
    internal static RawSecurityDescriptor Describe(Zone zone, bool isZoneRoot, bool isDirectory)
    {
        var control = ControlFlags.DiscretionaryAclPresent | ControlFlags.SelfRelative | ControlFlags.DiscretionaryAclAutoInherited;
        if (isZoneRoot)
            control |= ControlFlags.DiscretionaryAclProtected;

        var acl = new RawAcl(GenericAcl.AclRevision, zone.Aces.Count);
        foreach (var ace in isZoneRoot ? zone.Aces : zone.Aces.Select(ace => Inherit(ace, zone.Owner, isDirectory)).OfType<CommonAce>())
            acl.InsertAce(acl.Count, ace);

        return new RawSecurityDescriptor(control, zone.Owner, null, null, acl);
    }

    /// <summary>
    /// What <paramref name="ace"/> becomes on a child, following the Windows inheritance rules this needs:
    /// a directory takes container-inherit ACEs (keeping their inheritance, minus inherit-only), a file takes
    /// object-inherit ones as plain effective ACEs, and CREATOR OWNER turns into the child's owner.
    /// </summary>
    private static CommonAce? Inherit(CommonAce ace, SecurityIdentifier owner, bool isDirectory)
    {
        var flags = ace.AceFlags;
        AceFlags childFlags;
        if (isDirectory && flags.HasFlag(AceFlags.ContainerInherit))
            childFlags = (flags & Inheritable) | AceFlags.Inherited;
        else if (isDirectory && flags.HasFlag(AceFlags.ObjectInherit))
            childFlags = AceFlags.ObjectInherit | AceFlags.InheritOnly | AceFlags.Inherited;
        else if (!isDirectory && flags.HasFlag(AceFlags.ObjectInherit))
            childFlags = AceFlags.Inherited;
        else
            return null;

        var sid = ace.SecurityIdentifier.IsWellKnown(WellKnownSidType.CreatorOwnerSid) ? owner : ace.SecurityIdentifier;
        return new CommonAce(childFlags, ace.AceQualifier, ace.AccessMask, sid, false, null);
    }

    internal static CommonAce Allow(SecurityIdentifier sid, FileSystemRights rights, AceFlags flags) =>
        new(flags, AceQualifier.AccessAllowed, (int)rights, sid, false, null);

    /// <summary>
    /// Enables SeBackup/SeRestore for the walk: they let an elevated admin or SYSTEM open an entry whose
    /// planted DACL shuts Administrators out, and set an owner other than itself. Restored on dispose.
    /// A process without them (a non-elevated test run on its own temp directory) simply goes on without.
    /// </summary>
    private sealed class BackupRestorePrivileges : IDisposable
    {
        private readonly SafeAccessTokenHandle? _token;
        private readonly byte[]? _previous;

        private BackupRestorePrivileges(SafeAccessTokenHandle? token, byte[]? previous) => (_token, _previous) = (token, previous);

        public static BackupRestorePrivileges Enable()
        {
            var token = DirectoryLockNativeMethods.EnablePrivileges(["SeBackupPrivilege", "SeRestorePrivilege"], out var previous);
            return new BackupRestorePrivileges(token, previous);
        }

        public void Dispose()
        {
            if (_token is null)
                return;
            if (_previous is not null)
                DirectoryLockNativeMethods.RestorePrivileges(_token, _previous);
            _token.Dispose();
        }
    }
}
