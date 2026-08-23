using System.IO;
using System.Text.Json.Serialization;

namespace Flow.Launcher.Plugin;

/// <summary>
/// Plugin metadata loaded from plugin.json.
/// </summary>
public class PluginMetadata : BaseModel
{
    public string ID { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Language { get; set; } = AllowedLanguage.CSharp;
    public string Description { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public bool Disabled { get; set; }
    public bool HomeDisabled { get; set; }
    public string ExecuteFileName { get; set; } = string.Empty;
    public string ExecuteFilePath => string.IsNullOrEmpty(ExecuteFileName) || string.IsNullOrEmpty(PluginDirectory) ? string.Empty : Path.Combine(PluginDirectory, ExecuteFileName);

    [JsonIgnore]
    public string? AssemblyName { get; internal set; }

    private string _pluginDirectory = string.Empty;
    public string PluginDirectory
    {
        get => _pluginDirectory;
        internal set
        {
            _pluginDirectory = value;
            if (!string.IsNullOrEmpty(IcoPath) && !Path.IsPathRooted(IcoPath))
            {
                IcoPath = Path.Combine(value, IcoPath);
            }
        }
    }

    public string ActionKeyword { get; set; } = string.Empty;
    public List<string> ActionKeywords { get; set; } = [];
    public bool HideActionKeywordPanel { get; set; }
    public int? SearchDelayTime { get; set; }
    public string IcoPath { get; set; } = string.Empty;
    [JsonIgnore]
    public int Priority { get; set; }
    public long InitTime { get; set; }
    public long AvgQueryTime { get; set; }
    public int QueryCount { get; set; }
    public string PluginSettingsDirectoryPath { get; internal set; } = string.Empty;
    public string PluginCacheDirectoryPath { get; internal set; } = string.Empty;

    public override string ToString() => Name;
}

public class PluginPair
{
    public IAsyncPlugin Plugin { get; set; } = null!;
    public PluginMetadata Metadata { get; set; } = null!;

    public override string ToString() => Metadata?.Name ?? base.ToString() ?? string.Empty;

    public override bool Equals(object? obj) => obj is PluginPair other && string.Equals(Metadata?.ID, other.Metadata?.ID, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => Metadata?.ID?.GetHashCode() ?? 0;
}

public record UserPlugin
{
    public string ID { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Language { get; set; } = AllowedLanguage.CSharp;
    public string Website { get; set; } = string.Empty;
    public string UrlDownload { get; set; } = string.Empty;
    public string UrlSourceCode { get; set; } = string.Empty;
    public string LocalInstallPath { get; set; } = string.Empty;
    public string IcoPath { get; set; } = string.Empty;
    public DateTime? LatestReleaseDate { get; set; }
    public DateTime? DateAdded { get; set; }
    public bool IsFromLocalInstallPath => !string.IsNullOrEmpty(LocalInstallPath);
    public string MinimumAppVersion { get; set; } = string.Empty;
}
