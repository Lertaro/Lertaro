# Service Status

Manages the background indexing service, and gives you a live view of its logs.

## Service control

A status card shows the elevated indexer's current state — **Installing**, **Indexing**, **Ready**
(with a live file/folder count), or **Error** — with a matching icon (spinner, checkmark, or error
badge).

An **Install & Start Service** button appears only when the service isn't installed yet or hit an
error; once it's installed and running there's no manual start/stop/uninstall control on this
page — the service is expected to just keep running in the background.

## Logs

Three tabs — **App**, **Hook**, **Service** — corresponding to the three processes Lertaro runs
(the elevated background indexer, the per-user App you interact with, and the keyboard-hook
process). Each tab shows that process's log lines, color-coded by level.

- **Level filter** dropdown — All / Error / Warn / Info / Debug.
- **Search box** — filters the visible lines by keyword, combined with the level filter.
- **Clear** button — empties the log for the currently selected tab. Clearing the Service tab's
  log is routed through the service itself (the App process doesn't have permission to write to
  it directly); the App and Hook logs are cleared directly since they're per-user files.

This is the first place to look when troubleshooting — see
[Troubleshooting](../troubleshooting#still-stuck).
