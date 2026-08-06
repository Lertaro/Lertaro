using System.Collections.Concurrent;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
namespace Lertaro.Plugins.FolderCascader.Navigation;

// Content-only: deciding whether the Quick Navigation popup should open at all for a given click lives
// in App/Services/ShellMenu/QuickNavigationTriggerGate.cs, not here -- that's host recognition (Explorer
// empty-space hit-testing, other file managers via their adapters), not something specific to what this
// class contributes to the popup once it's already open.
//
// This instance is a process-wide singleton (registered once, reused by every Show() call -- see
// PluginManager's provider registry), so AllocateHandle/TryGetPath/ClearSession can genuinely run
// concurrently from different threads: QuickNavigationSubMenuLoader loads a submenu's children on a
// background Task while a NEW Show() (on the UI thread) can be clearing/rebuilding this same session at
// the same time. A plain Dictionary under that access pattern is undefined behavior, not just "an entry
// might be briefly missing" -- ConcurrentDictionary plus Interlocked counters make every individual
// operation safe; QuickNavigationMenu.cs's own generation check is what prevents a STALE clear from
// wiping a still-live session in the first place (a thread-safe clear at the wrong time is still wrong).
public class Provider : IQuickNavigationProvider
{
    private readonly ConcurrentDictionary<IntPtr, string> _nodeMap = new();
    private readonly ConcurrentDictionary<uint, string> _commandMap = new();
    private int _nextId = 1;
    private int _nextCmdId = 1;

    // Reuses the plugin's own display name rather than a separate translation key -- this provider IS
    // the plugin (FolderCascaderPlugin has no other component contributing quick-navigation items).
    public string GroupName => TranslationService.Get("FolderCascader_PluginName");

    // Root-level equivalent of the "+" MenuBuilder.InsertCategoryHeader prepends to each nested
    // category submenu -- the host renders this in the root group header itself (see
    // QuickNavigationMenu.Show), since that header isn't something this plugin builds directly.
    public Action<ISearchResult>? HeaderAction => result => MenuBuilder.PromptAndAddCurrentFolder(result.FullPath, "");
    public string? HeaderActionTooltip => TranslationService.Get("FolderCascader_AddCurrentFolder");

    public bool CanProvide(ISearchResult result) => result != null && !string.IsNullOrEmpty(result.FullPath);

    public IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu) =>
        MenuBuilder.GetMenuItems(result, hMenu, this);

    public void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd)
    {
        if (_commandMap.TryGetValue(commandId, out var path))
            CommandExecutor.Execute(result, path);
    }

    public void ClearSession()
    {
        _nodeMap.Clear();
        _commandMap.Clear();
        Interlocked.Exchange(ref _nextId, 1);
        Interlocked.Exchange(ref _nextCmdId, 1);
    }

    public IntPtr AllocateHandle(string path)
    {
        var handle = new IntPtr(Interlocked.Increment(ref _nextId));
        _nodeMap[handle] = path;
        return handle;
    }

    public uint AllocateCommand(string path)
    {
        var cmdId = (uint)Interlocked.Increment(ref _nextCmdId);
        _commandMap[cmdId] = path;
        return cmdId;
    }

    public bool TryGetPath(IntPtr handle, out string? path) => _nodeMap.TryGetValue(handle, out path);
}
