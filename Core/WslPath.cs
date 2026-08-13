namespace Lertaro.Core;

/// <summary>Classifies WSL UNC paths without touching the filesystem.</summary>
public static class WslPath
{
    internal const string UncPrefix = @"\\wsl$\";
    internal const string LocalhostPrefix = @"\\wsl.localhost\";

    public static bool IsPath(string? path) =>
        path?.StartsWith(UncPrefix, StringComparison.OrdinalIgnoreCase) == true ||
        path?.StartsWith(LocalhostPrefix, StringComparison.OrdinalIgnoreCase) == true;
}
