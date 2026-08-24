# Service Status

The Service Status page monitors the health of Lertaro's background indexing service and provides real-time log viewers across the Service, App, and Hook processes. The page is located at **Settings → Service Status**.

## 1. Service Control & Status Card

The top card displays real-time health metrics of the elevated Windows indexing service:

- **State Indicators**:
  - **Ready**: Service is healthy and operational, displaying live counts of indexed files and folders.
  - **Indexing**: An initial scan or incremental index rebuild is in progress, accompanied by a dynamic progress spinner.
  - **Installing**: Registering and launching the background Windows service.
  - **Error**: The service is offline or encountered a critical fault, showing error descriptions and an alert badge.
- **Self-Healing & Installation**: An **Install and Start Service** button appears exclusively when the service is missing or in an error state. During normal operation, the service runs continuously in the background.

## 2. Multi-Process Live Log Viewer

The integrated log viewer below is split into three dedicated tabs mapping to Lertaro's three active processes:

- **App Tab**: Logs user interactions, search queries, UI rendering, and hotkey activations from the foreground WPF application.
- **Hook Tab**: Logs low-level keyboard hook events and isolation states from the hook process (`Lertaro.Hook.exe`).
- **Service Tab**: Logs USN change journal parsing, filesystem scanning, in-memory tree building, and IPC communications from the background service (`Lertaro.Service.exe`).

### Filtering & Log Maintenance

- **Level Filtering**: Dropdown filtering by "All / Error / Warning / Information / Debug", with color-coded log lines.
- **Keyword Search**: Type text into the search box to filter log lines in real-time, combining additively with level filters.
- **Safe Log Clearing**: Click **Clear Logs** to truncate logs for the active tab. Clearing the Service tab is handled safely via IPC proxy without requiring administrative elevation.
