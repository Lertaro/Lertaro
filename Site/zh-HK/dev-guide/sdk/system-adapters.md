# 系統與對話方塊適配

本章節介紹 `Lertaro.PluginSdk` 中用於在 Windows 檔案總管、原生檔案對話方塊與第三方檔案管理器之間進行深度視窗掛載、活動目錄擷取與內嵌搜尋整合的適配器介面。

這四個介面全數位於 `Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters`，並繼承自 `IPluginComponent`；宿主列在**設定 → 外掛模組**下的 `Name` 就來自該基礎介面——這些介面沒有一個自行宣告 `Name`。

> [!NOTE]
> `IActivePathCollector`、`IFileDialogAdapter` 與 `IInlineSearchAdapter` 的實作會被宿主載入至**特權 Hook 輔助程序**中，以便在與以管理員身分執行的視窗互動時繞過 Windows UIPI 隔離。這正是它們的成員必須廉價、不帶任何互動的原因：它們運行在低階鍵盤/滑鼠掛鉤的回呼上，任何超過系統 `LowLevelHooksTimeout` 的停滯都會讓掛鉤被無聲地丟棄。

## 1. 已開啟資料夾收集器 `IOpenedFolderCollector`

這是契約中唯讀的那一半：回報目標管理器目前開啟的每一個資料夾，[**快速導覽**](../../user-guide/hotkeys)中的 **目前開啟的資料夾** 分組與內嵌搜尋清單的資料都來自這裡。

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

public readonly record struct OpenedFolder(string Path, IntPtr WindowHandle);

public interface IOpenedFolderCollector : IPluginComponent
{
    IReadOnlyList<OpenedFolder> GetOpenedFolders() => Array.Empty<OpenedFolder>();
}
```

適配器對每個開啟的視窗返回一筆資料，因此擁有五個分頁的管理器會回報五個資料夾。註冊表會**刻意保留重複**地把那份清單交下去——被兩個收集器看到的資料夾，或被同一個收集器看到兩次的資料夾，就會出現兩次；想要集合的呼叫端會自行依路徑去重。

## 2. 活動路徑收集器 `IActivePathCollector`

`IActivePathCollector` **繼承自** `IOpenedFolderCollector`：一個能指出特定視窗所屬資料夾的收集器，通常也會一併貢獻已開啟資料夾清單，而只要實作繼承下來的預設成員就等於免費取得它。

從取得焦點的前景視窗中擷取活動工作目錄，讓 Lertaro 得以界定內嵌搜尋的範圍或解析相對路徑：

```csharp
public interface IActivePathCollector : IOpenedFolderCollector
{
    string TargetName { get; }   // 目標管理器名稱（如 "Directory Opus"、"Total Commander"）

    // 三個多載，一個比一個粗糙。只有類別名稱形式是強制實作的；其餘多載都以它為預設值，
    // 因此暫時還無法分辨視窗的收集器也能為所有呼叫端給出正確答案。
    bool CanHandle(string className);
    bool CanHandle(string windowClassName, string windowTitle) => CanHandle(windowClassName);
    bool CanHandle(IntPtr windowHwnd, string windowClassName, string processName) => CanHandle(windowClassName);

    string? TryGetPath(
        IntPtr activeHwnd, string activeClassName,   // 取得焦點的控制項
        IntPtr windowHwnd, string windowClassName,   // 它的頂層視窗
        string processName);
}
```

- 取得焦點的控制項與其父視窗會分開傳入，讓路徑得以從巢狀控制項（網址列、樹狀檢視）中讀出，而不只是從整個視窗讀取。
- 視窗認得出、但當下無法解析其資料夾時返回 `null`——這不算失敗，宿主只是保留先前的範圍不去動它。

## 3. 原生檔案對話方塊適配器 `IFileDialogAdapter`

探測並操控 Windows 原生的開啟／儲存／瀏覽對話方塊：

```csharp
public interface IFileDialogAdapter : IPluginComponent
{
    bool CanHandle(IntPtr hwnd, string className, string processName);
    string? GetCurrentPath(IntPtr hwnd);
    bool NavigateTo(IntPtr hwnd, string targetPath);

    bool TargetIsFolderOnly => false;  // 目標輸入是否僅接受資料夾（如壓縮檔解除套用）
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor) => true;

    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);          // 卡片該停靠在何處

    // 位置探測：對話方塊自己的目標欄位在哪裡、它的檔案清單在哪裡。內嵌卡片兩者都讀，
    // 據此決定要掛在哪裡——欄位下方、清單上方，或是下方沒有空間時放得下的位置。
    // 兩者任一返回 false，宿主就退回 GetDockBounds。兩者預設都回答「那個控制項我看不到」。
    bool TryGetTargetFieldBounds(IntPtr hwnd, out AdapterRect bounds) { bounds = default; return false; }
    bool TryGetFileListBounds(IntPtr hwnd, out AdapterRect bounds) { bounds = default; return false; }

    bool RestoreFocus(IntPtr hwnd);
}

public struct AdapterRect   // 實體像素
{
    public int Left, Top, Right, Bottom;
}
```

- **`TargetIsFolderOnly`**：為 `true` 時，若使用者從搜尋結果中選取了一個檔案，宿主會在呼叫 `NavigateTo` 之前自動解析其父級資料夾。
- **`TryGetTargetFieldBounds` / `TryGetFileListBounds`**：僅供卡片定位使用。定位器會優先把卡片掛在對話方塊的目標欄位下方，並以檔案清單作為退路錨點；適配器兩者都解析不出的對話方塊，就直接拿 `GetDockBounds` 的矩形。
- **`RestoreFocus`**：把鍵盤交還給對話方塊自己的編輯欄位。宿主會在使用者離開內嵌卡片時呼叫它（按 `Escape`，或在卡片為空時再按一次呼出快速鍵），因此這個方法不得再去啟用任何別的東西。

## 4. 內嵌搜尋適配器 `IInlineSearchAdapter`

將 Lertaro 的搜尋卡片內嵌進目標檔案對話方塊或檔案總管視窗，並維持選取狀態的雙向同步：

```csharp
public interface IInlineSearchAdapter : IPluginComponent
{
    bool IsFileExplorer => false;      // 是否為 Windows 檔案總管

    bool CanHandle(IntPtr hwnd, string className, string processName);

    // 只認出、不觸發。預設沿用 CanHandle；當視窗確實屬於你支援的那個宿主程式，卻又不得呼出
    // 卡片時改寫它——例如焦點在命令列或重新命名編輯框上，那裡的打字權屬於該宿主程式，不是 Lertaro。
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

    // > 0：選取變更穩定後，宿主會在這麼多毫秒後重新啟用卡片自己的搜尋框，
    // 用於那些在同步選取狀態時會把焦點搶回去的宿主程式。0（預設值）代表永不重新取回。
    int SelectionSyncFocusReclaimDelayMs => 0;
}
```

- **`GetDockBounds`**：返回實際用於停靠的內容區域實體邊界。宿主會使用這個容器矩形計算內嵌搜尋框的大小和位置；適配器應返回目前的檔案總管窗格或對話方塊內容區域，而不是無關的外層視窗——前提是這些邊界解析得出來。
- **`CanTrigger`** 是*每一次*擊鍵的閘門，因此它必須只憑交給它的類別名稱就能回答——掛鉤在那個位置上負擔不起一次 UI Automation 往返。
- **`GetListItems`**：目前畫面上各列的名稱，供選取鏡像同步使用。返回空值沒問題；數個受支援的管理器會為自己的列回報空白字串，這正是宿主不只靠名稱來辨識資料列的原因。
- **`CanEnterActionsMode`**：`false` 會為這個宿主程式完全移除動作選單——滑鼠右鍵、`Ctrl+O` 與 `→` 一律退讓，而不是開出一個空白的面板。
- **`OnSearchFinished(hwnd, executed)`**：卡片關閉時呼叫，並一併帶上結果是否真的被執行過；被迫隱藏自身 UI（提示氣球、重新命名編輯框）的宿主程式就是在這個時機把它們放回原位。

## 5. 快速導覽提供者 `IQuickNavigationProvider`

為[**快速導覽級聯選單**](../../user-guide/hotkeys)貢獻動態分組與項目：

```csharp
public enum MouseTriggerType { DoubleClick, MiddleClick }

public interface IQuickNavigationProvider : IPluginComponent
{
    string GroupName { get; }                                   // 根分組的標題文字
    string IPluginComponent.Name => GroupName;                   // 由 GroupName 對應而來，並非另行撰寫

    Action<ISearchResult>? HeaderAction => null;                 // 標題列上的操作按鈕（如 "+"）
    string? HeaderActionTooltip => null;                         // 該按鈕的 ToolTip 提示

    bool CanProvide(ISearchResult result);
    IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu);
    void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd);

    // 沒有預設實作：實作它是強制性的。選單關閉時呼叫，用於釋放在選單期間留下的分配
    // （快取的 shell CDS 串流、原生圖示控制代碼）。
    void ClearSession();
}
```

- **`HeaderAction`**：在根分組標題上附加一個操作按鈕（例如書籤提供者加上「釘選目前資料夾」）。隨包 Folder Cascader 外掛模組那個「儲存你所在資料夾」的 `+` 按鈕就是這個成員。
- **`DynamicMenuItem.IsHeader`**：在巢狀子選單中返回 `IsHeader = true` 的項目，可以渲染出帶有操作按鈕的互動式分組標題列。
- **`MouseTriggerType`**：指出可以開啟選單的兩種全域滑鼠手勢。其中哪幾種生效是使用者設定，不是提供者的決定——詳見[**快速鍵 → 快速導覽滑鼠觸發**](../../user-guide/settings/hotkeys-page)。

## 6. 註冊表

宿主透過 `Lertaro.PluginSdk.Registries` 中的四個靜態註冊表查閱適配器，而外掛模組的元件也正是循這條路徑抵達 Hook 程序：

| 註冊表 | 成員 |
| :--- | :--- |
| `ActivePathCollectorRegistry` | `Register(IActivePathCollector)`、`GetCollectors()`、`GetAllCollectors()` |
| `FileDialogAdapterRegistry` | `Register(IFileDialogAdapter)`、`GetMatchingAdapter(hwnd, className, processName)`、`GetAdapters()`、`GetAllAdapters()` |
| `InlineSearchAdapterRegistry` | `Register(IInlineSearchAdapter)`、`GetMatchingAdapter(hwnd, className, processName)`、`GetAdapters()`、`GetAllAdapters()` |
| `OpenedFolderCollectorRegistry` | `GetOpenedFolders()`——串接所有已啟用收集器回報的內容；重複項目是**刻意保留**的，而擲出例外的那個收集器會被跳過，因此一個壞掉的檔案管理器無法拖垮整份快照 |

前三者各暴露一個由宿主指派的 `Func<T, bool> FilterFunc`：宿主會把它收窄到使用者已啟用的元件，因此 `GetCollectors()` / `GetAdapters()` 返回篩選後的檢視，而 `GetAllCollectors()` / `GetAllAdapters()` 返回全部已註冊的元件。外掛模組永遠不會指派它。比對順序就是註冊順序，第一個 `CanHandle` 回答 `true` 的適配器取得該視窗——這正是泛用的 `#32770` 對話方塊適配器不可宣稱一個已被專用適配器涵蓋的視窗的原因。對話方塊註冊表在此之上還多加了一項否決：一旦某個適配器宣稱了該視窗，被列在封鎖清單中的視窗標題會讓查詢直接返回 `null`，而不是繼續往下找下一個適配器，因此該視窗完全不會有任何適配器服務。
