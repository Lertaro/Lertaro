using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Shell.ContextMenu;

/// <summary>
/// Dynamic action provider that loads the standard Windows Explorer context menu for files/folders.
/// </summary>
public class ShellMenuActionProvider : IDynamicActionProvider
{
    public string GroupName => TranslationService.Get("Plugin_ShellGroup");
    public string Description => TranslationService.Get("Plugin_ShellGroup_Desc");
    public int Priority => -1;

    private ShellMenuSession? _session;
    private string? _lastPath;
    private readonly object _sessionGate = new();
    private readonly Func<string, ShellMenuSession?> _createSession;
    private int _generation;
    private static readonly IntPtr NativeMenuRoot = new(-550);

    public ShellMenuActionProvider() : this(ShellMenuSession.Create) { }
    internal ShellMenuActionProvider(Func<string, ShellMenuSession?> createSession) => _createSession = createSession;

    // Do not preload shell handlers. A normal right-click or built-in locate must not load foreign
    // context-menu DLLs into the process (#550); only opening their own submenu requests that work.
    public void Init() { }

    public bool CanProvide(IReadOnlyList<ISearchResult> results)
    {
        // The native shell menu is single-item only for now (multi-file menu needs multi-PIDL);
        // hide it when more than one result is selected.
        if (results.Count != 1) return false;
        var result = results[0];
        if (result == null || string.IsNullOrEmpty(result.FullPath)) return false;
        return PluginSdk.Helpers.PathExistenceCache.ExistsResult(result);
    }

    public IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> results, IntPtr hMenu)
    {
        if (results.Count != 1) return [];
        var result = results[0];
        if (hMenu == IntPtr.Zero)
        {
            ClearSession();
            lock (_sessionGate) _lastPath = result.FullPath;
            return [new DynamicMenuItem { Text = GroupName, HasSubMenu = true,
                SubMenuHandle = NativeMenuRoot, IsActionable = false }];
        }

        if (hMenu == NativeMenuRoot)
        {
            int generation;
            lock (_sessionGate)
            {
                if (_lastPath != result.FullPath) return [];
                generation = _generation;
            }
            var created = _createSession(result.FullPath);
            lock (_sessionGate)
            {
                if (generation != _generation)
                {
                    created?.Dispose();
                    return [];
                }
                _session?.Dispose();
                _session = created;
            }
            hMenu = IntPtr.Zero;
        }

        ShellMenuSession? session;
        lock (_sessionGate) session = _session;
        if (session == null) return [];

        try
        {
            var items = session.EnumerateItems(hMenu);
            var menuItems = new List<DynamicMenuItem>();
            foreach (var item in items)
            {
                menuItems.Add(new DynamicMenuItem
                {
                    Text = item.Text,
                    CommandId = item.CommandId,
                    IsSeparator = item.IsSeparator,
                    HasSubMenu = item.HasSubMenu,
                    SubMenuHandle = item.SubMenuHandle,
                    IsDisabled = item.IsDisabled,
                    HBitmapItem = item.HBitmapItem
                });
            }
            return menuItems;
        }
        catch
        {
            return Array.Empty<DynamicMenuItem>();
        }
    }

    public void ExecuteCommand(IReadOnlyList<ISearchResult> results, uint commandId, IntPtr ownerHwnd)
    {
        ShellMenuSession? sessionToExecute;
        lock (_sessionGate)
        {
            sessionToExecute = _session;
            _session = null;
            _generation++;
        }

        if (sessionToExecute != null)
        {
            Task.Run(() =>
            {
                try
                {
                    sessionToExecute.InvokeCommand(commandId, ownerHwnd);
                }
                finally
                {
                    sessionToExecute.Dispose();
                }
            });
        }
    }

    public void ClearSession()
    {
        ShellMenuSession? session;
        lock (_sessionGate)
        {
            session = _session;
            _session = null;
            _lastPath = null;
            _generation++;
        }
        session?.Dispose();
    }
}
