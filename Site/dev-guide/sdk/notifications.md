# Notifications

`Lertaro.PluginSdk.Services.PluginNotificationService` lets a plugin get the user's attention from the background through the host's own windows — no extra process, no OS toast channel, and no coupling to the host UI assembly. The host, not the plugin, decides how long anything stays on screen and where.

## 1. Entry Points

| Signature | Returns | Use when |
| :--- | :--- | :--- |
| `INotificationHandle Show(NotificationRequest request)` | a handle immediately | you want to replace or cancel this notification later |
| `Task<NotificationResult> ShowAsync(NotificationRequest request)` | the end state | you only care how it ended |
| `bool Show(string title, string text, Action? onClick = null)` | whether a host accepted it | legacy two-argument shape; a `false` return means no host is wired, so a fallback is available |

Every entry point is safe to call from a plugin's own background thread and **never throws** — an escaping exception would take the caller's loop down with it. When no host delegate is assigned at all (a plugin running outside the launcher) the call is a quiet no-op and the answer is a finished handle carrying `Unavailable`; the request is only logged, at `Warn`, when the host's own delegate threw.

`INotificationHandle` has exactly two members: `Task<NotificationResult> Completion` and `void Dismiss()`. `Completion` **always** finishes, including when nothing ever reached the screen, so an `await` on it cannot park a thread. `Dismiss()` closes a shown card as if the user had dismissed it, is a no-op once it is gone, and never reverses a result already delivered.

A plugin that needs a window it owns uses [`Windows.PluginWindow`](./services); one that needs an *answer* rather than an announcement uses `PluginMessageBoxService`.

## 2. `NotificationRequest`

| Property | Default | Behavior |
| :--- | :--- | :--- |
| `string? Id` | `null` | Two requests with the same `Id` address the same notification. See **Replacing by Id** (§4). |
| `string Title` | `string.Empty` | Card header. Ignored by `BottomNotice`, which has no title row. |
| `string Message` | `string.Empty` | Body text. A request whose `Title` and `Message` are both blank or whitespace-only is rejected as `InvalidRequest` — there is nothing to show. |
| `NotificationLevel Level` | `Info` | `Info` / `Warn` / `Error`. Drives the icon and the semantic colour, nothing else. |
| `NotificationPosition Position` | `CardStack` | `CardStack` or `BottomNotice`. Each has its own queue and its own rules; they never block one another. |
| `double? DurationSeconds` | `null` | `null` takes the position's own default. An explicit value outside that position's range is **clipped to the nearest bound**, never rejected; `NaN` or a negative lands on the shorter end. |
| `Action? OnClick` | `null` | Runs in addition to closing. See **The Click Callback** (§6). |

## 3. The Two Positions

| | `CardStack` (bottom-right) | `BottomNotice` (bottom-centre) |
| :--- | :--- | :--- |
| Form | card with title, source name, ✕ and "mark all read" | one line; no title, no source, no controls |
| Simultaneous | 5 visible, the rest queued | 1 — a new one replaces the current immediately, with no teardown to wait out |
| Default duration | 8 s | 4 s |
| Clamped range | 2 – 30 s | 2 – 10 s |
| `OnClick` | honoured | ignored (nothing interactive) |
| Fullscreen app owns the screen | collapses into the notice line, capped at 5 s | unaffected |

Two more pacing numbers are worth knowing when you emit a burst:

- The host runs one **100 ms tick**, and it promotes **at most one** waiting card per tick. A burst therefore spaces its own arrivals, and because each card counts its time from the moment it appeared, spacing the arrivals also spaces the expiries — a burst no longer takes five slots out of the stack in one move.
- **One plugin may have at most 10 accepted card requests** (on screen *and* waiting combined). The eleventh is dropped as `QueueFull` and logged. The cap is enforced on the card-admission path only: a `BottomNotice` request is never counted against it, because the notice is a single slot that overwrites itself. There is no global pending cap across plugins, so a plugin cannot be starved by another's burst, but neither is a runaway producer limited globally.

## 4. Replacing by Id

Giving a request an `Id` is how a repeating status update avoids filling the screen with copies of itself: a new request whose `Id` matches a **visible** card overwrites that card in place — same slot in the stack, not jumped to the top — and the earlier caller's handle completes as `Replaced`. Callers that need to know they were superseded are exactly the ones that keep the handle.

One limit to respect: deduplication is matched against the cards currently on screen, **not** against cards still waiting in the queue. A plugin that fires five copies of one `Id` in a row while the screen is full gets all five queued.

## 5. Results and Failure Reasons

`NotificationResult` is `Succeeded` plus an optional `NotificationFailure`. The first end state to reach an item wins; nothing rewrites it afterwards.

| `NotificationFailure` | When |
| :--- | :--- |
| `Unavailable` | No host served the request — a plugin running outside the launcher, or a host delegate that threw. |
| `InvalidRequest` | `Title` and `Message` were both blank or whitespace-only. |
| `QueueFull` | This plugin already had 10 card requests accepted. |
| `Replaced` | A newer request carried the same `Id`. A `BottomNotice` overwrite completes the superseded request this way **even when neither carries an `Id`**, because the notice is a single slot that always overwrites. |
| `CancelledByPluginUnload` | The plugin was disabled or unloaded while its card was showing or queued. |
| `HostShuttingDown` | The launcher is closing and took the notification with it. |

A notification that reached the screen and ran out its time, or was closed by the user or by `Dismiss()`, completes as `NotificationResult.Success` — those are endings, not failures.

## 6. The Click Callback

`OnClick` runs on **any user dismissal**, which the code treats as one outcome: clicking the card body and clicking ✕ take the same path. A countdown that simply expires does **not** run it, and neither does any of the failures in §5.

The callback is plugin code, so an exception inside it is caught, logged with the source name, and never carried back into the launcher's input pipeline. Do the work itself outside the callback if it can fail; the callback's job is to bring the user to your window (the in-repo example is `Plugins/Calendar`, which uses it to activate the calendar view).

## 7. Attribution the Plugin Cannot Forge

The name printed on the card comes from `Assembly.GetCallingAssembly()`, read in each **public** frame of the facade and then mapped by the host: an assembly named `Lertaro.App*` is shown as `Lertaro`, a registered plugin is shown by its managed display name, and anything else falls back to the assembly name with the `Lertaro.Plugins.` prefix stripped. Passing a source name as a string was deliberately not offered — it is the one attribution a plugin must not choose.

## 8. Placement and Surface

- **Which screen**: the foreground window's screen, else the cursor's, else the primary. The work area is converted to DIPs through that monitor's DPI, so a card is the same physical size on a 200 %-scaled panel as on a 100 % one.
- **Stack order**: bottom-right, ordered by arrival sequence; cards above a removed one re-slide (the only animation, ~200 ms with a 40 DIP entry drop).
- **Size**: a card is at most half the work area tall (`min(260 DIP, 50 %)`), so a long message cannot eat a small monitor.
- **Drag**: a card is draggable by its title bar only, and a dragged card keeps its own corner. A display-settings change re-anchors the stack, which drops a user's drag — the cheaper trade than keeping a card off-screen.
- **Surface kind**: decided once, at construction, from the active theme's `WindowOpacity`. A fully opaque theme gets a plain window whose corners are rounded by the window manager, which keeps ClearType; a translucent theme gets a layered window whose corner has to be painted and clipped instead. A card shown across a theme switch therefore keeps whichever kind it started as until it goes away.
- **Topmost** is always on, and the windows are excluded from Alt+Tab (`WS_EX_TOOLWINDOW`). Notifications do not fade: nothing animates `Window.Opacity`, because that would cost ClearType and a per-pixel composite on every frame. The arrival marker is a rim flash inside an already-opaque window (~750 ms).

## 9. Threading and Lifecycle

- Call from any thread. The host serializes every notification entry point on one gate, so `Show` is a short, blocking call — do not drive it from a per-item hot loop; send one summary card instead.
- Windows are presented at `DispatcherPriority.Background`, so a burst of notifications cannot starve the launcher's own input work.
- While the **session is locked** (screen saver, `Win+L`), the countdown is frozen and the cards are hidden; they return when the session unlocks, still with the time they were given.
- Disabling or unloading the plugin cancels its showing *and* queued cards (`CancelledByPluginUnload`); the launcher's orderly shutdown does the same to everything outstanding (`HostShuttingDown`).
- There is **no user-facing notification setting** in this build: no do-not-disturb, no per-plugin switch, and the position and duration rules above are the host's, not configurable. Design requests accordingly — a plugin that talks constantly should be quieting itself, not asking the user to mute it.

## 10. Example

```csharp
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;

// One card per reminder, replaced rather than repeated if it is still on screen.
var handle = PluginNotificationService.Show(new NotificationRequest
{
    Id = $"reminder-{due.Item.Id}",
    Title = TranslationService.Get("Reminder_Title"),
    Message = due.Item.Text,
    Level = NotificationLevel.Warn,
    DurationSeconds = 12,               // clipped into 2..30 by the host
    OnClick = () => CalendarView.ShowOrActivate(),
});

// Await it only where you truly need the end state; the task always completes.
_ = handle.Completion.ContinueWith(t =>
{
    if (t.Result.Failure is NotificationFailure.Replaced)
        Logger.Log($"Superseded by a newer reminder: {due.Item.Id}", LogLevel.Debug);
});
```

> [!NOTE]
> Signatures, defaults, and limits on this page were read against `PluginSdk/Abstractions/NotificationRequest.cs`, `PluginSdk/Abstractions/INotificationHandle.cs`, `PluginSdk/Services/PluginNotificationService.cs`, and the host side under `App/Services/Notifications/` and `App/Views/Notifications/`.
