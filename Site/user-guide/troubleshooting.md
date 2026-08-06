# Troubleshooting

## The global hotkey doesn't respond

1. Check **Settings → Service Status** — if the background service isn't running, reinstalling or restarting it from that page usually fixes indexing-related issues, but the toggle hotkey itself is handled by the App process, not the service, so this is a secondary check.
2. If the foreground app is running elevated (as administrator) and Lertaro isn't, Windows blocks lower-privilege processes from sending it input. Lertaro's background hotkey-listener process is elevated automatically whenever your account is an administrator — no setting to enable, no UAC prompt. If hotkeys still don't reach an elevated window, check **[Settings → Service Status](./settings/service-status)** to confirm the background service is running, since it's what launches the elevated listener. If your account isn't an administrator, there's no workaround — Windows won't let a lower-privilege process signal a higher-privilege one.
3. Check the **[Process Blacklist](./settings/hotkeys-page#process-blacklist)** — if the foreground app's executable name was added there (intentionally or by accident), Lertaro's global hotkeys are deliberately let through untouched while it's focused.
4. If the foreground app is genuinely full-screen (fills the entire monitor), Lertaro automatically lets its hotkeys through too by default — so it doesn't fight with fullscreen games. If your configured combo won't collide with anything the fullscreen app itself uses, enable **Still respond while a fullscreen app is focused** next to **Settings → Hotkeys → Show/Hide Quick Search** to keep it working there too. Otherwise, alt-tabbing out, or running the app in borderless/windowed mode instead of exclusive full-screen, avoids it.

## Search results seem out of date

NTFS and ReFS drives update from the USN Journal in near real time; other local file systems (FAT32, exFAT, ...) are watched for changes directly instead, just as continuously. If something still looks stale (a file you just created isn't showing up, or a deleted file still appears), use **Rebuild Index** on the affected drive under **Settings → Index → Local Drives** (or **Network Drives**).

## A network drive never seems to refresh

Network shares don't have a USN Journal Lertaro can watch, so they're re-scanned on a schedule instead. Check the drive's **Refresh Mode** under **Settings → Index → Network Drives** — if it's set to **Manual**, nothing updates automatically; switch it to a timed interval, or use **Rebuild Index** to refresh on demand. For NAS SMB shares with special symlinks or recursive folder loops, the scan engine includes built-in loop detection to prevent duplicate entries and recursion blips.

## The preview window looks wrong / cut off

This shouldn't happen — Lertaro clamps the QuickLook preview window's position and size to your monitor's usable area automatically. If you still see clipping, try **Settings → General → Preview → Reset Preview Window Settings** to rule out an unusual manual width/height value, and make sure you're on the latest release.

## A file/folder doesn't show up at all

- Check it isn't excluded — **Settings → Index → Exclusion Rules** supports exact paths, glob patterns, and regexes, and any of the three could be catching it unintentionally.
- Check the drive it lives on is enabled under **Settings → Index → Local/Network Drives**.

## Typing Chinese (or another IME language) into the inline window doesn't work

The [inline window](./getting-started#the-three-windows) deliberately never takes real keyboard focus away from the window it's docked in — that's what lets you dismiss it and land exactly back where you were, with no focus flicker. An IME's composition (the candidate list, and the character it actually commits) only happens for whichever window holds *real* focus, though, which is always the docked-in app, never Lertaro — so there's no message Lertaro could intercept to capture what you typed. This is a structural limitation of how the inline window works, not a bug.

Two options:

- Type the pinyin romanization directly (no IME candidate popup needed) — Lertaro matches Chinese filenames by pinyin automatically, see [Search Syntax](./search-syntax#chinese-filenames-pinyin-aliasing).
- Use the [quick window](./getting-started#the-three-windows) instead (double-tap `Ctrl` by default) — it's a real focused window, so IME composition works there normally.

## Still stuck?

Check the **Service**, **App**, and **Hook** log tabs under **[Settings → Service Status](./settings/service-status)** — the search box there filters by keyword, and the level dropdown filters by severity — before filing an issue on GitHub.
