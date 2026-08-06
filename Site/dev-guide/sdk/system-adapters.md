# System & Dialog Adapters

These interfaces let a plugin integrate Lertaro with *other* windows — File Explorer, native
file-picker dialogs, third-party file managers — rather than just its own search windows.

## `IActivePathCollector`

Extracts the "current directory" from whatever foreground window is active, so Lertaro knows
what to scope a search to (or resolve a relative action against).

```csharp
interface IActivePathCollector
{
    string Name { get; }
    string TargetName { get; }   // localized name of the app/manager this targets
    bool CanHandle(string className);
    string? TryGetPath(
        IntPtr activeHwnd, string activeClassName,
        IntPtr windowHwnd, string windowClassName,
        string processName);
}
```

Both the active (focused) element and its containing window are passed in separately, since many
file managers put the actual path in a child control (an address bar, a tree view selection) that
isn't the top-level window itself.

## `IFileDialogAdapter`

Reads and drives natively-rendered Windows Open/Save file dialogs, so Lertaro can be embedded
into them (see [`IInlineSearchAdapter`](#iinlinesearchadapter) below) and keep them in sync.

```csharp
interface IFileDialogAdapter
{
    string Name { get; }
    bool CanHandle(IntPtr hwnd, string className, string processName);
    string? GetCurrentPath(IntPtr hwnd);
    bool NavigateTo(IntPtr hwnd, string targetPath);
    bool TargetIsFolderOnly { get; } // default: false
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor); // default: true
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);
    bool RestoreFocus(IntPtr hwnd);
}
```

`TargetIsFolderOnly` is `true` for a dialog whose target field can only ever hold a folder — an
archive tool's "extract to" destination, say — never a specific file, unlike an Open/Save dialog's
filename box. The host uses it to decide whether a picked search result that's a file needs
resolving to its containing folder before ever reaching `NavigateTo`, rather than leaving that to
`NavigateTo` itself: that call runs in the elevated Hook process, where `File.Exists`/
`Directory.Exists` can't be trusted for a drive the interactive user mapped without elevation.
Leave it at its default `false` for anything with a real filename box.

## `IInlineSearchAdapter`

Embeds a Lertaro search bar directly into a target file dialog or file explorer window (the
"inline window" from the User Manual), keeping selection in sync in both directions.

```csharp
interface IInlineSearchAdapter
{
    string Name { get; }
    bool IsFileExplorer { get; }   // default false
    bool CanHandle(IntPtr hwnd, string className, string processName);
    bool CanTrigger(IntPtr focusedHwnd, string className);
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor); // default: delegates to CanTrigger
    bool CanEnterActionsMode(IntPtr hwnd);
    string? GetSearchScope(IntPtr hwnd);
    bool ExecuteItem(IntPtr hwnd, string path, string searchInput);
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);
    IEnumerable<string> GetListItems(IntPtr hwnd);        // optional
    void OnSelectionChanged(IntPtr hwnd, string path);    // optional
    void OnSearchFinished(IntPtr hwnd, bool executed);    // optional
}
```

`AdapterRect` (shared with `IFileDialogAdapter`) is a plain `{ Left, Top, Right, Bottom }` `int`
rectangle.

## `IQuickNavigationProvider`

Supplies content (a cascaded menu, typically) for the Quick Navigation popup — see
[Hotkeys → Quick navigation](../../user-guide/hotkeys#quick-navigation-mouse). Whether the popup
opens at all for a given click is decided by the host, not by this interface: any window already
recognized by `IInlineSearchAdapter`/`IFileDialogAdapter` triggers it for you, so this is purely a
content source.

```csharp
interface IQuickNavigationProvider
{
    string GroupName { get; }
    Action<ISearchResult>? HeaderAction => null;
    string? HeaderActionTooltip => null;
    bool CanProvide(ISearchResult result);
    IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu);
    void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd);
    void ClearSession();
}
```

`GroupName` labels a section header shown above this provider's own root-level items, so a user with
more than one quick-navigation provider active can tell which contributed which entries — the same
role `IDynamicActionProvider.GroupName` plays for the actions menu.

`HeaderAction` (optional, default `null`) adds a small button to that same root-level group header —
for example, a bookmarking-style provider might use it for "add the current folder." It's invoked
with the same `ISearchResult` `GetMenuItems` itself receives for the root level; `HeaderActionTooltip`
sets the button's tooltip and is ignored when `HeaderAction` is null. A nested submenu (any depth
below the root) has no host-rendered header of its own, so `HeaderAction`'s effect stops at the root —
a provider wanting the same "+" button on a submenu returns a `DynamicMenuItem` with `IsHeader = true`
(see below) as that submenu's own first item instead, with its own `OnExecute` playing the same role.

`DynamicMenuItem` is the same model used by
[`IDynamicActionProvider`](./core-search-actions#idynamicactionprovider), including its `IsHeader`
flag for a submenu-level header row.
