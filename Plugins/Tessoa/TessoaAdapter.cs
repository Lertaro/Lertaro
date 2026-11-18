using System.IO;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.Tessoa;

public sealed class TessoaAdapter : IInlineSearchAdapter, IActivePathCollector
{
    public string Name => "tessoa";
    public string TargetName => Name;
    public bool IsFileExplorer => true;
    public bool NavigateInUserApp => true;
    public bool CanHandle(string className) => false;
    public bool CanHandle(nint hwnd, string className, string processName) =>
        hwnd != 0 && processName.Equals("tessoa", StringComparison.OrdinalIgnoreCase) && TessoaWindow.From(hwnd) != null;

    public bool CanTrigger(nint focusedHwnd, string className) => focusedHwnd != 0
        && PluginSettingsService.GetSetting(TessoaPlugin.Id, "TriggerOnTyping", false);
    public bool CanShowQuickNav(nint hwndUnderCursor, string classNameUnderCursor) => hwndUnderCursor != 0;
    public bool CanEnterActionsMode(nint hwnd) => true;
    public bool GetDockBounds(nint hwnd, out AdapterRect rect) { rect = default; return false; }

    public string? GetSearchScope(nint hwnd) => TessoaWindow.From(hwnd) is { } window ? CurrentFolder(window) : null;
    public string? TryGetPath(nint activeHwnd, string activeClassName, nint windowHwnd, string windowClassName, string processName) =>
        GetSearchScope(windowHwnd != 0 ? windowHwnd : activeHwnd);

    private static string? CurrentFolder(TessoaWindow window) =>
        TessoaQuery.Read(window, "query-cwd") is { } path && TessoaQuery.IsPath(path) ? path : null;

    public IReadOnlyList<OpenedFolder> GetOpenedFolders()
    {
        var folders = new List<OpenedFolder>();
        foreach (var window in TessoaWindow.Visible())
        {
            var reply = TessoaQuery.Read(window, "query-tabs");
            if (reply == "" && CurrentFolder(window) is { } current)
                folders.Add(new(current, window.Handle));
            else if (reply != null)
                folders.AddRange(TessoaQuery.TabPaths(reply).Select(path => new OpenedFolder(path, window.Handle)));
        }
        return folders;
    }

    public bool ExecuteItem(nint hwnd, string path, string searchInput) => TessoaWindow.From(hwnd) is { } window
        && TessoaNavigation.Navigate(window, path, reveal: !Path.EndsInDirectorySeparator(path));
    public bool LocateItem(nint hwnd, string path) => TessoaWindow.From(hwnd) is { } window
        && TessoaNavigation.Navigate(window, path, reveal: true);
}
