# LocalSend Settings

Lertaro includes built-in compatibility with the official [LocalSend](https://localsend.org) protocol for local network cross-device transfers, enabling fast file, folder, and plain text sharing between mobile and desktop devices on the same LAN. Located at **Settings → LocalSend**.

## Basic Settings

- **Enable LocalSend Transfer** —— Toggles the LocalSend service on or off. When enabled, the tray menu provides a "Send via LocalSend..." option and responds to the global hotkey.
- **Device Alias** —— Alias displayed when recognized by other LocalSend clients on the LAN. Automatically generated from host system name by default.
- **Port** —— HTTP/HTTPS port for listening to incoming transfer requests (default `53317`).

## Security & Encryption

- **Encrypted Transfer (HTTPS)** —— Uses HTTPS encrypted connection for data transfer when enabled (requires target device HTTPS support).
- **Receive PIN** —— Optional PIN code required for incoming transfer authorization (leave blank to disable).

## Receiving & Storage

- **Quick Save (Auto Save)** —— Automatically accepts and saves incoming files from trusted clients without requiring manual confirmation.
- **Download Location** —— Default destination directory for received files.

## Summon & Transfer Modes

- **Hotkey Summon** —— Press **`Ctrl+S`** by default (configurable in [Hotkeys Settings](./hotkeys-page)) or select "Send via LocalSend..." in tray menu.
- **Mode Switching** —— Send window supports switching between [ Send Files / Folders ] and [ Send Text ] modes, as well as drag-and-drop file addition directly from search results.
