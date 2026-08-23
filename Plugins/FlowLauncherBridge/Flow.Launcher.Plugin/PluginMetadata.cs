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
    public string IcoPath { get; set; } = string.Empty;
    public long InitTime { get; set; }
    public long AvgQueryTime { get; set; }
    public int QueryCount { get; set; }

    public override string ToString() => Name;
}

public class PluginPair
{
    public IAsyncPlugin Plugin { get; set; } = null!;
    public PluginMetadata Metadata { get; set; } = null!;

    public override string ToString() => Metadata?.Name ?? base.ToString() ?? string.Empty;
}

public class UserPlugin
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
    public string IcoPath { get; set; } = string.Empty;
}
