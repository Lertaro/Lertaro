namespace Lertaro.Core.Services.Installation;

/// <summary>Chooses machine and per-user state directories for the running deployment form.</summary>
public static class DataDirectoryResolver
{
    public static string ResolveShared(InstallationMode mode, string applicationDirectory, string commonApplicationDataDirectory)
        => mode == InstallationMode.Portable
            ? Path.Combine(applicationDirectory, "Data", "Machine")
            : Path.Combine(commonApplicationDataDirectory, "Lertaro");

    public static string ResolveUser(
        InstallationMode mode,
        string applicationDirectory,
        string localApplicationDataDirectory,
        string userSid)
        => mode == InstallationMode.Portable
            ? Path.Combine(applicationDirectory, "Data", "Users", CurrentUserIdentity.Hash(userSid))
            : Path.Combine(localApplicationDataDirectory, "Lertaro");
}
