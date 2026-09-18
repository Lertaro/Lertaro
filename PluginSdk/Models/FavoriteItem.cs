namespace Lertaro.PluginSdk.Models;

public class FavoriteItem
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Optional global hotkey for this favorite, in the flat recorder format
    /// ("Ctrl+Shift+D"); empty means none. Only the App acts on it -- the Hook process has no
    /// per-favorite registration -- so plugins can safely leave it empty.
    /// </summary>
    public string Hotkey { get; set; } = string.Empty;
}
