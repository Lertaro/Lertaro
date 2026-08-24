# LocalSend Settings

Lertaro includes a built-in transmission engine natively compatible with the open-source [LocalSend](https://localsend.org) protocol. Without requiring internet access or physical cables, you can instantly share files, directories, and raw text across Windows PCs, Macs, Linux workstations, iPhones, iPads, and Android devices on the same local network. The settings page is located under **Settings → LocalSend**.

## 1. Basic & Network Settings

- **Enable LocalSend Transfer Service**: Master switch. When enabled, Lertaro starts a lightweight background listener and activates the "Send to other devices..." item in the tray context menu.
- **Device Name**: The alias broadcast to other devices across the local subnet. Automatically generated from your computer name by default, and fully customizable (e.g. `Work Laptop`).
- **Service Port**: The HTTP/HTTPS listener port (follows the official LocalSend standard default `53317`). Can be changed if conflicting with other local services.

## 2. Security, Encryption & Storage

- **Encrypted Transfer (HTTPS)**: Enabled by default. Uses TLS encryption for all cross-device transfers to prevent local packet inspection.
- **Receive PIN Code**: Optional numeric PIN (4–6 digits). When configured, incoming senders must input this PIN before transfers are accepted (leave blank to disable).
- **Auto-Save Files**: When checked, incoming transfers from local devices are automatically accepted and written to disk without prompt dialogs.
- **Save Directory**: The destination folder where received files are saved (defaults to `Downloads\LocalSend`), customizable via the browse button.

## 3. Sender Window & Workflows

- **Hotkey Activation**: Press the default global shortcut **`Ctrl+S`** (rebindable under [**Settings → Hotkeys**](./hotkeys-page)) to summon the standalone LocalSend sender window instantly.
- **Mode Switching**: Toggle seamlessly between **[Send Files / Folders]** and **[Send Text]** modes at the top of the sender window.
- **Drag-and-Drop Staging**: Drag files directly from Lertaro search results, Quick Panel workspaces, or File Explorer into the sender window. The radar automatically discovers all online devices on the LAN; click any target device card to transmit at wire speed.
