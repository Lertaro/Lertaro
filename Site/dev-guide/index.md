# Developer Guide

Welcome to the Lertaro Developer Reference Manual. Built upon a decoupled multi-process architecture and an extensible plugin ecosystem, Lertaro provides an official SDK assembly: `Lertaro.PluginSdk`. By referencing this SDK, third-party developers can contribute custom search sources, extend context action menus, deeply integrate with third-party file managers and native file dialogs, and customize themes and preview handlers.

## 1. Architecture & Workflow

- **[System Architecture](./architecture)** —— Deep dive into the three-process isolation model (SYSTEM-level Windows Service, user-mode WPF App, and keyboard Hook process) and named pipe IPC.
- **[Getting Started](./getting-started)** —— Step-by-step guide to creating a plugin class library, referencing the SDK, implementing `IPlugin`, and local debugging.
- **[Packaging & Distribution](./packaging)** —— Assembly directory conventions, bundling third-party managed/native DLLs, embedding i18n resources, and PostBuild deployment automation.
- **[Plugin Examples](./examples)** —— Real-world case studies analyzing the official open-source `CoreExtensions`, `PinyinAlias`, `FlowLauncherBridge`, and `FileUnlocker` plugins.

## 2. Plugin SDK Reference

| SDK Category | Core Interfaces & Services | Key Capabilities |
| :--- | :--- | :--- |
| **[Core Search & Actions](./sdk/core-search-actions)** | `ISearchableItemProvider`<br>`IInstantResultProvider`<br>`IAliasProvider`<br>`IQueryTokenProvider`<br>`ISearchResultAction`<br>`IDynamicActionProvider` | Contribute indexed items, instant calculation answers, non-ASCII alias transliteration engines, query suffix token handlers, and static/dynamic context action menus. |
| **[System & Dialog Adapters](./sdk/system-adapters)** | `IActivePathCollector`<br>`IFileDialogAdapter`<br>`IInlineSearchAdapter`<br>`IQuickNavigationProvider` | Detect active directories in file managers, hook native file dialogs, embed inline search bars with two-way selection sync, and provide Quick Navigation cascading menus. |
| **[UI & Preview Extensions](./sdk/ui-extensions)** | `ISidebarFilterProvider`<br>`IResultColumnProvider`<br>`IQuickPanelTabProvider`<br>`IFilePreviewProvider`<br>`IThumbnailProvider`<br>`IThemeProvider`<br>`ITranslationProvider` | Add sidebar filter categories, custom table columns, Quick Panel dynamic workspace tabs, QuickLook custom preview renderers, thumbnail extractors, WPF themes, and i18n language packs. |
| **[Shared Abstractions](./sdk/abstractions)** | `ISearchResult`<br>`FileMetadata`<br>`IPluginSearchWindow`<br>`IConfigurable` | Read-only result models, high-precision file timestamps and size metadata, host window control handles, and schema-driven native configuration forms. |
| **[Host Services](./sdk/services)** | `FuzzyMatchService`<br>`TranslationService`<br>`IconService`<br>`FavoritesService`<br>`HistoryService`<br>`FileMetadataService`<br>`DirectoryIndexerService`<br>`MemoryMaintenanceService`<br>`RecentFilesService`<br>`ExplorerPathService`<br>`PluginSettingsService`<br>`SettingsSearchService`<br>`SettingsWindowService`<br>`SearchRefreshService`<br>`UserDataService`<br>`Logger` | High-performance host infrastructure: fzf fuzzy matching and highlight masks, cached icon extraction, favorite management, directory indexer proxies, deferred memory maintenance, data directory isolation, and native Shell file operations. |
| **[Notifications](./sdk/notifications)** | `PluginNotificationService`<br>`NotificationRequest`<br>`INotificationHandle`<br>`NotificationLevel`<br>`NotificationPosition`<br>`NotificationResult`<br>`NotificationFailure` | Background attention through the host's own windows: a bottom-right card stack or a bottom-centre notice, durations clipped by the host, replacement by `Id`, per-plugin limits, and a completion task that always finishes. |

> [!NOTE]
> Interface signatures, member names, defaults and parameter order on these pages are read off the `Lertaro.PluginSdk` source. Where a page also describes how the **host** reacts to a plugin's call, that sentence is documentation of current behavior, not a contract a plugin may depend on.
