# System & Dialog Adapters

This chapter introduces adapter interfaces in `Lertaro.PluginSdk` for deep window docking, active directory extraction, and inline search integration across Windows File Explorer, native file dialogs, and third-party file managers.

All four live in `Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters` and derive from `IPluginComponent`, which is where the `Name` the host lists under **Settings → Plugins** comes from — none of these interfaces declares `Name` itself.

> [!NOTE]
> `IActivePathCollector`, `IFileDialogAdapter`, and `IInlineSearchAdapter` implementations are loaded into the **elevated Hook helper process** by the host to bypass Windows UIPI isolation when interacting with administrator-run windows. That is why their members must be cheap and non-interactive: they run on a low-level keyboard/mouse hook callback, where any stall past the system's `LowLevelHooksTimeout` silently drops the hook.

## 1. Opened Folder Collector `IOpenedFolderCollector`

The read-only half of the contract: report every folder the target manager currently has open, which is what feeds the **Currently Open Folders** group in [**Quick Navigation**](../../user-guide/hotkeys) and in the inline search list.

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

public readonly record struct OpenedFolder(string Path, IntPtr WindowHandle);

public interface IOpenedFolderCollector : IPluginComponent
{
    IReadOnlyList<OpenedFolder> GetOpenedFolders() => Array.Empty<OpenedFolder>();
}
```

An adapter returns one entry per open window, so a manager with five tabs reports five folders. The registry hands that list on **with duplicates intact on purpose** — a folder seen by two collectors, or by one collector twice, appears twice; callers that want a set deduplicate by path themselves.

## 2. Active Path Collector `IActivePathCollector`

`IActivePathCollector` **extends** `IOpenedFolderCollector`: a collector that can name the folder of a specific window usually contributes the open-folder list too, and gets it for free by implementing the inherited default.

Extracts the active working directory from the focused foreground window, enabling Lertaro to scope inline searches or resolve relative paths:

```csharp
public interface IActivePathCollector : IOpenedFolderCollector
{
    string TargetName { get; }   // Target manager name (e.g. "Directory Opus", "Total Commander")

    // Three overloads, each a coarser question than the last. Only the class-name form is
    // mandatory; the others default to it, so a collector that cannot tell windows apart yet
    // answers correctly for every caller.
    bool CanHandle(string className);
    bool CanHandle(string windowClassName, string windowTitle) => CanHandle(windowClassName);
    bool CanHandle(IntPtr windowHwnd, string windowClassName, string processName) => CanHandle(windowClassName);

    string? TryGetPath(
        IntPtr activeHwnd, string activeClassName,   // the focused control
        IntPtr windowHwnd, string windowClassName,   // its top-level window
        string processName);
}
```

- Focused control and parent window arrive separately so a path can be read out of nested controls (address bars, tree views) rather than only from the window as a whole.
- Return `null` when the window is recognised but its folder is not resolvable at that moment — that is not a failure and the host simply leaves the previous scope alone.

## 3. Native File Dialog Adapter `IFileDialogAdapter`

Inspects and controls native Windows Open / Save / Browse dialogs:

```csharp
public interface IFileDialogAdapter : IPluginComponent
{
    bool CanHandle(IntPtr hwnd, string className, string processName);
    string? GetCurrentPath(IntPtr hwnd);
    bool NavigateTo(IntPtr hwnd, string targetPath);

    bool TargetIsFolderOnly => false;  // True if target input accepts only folders (e.g. archive extraction)
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor) => true;

    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);          // where to dock the card

    // Placement probes: where the dialog's own target field is, and where its file list is. The
    // inline card reads both to decide where to hang itself -- under the field, over the list, or
    // where it fits when there is no room below. Return false for either and the host falls back to
    // GetDockBounds. Both default to "I cannot see that control".
    bool TryGetTargetFieldBounds(IntPtr hwnd, out AdapterRect bounds) { bounds = default; return false; }
    bool TryGetFileListBounds(IntPtr hwnd, out AdapterRect bounds) { bounds = default; return false; }

    bool RestoreFocus(IntPtr hwnd);
}

public struct AdapterRect   // physical pixels
{
    public int Left, Top, Right, Bottom;
}
```

- **`TargetIsFolderOnly`**: When `true`, if the user selects a file from search results, the host automatically resolves its parent folder before invoking `NavigateTo`.
- **`TryGetTargetFieldBounds` / `TryGetFileListBounds`**: Card placement only. The positioner prefers to hang the card under the dialog's target field, and uses the file list as the fallback anchor; a dialog whose adapter cannot resolve either simply gets the `GetDockBounds` rectangle.
- **`RestoreFocus`**: Hands the keyboard back to the dialog's own edit field. The host calls it when the user leaves the inline card (`Escape`, or the summon hotkey pressed again on an empty card), so this must not activate anything else.

## 4. Inline Search Adapter `IInlineSearchAdapter`

Embeds the Lertaro search card into target file dialogs or File Explorer windows, maintaining two-way selection synchronization:

```csharp
public interface IInlineSearchAdapter : IPluginComponent
{
    bool IsFileExplorer => false;      // True for Windows File Explorer

    bool CanHandle(IntPtr hwnd, string className, string processName);

    // Recognition without triggering. Defaults to CanHandle; override it when a window is clearly
    // the host you support but must not summon the card -- e.g. a command line or rename edit has
    // focus, where typing belongs to the host, not to Lertaro.
    bool CanRecognizeHost(IntPtr hwnd, string className, string processName) => CanHandle(hwnd, className, processName);

    bool CanTrigger(IntPtr focusedHwnd, string className);
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor) => CanTrigger(hwndUnderCursor, classNameUnderCursor);
    bool CanEnterActionsMode(IntPtr hwnd);

    string? GetSearchScope(IntPtr hwnd);
    bool ExecuteItem(IntPtr hwnd, string path, string searchInput);
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);

    IEnumerable<string> GetListItems(IntPtr hwnd) => Array.Empty<string>();
    void OnSelectionChanged(IntPtr hwnd, string path) { }
    void OnSearchFinished(IntPtr hwnd, bool executed) { }

    // > 0: the host re-activates the card's own box this many milliseconds after a selection change
    // settles, for hosts that steal focus back while mirroring the selection. 0 (the default) means
    // never re-take it.
    int SelectionSyncFocusReclaimDelayMs => 0;
}
```

- **`GetDockBounds`**: Returns the physical bounds of the actual content area used for docking. The host uses this container rectangle to size and position the inline search box; adapters should return the active Explorer pane or dialog content region rather than an unrelated outer window when those bounds can be resolved.
- **`CanTrigger`** is the gate on *every* keystroke, so it must answer from the class name it is given — the hook cannot afford a UI Automation round trip there.
- **`GetListItems`**: Names of the rows currently shown, used for selection mirroring. Returning nothing is fine; several supported managers report empty strings for their rows, which is why the host does not identify a row by name alone.
- **`CanEnterActionsMode`**: `false` removes the action menu for this host entirely — right-click, `Ctrl+O` and `→` all stand down, rather than opening an empty panel.
- **`OnSearchFinished(hwnd, executed)`**: Called when the card closes, with whether a result was actually run, which is when a host that had to suppress its own UI (info tips, rename edits) should put it back.

## 5. Quick Navigation Provider `IQuickNavigationProvider`

Contributes dynamic groups and items to the [**Quick Navigation Menu**](../../user-guide/hotkeys):

```csharp
public enum MouseTriggerType { DoubleClick, MiddleClick }

public interface IQuickNavigationProvider : IPluginComponent
{
    string GroupName { get; }                                   // Root group header text
    string IPluginComponent.Name => GroupName;                   // mapped, not authored

    Action<ISearchResult>? HeaderAction => null;                 // Action button on header row (e.g. "+")
    string? HeaderActionTooltip => null;                         // ToolTip for that button

    bool CanProvide(ISearchResult result);
    IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu);
    void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd);

    // No default: implementing it is mandatory. Tears down anything the menu left allocated
    // (cached shell CDS streams, native icon handles) when the menu closes.
    void ClearSession();
}
```

- **`HeaderAction`**: Appends an action button to the root group header (e.g. bookmark providers adding "Pin current folder"). The Folder Cascader plugin's "save the folder you are in" `+` button is this member.
- **`DynamicMenuItem.IsHeader`**: In nested submenus, returning items with `IsHeader = true` renders interactive group headers with action buttons.
- **`MouseTriggerType`**: Names the two global gestures that can open the menu. Which of them are live is a user setting, not a provider decision — see [**Hotkeys → Quick Navigation Mouse Triggers**](../../user-guide/settings/hotkeys-page).

## 6. Registries

The host looks adapters up through four static registries in `Lertaro.PluginSdk.Registries`, which is also how a plugin's component reaches the Hook process:

| Registry | Members |
| :--- | :--- |
| `ActivePathCollectorRegistry` | `Register(IActivePathCollector)`, `GetCollectors()`, `GetAllCollectors()` |
| `FileDialogAdapterRegistry` | `Register(IFileDialogAdapter)`, `GetMatchingAdapter(hwnd, className, processName)`, `GetAdapters()`, `GetAllAdapters()` |
| `InlineSearchAdapterRegistry` | `Register(IInlineSearchAdapter)`, `GetMatchingAdapter(hwnd, className, processName)`, `GetAdapters()`, `GetAllAdapters()` |
| `OpenedFolderCollectorRegistry` | `GetOpenedFolders()` — concatenates what every enabled collector reports; duplicates are **retained by design**, and one collector that throws is skipped so a broken file manager cannot sink the whole snapshot |

The first three each expose a host-assigned `Func<T, bool> FilterFunc`: the host narrows it to the components the user has enabled, so `GetCollectors()` / `GetAdapters()` return the filtered view while `GetAllCollectors()` / `GetAllAdapters()` return everything registered. A plugin never assigns it. Matching order is registration order, and the first adapter whose `CanHandle` answers `true` owns the window — which is why a generic `#32770` dialog adapter must not claim a window a specialised one already covers. The dialog registry adds one veto on top of that: after an adapter claims a window, a block-listed window caption makes the lookup return `null` instead of falling through to the next adapter, so no adapter serves that window at all.
