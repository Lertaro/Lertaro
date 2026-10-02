# System Architecture

Lertaro is built upon a multi-process isolation model and a layered architecture, ensuring sub-millisecond retrieval speeds and deep desktop integration while maintaining maximum system stability and security.

![Lertaro Architecture](/architecture.svg)

## 1. Three-Process Isolation Model

To prevent single component failures from crashing the entire system and to minimize elevated Windows privileges, Lertaro's runtime is decoupled into three independent processes:

### 1. Background Indexing Service (`Lertaro.Service`)

- **Identity**: Runs continuously as a Windows Service under the `LocalSystem` account.
- **Responsibilities**: Performs disk indexing and change tracking. Reads NTFS / ReFS USN Change Journals and \$MFT tables; listens to FAT32 / exFAT change events; periodically crawls and caches SMB / NAS network shares.
- **Security & Performance**: Running at the SYSTEM level allows raw disk volume metadata access without prompting UAC dialogs, returning results to the user-mode App over high-speed named pipes while keeping the UI unprivileged.

### 2. User Interaction Application (`Lertaro.App`)

- **Identity**: Standard user-mode per-session WPF desktop application.
- **Responsibilities**: Hosts the centered Quick Search bar, Full Search window, Settings Center, global hotkey dispatching, action menus (`Ctrl+O`), and QuickLook preview panels.
- **IPC Bridge & CLI Hosting**: Talks to the background service over the bidirectional named pipe `LertaroPipe` (`Core.Services.Search.SearchService` is the App-side client). It also hosts a dedicated per-user named pipe (`AppSearchPipeService`), allowing external tools like the `lff` CLI companion to reuse the App's initialized memory aliases, plugin providers, and cache trees without separate initialization.

### 3. Global Keyboard Hook & Window Adapter Process (`Lertaro.Service --hook`)

- **Identity**: Dedicated helper process started by the background service. It is launched **elevated only when the signed-in account is a genuine administrator**; otherwise it runs on the user's own token, so the bypass below is available on that machine only.
- **Responsibilities**: Hosts low-level global keyboard hooks and mouse activity listeners.
- **UIPI Bypass & Crash Isolation**: Windows User Interface Privilege Isolation (UIPI) prohibits lower-integrity user applications from sending messages to elevated administrator windows. By hosting window adapters ([`IActivePathCollector`, `IFileDialogAdapter`, `IInlineSearchAdapter`](./sdk/system-adapters)) inside this process, Lertaro can read and drive administrator-run Explorers, Total Commander, and file dialogs when the hook was started elevated. Furthermore, anti-cheat hooks or crashes cannot bring down the main UI.

## 2. Shared Core Library (`Lertaro.Core`)

`Lertaro.Core` is referenced simultaneously by the Service, App, and Hook processes, containing:

- **fzf Fuzzy Matching Engine (`Core/SearchIndex/Fzf/*`)**: Optimized character jump matching, substring scoring, and highlight masks, coupled with `SearchQueryParser` for drive letters and path tokens.
- **Columnar In-Memory Index (`Core/IndexV2/*`)**: High-performance memory-mapped columnar snapshots with an in-memory delta overlay for sub-millisecond searches across hundreds of millions of files.
- **Binary IPC Protocols**: Standard serialized message structures (`SearchRequestMessage`, binary serializers) enabling zero-copy inter-process communication.
- **Multi-Process Logging (`Logger`)**: Structured logging output to `service.log`, `app.log`, and `hook.log`, presented uniformly within the Settings live log viewer.

## 3. Plugin Architecture & Lifecycle

All plugins are built on `Lertaro.PluginSdk` and dynamically loaded by `Lertaro.App`:

- **Zero-Privilege Direct Access**: A plugin's own code runs in-process with whoever loaded it — normally the App, but see the next bullet for the two kinds the Service loads too. Nothing crosses a process boundary on its own account, so a plugin that needs custom directory indexing delegates the work through `DirectoryIndexerService` rather than walking the disk itself.
- **Selective Dual-Loading**: Search sources, actions and UI components run in the App process only. Three things are loaded into the other processes as well: window and file dialog adapters (`IActivePathCollector`, `IFileDialogAdapter`, `IInlineSearchAdapter`) go into the Hook process for cross-integrity window automation, and `IAliasProvider` plus `ITranslationProvider` are loaded by the **Service** too (`ServicePluginLoader`), because the indexer builds alias rows and needs the same transliteration and naming the UI shows.
