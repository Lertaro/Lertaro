# 系統與對話方塊適配

本章節介紹 `Lertaro.PluginSdk` 中用於與外部前景視窗（Windows 檔案總管、原生檔案開啟/儲存對話方塊、第三方檔案管理器等）進行深度掛載、目錄探測與內嵌搜尋互動的適配器介面。

> [!NOTE]
> 本頁面中的 `IActivePathCollector`、`IFileDialogAdapter` 與 `IInlineSearchAdapter` 元件會被宿主載入至**特權 Hook 輔助處理程序**中執行，以確保能夠跨越 UIPI 隔離與以管理員身分啟動的檔案視窗安全互動。

## 1. 活動目錄收集器 `IActivePathCollector`

用於從目前獲得焦點的活動視窗中精準擷取「目前工作目錄路徑」，使 Lertaro 能夠正確界定內嵌搜尋的作用範圍或執行相對路徑定位：

```csharp
namespace Lertaro.PluginSdk;

public interface IActivePathCollector
{
    string Name { get; }
    string TargetName { get; }   // 目標管理器名稱（如 "Directory Opus"、"Total Commander"）
    bool CanHandle(string className);
    string? TryGetPath(
        IntPtr activeHwnd, string activeClassName,
        IntPtr windowHwnd, string windowClassName,
        string processName);
}
```

- 將獲得焦點的子控制項（`activeHwnd`）與頂層主視窗（`windowHwnd`）分開傳入，以便適配器從深層網址列或樹狀檢視中擷取路徑。

## 2. 原生檔案對話方塊適配器 `IFileDialogAdapter`

用於讀取並驅動 Windows 原生檔案開啟/儲存對話方塊（Common Item Dialog 或傳統對話方塊）：

```csharp
public interface IFileDialogAdapter
{
    string Name { get; }
    bool CanHandle(IntPtr hwnd, string className, string processName);
    string? GetCurrentPath(IntPtr hwnd);
    bool NavigateTo(IntPtr hwnd, string targetPath);
    bool TargetIsFolderOnly => false;  // 目標是否僅接受資料夾（如壓縮解壓目錄）
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor) => true;
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);
    bool RestoreFocus(IntPtr hwnd);
}
```

- **`TargetIsFolderOnly`**：當設為 `true` 時，若使用者在搜尋結果中選取了一個具體檔案，宿主會在呼叫 `NavigateTo` 前自動將其剖析為其所在的父級資料夾，避免輸入框格式錯誤。
- **`AdapterRect`**：包含 `{ Left, Top, Right, Bottom }` 實體像素邊界。

## 3. 內嵌搜尋適配器 `IInlineSearchAdapter`

將 Lertaro 的搜尋列直接掛載至目標視窗或檔案對話方塊內部，實現即時的內嵌搜尋與選取狀態雙向同步：

```csharp
public interface IInlineSearchAdapter
{
    string Name { get; }
    bool IsFileExplorer => false;      // 是否為系統檔案總管
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

## 4. 滑鼠快速導覽提供者 `IQuickNavigationProvider`

為滑鼠連按兩下或中鍵呼出的[**快速導覽階層式功能表**](../../user-guide/hotkeys#3-快速導覽滑鼠觸發)貢獻動態分組與項目：

```csharp
public interface IQuickNavigationProvider
{
    string GroupName { get; }           // 根層級分組標題
    Action<ISearchResult>? HeaderAction => null; // 分組標題列尾部的附加操作按鈕（如 "+" 按鈕）
    string? HeaderActionTooltip => null;// 標題列操作按鈕的 ToolTip 提示
    bool CanProvide(ISearchResult result);
    IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu);
    void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd);
    void ClearSession() { }
}
```

- **`HeaderAction`**：可在根層級分組標題右側新增小圖示按鈕（例如書籤提供者利用此按鈕實現「新增目前資料夾」）。
- **`DynamicMenuItem.IsHeader`**：在巢狀子功能表中，可透過返回帶有 `IsHeader = true` 的功能表項目實現子功能表內部帶有操作按鈕的分組標題行。
