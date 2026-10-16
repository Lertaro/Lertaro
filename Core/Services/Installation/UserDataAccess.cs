using System.Security.AccessControl;
using System.Security.Principal;

namespace Lertaro.Core.Services.Installation;

public static class UserDataAccess
{
    public static void Verify(string directory)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new UnauthorizedAccessException("Cannot identify the settings owner.");
        if (!Directory.Exists(directory))
        {
            var acl = new DirectorySecurity();
            acl.SetSecurityDescriptorBinaryForm(DescribeUserDirectory(user), AccessControlSections.Access | AccessControlSections.Owner);
            new DirectoryInfo(directory).Create(acl);
        }
        var info = new DirectoryInfo(directory);
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new UnauthorizedAccessException($"The user data root is a link: {directory}");
        var owner = info.GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier));
        if (owner != user)
            // Both SIDs, because "belongs to another security identity" on its own is not something a user can
            // act on, and the pair is the whole diagnosis: a DACL that already grants this user FullControl
            // beside an Administrator owner is a legacy artifact, not tampering.
            throw new UnauthorizedAccessException(
                $"The user data directory belongs to another security identity: {directory} (owner: {owner?.Value ?? "unknown"}, expected: {user.Value})");

        // An actual read/create/delete, not File.Exists or an Allow-ACE heuristic. Never truncate settings.
        _ = SettingsFileReader.ReadIfPresent(Path.Combine(directory, "user-settings.json"));
        using var probe = new FileStream(Path.Combine(directory, $".access-{Guid.NewGuid():N}.tmp"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        probe.WriteByte(1);
    }

    /// <summary>
    /// The owner SID of <paramref name="directory"/>, or null when it cannot be read.
    /// </summary>
    /// <remarks>
    /// For checking a repair's own result: the elevated repair has to be able to say whether the owner it
    /// claims to have set is the owner that is actually there. On the machine in issue #342 the walk reported
    /// success while every later start still refused, and nothing could tell those two apart.
    /// </remarks>
    internal static string? TryGetOwner(string directory)
    {
        try
        {
            return new DirectoryInfo(directory).GetAccessControl(AccessControlSections.Owner)
                .GetOwner(typeof(SecurityIdentifier))?.Value;
        }
        catch { return null; }
    }

    private static byte[] DescribeUserDirectory(SecurityIdentifier user)
    {
        var descriptor = InstallDirectoryLock.Describe(PortableDirectoryLock.OwnedBy(user), true, true);
        var bytes = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(bytes, 0);
        return bytes;
    }
}
