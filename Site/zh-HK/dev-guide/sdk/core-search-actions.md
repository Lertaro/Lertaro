# 核心檢索與動作

本章節詳細介紹 `Lertaro.PluginSdk` 中用於貢獻搜尋資料來源、即時計算答案、非 ASCII 別名轉寫引擎、查詢後綴 Token 處理器以及靜態/動態快顯動作選單的核心介面與資料結構。

## 1. 基礎元件規範 `IPluginComponent` 與 `IPlugin`

所有外掛模組擴充元件均直接或間接繼承自 `IPluginComponent`，用於向宿主宣告元件的中繼資料：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IPluginComponent
{
    string Name => GetType().Name;      // 元件顯示名稱（預設取類別名稱）
    string Description => string.Empty; // 功能描述，在設定介面中作為 ToolTip 提示氣泡呈現
}

public interface IPlugin : IPluginComponent
{
    // 外掛模組主組件的進入點。兩個網站成員都是選填的：`WebsiteUrl` 為 null 時，
    // 設定卡片根本就不顯示連結。
    string? WebsiteUrl => null;
    string? WebsiteLabel => null;
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

    // 叫用此提供者的詞，由宿主自己的後綴剝除步驟讀取，使宿主的剝除與你的比對不會各走各路。
    // 見第 6 節「觸發詞」。
    IReadOnlyList<string> QueryTriggerKeywords => [];
}
```

> [!TIP]
> `GetInstantResults` 為同步呼叫以保障打字流暢度。若需要發起網路請求（如線上翻譯或搜尋建議）：可先立即返回一個佔位結果項目，透過 `Task.Run` 在後台非同步獲取資料並快取，請求完成後呼叫 `SearchRefreshService.RefreshIfMatches` 通知宿主就地重新整理當前搜尋結果。

### 非 ASCII 別名轉寫引擎 `IAliasProvider`

用於為中文檔案名稱等非 ASCII 文字產生額外的可索引別名，支援混合拼音輸入比對：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IAliasProvider : IPluginComponent   // Name 來自 IPluginComponent
{
    bool CanHandle(string text);
    IReadOnlyList<(char Start, char End)> InputRanges { get; }  // 來源字元範圍（如 CJK 表意文字）
    IReadOnlyList<(char Start, char End)> OutputRanges { get; } // 產生別名字元範圍（如 a-z）

    // 多音節引擎（如拼音）才需要設定：產生別名時用來連接各音節的字元。
    // '\0'（預設值）代表引擎完全不輸出分隔符號。
    char SyllableSeparator => '\0';

    IEnumerable<string> GetAliases(string text);

    int Version => 1;                                           // 規則更新時遞增以觸發重新索引
    IEnumerable<string> GetQueryForms(string term) => Array.Empty<string>(); // 查詢側改寫（如拼音音節邊界切分）
    int[]? MapAliasToSourceIndices(string text, string alias) => null;       // 對應別名命中位置至原文以供反白

    // 供索引器熱路徑使用的零分配 UTF-8 建置器。預設實作把 GetAliases() 的結果轉進目標緩衝區，
    // 且只對確實含有大寫字母的別名做小寫化，因此引擎只有在能用比產生字串更廉價的方式
    // 產出位元組時，才有改寫這個方法的必要。
    void GetAliasesUtf8(string text, AliasByteSink dest);
}
```

### 查詢後綴 Token 處理器 `IQueryTokenProvider`

用於認領並處理搜尋框尾部的特定 Token 標記（例如 `report :size`、`doc :@today` 或 `image ::"hello world"`），對初步比對的結果清單進行串流二次變換（過濾、重新排序等）：

```csharp
public interface IQueryTokenProvider : IPluginComponent
{
    bool CanHandle(string token);
    Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results);

    // 這個 Token 自查詢字串中被取走之後，仍要持續在結果列上反白的文字。
    // null（預設值）表示不動宿主自己的反白。
    string? GetHighlightText(string token) => null;
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

表示一個明確的靜態操作（如「複製完整路徑」、「以管理員身分執行」等），呈現在 `Ctrl+O` 動作選單中或綁定為全域動作快速鍵：

```csharp
namespace Lertaro.PluginSdk.Abstractions;

public interface ISearchResultAction : Plugins.IPluginComponent
{
    string GroupName { get; }           // 動作選單中的分組標題
    string DisplayName { get; }         // 動作顯示文字
    // 動作是以顯示名稱來定址的，因此 Name 由 DisplayName 對應而來，並非另行撰寫：
    string Plugins.IPluginComponent.Name => DisplayName;

    // 不可為 null，但帶有預設值。空字串代表「沒有快速鍵」，那些會破壞檔案的動作在拿回
    // 檔案總管的按鍵組合之前，就是這樣保持未綁定的。
    string Hotkey => string.Empty;
    IReadOnlyList<string> Keywords => Array.Empty<string>();
    IReadOnlyList<string> Parameters => Array.Empty<string>();

    // 動作出現在哪裡。預設：在搜尋中可見，而只有當它沒有宣稱任何關鍵字時才在選單中可見
    // （關鍵字正是動作改以清單列呈現的方式）。
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => true;
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => Keywords.Count == 0;

    ImageSource? Icon { get; }          // 動作圖示；為 null 時描繪所屬分組的預設圖示
    bool CanExecute(IReadOnlyList<ISearchResult> results);
    void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view);
}
```

### 動態選單建置器 `IDynamicActionProvider`

在執行階段動態建置深層巢狀或系統級選單（例如將 Windows Shell 原生快顯選單插入到 Lertaro 中）：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IDynamicActionProvider : IPluginComponent
{
    string GroupName { get; }
    string IPluginComponent.Name => GroupName;    // 與 ISearchResultAction 相同的對應方式

    int Priority => 0;                            // 選單排序權重，不可為 null
    IReadOnlyList<string> Keywords => Array.Empty<string>();
    IReadOnlyList<string> Parameters => Array.Empty<string>();

    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => true;
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => Keywords.Count == 0;

    void Init() { }                               // 一次性預熱，由第一次開出的選單觸發
    bool CanProvide(IReadOnlyList<ISearchResult> results);

    // 自行選擇是否要為即時結果（視窗標題、程序列）提供選單。預設 false，因為多數提供者
    // 是以那些列根本沒有的檔案路徑作為判斷依據。
    bool CanProvideForInstantResults => false;

    IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> results, IntPtr hMenu);
    IEnumerable<(string Hotkey, Action Execute)> GetHotkeyActions(IReadOnlyList<ISearchResult> results)
        => Array.Empty<(string, Action)>();
    void ExecuteCommand(IReadOnlyList<ISearchResult> results, uint commandId, IntPtr ownerHwnd);

    // 沒有預設實作：實作這個成員是強制性的。選單拆解時呼叫，讓持有原生控制代碼或
    // 快取 shell CDS 串流的提供者得以釋放它們。
    void ClearSession();
}
```

## 4. 輔助資料結構

- **`SearchableItem`**：包含 `Title`、`Description`、`IconData`、`IconColor`、`ActionType`（`"Copy"` / `"Execute"` / `"None"`）、`ActionArgument`、`TabCompletion`、`HBitmapIcon`（GDI 點陣圖控制代碼，宿主自動接管釋放）、`ResultKind`（由外掛模組自選的標籤，宿主的篩選器與資料欄可以據以判斷），以及兩個執行回呼：即發即忘用的 `OnExecute`（`Action`），或動作需要回報成功與否時改用的 `OnExecuteFunc`（`Func<bool>`）——宿主會拿這個答案去決定例如是否關閉視窗。`InstantResultItem` 帶有相同的顯示與回呼成員，但**沒有 `ResultKind`**，那個成員只有可搜尋項目模型才有。
- **`DynamicMenuItem`**：包含 `Text`、`CommandId`、`IsSeparator`、`HasSubMenu`、`SubMenuHandle`、`IsDisabled`、`OnExecute`、`IsActionable`（預設 `true`；`false` 標記的是只會打開子選單的列）、`HBitmapItem`（來自被鏡像的 Shell 選單之原生圖示控制代碼）、`ShortcutHint`（記憶按鍵比對時對應的字母）、`IsContinuation`（一個分頁游標：這批項目接續的仍是宿主還在填的選單，只要它被設定，宿主就會繼續索要下一批），以及 `IsHeader`（轉譯為帶可選操作按鈕的分組標題列）。
- **`SearchWindowType`**：列舉值包括 `Main`（主搜尋視窗）、`Quick`（置中快速浮動視窗）與 `Inline`（嵌入式檔案對話方塊）。

## 5. 具名搜尋範圍 `ISearchScopeProvider`

一個**範圍**就是一個關鍵字前綴加上一組目錄：輸入 `tf report` 會讓宿主在它正常的索引搜尋中查 `report`，但限定在那些資料夾之內。它是既有索引之上的第二層篩選，而不是第二個搜尋引擎。

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface ISearchScopeProvider : IPluginComponent
{
    // 每次擊鍵派發時都會查問：請返回快取好的清單，只在配置變更時才重建。
    // 關鍵字為空白或沒有任何資料夾的範圍會被宿主忽略。
    IReadOnlyList<SearchScope> GetSearchScopes();
}

public sealed class SearchScope
{
    public string Keyword { get; init; } = string.Empty;            // 不分大小寫的第一個 Token，如 "tf"
    public IReadOnlyList<string> Folders { get; init; } = Array.Empty<string>();
    public string FilterPattern { get; init; } = "*";               // 以 ';' 分隔、套用在「檔案」名稱上的 Win32 萬用字元
}
```

與 `ISearchableItemProvider` 不同，範圍提供者從不列舉或具體化檔案，因此不論設定的資料夾有多大，記憶體與每次擊鍵的成本都維持持平。沒有任何宿主索引涵蓋的資料夾會被跳過並記錄一則警告，而不是當場走訪——這與索引輔助方法遵循的是同一條規則：先把該資料夾納入已設定的本機磁碟機、網路或資料夾索引。目錄一律通過 `FilterPattern`。

隨包的實作是 File Filters 外掛模組。

## 6. 觸發詞 `TriggerWord`

只要是使用者靠輸入一個前導字詞來呼出的功能——即時提供者的 `QueryTriggerKeywords`、動作的 `Keywords`、範圍的 `Keyword`、檔案篩選器的觸發詞——都要透過 `Lertaro.PluginSdk.Services.TriggerWord` 解析該字詞，宿主的剝離動作與外掛模組的比對才不會各自漂移。

| 輔助方法 | 比對條件 |
| :--- | :--- |
| `string Normalize(string? configured)` | 把設定裡保存的字詞整理成所有比對都預期的形式：移除前後空白，`null` 或空白時為空字串。讀取時就要正規化——宿主會先修剪它剝離的那個字詞，因此直接拿未修剪的值去比對什麼也認不出，而宿主仍然會把那個字詞從檔案搜尋中移除。 |
| `bool TryMatch(string query, string? word, out string argument)` | 第一個 Token 與該字詞**相等**（不分大小寫）。`argument` 是剩下的已修剪文字，當查詢只有那個字詞時為空——這種情況仍然算命中。只是以它開頭的較長字詞不算命中（`csreport` 不是 `cs`）。 |
| `bool TryMatchInvoked(...)` | 同上，但只有在該字詞之後確實還打了別的東西時才算數。當裸字詞就會列出使用者沒要的列時改用它：只輸入 `cs` 仍是檔案搜尋，`cs `（尾端帶空格）才是這個提供者。 |
| `bool TryMatchAny(string query, IReadOnlyList<string> words, out string matchedWord, out string argument)` | 依清單順序，第一個命中的字詞勝出，並回報命中的是**哪一個**——一個提供者帶有多個關鍵字的網頁搜尋引擎需要這個資訊才能重新解析。 |
| `bool IsTypedPrefixOf(string query, string? word)` | 查詢正是該字詞還沒打完的輸入過程（朝 `mkdir` 前進中的 `m`）。這是唯一一個被允許在字詞還沒完整存在前就提供觸發點的分支；一旦打了任何分隔字元即返回 false。 |

`IInstantResultProvider.QueryTriggerKeywords`（預設為空）就是宿主執行自身剝離步驟時讀取的來源，而 `PluginConfigField.IsTriggerWord` 會標記存放這類字詞的 `Text` 設定，讓設定頁面能在另一個功能已經認得同一個字詞時提出警告——但不會阻擋儲存。否則兩個功能共用一個字詞是完全無聲的：檔案搜尋會聽從較早註冊的那一個，另一個的列就此不再出現，而沒有任何東西告訴使用者該替其中一個改名。
