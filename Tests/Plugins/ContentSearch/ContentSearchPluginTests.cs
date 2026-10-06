using Lertaro.PluginSdk;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.ContentSearch.Providers;

namespace Lertaro.Plugins.ContentSearch.Tests;

// Exercises the one-time type initializer and its retry events in one lifecycle;
// the SDK hooks and plugin runtime are process-wide state.
[TestClass]
[DoNotParallelize]
public sealed class ContentSearchPluginTests
{
    [TestMethod]
    public async Task RuntimeInitialization_FailuresKeepSettingsUsableAndAllowRetry()
    {
        const string pluginId = "Lertaro.Plugins.ContentSearch";
        var root = Path.Combine(Path.GetTempPath(), $"content_startup_{Guid.NewGuid():N}");
        var originalDataDirectory = UserDataService.GetUserDataDirectoryFunc;
        var originalEnablement = PluginSettingsService.IsComponentEnabledFunc;
        var originalSettings = PluginSettingsService.GetSettingFunc;
        var originalRegister = DirectoryIndexerService.RegisterDirectoryAction;
        var originalUnregister = DirectoryIndexerService.UnregisterDirectoriesAction;
        var originalLogger = Logger.LogAction;
        var errors = new List<string>();
        var enabled = true;
        var initialized = false;
        Directory.CreateDirectory(root);

        try
        {
            Logger.LogAction = (message, level) =>
            {
                if (level == LogLevel.Error) errors.Add(message);
            };
            PluginSettingsService.IsComponentEnabledFunc = (_, _, _) => enabled;
            PluginSettingsService.GetSettingFunc = (_, key, fallback) => key switch
            {
                "MonitoredFolders" => new List<string> { root },
                "IndexedExtensions" => "txt",
                _ => fallback
            };
            DirectoryIndexerService.UnregisterDirectoriesAction = _ => { };
            UserDataService.GetUserDataDirectoryFunc = () =>
                throw new UnauthorizedAccessException("user data access denied");

            var plugin = new ContentSearchPlugin();
            initialized = true;
            Assert.IsNotNull(plugin.GetConfigSchema());
            Assert.IsNull(ContentSearchPlugin.Database);
            Assert.IsNull(ContentSearchPlugin.Scheduler);
            Assert.Contains("UnauthorizedAccessException", errors.Single());
            Assert.Contains("user data access denied", errors.Single());
            Assert.IsEmpty(new ContentSearchInstantProvider().GetInstantResults("cs text"));

            // A real SQLite open failure must not publish a half-initialized database.
            var blockedDatabase = Path.Combine(root, "ContentIndex", "content_index.db");
            Directory.CreateDirectory(blockedDatabase);
            UserDataService.GetUserDataDirectoryFunc = () => root;
            errors.Clear();
            PluginSettingsService.NotifySettingChanged(pluginId, "MonitoredFolders");
            Assert.IsNull(ContentSearchPlugin.Database);
            Assert.IsNull(ContentSearchPlugin.Scheduler);
            Assert.Contains("SqliteException", errors.Single());
            Directory.Delete(blockedDatabase);

            // Registration can fail even when the directory exists and SQLite is writable.
            DirectoryIndexerService.RegisterDirectoryAction = (_, _, _, _) =>
                throw new UnauthorizedAccessException("monitored folder access denied");
            errors.Clear();
            plugin.GetConfigSchema().OnSave!();
            Assert.IsNotNull(ContentSearchPlugin.Database);
            Assert.IsNull(ContentSearchPlugin.Scheduler);
            Assert.Contains("monitored folder access denied", errors.Single());

            var registrations = new List<string>();
            DirectoryIndexerService.RegisterDirectoryAction = (_, folder, _, _) => registrations.Add(folder);
            errors.Clear();
            PluginSettingsService.NotifyComponentEnablementChanged();
            var scheduler = ContentSearchPlugin.Scheduler;
            Assert.IsNotNull(scheduler);
            await scheduler.TriggerFullScan();
            Assert.Contains(root, registrations);
            Assert.IsEmpty(errors);
            Assert.IsNotEmpty(new ContentSearchInstantProvider().GetInstantResults("cs "));

            // Settings refreshes also stay inside the error boundary after startup.
            DirectoryIndexerService.RegisterDirectoryAction = (_, _, _, _) =>
                throw new UnauthorizedAccessException("changed folder access denied");
            PluginSettingsService.NotifySettingChanged(pluginId, "MonitoredFolders");
            Assert.Contains("changed folder access denied", errors.Single());
            DirectoryIndexerService.RegisterDirectoryAction = (_, _, _, _) => { };
            plugin.GetConfigSchema().OnSave!();
            Assert.AreSame(scheduler, ContentSearchPlugin.Scheduler);

            enabled = false;
            PluginSettingsService.NotifyComponentEnablementChanged();
            Assert.IsNull(ContentSearchPlugin.Scheduler);
        }
        finally
        {
            if (initialized)
            {
                enabled = false;
                PluginSettingsService.NotifyComponentEnablementChanged();
                ContentSearchPlugin.Database?.Dispose();
            }
            UserDataService.GetUserDataDirectoryFunc = originalDataDirectory;
            PluginSettingsService.IsComponentEnabledFunc = originalEnablement;
            PluginSettingsService.GetSettingFunc = originalSettings;
            DirectoryIndexerService.RegisterDirectoryAction = originalRegister;
            DirectoryIndexerService.UnregisterDirectoriesAction = originalUnregister;
            Logger.LogAction = originalLogger;
            Directory.Delete(root, recursive: true);
        }
    }
}
