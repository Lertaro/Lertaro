# 核心檢索與動作

本章節詳細介紹 `Lertaro.PluginSdk` 中用於貢獻搜尋資料來源、即時計算答案、非 ASCII 別名轉寫引擎、查詢後綴 Token 處理器以及靜態/動態快顯動作功能表的核心介面與資料結構。

## 1. 基礎元件規範 `IPluginComponent` 與 `IPlugin`

所有外掛模組擴充元件均直接或間接繼承自 `IPluginComponent`，用於向宿主宣告元件的中繼資料：

```csharp
namespace Lertaro.PluginSdk;

public interface IPluginComponent
{
    string Name => GetType().Name;      // 元件顯示名稱（預設取類別名稱）
    string Description => string.Empty; // 功能描述，在設定介面中作為 ToolTip 提示氣泡呈現
}

public interface IPlugin : IPluginComponent
{
    // 外掛模組主組件進入點識別碼
}
```

## 2. 貢獻搜尋結果

### 靜態可快取項目來源 `ISearchableItemProvider`

適用於內容相對靜態、列舉耗時但不需要隨每次擊鍵即時變化的場景（例如：開始功能表捷徑、瀏覽器書籤、系統控制台項目等）。

```csharp
public interface ISearchableItemProvider : IPluginComponent
{
    bool EnableAlias => true;           // 是否允許對此資料來源套用拼音等別名轉寫
    event Action? ItemsChanged;         // 當資料來源發生變動時觸發，通知宿主重新拉取並更新索引
    IEnumerable<SearchableItem> GetSearchableItems();
}
```

### 動態即時計算來源 `IInstantResultProvider`

在使用者每次敲擊鍵盤時即時觸發，適合形態由查詢字串本身決定的結果（例如：數學計算機、進位轉換、環境變數展開、網頁即時跳轉等）。

```csharp
public interface IInstantResultProvider : IPluginComponent
{
    IEnumerable<InstantResultItem> GetInstantResults(string query);
    bool[]? GetHighlightMask(string text, string query) => null; // 自訂比對反白遮罩
}
```

> [!TIP]
> `GetInstantResults` 為同步呼叫以保障打字流暢度。若需要發起網路請求（如線上翻譯或搜尋建議）：可先立即返回一個佔位結果項目，透過 `Task.Run` 在背景非同步獲取資料並快取，請求完成後呼叫 `SearchRefreshService.RefreshIfMatches` 通知宿主就地重新整理目前搜尋結果。

### 非 ASCII 別名轉寫引擎 `IAliasProvider`

用於為中文檔案名稱等非 ASCII 文字產生額外的可索引別名，支援混合拼音輸入比對：

```csharp
public interface IAliasProvider
{
    string Name { get; }
    bool CanHandle(string text);
    IReadOnlyList<(char Start, char End)> InputRanges { get; }  // 來源字元範圍（如 CJK 表意文字）
    IReadOnlyList<(char Start, char End)> OutputRanges { get; } // 產生別名字元範圍（如 a-z）
    IEnumerable<string> GetAliases(string text);

    int Version => 1;                                           // 規則更新時遞增以觸發重新索引
    int[]? MapAliasToSourceIndices(string text, string alias) => null; // 對應別名命中位置至原文以供反白
    void GetAliasesUtf8(string text, AliasByteSink dest);       // 零分配位元組原生建置
    IEnumerable<string> GetQueryForms(string term);             // 查詢側改寫（如拼音音節邊界切分）
}
```

### 查詢後綴 Token 處理器 `IQueryTokenProvider`

用於認領並處理搜尋框尾部的特定 Token 標記（例如 `report :size`、`doc :@today` 或 `image ::"hello world"`），對初步比對的結果清單進行串流二次變換（過濾、重新排序等）：

```csharp
public interface IQueryTokenProvider : IPluginComponent
{
    bool CanHandle(string token);
    Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results);
}
```

## 3. 結果上的快顯動作

### 動作提供者容器 `IActionProvider`

```csharp
public interface IActionProvider
{
    IEnumerable<ISearchResultAction> GetActions();
    IEnumerable<IDynamicActionProvider> GetDynamicActionProviders();
}
```

### 靜態動作契約 `ISearchResultAction`

表示一個明確的靜態操作（如「複製完整路徑」、「以管理員身分執行」等），呈現在 `Ctrl+O` 動作功能表中或綁定為全域動作快速鍵：

```csharp
public interface ISearchResultAction : IPluginComponent
{
    string GroupName { get; }           // 動作所屬分組名稱
    string DisplayName { get; }         // 動作顯示文字
    string? Hotkey { get; }             // 預設快速鍵（如 "Ctrl+Shift+C"）
    IReadOnlyList<string>? Keywords { get; }
    IReadOnlyList<string>? Parameters { get; }
    ImageSource Icon { get; }           // 動作圖示
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool CanExecute(IReadOnlyList<ISearchResult> selection);
    void Execute(IReadOnlyList<ISearchResult> selection, IPluginSearchWindow window);
}
```

### 動態功能表建置器 `IDynamicActionProvider`

在執行階段動態建置深層巢狀或系統級功能表（例如將 Windows Shell 原生快顯功能表插入到 Lertaro 中）：

```csharp
public interface IDynamicActionProvider
{
    string GroupName { get; }
    int? Priority => 0;                 // 在動作功能表中的預設展示優先順序
    IReadOnlyList<string>? Keywords { get; }
    IReadOnlyList<string>? Parameters { get; }
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    void Init() { }                     // 處理程序生命週期內最多觸發一次的預熱初始化
    bool CanProvide(IReadOnlyList<ISearchResult> selection);
    IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> selection, IntPtr hMenu);
    IEnumerable<(string Hotkey, Action Execute)> GetHotkeyActions(IReadOnlyList<ISearchResult> selection);
    void ExecuteCommand(IReadOnlyList<ISearchResult> selection, uint commandId, IntPtr ownerHwnd);
    void ClearSession() { }
}
```

## 4. 輔助資料結構

- **`SearchableItem` / `InstantResultItem`**：包含 `Title`、`Description`、`IconData`、`IconColor`、`ActionType`（`"Copy"` / `"Execute"` / `"None"`）、`ActionArgument`、`TabCompletion`、`HBitmapIcon`（GDI 點陣圖控制代碼，宿主自動接管釋放）以及 `OnExecute` 執行委派。
- **`DynamicMenuItem`**：包含 `Text`、`CommandId`、`IsSeparator`、`HasSubMenu`、`SubMenuHandle`、`IsDisabled`、`IsActionable`、`IsContinuation`、`OnExecute`、`IsHeader` 和 `ShortcutHint`。純分類節點只負責展開子功能表時應設定 `IsActionable = false`；真實資料夾節點可以保留預設值 `true`。`IsContinuation = true` 表示分頁子功能表的延續游標，主機會自動載入下一頁而不會轉譯可見的「載入更多」列。`IsHeader` 會將項目轉譯為帶可選操作按鈕的分組標題列。
- **`SearchWindowType`**：列舉值包括 `Main`（主搜尋視窗）、`Quick`（置中快速浮動視窗）與 `Inline`（嵌入式檔案對話方塊）。
