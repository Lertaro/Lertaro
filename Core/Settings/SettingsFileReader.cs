namespace Lertaro.Core;

internal static class SettingsFileReader
{
    // File.Exists also returns false for access denied. Only an actual missing path is a fresh
    // installation; permission and sharing errors must not turn an existing configuration into defaults.
    public static string? ReadIfPresent(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            catch (IOException) when (attempt < 3) { Thread.Sleep(50); }
        }
    }
}
