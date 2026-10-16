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

    // One long-lived STA apartment serves every Shell.Application call in this plugin, and work reaches it
    // by posting, so calls run one at a time. The shell hands the SAME agile folder object to whichever
    // apartment asks for it, so two enumerations in flight (the popup's own keyboard-focus prefetch of the
    // first row, plus the row the pointer actually reached) used to end with the one that finished first
    // releasing the shared RCW while the other was still walking Items() -- InvalidComObjectException, zero
    // children, and a submenu that renders as the empty-folder placeholder until the shell is warm enough
    // that the two calls stop overlapping.
    //
    // Same apartment shape as PluginSdk's ShellOperationStaWorker and CoreExtensions' ShellMenuStaWorker
    // (see those for why: OLE-initialized so shell extensions load completely, a real message pump so a COM
    // callback can re-enter this apartment while it would otherwise be idle, and a thread built once rather
    // than started per action). Deliberately its own worker, not a shared one: a stalled cascader read must
    // not take a file operation's or a context menu's apartment down with it.
    // ponytail: one apartment serializes all cascader shell work, so a hung enumeration blocks the ones
    // queued behind it. The old per-call thread blocked only its own caller. Upgrade path is a bounded wait
    // plus a cancellation-aware enumeration loop, not more apartments.
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
