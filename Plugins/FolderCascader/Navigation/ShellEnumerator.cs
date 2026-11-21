using System.Runtime.InteropServices;
using System.Windows.Threading;
using Lertaro.PluginSdk;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Plugins.FolderCascader.Navigation;

public static class ShellEnumerator
{
    public static void EnumerateShellFolder(string scanPath, List<DynamicMenuItem> items, Provider provider)
        => RunOnSta(() => EnumerateShellFolderCore(scanPath, items, provider));

    private static void EnumerateShellFolderCore(string scanPath, List<DynamicMenuItem> items, Provider provider)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return;

            var shell = Activator.CreateInstance(shellType);
            if (shell == null) return;

            dynamic dShell = shell;
            var fullShellPath = scanPath.StartsWith("::") ? "shell:::" + scanPath : scanPath;
            dynamic folder = dShell.NameSpace(fullShellPath);
            if (folder == null) return;

            foreach (var item in folder.Items())
            {
                string p = item.Path;
                string name = item.Name;
                if (string.IsNullOrEmpty(p)) continue;

                if (item.IsFolder)
                {
                    items.Add(new DynamicMenuItem
                    {
                        Text = name,
                        HasSubMenu = true,
                        SubMenuHandle = provider.AllocateHandle(p),
                        HBitmapItem = IntPtr.Zero
                    });
                }
                else
                {
                    // Get the icon while we still hold the FolderItem COM object.
                    // The path string format (::{...}\0\::{...}\{guid}) cannot be
                    // re-parsed by SHParseDisplayName after the COM object is released.
                    var hBitmap = ShellPathHelper.TryGetIconHBitmapForShellItem((object)item);

                    // Capture the parent path and item path for re-enumeration on click.
                    // InvokeVerb() via Shell.Application is the only reliable way to open
                    // virtual Shell namespace items (no valid FileName for Process.Start).
                    var parentPath = fullShellPath;
                    var itemPath = p;
                    items.Add(new DynamicMenuItem
                    {
                        Text = name,
                        HBitmapItem = hBitmap,
                        OnExecute = () => InvokeShellItem(parentPath, itemPath)
                    });
                }
            }
        }
        catch (Exception ex) { Logger.Log($"[ShellEnumerator] enumeration failed for {scanPath}: {ex.Message}", LogLevel.Error); }
    }

    // Serialize Shell.Application calls on one long-lived OLE-initialized STA with a message pump.
    // Concurrent apartments can receive the same RCW and release it while another enumeration still uses
    // it. Keep this worker separate from file-operation and context-menu workers.
    //
    // ponytail: a hung enumeration blocks later cascader requests. Improve with bounded waits and a
    // cancellation-aware enumeration loop, not additional apartments.
    private static readonly object _startLock = new();
    private static Dispatcher? _dispatcher;
    private static bool _startupFailed;

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(IntPtr pvReserved);

    internal static void RunOnSta(Action action)
    {
        var dispatcher = GetWorker();
        if (dispatcher == null)
        {
            Logger.Log("[ShellEnumerator] No STA worker available; skipped enumeration", LogLevel.Error);
            return;
        }

        // Invoke, not BeginInvoke: the caller is already a background submenu fetch that has to wait for
        // these items, and Invoke rethrows on the calling thread the way the old per-call thread did.
        try { dispatcher.Invoke(action); }
        catch (Exception ex) { Logger.Log($"[ShellEnumerator] STA enumeration failed: {ex.Message}", LogLevel.Error); }
    }

    private static Dispatcher? GetWorker()
    {
        if (_dispatcher != null || _startupFailed) return _dispatcher;
        lock (_startLock)
        {
            if (_dispatcher != null || _startupFailed) return _dispatcher;
            using var ready = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                OleInitialize(IntPtr.Zero);
                _dispatcher = Dispatcher.CurrentDispatcher;
                ready.Set();
                Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "FolderCascaderShellSta"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            if (!ready.Wait(TimeSpan.FromSeconds(5)))
            {
                Logger.Log("[ShellEnumerator] STA worker failed to start within 5s", LogLevel.Error);
                _startupFailed = true;
            }

            return _dispatcher;
        }
    }

    // Same apartment as the enumeration: this re-opens the parent folder and matches the item by path, so it
    // touches the very objects the enumeration is sharing, and running it on the click thread's own
    // apartment was the second half of the same race.
    private static void InvokeShellItem(string parentPath, string itemPath)
        => RunOnSta(() => InvokeShellItemCore(parentPath, itemPath));

    private static void InvokeShellItemCore(string parentPath, string itemPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return;
            var shell = Activator.CreateInstance(shellType);
            if (shell == null) return;

            dynamic dShell = shell;
            dynamic folder = dShell.NameSpace(parentPath);
            if (folder == null) return;

            foreach (var item in folder.Items())
            {
                string p = item.Path;
                if (string.Equals(p, itemPath, StringComparison.OrdinalIgnoreCase))
                {
                    item.InvokeVerb();
                    return;
                }
            }
            Logger.Log($"[ShellEnumerator] InvokeShellItem: item not found for path '{itemPath}'", LogLevel.Warn);
        }
        catch (Exception ex)
        {
            Logger.Log($"[ShellEnumerator] InvokeShellItem failed: {ex.Message}", LogLevel.Error);
        }
    }
}
