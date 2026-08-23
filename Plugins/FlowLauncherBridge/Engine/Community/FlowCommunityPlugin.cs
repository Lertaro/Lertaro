namespace Lertaro.Plugins.FlowLauncherBridge.Engine.Community;

/// <summary>
/// Model representing a community plugin entry in the Flow.Launcher plugin manifest.
/// </summary>
public sealed class FlowCommunityPlugin
{
    public string ID { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public string UrlDownload { get; set; } = string.Empty;
    public string UrlSourceCode { get; set; } = string.Empty;
    public string IcoPath { get; set; } = string.Empty;
    public DateTime? LatestReleaseDate { get; set; }
    public DateTime? DateAdded { get; set; }
    public string MinimumAppVersion { get; set; } = string.Empty;
}
