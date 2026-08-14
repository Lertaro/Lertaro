using System.IO;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
namespace Lertaro.Plugins.TotalCommander;

/// <summary>
/// Collects the active source-pane path through the adapter's editor-safe scope reader. This remains necessary
/// when inline search is disabled: querying TC while its path editor is active cancels that edit.
/// </summary>
public class TotalCommanderPathCollector : IActivePathCollector
{
    private readonly TotalCommanderInlineSearchAdapter _scopeReader = new();

    public string Name => "Total Commander";
    public string TargetName => "Total Commander";

    public bool CanHandle(string className)
    {
        if (string.IsNullOrEmpty(className)) return false;
        return className.Equals("TTOTAL_CMD", StringComparison.OrdinalIgnoreCase);
    }

    public string? TryGetPath(IntPtr activeHwnd, string activeClassName, IntPtr windowHwnd, string windowClassName, string processName)
    {
        var main = windowHwnd != IntPtr.Zero ? windowHwnd : activeHwnd;
        var path = _scopeReader.GetSearchScope(main);
        if (string.IsNullOrEmpty(path)) return null;

        return Directory.Exists(path) ? path : null;
    }
}
