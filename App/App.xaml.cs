using System.Diagnostics;
using System.Windows;
using Lertaro.App.Services;
using Lertaro.App.Services.AppWindow;
using Lertaro.App.Services.Pipe;
using Lertaro.App.Services.Plugin;
using Lertaro.App.Services.ShellIcons;
using Lertaro.App.Services.ShellMenu.QuickNav;
using Lertaro.App.Services.Theme;
using Lertaro.App.Services.Update;
using Lertaro.App.Services.UrlProtocol;
using Lertaro.App.ViewModels.Search;
using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.App.ViewModels.Settings.General;
using Lertaro.Core;
using Lertaro.Core.Hook.Ipc;
using Lertaro.Core.Services.Installation;
using Lertaro.Core.Services;
using Application = System.Windows.Application;
namespace Lertaro.App;
public partial class App : Application
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    [System.Runtime.InteropServices.DllImport("shell32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
    private Mutex? _appMutex;
    public static HookIpcClient? HookClient { get; private set; }

    // Held for the process lifetime so its hotkey registration and message window stay alive.
    private Services.QuickPanel.QuickPanelManager? _quickPanelManager;

    // Same reason: this owns the per-favorite global hotkeys' message-only window, and dropping the
    // instance would unregister every one of them.
    private Services.Favorites.FavoriteHotkeyService? _favoriteHotkeys;
    private readonly Helpers.App.DispatcherExceptionHandler _dispatcherExceptionHandler = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        Services.AppLifecycle.AppRestartService.WaitForParentExit(e.Args);
        // The shell worker pool is built here rather than on the first shell action, so that no click ever
        // asks the loader for a thread while a third-party shell extension is holding it (see ShellThread).
        ShellThread.Start();
        // Lertaro never set an explicit AppUserModelID, so Windows infers one on its own (commonly
        // derived from the exe's own path) -- the taskbar's default/resting icon for windows from a
        // path Windows treats as an "installed app" (Program Files + Start Menu registration) came
        // from that inferred identity rather than the live window icon ThemedWindowIconHelper sets,
        // even though title bar/Alt-Tab (which read the live window directly) were already correct.
        // Owning the identity explicitly is also just standard practice for a real desktop app
        // (correct taskbar grouping/pinning/jump-list/notification behavior).
        try
        {
            // Derived from the assembly name (App.csproj's <AssemblyName>) rather than a hardcoded
            // literal, so the two can't drift apart if the assembly is ever renamed. A null Name here
            // would mean the executing assembly has no name at all, which can't happen in practice;
            // the surrounding try/catch is the fallback if it somehow did.
            var appId = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name!;
            SetCurrentProcessExplicitAppUserModelID(appId);
        }
        catch { /* best-effort; taskbar grouping falls back to Windows' own inference */ }

        // Only this thread (the Dispatcher), not Process.PriorityClass -- keeps input/rendering responsive
        // under CPU contention without making the whole process compete unfairly against everything else.
        Thread.CurrentThread.Priority = ThreadPriority.Highest;

        // Initialize logger first so we can log elevation decisions and issues
        Logger.Initialize("app.log", overwrite: true);

        // App-wide smooth wheel scrolling, keyed off ScrollViewer.CanContentScroll so virtualized lists
        // stay item-based while pixel-scrolling lists get the glide (see SmoothWheelScrollBehavior).
        Helpers.Visuals.SmoothWheelScrollBehavior.EnableGlobally();

        // Global exception handlers, registered as early as possible: anything thrown before the old
        // registration point (UserSettings.Load, hook client startup, ...) crashed with no log at all.
        AppDomain.CurrentDomain.UnhandledException += (s, args) => Helpers.App.AppCrashHandler.LogException("AppDomain UnhandledException", args.ExceptionObject as Exception);
        DispatcherUnhandledException += _dispatcherExceptionHandler.Handle;
        TaskScheduler.UnobservedTaskException += (s, args) => { Helpers.App.AppCrashHandler.LogException("TaskScheduler UnobservedTaskException", args.Exception); args.SetObserved(); };

        var settings = UserSettings.Load();
        Logger.MinimumLevel = SettingsOptionGenerator.ParseLogLevel(settings.LogLevel);
        // Everything this process matches outside the search pipeline -- plugin catalog items,
        // favorites, shell-menu filtering, display highlighting -- reads this rather than the
        // per-request value, which only ever reaches the search pipeline's own async flow.
        SearchContext.DefaultFuzzyMatchEnabled = settings.EnableFuzzyMatch;
        SearchContext.DefaultAndFirstPrecedence = !settings.OrFirstPrecedence;
        StartupManager.SetEnabled(settings.StartWithWindows);
        Logger.Log("=========================================");
        Logger.Log($"Application starting with arguments: {string.Join(" ", e.Args)}");
        Logger.Log($"[App] Running as Administrator: {ElevationManager.IsRunningAsAdmin()}");

        // Single instance check per user session

        // The hash includes both SID and session, so accounts sharing a short username cannot collide
        // and no account/session identifier is exposed in the mutex name.
        var mutexName = $@"Local\Lertaro_App_{CurrentUserIdentity.SessionHash}";
        _appMutex = AppSingleInstance.AcquireMutex(mutexName, out var createdNew);
        if (!createdNew)
        {
            try
            {
                using var current = Process.GetCurrentProcess();
                foreach (var proc in Process.GetProcessesByName(current.ProcessName))
                {
                    try { if (proc.Id != current.Id) AllowSetForegroundWindow(proc.Id); }
                    finally { proc.Dispose(); }
                }
            }
            catch { }
            // Send activation command to the already running process and then exit immediately.
            // A lertaro:// launch arg is forwarded as-is so the running instance can route it;
            // anything else (a plain second launch) falls back to the bare activate signal.
            var launchUri = e.Args.Length > 0 && UriRouter.IsLertaroUri(e.Args[0]) ? e.Args[0] : null;
            await AppPipeService.SendActivateSignalAsync(launchUri);
            Shutdown();
            return;
        }

        HookClient = new HookIpcClient();
        PluginSdkBridge.ConfigureExplorerPathTracking();
        HookClient.OnOpenedFoldersCaptured += PluginSdkBridge.UpdateOpenedFolders;
        QuickNavigationHookHandlers.AttachTo(HookClient, Dispatcher);

        HookClient.OnActivated += () => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (InlineSearchManager.Instance.IsInlineSearchActive)
            {
                InlineSearchManager.Instance.FocusSearchBox();
            }
            else
            {
                // The manager decides, not a settings check here: the full window is reachable even with
                // the "open full panel by default" option off, so it has to be considered before it.
                AppWindowManager.HandleGlobalSummonHotkey();
            }
        }));
        HookClient.OnQuickPanelHotkey += () => Dispatcher.BeginInvoke(
            new Action(() => _quickPanelManager?.Toggle()));
        HookClient.OnQuickNavigationHotkey += () => Dispatcher.BeginInvoke(
            new Action(QuickNavigationMenu.ShowFromKeyboard));
        HookClient.Start();
        // Set up the quick panel. Built here rather than lazily on the first hotkey so the handler above always
        // has something to call; it creates no window of its own until it is first opened.
        _quickPanelManager = new Services.QuickPanel.QuickPanelManager();

        // Per-favorite global hotkeys. Registered in this process rather than through the Hook: the App
        // already pumps messages, and the file-manager window these navigate is resolved here anyway.
        // Created on the Dispatcher thread, which is the thread RegisterHotKey must be called from.
        try
        {
            _favoriteHotkeys = Services.Favorites.FavoriteHotkeyService.Initialize();
            _favoriteHotkeys.AttachHandler();
        }
        catch (Exception ex)
        {
            Logger.Log($"[FavoriteHotkeys] Initialization failed: {ex}", LogLevel.Error);
        }

        // Force load all plugins (actions and alias providers) on startup
        _ = PluginManager.Instance;
        _ = Task.Delay(10000).ContinueWith(_ => Win32Api.TrimWorkingSet());

        try
        {
            PluginSdk.Services.TranslationService.LookupFunc = key => TranslationManager.Instance[key];
            PluginSdk.Services.TranslationService.TryLookupFunc = key => TranslationManager.Instance.TryGet(key, out var val) ? val : null;
            PluginSdk.Services.TranslationService.CurrentCultureFunc = () => TranslationManager.Instance.CurrentCulture;
            PluginSdk.Services.LocalSendTransferService.OpenSendWindowFunc = (files, text) => Core.Services.LocalSend.LocalSendServiceManager.Instance.OpenSendWindow(files, text);
            PluginSdk.Services.SearchRefreshService.RefreshMatchingFunc = queryMatches =>
                // Callers may invoke this from a background thread (e.g. after an async fetch
                // completes), so marshal onto the UI thread here rather than requiring every caller
                // to remember to do so themselves.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (Window window in Windows)
                    {
                        // A view that has never run a query carries a null one (SearchExecutionViewModel
                        // seeds _searchQuery with null!), so this walk is what holds up SearchRefreshService's
                        // Func<string,bool>: a plugin's predicate is entitled to dereference its argument, and
                        // most of them do. A blank box has nothing to re-run either way.
                        if (window.DataContext is QuickSearchViewModel quickVm)
                        {
                            var currentQuery = quickVm.SearchQuery;
                            if (!string.IsNullOrWhiteSpace(currentQuery) && queryMatches(currentQuery))
                                quickVm.Search.PerformSearch(currentQuery);
                        }
                        else if (window.DataContext is SearchViewModel searchVm)
                        {
                            var currentQuery = searchVm.AdvancedQuery;
                            if (!string.IsNullOrWhiteSpace(currentQuery) && queryMatches(currentQuery))
                                searchVm.PerformSearch(currentQuery);
                        }
                    }
                }));
            PluginSdk.Services.IconService.GetIconFunc = (path, isDir) => ShellIconHelper.GetIconForPath(path, isDir);
            PluginSdk.Services.IconService.GetIconCacheOnlyFunc = (path, isDir) =>
            {
                var icon = ShellIconHelper.GetIconFromCacheOnly(path, isDir, out var needsLoad);
                return (icon, needsLoad);
            };
            PluginSdk.Services.IconService.GetThumbnailFunc = (path, size) => ShellImageListInterop.TryGetPreviewThumbnail(path, size);
            PluginSdk.Services.FileMetadataService.BatchLookupFunc = FileMetadataBridge.GetMetadataBatchAsync;
            // Cached across calls: this feed's own doc comment calls it "the host's static list of
            // searchable settings entries", but the naive version (call BuildAllEntries fresh every
            // time) silently broke that -- CoreExtensions' SearchSettingsInstantProvider calls
            // GetEntries() on every debounced keystroke of a "set ..." query in the main search window,
            // which was re-running BuildAllEntries(vm: null)'s PluginLoaderHelper.BuildPluginList
            // reflection scan (AppDomain.GetAssemblies + two GetTypes() passes per plugin DLL) per
            // keystroke -- independent of whether Settings was even open, and worse than the
            // once-per-window-open cost issue #186 was about. Safe to cache: with vm: null, none of the
            // built entries' Activate/Reveal delegates (which close over live PluginInfoViewModel/etc.
            // instances) are ever invoked -- JumpToEntry always rebuilds fresh against the real live vm
            // before activating anything, using the index purely as a positional lookup -- so only the
            // translated Label/Breadcrumb/Index actually returned here need to stay current. Invalidated
            // on language change (labels/breadcrumbs are translated at build time) and on
            // PluginManager.ComponentsRefreshed: unlike the Plugins-section entries (which include every
            // component regardless of IsEnabled, only ever toggling a flag PluginLoaderHelper doesn't
            // even expose here), PluginManager.QuickPanelTabProviders -- which the QuickPanel-section
            // entries are built from -- IS enabled-filtered, so disabling a quick-panel-tab-providing
            // component genuinely changes this feed's membership, not just some unexposed flag on it.
            List<PluginSdk.Services.SettingsSearchEntryInfo>? cachedSettingsSearchEntries = null;
            TranslationManager.Instance.PropertyChanged += (_, _) => cachedSettingsSearchEntries = null;
            PluginManager.Instance.ComponentsRefreshed += () => cachedSettingsSearchEntries = null;
            PluginSdk.Services.SettingsSearchService.InvalidateFunc = () => cachedSettingsSearchEntries = null;
            PluginSdk.Services.SettingsSearchService.GetEntriesFunc = () =>
            {
                if (cachedSettingsSearchEntries != null)
                    return cachedSettingsSearchEntries;

                // No live SettingsWindow is guaranteed to exist here (Settings may never have been
                // opened yet), so this passes vm: null -- BuildAllEntries then builds the Plugins/
                // Hotkeys-actions sections straight from PluginManager.Instance/
                // UserSettings instead of a live window's collections, and conservatively excludes any
                // conditionally-visible static entry (e.g. the WSL tab) it can't evaluate without one.
                var entries = SettingsWindowSearchExtensions.BuildAllEntries(vm: null);
                var list = new List<PluginSdk.Services.SettingsSearchEntryInfo>(entries.Count);
                for (var i = 0; i < entries.Count; i++)
                    list.Add(new PluginSdk.Services.SettingsSearchEntryInfo(entries[i].Label, entries[i].SectionLabel, i));
                cachedSettingsSearchEntries = list;
                return cachedSettingsSearchEntries;
            };
            PluginSdk.Logger.LogAction = (msg, lvl) => Logger.Log(msg, (LogLevel)(int)lvl);
            TranslationManager.Instance.ReloadTranslations();
            Logger.Log("[App] TranslationManager initialized.");

            // Preload app searchable items now that translations are fully loaded and settled
            SearchableItemMapper.Preload();

            var startupThemeId = settings.ThemeFollowSystem
                ? ThemeManager.Instance.ResolveLightDarkThemeId(SystemThemeWatcher.IsSystemLight, settings)
                : settings.Theme;
            ThemeManager.Instance.Initialize(startupThemeId);
            ThemeManager.Instance.InitializeSystemFollow();
            Logger.Log($"[App] ThemeManager initialized with theme: {startupThemeId}");
        }
        catch (Exception ex)
        {
            Logger.Log($"[App] Failed to initialize TranslationManager or ThemeManager: {ex.Message}", LogLevel.Error);
        }

        _ = AppPipeService.StartPipeServerAsync();
        _ = AppSearchPipeService.StartPipeServerAsync();
        AppStartupServiceBootstrapper.EnsureServiceStarted();
        UrlProtocolManager.EnsureRegistered();
        Logger.Log("Starting normal WPF GUI client mode.");
        base.OnStartup(e);

        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (Current.MainWindow is QuickSearchWindow)
            {
                InlineSearchManager.Instance.Start();
                Logger.Log("[App] InlineSearchManager started.");
            }

            if (e.Args.Length > 0 && UriRouter.IsLertaroUri(e.Args[0]))
                UriRouter.Route(e.Args[0]);
        }), System.Windows.Threading.DispatcherPriority.Loaded);

        // Background update check on startup
        UpdateCheckService.RunOnStartupAsync();

        // LocalSend transfer service runs in App process
        Helpers.LocalSend.LocalSendAppEventHandler.Initialize(settings);
    }

    public static void HideInlineSearch() => InlineSearchManager.Instance.CloseInlineSearch();

    public static void ShowSettingsWindow(string? targetSection = null) => AppWindowManager.ShowSettingsWindow(targetSection);
    public static void ShowSearchWindow() => AppWindowManager.ShowSearchWindow();
    public static void CloseAllManagedWindows() => AppWindowManager.CloseAllManagedWindows();

    protected override void OnExit(ExitEventArgs e)
    {
        Core.Services.LocalSend.LocalSendServiceManager.Instance.Stop();
        _favoriteHotkeys?.Dispose(); _favoriteHotkeys = null;
        foreach (var provider in PluginManager.Instance.AllSearchScopeProviders.OfType<IDisposable>()) provider.Dispose();
        HookClient?.Stop(); HookClient?.Dispose(); HookClient = null;
        AppPipeService.StopServer(); AppSearchPipeService.StopServer(); Services.Everything.EverythingServiceBootstrapper.Stop(); InlineSearchManager.Instance.Dispose(); CloseAllManagedWindows();
        if (_appMutex != null) { try { _appMutex.ReleaseMutex(); } catch { } _appMutex.Dispose(); }
        base.OnExit(e);
    }
}
