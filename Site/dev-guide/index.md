# Developer Manual

Lertaro ships an open plugin SDK (`Lertaro.PluginSdk`) that third-party assemblies can target
to extend search behavior, add context-menu actions, integrate with other windows, and customize
the UI. This manual documents that surface.

- **[Architecture](./architecture)** — how the App, background Service, and plugins fit together.
- **[Getting Started](./getting-started)** — scaffolding a plugin project and loading it.
- **Plugin SDK Reference**:
  - **[Core Search & Actions](./sdk/core-search-actions)** — contributing search results and
    result actions.
  - **[System & Dialog Adapters](./sdk/system-adapters)** — integrating with File Explorer, native
    file dialogs, and other foreground windows.
  - **[UI & Preview Extensions](./sdk/ui-extensions)** — sidebar filters, result columns, file
    previews, thumbnails, themes, and translations.
  - **[Shared Abstractions](./sdk/abstractions)** — the read-only models plugins receive
    (`ISearchResult`, `IPluginSearchWindow`) and configuration schema (`IConfigurable`).
  - **[Host Services](./sdk/services)** — static services the host exposes back to plugins
    (icons, favorites, history, file metadata, directory indexing, per-plugin settings, logging).
- **[Example Plugins](./examples)** — two real, shipped plugins as case studies.
- **[Packaging & Deployment](./packaging)** — how a built plugin DLL gets discovered and loaded.

All interface signatures here were verified directly against the current `PluginSdk` source —
if you find a discrepancy, the code is authoritative.
