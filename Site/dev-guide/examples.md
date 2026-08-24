# Plugin Examples

To help developers understand how `Lertaro.PluginSdk` interfaces cooperate in real-world scenarios, this chapter analyzes three representative open-source plugins included in the Lertaro repository.

## 1. CoreExtensions —— Actions, Shell Menus & Quick Panel

The `CoreExtensions` plugin is Lertaro's core functionality bundle, implementing `IPlugin`, `IActionProvider`, `IConfigurable`, and multiple sub-providers.

### Key Implementation Highlights

- **Static Result Actions (`IActionProvider.GetActions()`)**: Exposes 10 essential file actions (Open, Locate in Explorer, Copy Path, Copy/Cut Files, Open Command Prompt, and Run as Administrator variants).
- **Native Shell Menu Integration (`IDynamicActionProvider`)**: Interacts with Windows Shell COM interfaces via `ShellMenuActionProvider`, rendering full Windows context menus (with cascading submenus like "Send to", 7-Zip, VS Code) directly inside Lertaro's `Ctrl+O` action menu.
- **Schema-Driven Configuration Forms (`IConfigurable`)**: Demonstrates defining configuration schemas with nested groups (`Group`), string lists (`StringList`), and hotkey recorders (`Hotkey`), rendering native UI forms in Settings without custom XAML.
- **Diverse Quick Panel Tabs (`IQuickPanelTabProvider`)**:
  - `FavoritesTabProvider` / `HistoryTabProvider`: Returns in-memory collections with zero disk I/O overhead.
  - `WindowsRecentTabProvider`: Crawls the Windows `Recent` folder on a background thread, resolves COM shortcut targets, truncates results, and populates `Metadata.Modified` for accurate sorting.
  - `LastDirectoryTabProvider` / `RecentFilesTabProvider`: Directly queries the host's [`ExplorerPathService`](./sdk/services) and `RecentFilesService`.

## 2. PinyinAlias —— Non-ASCII Transliteration Engine

The `PinyinAlias` plugin provides full pinyin and initialism alias search support for Chinese filenames, implementing both `IAliasProvider` and `ITranslationProvider`.

### Key Implementation Highlights

- **Alphabet Boundaries (`InputRanges` / `OutputRanges`)**: Declares CJK Ideograph blocks as the input range and lowercase `a`–`z` as the output range. The host uses these boundaries to partition mixed queries (e.g. `大cj` matching `大长今`) into literal and alias segments.
- **Pre-flight Fast Checks (`CanHandle(text)`)**: Scans for Chinese characters before generating aliases, returning `false` immediately for pure English strings to avoid allocation overhead.
- **Polyphonic Combinations (`GetAliases(text)`)**: Constructs a syllable map and generates all common pronunciation combinations connected with `|` (capped at 32 combinations to prevent combinatorial explosions), allowing parallel matching across all permutations.
- **Embedded Localization & Thread-Safe Caching**: Provides localized plugin display names via `ITranslationProvider`, caching parsed JSON tables inside a `lock`-guarded dictionary to avoid repeated disk reads.

## 3. FlowLauncherBridge —— Cross-Ecosystem Compatibility & Isolated Runtimes

The `FlowLauncherBridge` plugin demonstrates building a large-scale bridge system to integrate external community ecosystems seamlessly.

### Key Implementation Highlights

- **Multi-Language IPC Bridge**: Runs Flow Launcher plugins written in C# (.NET), Python 3.12, Node.js v20 LTS, and standalone executables (`.exe`).
- **Isolated Self-Contained Runtimes**: Deploys isolated Python and Node.js runtimes within Lertaro's user data directory, communicating via named pipes using JSON-RPC without polluting the system PATH.
- **Dynamic Configuration & WebView2 Previews**: Dynamically maps external `SettingsTemplate.yaml`/`.json` forms to `PluginConfigSchema`, and renders rich interactive HTML/WebView2 previews (e.g. MDict definitions, weather cards) inside QuickLook.
