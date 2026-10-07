using System.Text;
using System.Text.Json;

namespace Lertaro.Core;

public sealed class SettingsBackupManifest
{
    public int FormatVersion { get; set; } = 1;
    public string ApplicationVersion { get; set; } = "";
    public string? BridgeVersion { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public bool IncludesPluginFiles { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsLegacyJson { get; internal set; }
    public List<SettingsBackupFile> Files { get; set; } = [];
    public List<SettingsBackupPlugin> Plugins { get; set; } = [];
}

public sealed record SettingsBackupFile(string Path, long Length, string Sha256, string Category);
public sealed record SettingsBackupPlugin(string? Id, string Name, string Kind, string? Version,
    string? Language, string? Source, string[] Aliases, string? SettingsDirectory, bool? Disabled,
    JsonElement? Metadata = null, string[]? DisabledComponents = null);

internal static class SettingsBackupFormat
{
    public const int MaximumFiles = 100000;
    public const long MaximumBytes = 16L * 1024 * 1024 * 1024;
    public const int MaximumJsonBytes = 16 * 1024 * 1024;
    public static readonly UTF8Encoding Utf8 = new(false, true);
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    public static readonly Encoding Gbk = CodePagesEncodingProvider.Instance.GetEncoding(936,
        EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)!;

    public static string Category(string path, bool includePluginFiles)
    {
        SettingsBackupPaths.Validate(path);
        if (path.Equals("user-settings.json", StringComparison.OrdinalIgnoreCase)) return "user-settings";
        if (path.Equals(PluginSettingsStore.FileName, StringComparison.OrdinalIgnoreCase)) return "plugin-settings";
        if (path.StartsWith("FlowData/Settings/", StringComparison.OrdinalIgnoreCase)) return "flow-settings";
        if (path.StartsWith("Calendar/", StringComparison.OrdinalIgnoreCase)) return "calendar";
        if (includePluginFiles && path.StartsWith("FlowData/Plugins/", StringComparison.OrdinalIgnoreCase)) return "flow-plugin-files";
        throw new InvalidDataException($"The backup cannot write this location: {path}");
    }

    public static string ReadJson(Stream stream) => ReadJson(stream, out _);

    public static string ReadJson(Stream stream, out bool legacyEncoding)
    {
        legacyEncoding = false;
        using var bytes = new MemoryStream();
        CopyBounded(stream, bytes, MaximumJsonBytes);
        var data = bytes.ToArray();
        try
        {
            using var reader = new StreamReader(new MemoryStream(data), Utf8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (DecoderFallbackException) { legacyEncoding = true; return Gbk.GetString(data); }
    }

    public static long CopyBounded(Stream input, Stream output, long maximum)
    {
        var buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = input.Read(buffer)) != 0)
        {
            total += count;
            if (total > maximum) throw new InvalidDataException("Backup size limit exceeded.");
            output.Write(buffer, 0, count);
        }
        return total;
    }

    public static void ValidateSettings(Stream stream)
    {
        if (UserSettingsPersistence.TryParse(ReadJson(stream)) == null)
            throw new InvalidDataException("The backup does not contain valid user settings.");
    }
}
