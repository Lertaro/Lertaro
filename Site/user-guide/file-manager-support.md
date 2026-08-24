# Supported File Managers & Dialog Integrations

Lertaro is more than a standalone search launcher — it deeply embeds into Windows File Explorer, third-party file managers, and software dialogs, dramatically streamlining file opening, saving, and folder navigation.

## 1. Three Core Integration Capabilities

Depending on the host window's characteristics, Lertaro provides up to three distinct integration features:

- **Inline Search**: Lertaro's fast search box embeds directly at the top of the host window or file dialog, enabling instant directory-scoped or global searches without context switching.
- **Quick Navigation**: Middle-click (or double-left-click) empty areas or click the embedded logo to summon a cascading menu of active folders, favorites, history, and custom categories.
- **Active Path Detection**: Senses the physical directory currently opened in the host window, scoping searches automatically and resolving relative path actions.

## 2. Native Windows Components (Built-in Out of the Box)

These native components are supported directly by Lertaro's core engine without requiring additional plugins:

| Host Window Type | Inline Search | Quick Navigation Trigger | Active Path Detection |
| :--- | :--- | :--- | :--- |
| **Windows File Explorer** | Supported | Double-left-click or middle-click empty areas | Supported |
| **Modern Open/Save Dialogs** | Supported (Directly embedded) | Middle-click, or left-click the embedded logo | — |
| **Legacy Open/Save Dialogs** | Supported | Middle-click, or left-click the embedded logo | — |
| **Legacy Browse for Folder Dialogs** | Supported | Middle-click, or left-click the embedded logo | — |

> [!NOTE]
> In file selection dialogs, Lertaro is already directly embedded inside the window, eliminating the need for external path detection. Clicking a target in Quick Navigation jumps the dialog straight to that folder.

## 3. Professional Third-party File Managers (Optional Plugins)

For advanced users relying on third-party file managers, Lertaro offers dedicated integration plugins. Enable them under [**Settings → Plugins**](./settings/plugins) and configure Inline Search and Quick Navigation independently:

| File Manager | Inline Search | Quick Navigation Trigger | Active Path Detection | Core Communication Technology |
| :--- | :--- | :--- | :--- | :--- |
| **Directory Opus** | Supported | Middle-click in the file list area | Supported | `WM_COPYDATA` official remote API |
| **Total Commander** | Supported | Middle-click in the file list area | Supported | `WM_COPYDATA` message protocol |
| **XYplorer** | Supported | Middle-click in the file list area | Supported | Dedicated process communication |
| **Files** | Supported | Middle-click in the file list area | Supported | Windows UI Automation framework |
| **One Commander** | Supported | Middle-click in the file list area | Supported | Windows UI Automation framework |

### Directory Opus Exclusive Deep Integration

- **Recursive Folder Size Column (Lertaro Size)**: When "Enable Lertaro Size Column" is enabled, Lertaro installs a custom script column into Directory Opus. This column reads recursive folder sizes directly from Lertaro's in-memory index, displaying total folder sizes across entire drives in sub-seconds **with zero disk I/O**.
- **Persistent Format**: Add the "Lertaro Size" column in Directory Opus, then click **Folder → Folder Formats → Save → Save Format to All Folders** to make it permanent globally.

### Everything Compatibility Service (IPC)

Under [**Settings → General → System**](./settings/general#system), enable **Enable Everything Compatibility Service (IPC)** to emulate the standard Everything Win32 IPC interface. Tools like Directory Opus, Total Commander, and Flow Launcher can query Lertaro's fast in-memory index directly via their existing Everything plugins.

## 4. Custom Application Dialogs (Dedicated Plugins)

Many software suites use custom-drawn windows instead of native Windows dialogs. Lertaro provides dedicated plugin integrations for these popular tools:

| Application & Dialog Type | Inline Search | Quick Navigation Trigger | Integration Notes |
| :--- | :--- | :--- | :--- |
| **WPS Office** Open/Save Dialogs | Supported | Middle-click or click embedded logo | Supports WPS Writer, Spreadsheets, Presentation, and PDF |
| **WinRAR** Extraction Dialog | Supported | Middle-click or click embedded logo | Quickly target destination extraction paths |
| **Bandizip** Extraction Dialog | Supported | Middle-click or click embedded logo | Specify extraction output folders instantly |
| **Bandizip** Add Files Dialog | Supported | Middle-click or click embedded logo | Select archive source files rapidly |
| **AutoCAD** Open/Save Dialogs | Supported | Middle-click or click embedded logo | Optimized for CAD drawing retrieval and storage |

Integrations identify internal control hierarchies and handle levels, remaining fully compatible regardless of software language packs or UI skin variations.
