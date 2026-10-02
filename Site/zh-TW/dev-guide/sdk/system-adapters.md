# 系統與對話方塊適配

本章節介紹 `Lertaro.PluginSdk` 中用於深度視窗停靠、活動目錄擷取，以及跨 Windows 檔案總管、原生檔案對話方塊與第三方檔案管理器進行內嵌搜尋整合的適配器介面。

這四個介面都位於 `Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters`，並衍生自 `IPluginComponent`——宿主在**設定 → 外掛模組**中列出的 `Name` 就來自那裡——這些介面本身都沒宣告 `Name`。

> [!NOTE]
> `IActivePathCollector`、`IFileDialogAdapter` 與 `IInlineSearchAdapter` 的實作會被宿主載入至**特權 Hook 輔助處理程序**中，以便與以管理員身分執行的視窗互動時繞過 Windows UIPI 隔離。這就是為什麼它們的成員必須廉價且不具互動性：它們跑在低階鍵盤/滑鼠鉤子回呼上，任何超過系統 `LowLevelHooksTimeout` 的停滯都會無聲地丟棄該鉤子。

## 1. 已開啟資料夾收集器 `IOpenedFolderCollector`

契約中唯讀的那一半：報告目標管理器目前開啟的每一個資料夾，這正是餵給[**快速導覽**](../../user-guide/hotkeys)以及內嵌搜尋清單中「目前開啟的資料夾」群組的來源。

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

public readonly record struct OpenedFolder(string Path, IntPtr WindowHandle);

public interface IOpenedFolderCollector : IPluginComponent
{
    IReadOnlyList<OpenedFolder> GetOpenedFolders() => Array.Empty<OpenedFolder>();
}
```

適配器會為每個開啟的視窗返回一筆項目，因此有五個分頁的管理器會報告五個資料夾。註冊表會把那份清單**刻意保留重複**地交下去——被兩個收集器看見、或被同一個收集器看見兩次的資料夾就會出現兩次；想要集合形式的呼叫端得自行依路徑去重。

## 2. 活動路徑收集器 `IActivePathCollector`

`IActivePathCollector` **延伸** `IOpenedFolderCollector`：一個能指出特定視窗所屬資料夾的收集器，通常也會一併提供已開啟資料夾清單，而它只要實作繼承下來的預設方法就能免費取得。

從已取得焦點的前景視窗擷取活動工作目錄，讓 Lertaro 能夠界定內嵌搜尋的範圍或解析相對路徑：

```csharp
public interface IActivePathCollector : IOpenedFolderCollector
{
    string TargetName { get; }   // 目標管理器名稱（如 "Directory Opus"、"Total Commander"）

    // 三個多載，一個比一個是更粗糙的問題。只有類別名稱形式是必須的；其餘多載預設轉呼叫它，
    // 因此一個尚無法區分視窗的收集器，也能為每個呼叫端正確作答。
    bool CanHandle(string className);
    bool CanHandle(string windowClassName, string windowTitle) => CanHandle(windowClassName);
    bool CanHandle(IntPtr windowHwnd, string windowClassName, string processName) => CanHandle(windowClassName);

    string? TryGetPath(
        IntPtr activeHwnd, string activeClassName,   // 取得焦點的控制項
        IntPtr windowHwnd, string windowClassName,   // 其頂層視窗
        string processName);
}
```

- 取得焦點的控制項與父視窗分開傳入，讓路徑能從巢狀控制項（網址列、樹狀檢視）讀出，而不只是從整個視窗。
- 當視窗可辨識、但其資料夾在那個當下無法解析時返回 `null`——那不是失敗，宿主只是保留先前的範圍不動。

## 3. 原生檔案對話方塊適配器 `IFileDialogAdapter`

檢查並控制 Windows 原生的 Open / Save / Browse 對話方塊：

```csharp
public interface IFileDialogAdapter : IPluginComponent
{
    bool CanHandle(IntPtr hwnd, string className, string processName);
    string? GetCurrentPath(IntPtr hwnd);
    bool NavigateTo(IntPtr hwnd, string targetPath);

    bool TargetIsFolderOnly => false;  // 目標輸入是否僅接受資料夾（如壓縮檔解壓）
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor) => true;

    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);          // 卡片要停靠在何處

    // 位置探測：對話方塊自己的目標欄位在哪裡、它的檔案清單又在哪裡。內嵌卡片會讀取這兩個
    // 結果來決定要把自己掛在哪——欄位下方、清單上方，或下方沒空間時能放下的地方。任一項返回
    // false 時，宿主就退回 GetDockBounds。兩者都預設為「我看不到那個控制項」。
    bool TryGetTargetFieldBounds(IntPtr hwnd, out AdapterRect bounds) { bounds = default; return false; }
    bool TryGetFileListBounds(IntPtr hwnd, out AdapterRect bounds) { bounds = default; return false; }

    bool RestoreFocus(IntPtr hwnd);
}

public struct AdapterRect   // 實體像素
{
    public int Left, Top, Right, Bottom;
}
```

- **`TargetIsFolderOnly`**：為 `true` 時，若使用者從搜尋結果中選取了一個檔案，宿主會在呼叫 `NavigateTo` 前自動解析出它的父資料夾。
- **`TryGetTargetFieldBounds` / `TryGetFileListBounds`**：只用於卡片的位置安排。定位器偏好把卡片掛在對話方塊目標欄位的下方，並拿檔案清單作為退路錨點；適配器兩者都解析不了的對話方塊，就直接取用 `GetDockBounds` 的矩形。
- **`RestoreFocus`**：把鍵盤交還給對話方塊自己的編輯欄。宿主在使用者離開內嵌卡片時（`Escape`，或在一張空白卡片上再次按下呼出快速鍵）呼叫它，因此它不得啟用其他任何東西。

## 4. 內嵌搜尋適配器 `IInlineSearchAdapter`

將 Lertaro 的搜尋卡片嵌入目標檔案對話方塊或檔案總管視窗，維持雙向的選取同步：

```csharp
public interface IInlineSearchAdapter : IPluginComponent
{
    bool IsFileExplorer => false;      // Windows 檔案總管為真

    bool CanHandle(IntPtr hwnd, string className, string processName);

    // 辨識而不觸發。預設轉呼叫 CanHandle；當視窗明明是你支援的宿主、卻不該呼出卡片時覆寫它
    // ——例如命令列或重新命名編輯框取得焦點時，那裡的輸入屬於宿主，而非 Lertaro。
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

    // > 0：選取變動安定之後，過了這麼多毫秒，宿主會重新啟用卡片自己的輸入框，
    // 用於那些在鏡像選取時會把焦點搶回去的宿主。0（預設）表示永不重新取回。
    int SelectionSyncFocusReclaimDelayMs => 0;
}
```

- **`GetDockBounds`**：返回實際用於停靠的內容區域實體邊界。宿主使用這個容器矩形來決定內嵌搜尋框的大小與位置；當這些邊界可以解析時，適配器應返回目前的檔案總管窗格或對話方塊內容區域，而不是無關的外層視窗。
- **`CanTrigger`** 是*每一次*擊鍵的閘門，因此它必須只用傳入的類別名稱作答——鉤子在那裡負擔不起一次 UI Automation 往返。
- **`GetListItems`**：目前顯示的各列名稱，用於選取鏡像。返回空也沒有關係；幾個受支援的管理器會為其各列回報空字串，這就是宿主不只用名稱辨識某列的原因。
- **`CanEnterActionsMode`**：`false` 會為此宿主完全移除動作功能表——右鍵、`Ctrl+O` 與 `→` 一併退下，而不是開啟一個空白面板。
- **`OnSearchFinished(hwnd, executed)`**：卡片關閉時呼叫，並帶上是否有結果真的被執行；這時那些必須隱藏自身 UI（資訊提示、重新命名編輯框）的宿主就該把它們放回去。

## 5. 快速導覽提供者 `IQuickNavigationProvider`

為[**快速導覽功能表**](../../user-guide/hotkeys)貢獻動態群組與項目：

```csharp
public enum MouseTriggerType { DoubleClick, MiddleClick }

public interface IQuickNavigationProvider : IPluginComponent
{
    string GroupName { get; }                                   // 根群組標題文字
    string IPluginComponent.Name => GroupName;                   // 對應而來，非自行撰寫

    Action<ISearchResult>? HeaderAction => null;                 // 標題列上的動作按鈕（如 "+"）
    string? HeaderActionTooltip => null;                         // 該按鈕的 ToolTip

    bool CanProvide(ISearchResult result);
    IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu);
    void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd);

    // 無預設：實作它是必須的。當功能表關閉時，清除功能表遺留的任何已配置資源
    // （快取的 Shell CDS 串流、原生圖示控制代碼）。
    void ClearSession();
}
```

- **`HeaderAction`**：在根群組標題附加一個動作按鈕（例如書籤提供者加上「釘選目前資料夾」）。資料夾層級選擇器（Folder Cascader）外掛模組那個「儲存你所在的資料夾」的 `+` 按鈕就是這個成員。
- **`DynamicMenuItem.IsHeader`**：在巢狀子功能表中，返回 `IsHeader = true` 的項目會轉譯出帶有動作按鈕的互動式群組標題。
- **`MouseTriggerType`**：命名了兩個可以開啟功能表的全域手勢。哪些手勢生效屬於使用者設定，而非提供者的決定——見[**快速鍵 → 快速導覽滑鼠觸發**](../../user-guide/settings/hotkeys-page)。

## 6. 註冊表

宿主透過 `Lertaro.PluginSdk.Registries` 中的四個靜態註冊表查找適配器，這也是一個外掛模組的元件抵達 Hook 處理程序的方式：

| 註冊表 | 成員 |
| :--- | :--- |
| `ActivePathCollectorRegistry` | `Register(IActivePathCollector)`、`GetCollectors()`、`GetAllCollectors()` |
| `FileDialogAdapterRegistry` | `Register(IFileDialogAdapter)`、`GetMatchingAdapter(hwnd, className, processName)`、`GetAdapters()`、`GetAllAdapters()` |
| `InlineSearchAdapterRegistry` | `Register(IInlineSearchAdapter)`、`GetMatchingAdapter(hwnd, className, processName)`、`GetAdapters()`、`GetAllAdapters()` |
| `OpenedFolderCollectorRegistry` | `GetOpenedFolders()`——串接每個已啟用收集器所回報的內容；重複項目是**刻意保留**的，而擲出例外的某個收集器會被跳過，這樣一個壞掉的檔案管理器就無法拖垮整份快照 |

前三個註冊表各自暴露一個由宿主指派的 `Func<T, bool> FilterFunc`：宿主會把它收窄到使用者已啟用的元件，因此 `GetCollectors()` / `GetAdapters()` 返回篩選後的視圖，而 `GetAllCollectors()` / `GetAllAdapters()` 返回所有已註冊的項目。外掛模組永遠不會指派它。比對順序就是註冊順序，而且第一個 `CanHandle` 回答 `true` 的適配器就擁有該視窗——這正是為何一個通用的 `#32770` 對話方塊適配器不得聲稱某個已被特化適配器涵蓋的視窗。對話方塊註冊表在此之上還多加了一項否決權：適配器認領某個視窗之後，若視窗標題落在封鎖清單上，這次查詢就會返回 `null`，而不會繼續往下嘗試下一個適配器，也就是說沒有任何適配器會服務那個視窗。
