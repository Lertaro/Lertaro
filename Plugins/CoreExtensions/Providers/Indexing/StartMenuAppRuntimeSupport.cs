using System.IO;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.Indexing;

/// <summary>
/// Owns the Start Menu provider's directory watcher lifecycle so the provider remains below the
/// repository per-file line limit. This support object always operates on one provider instance.
/// </summary>
internal sealed class StartMenuAppRuntimeSupport : IDisposable
{
    private const string RegistrationId = "CoreExtensions.StartMenu";
    private readonly StartMenuAppItemProvider _owner;
    private readonly object _lock = new();
    private IDisposable? _directoryWatch;
    private bool _disposed;

    public StartMenuAppRuntimeSupport(StartMenuAppItemProvider owner)
    {
        _owner = owner;
        PluginSettingsService.ComponentEnablementChanged += UpdateRuntimeState;
        UpdateRuntimeState();
    }

    public bool IsComponentEnabled => PluginSettingsService.IsComponentEnabled(
        Path.GetFileName(typeof(StartMenuAppItemProvider).Assembly.Location),
        "SearchableItemProvider", nameof(StartMenuAppItemProvider));

    private void UpdateRuntimeState()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            if (IsComponentEnabled)
            {
                if (_directoryWatch != null)
                    return;

                _directoryWatch = DirectoryIndexerService.WatchDirectories(
                    RegistrationId, _owner.NotifyItemsChanged);
                _owner.RefreshDirectoryRegistrations();
                return;
            }

            _directoryWatch?.Dispose();
            _directoryWatch = null;
            DirectoryIndexerService.UnregisterDirectories(RegistrationId);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
            _directoryWatch?.Dispose();
            _directoryWatch = null;
            PluginSettingsService.ComponentEnablementChanged -= UpdateRuntimeState;
            try
            {
                DirectoryIndexerService.UnregisterDirectories(RegistrationId);
            }
            catch { }
        }
    }
}
