using System.Collections.Concurrent;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

namespace Lertaro.Core.Hook;

internal sealed class FileDialogNavigationTracker
{
    private readonly ConcurrentDictionary<IntPtr, long> _dialogPathVersions = new();
    private string? _lastActiveExplorerPath;
    private long _pathVersion;

    public string? LastActiveExplorerPath => _lastActiveExplorerPath;

    public void SetLastActiveExplorerPath(string? path)
    {
        if (string.Equals(path, _lastActiveExplorerPath, StringComparison.OrdinalIgnoreCase)) return;
        _lastActiveExplorerPath = path;
        _pathVersion++;
    }

    public Task HandleDialogSeenAsync(IntPtr mainDialog, IFileDialogAdapter? adapter, bool previousWasPathProvider,
        Func<string?>? readProviderPath = null)
    {
        if (_dialogPathVersions.TryAdd(mainDialog, _pathVersion))
        {
            if (_dialogPathVersions.Count > 100)
                foreach (var key in _dialogPathVersions.Keys)
                    if (!ExplorerNativeHooks.IsWindow(key)) _dialogPathVersions.TryRemove(key, out _);
            return Task.CompletedTask;
        }

        // Only a real return from a provider needs a fresh host read. Re-reading an unrelated remembered
        // window on every dialog activation made a manual dialog navigation jump back to an old folder.
        if (previousWasPathProvider && readProviderPath?.Invoke() is { Length: > 0 } freshPath)
            SetLastActiveExplorerPath(freshPath);

        if (!previousWasPathProvider && _dialogPathVersions[mainDialog] == _pathVersion)
            return Task.CompletedTask;

        _dialogPathVersions[mainDialog] = _pathVersion;
        var path = _lastActiveExplorerPath;
        if (string.IsNullOrEmpty(path) || adapter == null) return Task.CompletedTask;

        return Task.Run(() =>
        {
            try { adapter.NavigateTo(mainDialog, path); }
            catch (Exception ex) { Logger.Log($"[ExplorerTracker] Dialog follow failed: {ex.Message}", LogLevel.Warn); }
        });
    }

    public void Clear()
    {
        _dialogPathVersions.Clear();
        _lastActiveExplorerPath = null;
        _pathVersion = 0;
    }
}
