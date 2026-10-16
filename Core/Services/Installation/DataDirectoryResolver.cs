namespace Lertaro.Core.Services.Installation;

/// <summary>Chooses machine and per-user state directories for the running deployment form.</summary>
public static class DataDirectoryResolver
{
    public static string ResolveShared(InstallationMode mode, string applicationDirectory, string commonApplicationDataDirectory)
    {
        var portableDataDirectory = Path.Combine(applicationDirectory, "Data");
        var installedDataDirectory = Path.Combine(commonApplicationDataDirectory, "Lertaro");
        return ResolveFinalTarget(ResolveShared(mode, applicationDirectory, commonApplicationDataDirectory,
            Directory.Exists(portableDataDirectory), Directory.Exists(installedDataDirectory)));
    }

    // A portable update opened over an older installed copy should keep using its existing data until
    // the portable Data folder is deliberately created. This avoids silently starting with empty settings
    // and indexes just because the executable was launched from a different directory.
    internal static string ResolveShared(
        InstallationMode mode,
        string applicationDirectory,
        string commonApplicationDataDirectory,
        bool portableDataDirectoryExists,
        bool installedDataDirectoryExists)
    {
        var portablePath = Path.Combine(applicationDirectory, "Data", "Machine");
        var installedPath = Path.Combine(commonApplicationDataDirectory, "Lertaro");
        return mode == InstallationMode.Portable && !portableDataDirectoryExists && installedDataDirectoryExists
            ? installedPath
            : mode == InstallationMode.Portable ? portablePath : installedPath;
    }

    public static string ResolveUser(
        InstallationMode mode,
        string applicationDirectory,
        string localApplicationDataDirectory,
        string userSid)
    {
        var portableDataDirectory = Path.Combine(applicationDirectory, "Data");
        var installedDataDirectory = Path.Combine(localApplicationDataDirectory, "Lertaro");
        return ResolveFinalTarget(ResolveUser(mode, applicationDirectory, localApplicationDataDirectory, userSid,
            Directory.Exists(portableDataDirectory), Directory.Exists(installedDataDirectory)));
    }

    internal static string ResolveUser(
        InstallationMode mode,
        string applicationDirectory,
        string localApplicationDataDirectory,
        string userSid,
        bool portableDataDirectoryExists,
        bool installedDataDirectoryExists)
    {
        var portablePath = Path.Combine(applicationDirectory, "Data", "Users", CurrentUserIdentity.Hash(userSid));
        var installedPath = Path.Combine(localApplicationDataDirectory, "Lertaro");
        return mode == InstallationMode.Portable && !portableDataDirectoryExists && installedDataDirectoryExists
            ? installedPath
            : mode == InstallationMode.Portable ? portablePath : installedPath;
    }

    /// <summary>
    /// Follows a user's own relocation of the data directory -- a junction from it, or from the profile that
    /// contains it, to another drive -- to the plain directory it really names.
    /// </summary>
    /// <remarks>
    /// Refusing links is what the settings backup and the installer's ACL walk do, and they are right to: a
    /// link planted *below* the data root while a transfer runs is how a write gets redirected. This is not
    /// that. A folder the user chose to move is theirs, and the alternative -- refusing to start, with a
    /// message about a settings transfer that was never requested (issue: "after linking the users folder to
    /// D:, installation cannot start") -- is a hard failure for a supported deployment. So the root's own
    /// chain is resolved once, here, before anything opens it; every pin, ACL and path below it still sees
    /// ordinary directories. Anything that is not a plain local fixed-volume directory is returned unchanged,
    /// so the existing refusals keep their say instead of this silently choosing another place to write.
    /// </remarks>
    internal static string ResolveFinalTarget(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        try
        {
            var rooted = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            // Two or three passes settle a nested relocation (a link whose target sits under another link);
            // the cap is what keeps a link that points back at itself from looping.
            for (var pass = 0; pass < 8; pass++)
            {
                var next = ResolveExistingChain(rooted);
                if (string.Equals(next, rooted, StringComparison.OrdinalIgnoreCase)) break;
                rooted = next;
            }
            return VolumeIsSupported(rooted) ? rooted : path;
        }
        catch
        {
            // Best effort by definition: Logger's own initializer calls this, and a resolution fault must
            // leave the path exactly as it was rather than fail the process that is asking.
            return path;
        }
    }

    // From the volume root down, so every component -- not only the leaf -- is asked whether it is a link.
    // Components that do not exist yet are kept verbatim: the caller creates them (a fresh install's
    // Data\Users\<hash>, for one), and nothing below the last existing component can be a planted link.
    private static string ResolveExistingChain(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root)) return path;

        var current = root;
        var present = true;
        foreach (var part in path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (!present) continue;

            // Asked before ResolveLinkTarget, which throws for an entry that is not there yet.
            if (!Directory.Exists(current) && !File.Exists(current)) { present = false; continue; }

            var link = Directory.ResolveLinkTarget(current, returnFinalTarget: true);
            if (link != null) current = Path.TrimEndingDirectorySeparator(link.FullName);
        }
        return current;
    }

    private static bool IsLocalFixedVolume(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            return !string.IsNullOrEmpty(root) && !root.StartsWith(@"\\", StringComparison.Ordinal) &&
                   new DriveInfo(root).DriveType == DriveType.Fixed;
        }
        catch { return false; }
    }

    /// <summary>
    /// Overridable so the "unsupported volume" branch can be exercised: no test machine has a network share to
    /// relocate a data folder onto, and that branch is the whole point of the refusal below.
    /// </summary>
    internal static Func<string, bool> VolumeIsSupported { get; set; } = IsLocalFixedVolume;

    /// <summary>
    /// Why a data root reached through a link cannot be used, or null when it can.
    /// </summary>
    /// <remarks>
    /// <see cref="ResolveFinalTarget"/> deliberately leaves a path it cannot vouch for untouched, and the layer
    /// below then refuses it as a link -- which is how a data folder relocated to a network share produced the
    /// message about another instance transferring settings. Only a relocation is refused here: a path that is
    /// simply somewhere unusual (no link anywhere in its chain) is left to behave exactly as it always has.
    /// </remarks>
    public static string? DescribeRelocationRefusal(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            var rooted = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var resolved = ResolveExistingChain(rooted);
            if (string.Equals(resolved, rooted, StringComparison.OrdinalIgnoreCase)) return null;
            if (VolumeIsSupported(resolved)) return null;

            return $"The data folder is reached through a link whose target is not a local fixed disk. "
                + $"Move the folder itself, or point the link at a local disk. ({rooted} -> {resolved})";
        }
        catch
        {
            // A path that cannot even be resolved is not something this check can speak for; the layer below
            // still has its own refusal for it.
            return null;
        }
    }
}
