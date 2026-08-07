namespace Lertaro.Core.Services.Installation;

/// <summary>Chooses machine and per-user state directories for the running deployment form.</summary>
public static class DataDirectoryResolver
{
    public static string ResolveShared(InstallationMode mode, string applicationDirectory, string commonApplicationDataDirectory)
    {
        var portableDataDirectory = Path.Combine(applicationDirectory, "Data");
        var installedDataDirectory = Path.Combine(commonApplicationDataDirectory, "Lertaro");
        return ResolveShared(mode, applicationDirectory, commonApplicationDataDirectory,
            Directory.Exists(portableDataDirectory), Directory.Exists(installedDataDirectory));
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
        return ResolveUser(mode, applicationDirectory, localApplicationDataDirectory, userSid,
            Directory.Exists(portableDataDirectory), Directory.Exists(installedDataDirectory));
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
}
