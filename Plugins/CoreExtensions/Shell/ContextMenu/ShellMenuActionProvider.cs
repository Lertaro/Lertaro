using System.IO;
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

    // Called by the host right as the actions menu starts opening (see ShellMenuPresenter.EnterActionsMode),
    // well before CanProvide/GetMenuItems run for real -- unlike triggering the warm-up from inside
    // CanProvide itself, this gives it a genuine head start instead of racing the real GetMenuItems
    // call that follows moments later on the same shared, single-threaded STA worker (a race the
    // warm-up would usually lose, making it pure overhead instead of actually warming anything). Still
    // the same risky in-process COM call a buggy shell extension could crash on (see GetMenuItems
    // below); moving the trigger doesn't remove that risk, but a crash here can now only happen once
    // the app is already up and the user is opening an actions menu, not on every single launch
    // regardless of whether they ever touch this feature. The host guarantees this is only ever called
    // once per process, so there's no need to self-guard against repeat calls here. Runs on a
    // background task so it never delays the actions menu the user is looking at right now.
    public void Init() => _ = Task.Run(() =>
                               {
                                   try
                                   {
                                       var warmPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                                       if (!string.IsNullOrEmpty(warmPath) && Directory.Exists(warmPath))
                                       {
                                           var session = ShellMenuSession.Create(warmPath);
                                           session?.EnumerateItems();
                                           session?.Dispose();
                                       }
                                   }
                                   catch
                                   {
                                       // Warm-up is best-effort.
                                   }
                               });

    public bool CanProvide(IReadOnlyList<ISearchResult> results)
    {
        // The native shell menu is single-item only for now (multi-file menu needs multi-PIDL);
        // hide it when more than one result is selected.
        if (results.Count != 1) return false;
        var result = results[0];
        if (result == null || string.IsNullOrEmpty(result.FullPath)) return false;
        return PluginSdk.Helpers.PathExistenceCache.Exists(result.FullPath);
    }

    public IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> results, IntPtr hMenu)
    {
        var result = results[0];
        // If root menu, create a new session
        if (hMenu == IntPtr.Zero)
        {
            _session?.Dispose();
            _session = ShellMenuSession.Create(result.FullPath);
            _lastPath = result.FullPath;
        }

        if (_session == null)
        {
            return Array.Empty<DynamicMenuItem>();
        }

        try
        {
            var items = _session.EnumerateItems(hMenu);
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
        var sessionToExecute = _session;
        _session = null; // Detach to allow parallel executions/cleanup

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
        _session?.Dispose();
        _session = null;
        _lastPath = null;
    }
}
