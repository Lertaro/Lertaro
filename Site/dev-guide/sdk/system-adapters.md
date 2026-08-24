# System & Dialog Adapters

This chapter introduces adapter interfaces in `Lertaro.PluginSdk` for deep window docking, active directory extraction, and inline search integration across Windows File Explorer, native file dialogs, and third-party file managers.

> [!NOTE]
> `IActivePathCollector`, `IFileDialogAdapter`, and `IInlineSearchAdapter` implementations are loaded into the **elevated Hook helper process** by the host to bypass Windows UIPI isolation when interacting with administrator-run windows.

## 1. Active Path Collector `IActivePathCollector`

Extracts the active working directory from the focused foreground window, enabling Lertaro to scope inline searches or resolve relative paths:

```csharp
namespace Lertaro.PluginSdk;

public interface IActivePathCollector
{
    string Name { get; }
    string TargetName { get; }   // Target manager name (e.g. "Directory Opus", "Total Commander")
    bool CanHandle(string className);
    string? TryGetPath(
        IntPtr activeHwnd, string activeClassName,
        IntPtr windowHwnd, string windowClassName,
        string processName);
}
```

- Passes the focused control (`activeHwnd`) and parent window (`windowHwnd`) separately to handle cases where paths reside inside nested controls (address bars, tree views).

## 2. Native File Dialog Adapter `IFileDialogAdapter`

Inspects and controls native Windows Open / Save file dialogs:

```csharp
public interface IFileDialogAdapter
{
    string Name { get; }
    bool CanHandle(IntPtr hwnd, string className, string processName);
    string? GetCurrentPath(IntPtr hwnd);
    bool NavigateTo(IntPtr hwnd, string targetPath);
    bool TargetIsFolderOnly => false;  // True if target input accepts only folders (e.g. archive extraction)
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor) => true;
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);
    bool RestoreFocus(IntPtr hwnd);
}
```

- **`TargetIsFolderOnly`**: When `true`, if the user selects a file from search results, the host automatically resolves its parent folder before invoking `NavigateTo`.
- **`AdapterRect`**: Contains physical pixel boundaries `{ Left, Top, Right, Bottom }`.

## 3. Inline Search Adapter `IInlineSearchAdapter`

Embeds the Lertaro search bar directly into target file dialogs or File Explorer windows, maintaining two-way selection synchronization:

```csharp
public interface IInlineSearchAdapter
{
    string Name { get; }
    bool IsFileExplorer => false;      // True for Windows File Explorer
    bool CanHandle(IntPtr hwnd, string className, string processName);
    bool CanTrigger(IntPtr focusedHwnd, string className);
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor) => CanTrigger(hwndUnderCursor, classNameUnderCursor);
    bool CanEnterActionsMode(IntPtr hwnd);
    string? GetSearchScope(IntPtr hwnd);
    bool ExecuteItem(IntPtr hwnd, string path, string searchInput);
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);
    IEnumerable<string> GetListItems(IntPtr hwnd) => [];
    void OnSelectionChanged(IntPtr hwnd, string path) { }
    void OnSearchFinished(IntPtr hwnd, bool executed) { }
}
```

## 4. Quick Navigation Provider `IQuickNavigationProvider`

Contributes dynamic groups and items to the [**Quick Navigation Menu**](../../user-guide/hotkeys#3-quick-navigation-mouse-triggers):

```csharp
public interface IQuickNavigationProvider
{
    string GroupName { get; }           // Root group header text
    Action<ISearchResult>? HeaderAction => null; // Action button on header row (e.g. "+" button)
    string? HeaderActionTooltip => null;// ToolTip for header button
    bool CanProvide(ISearchResult result);
    IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu);
    void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd);
    void ClearSession() { }
}
```

- **`HeaderAction`**: Appends an action button to the root group header (e.g. bookmark providers adding "Pin current folder").
- **`DynamicMenuItem.IsHeader`**: In nested submenus, returning items with `IsHeader = true` renders interactive group headers with action buttons.
