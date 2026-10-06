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
            throw new UnauthorizedAccessException($"The user data directory belongs to another security identity: {directory}");

        // An actual read/create/delete, not File.Exists or an Allow-ACE heuristic. Never truncate settings.
        _ = SettingsFileReader.ReadIfPresent(Path.Combine(directory, "user-settings.json"));
        using var probe = new FileStream(Path.Combine(directory, $".access-{Guid.NewGuid():N}.tmp"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        probe.WriteByte(1);
    }

    private static byte[] DescribeUserDirectory(SecurityIdentifier user)
    {
        var descriptor = InstallDirectoryLock.Describe(PortableDirectoryLock.OwnedBy(user), true, true);
        var bytes = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(bytes, 0);
        return bytes;
    }
}
