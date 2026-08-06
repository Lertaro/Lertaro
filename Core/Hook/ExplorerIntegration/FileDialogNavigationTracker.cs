using System.Collections.Concurrent;

using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
namespace Lertaro.Core.Hook;

internal sealed class FileDialogNavigationTracker
{
    private readonly ConcurrentDictionary<IntPtr, DateTime> _dialogFirstSeenTimes = new();
    private string? _lastActiveExplorerPath;
    private DateTime _lastExplorerPathUpdateTime = DateTime.MinValue;

    public string? LastActiveExplorerPath => _lastActiveExplorerPath;

    public void SetLastActiveExplorerPath(string? path)
    {
        _lastActiveExplorerPath = path;
        _lastExplorerPathUpdateTime = DateTime.Now;
    }

    public void HandleDialogSeen(IntPtr mainDialog, IFileDialogAdapter? adapter, bool previousWasPathProvider)
    {
        var isNewDialog = false;
        var dialogFirstSeenTime = _dialogFirstSeenTimes.GetOrAdd(mainDialog, _ =>
        {
            isNewDialog = true;
            return DateTime.Now;
        });

        if (isNewDialog)
        {
            Logger.Log($"[ExplorerTracker] Dialog 0x{mainDialog:X} newly detected. Created/Seen at: {dialogFirstSeenTime}", LogLevel.Debug);
            if (_dialogFirstSeenTimes.Count > 100)
            {
                foreach (var key in _dialogFirstSeenTimes.Keys)
                {
                    if (!ExplorerNativeHooks.IsWindow(key))
                    {
                        _dialogFirstSeenTimes.TryRemove(key, out _);
                    }
                }
            }
        }
        else
        {
            if (_lastExplorerPathUpdateTime > dialogFirstSeenTime || previousWasPathProvider)
            {
                var currentPath = _lastActiveExplorerPath;
                if (!string.IsNullOrEmpty(currentPath))
                {
                    Logger.Log($"[ExplorerTracker] Dialog 0x{mainDialog:X} reactivated. PreviousWasPathProvider={previousWasPathProvider}, PathUpdatedLater={_lastExplorerPathUpdateTime > dialogFirstSeenTime}. Auto-navigating!", LogLevel.Debug);
                    ThreadPool.QueueUserWorkItem(_ => adapter?.NavigateTo(mainDialog, currentPath));
                }

                _dialogFirstSeenTimes[mainDialog] = DateTime.Now;
            }
        }
    }

    public void Clear()
    {
        _dialogFirstSeenTimes.Clear();
        _lastActiveExplorerPath = null;
        _lastExplorerPathUpdateTime = DateTime.MinValue;
    }
}
