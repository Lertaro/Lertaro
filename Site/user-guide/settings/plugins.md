# Plugins

Lertaro features a modular plugin architecture. Core extensions, native C# plugins, and third-party community extensions can all be viewed, configured, and managed in the Plugin Center under **Settings → Plugins**.

## 1. Page Layout & SDK Version

- **Plugin SDK Badge**: The top-right badge displays the currently loaded `Lertaro.PluginSdk` version. Clicking it opens the [**Developer Guide**](../../dev-guide/) directly.
- **Independent Dual-Pane Layout**: The left pane lists all installed plugins, while the right pane renders details and configuration forms for the selected plugin, with independent scrolling.

## 2. Plugin Details & Component Toggles

Clicking any plugin on the left displays its icon, name, version, and overview in the right pane:

### Details Tab

- **Component Grouping**: Lists all registered functional components (Search Sources, Action Providers, Quick Navigation Providers, Preview Handlers, etc.).
- **Individual Component Switches**: Non-essential components feature an **Enable/Disable Checkbox**; mandatory system components display a lock icon and cannot be disabled.
- **Select All / Deselect All**: Convenient links appear beside group headers containing multiple toggleable components.
- **Component Tooltips**: Hover over the **(!)** icon next to any component to inspect its implementation details and activation triggers.

### Configure Tab

- **Embedded Configuration Forms**: Custom settings are rendered directly within the settings pane without opening modal dialogs. Supports text boxes, numerical steppers, toggles, dropdowns, and grouped sub-tabs.
- **Icon Input Fields**: Text fields using the schema key `Icon` show an icon preview and accept WPF Path Data. Pasting a complete SVG/XML document automatically extracts and combines its path data; invalid icon content is cleared and reported in a themed dialog.
- **Multiline Configuration Editors**: `StringList` fields use soft wrapping in the expanded editor. Actual line breaks show a subtle `↵` marker for readability; the marker is visual only and is never saved or included in copied text.
- **Safe Staging & Rollback**: Modifications are kept in memory until **OK** is clicked; switching plugins or navigating away automatically rolls uncommitted edits back to their saved state.

### CoreExtensions Search Type Filters

Under **Settings → Plugins → CoreExtensions → Configure → Search Filters**, you can control the type filters shown in the left side of the full search window. File and Folder are always available; Document, Image, and Video can be disabled individually.

The **Custom Sidebar Filters** list adds extra filters with a display name, an optional WPF Path Data icon, and a wildcard rule. Empty names or rules that become empty after expansion are hidden, and an empty icon uses the default icon. The name is display-only and is not an `@` reference. `@` references are valid only inside the Rule field: `@keyword` expands a matching keyword from the existing **Custom Filters** list, even if that filter is disabled. References are recursively expanded and duplicate patterns are removed; unknown or cyclic references produce no matching rule.

## 3. Flow Launcher Community Ecosystem Bridge

In addition to native plugins built against `Lertaro.PluginSdk`, Lertaro includes a built-in **Flow Launcher Bridge** providing native-grade compatibility for the expansive Flow Launcher plugin community.

### Multi-Language Isolated Runtimes

- **Full Language Compatibility**: Seamlessly executes Flow Launcher plugins written in **C# (.NET)**, **Python 3.12**, **Node.js v20 LTS**, and standalone executables (`.exe`).
- **Isolated Self-Contained Environments**: Python (`FlowData\PythonEmbeded-{arch}`) and Node.js (`FlowData\NodeEmbeded-{arch}`) runtimes are automatically deployed on-demand within Lertaro's data directory, ensuring zero contamination of system PATH.
- **Automated Dependency Management**: Automatically installs Python `pip` packages from `requirements.txt` or Node.js `npm` dependencies from `package.json` silently in the background upon first load.

### Online Store & CLI Package Management

Manage Flow plugins directly from the Lertaro search box:

- **`flow install <keyword>`**: Searches the official Flow.Launcher online repository, downloads, extracts, deploys, and bootstraps dependencies with a single click.
- **`flow update`**: Checks for updates across all installed Flow plugins and upgrades them in-place.
- **`flow uninstall <plugin>`**: Safely uninstalls plugins and cleans up local directories.
- **Manual Installation**: Drop third-party plugin folders directly into `<UserData>\FlowData\Plugins\`.

### Configuration, ActionKeywords & Interactive Previews

- **Centralized ActionKeywords**: Under **Settings → Plugins → Flow Launcher Bridge → Configure**, toggle individual Flow plugins and customize their **ActionKeyword**. Settings persist cleanly in `FlowData\Settings\Plugins.json`.
- **Dynamic Configuration Forms**: Fully supports YAML/JSON template forms (`SettingsTemplate.yaml`/`.json`) and C# WPF panels (`ISettingProvider`), automatically styled to match the active theme with full i18n support.
- **WebView2 Rich Previews**: Renders complex interactive preview panels (e.g. MDict dictionary definitions, live weather, API debuggers, webpage snapshots) seamlessly inside QuickLook with automatic dark/light styling and custom scrollbars.
- **Deep Host Integration**: Flow plugins opening directories automatically respect the host's configured third-party file manager, message boxes render in host-native themed dialogs, and internal plugin logs flow directly into the Settings log viewer.
- **Quick Overview**: Type `flow` into the search box to list all loaded Flow plugins and their action keywords; selecting a plugin opens its corresponding configuration group in Settings.

## 4. Audio Device Selector

The **Audio Device Selector** is a Windows-only native plugin. Type `ad` to list active output and input devices, and select one to make it the corresponding default multimedia device. You can append a device name after the trigger keyword to filter the results.

Its settings let you change the trigger keyword and choose whether results show the friendly name, device name, or device description. It changes the default output or input endpoint; it does not control volume, mute state, or per-application audio routing.

## 5. Content Search

The **Content Search** plugin searches text inside configured local documents. Open its **Configure** tab to set the trigger keyword, monitored folders, indexed extensions, per-file size limit, index size cap, and excluded full-path regular expressions. The default trigger is `cs`, so `cs project plan` searches document content and shows matching files with snippets.

The configuration also provides **Clear Index** and **Rebuild Index**. Clear removes only the content index, while Rebuild removes it and scans all monitored folders again. Neither action removes Lertaro's normal filename index.

## 6. Plugin Runtime Status

The **Runtime Status** tab under **Settings → Plugins** shows live host-side activity for installed plugins while Lertaro is running. The values are aggregated by plugin and refresh while the tab is open.

- **Plugin filter**: Use the filter box to fuzzy-search plugin names.
- **Sortable columns**: Click a column header to cycle through ascending order, descending order, and the default plugin order.
- **Available metrics**: Calls, average duration, most recent duration, maximum duration, managed allocation, and observed exceptions.
- **Session scope**: Statistics accumulate during the current Lertaro process and reset after restart. A zero value means no measured call has been recorded yet. Managed allocation is the memory allocated by measured calls, not the plugin's total private memory usage.
