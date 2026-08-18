namespace Lertaro.Cli.Space;

/// <summary>Parses the non-interactive batch query used by external folder-size integrations.</summary>
public static class SpaceEntriesCommand
{
    public const string Switch = "--space-entries";

    public static bool IsRequested(string[] args) => args.Length > 0 && string.Equals(args[0], Switch, StringComparison.OrdinalIgnoreCase);

    public static bool TryGetDirectory(string[] args, out string directory)
    {
        directory = string.Empty;
        if (!IsRequested(args) || args.Length != 2)
            return false;

        directory = args[1];
        return true;
    }
}
