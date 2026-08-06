namespace Lertaro.Core;

public static class FileSystemItemFilter
{
    public static bool IsHiddenOrSystem(FileAttributes attributes)
        => (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;

    public static bool IsHiddenOrSystem(SearchResult result)
    {
        if (result == null)
            return false;

        // Prefer memory cached attributes to avoid triggering network or physical disk IO (critical WSL fix)
        return IsHiddenOrSystem(result.Attributes);
    }

    public static bool IsHiddenOrSystem(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            return IsHiddenOrSystem(File.GetAttributes(path));
        }
        catch
        {
            return false;
        }
    }
}
