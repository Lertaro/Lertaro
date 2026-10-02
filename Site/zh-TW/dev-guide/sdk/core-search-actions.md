# 核心檢索與動作

本章節詳細介紹 `Lertaro.PluginSdk` 中用於貢獻搜尋資料來源、即時計算答案、非 ASCII 別名轉寫引擎、查詢後綴 Token 處理器以及靜態/動態快顯動作功能表的核心介面與資料結構。

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
    // 外掛模組主組件的進入點。兩個網站成員都是選填的：當 WebsiteUrl 為 null 時，
    // 設定卡片完全不會顯示連結。
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

    // 呼叫這個提供者的詞，由宿主自己的移除步驟讀取，因此宿主的移除與你的比對不會
    // 各自跑偏。見第 6 節「觸發詞」。
    IReadOnlyList<string> QueryTriggerKeywords => [];
}
```

> [!TIP]
> `GetInstantResults` 為同步呼叫以保障打字流暢度。若需要發起網路請求（如線上翻譯或搜尋建議）：可先立即返回一個佔位結果項目，透過 `Task.Run` 在背景非同步獲取資料並快取，請求完成後呼叫 `SearchRefreshService.RefreshIfMatches` 通知宿主就地重新整理目前搜尋結果。

### 非 ASCII 別名轉寫引擎 `IAliasProvider`

用於為中文檔案名稱等非 ASCII 文字產生額外的可索引別名，支援混合拼音輸入比對：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IAliasProvider : IPluginComponent   // Name 來自 IPluginComponent
{
    bool CanHandle(string text);
    IReadOnlyList<(char Start, char End)> InputRanges { get; }  // 來源字元範圍（如 CJK 表意文字）
    IReadOnlyList<(char Start, char End)> OutputRanges { get; } // 產生別名字元範圍（如 a-z）

    // 為多音節引擎（拼音）設定：在產生出的別名中連接各個音節的字元。
    // '\0'（預設值）代表引擎完全不發出任何分隔符。
    char SyllableSeparator => '\0';

    IEnumerable<string> GetAliases(string text);

    int Version => 1;                                           // 規則更新時遞增以觸發重新索引
    IEnumerable<string> GetQueryForms(string term) => Array.Empty<string>(); // 查詢側改寫（如拼音音節邊界切分）
    int[]? MapAliasToSourceIndices(string text, string alias) => null;       // 對應別名命中位置至原文以供反白

    // 供索引器熱路徑使用的零分配 UTF-8 建置器。預設實作會把 GetAliases() 轉送入
    // sink，且只小寫化那些確實含有大寫字母的別名，因此引擎只有在能以比字串更廉價
    // 的方式產生位元組時，才會覆寫這個方法。
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

    // 當此 token 已從查詢中被消耗之後，仍要在結果列中持續反白的文字。
    // Null（預設值）表示不去動宿主自己的反白。
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

表示一個明確的靜態操作（如「複製完整路徑」、「以管理員身分執行」等），呈現在 `Ctrl+O` 動作功能表中或綁定為全域動作快速鍵：

```csharp
namespace Lertaro.PluginSdk.Abstractions;

public interface ISearchResultAction : Plugins.IPluginComponent
{
    string GroupName { get; }           // 動作功能表中的分組標題
    string DisplayName { get; }         // 動作顯示文字
    // 動作以其顯示名稱來尋址，因此 Name 是對應而來、而非自行撰寫：
    string Plugins.IPluginComponent.Name => DisplayName;

    // 不可為 null，並帶有預設值。空字串代表「沒有快速鍵」，這正是那些破壞性的檔案動作
    // 在重新取得檔案總管的按鍵組合之前，維持未綁定的方式。
    string Hotkey => string.Empty;
    IReadOnlyList<string> Keywords => Array.Empty<string>();
    IReadOnlyList<string> Parameters => Array.Empty<string>();

    // 動作出現在何處。預設：在搜尋中可見，且只有在它沒有聲明任何關鍵字時才在功能表中可見
    // （關鍵字是動作改為以列出現的方式）。
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => true;
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => Keywords.Count == 0;

    ImageSource? Icon { get; }          // 動作圖示；null 會繪製該分組的預設圖示
    bool CanExecute(IReadOnlyList<ISearchResult> results);
    void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view);
}
```

### 動態功能表建置器 `IDynamicActionProvider`

在執行階段動態建置深層巢狀或系統級功能表（例如將 Windows Shell 原生快顯功能表插入到 Lertaro 中）：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IDynamicActionProvider : IPluginComponent
{
    string GroupName { get; }
    string IPluginComponent.Name => GroupName;    // 與 ISearchResultAction 相同的對應方式

    int Priority => 0;                            // 功能表排序權重，非可空型別
    IReadOnlyList<string> Keywords => Array.Empty<string>();
    IReadOnlyList<string> Parameters => Array.Empty<string>();

    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => true;
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => Keywords.Count == 0;

    void Init() { }                               // 一次性預熱，於第一個功能表觸發時呼叫
    bool CanProvide(IReadOnlyList<ISearchResult> results);

    // 選擇加入套用在即時結果（視窗標題、程序列）上的功能表。預設為 false，因為
    // 大多數提供者是以那些列所沒有的檔案路徑為鍵。
    bool CanProvideForInstantResults => false;

    IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> results, IntPtr hMenu);
    IEnumerable<(string Hotkey, Action Execute)> GetHotkeyActions(IReadOnlyList<ISearchResult> results)
        => Array.Empty<(string, Action)>();
    void ExecuteCommand(IReadOnlyList<ISearchResult> results, uint commandId, IntPtr ownerHwnd);

    // 無預設實作：實作它是必須的。在功能表被拆毀時呼叫，
    // 好讓持有原生控制代碼或快取 Shell CDS 串流的提供者能釋放它們。
    void ClearSession();
}
```

## 4. 輔助資料結構

- **`SearchableItem`**：包含 `Title`、`Description`、`IconData`、`IconColor`、`ActionType`（`"Copy"` / `"Execute"` / `"None"`）、`ActionArgument`、`TabCompletion`、`HBitmapIcon`（GDI 點陣圖控制代碼，宿主自動接管釋放）、`ResultKind`（一個由外掛模組選擇、供宿主的篩選器與資料欄據以取用的標籤），以及兩個執行回呼：用於一觸即擲的 `OnExecute`（`Action`），或當動作需要回報成功時的 `OnExecuteFunc`（`Func<bool>`）——宿主會依據那個回答，例如決定是否關閉視窗。`InstantResultItem` 帶有相同的顯示與回呼成員，**唯獨沒有 `ResultKind`**，那個成員只有可搜尋項目的模型才有。
- **`DynamicMenuItem`**：包含 `Text`、`CommandId`、`IsSeparator`、`HasSubMenu`、`SubMenuHandle`、`IsDisabled`、`OnExecute`、`IsActionable`（預設 `true`；`false` 標記一個只會開啟子功能表的列）、`HBitmapItem`（來自被鏡像的 Shell 功能表的原生圖示控制代碼）、`ShortcutHint`（記憶鍵所對應的字母）、`IsContinuation`（一個分頁游標：這一梯次接續的是宿主仍在填補的功能表，而只要它被設定，宿主就會持續詢問），以及 `IsHeader`（轉譯為帶選填動作按鈕的分組標題）。
- **`SearchWindowType`**：列舉值包括 `Main`（主搜尋視窗）、`Quick`（置中快速浮動視窗）與 `Inline`（嵌入式檔案對話方塊）。

## 5. 具名搜尋範圍 `ISearchScopeProvider`

一個**範圍**是一個關鍵字前綴加上一組目錄：輸入 `tf report` 會讓宿主針對 `report` 執行它一般的索引搜尋、但限制在那些資料夾內。它是對既有索引的第二階段篩選，而不是第二個搜尋引擎。

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface ISearchScopeProvider : IPluginComponent
{
    // 在每次擊鍵派發時都會被諮詢：返回一個快取清單，只在配置變更時才重建。
    // 關鍵字為空白或沒有任何資料夾的範圍會被宿主忽略。
    IReadOnlyList<SearchScope> GetSearchScopes();
}

public sealed class SearchScope
{
    public string Keyword { get; init; } = string.Empty;            // 不分大小寫的第一個 token，例如 "tf"
    public IReadOnlyList<string> Folders { get; init; } = Array.Empty<string>();
    public string FilterPattern { get; init; } = "*";               // 以 ';' 分隔、套用於「檔案」名稱的 Win32 萬用字元
}
```

不同於 `ISearchableItemProvider`，範圍提供者絕不列舉或具體化檔案，因此不論配置的資料夾有多大，記憶體與每次擊鍵的負載都保持平穩。沒有任何宿主索引涵蓋的資料夾會被跳過並記錄一則警告，而不是即時走訪——這與索引器輔助方法遵循的規則是同一條：先把該資料夾涵蓋在已配置的本機磁碟機、網路或資料夾索引中。目錄一律通過 `FilterPattern`。

倉庫內的實作是檔案篩選（File Filters）外掛模組。

## 6. 觸發詞 `TriggerWord`

任何由使用者輸入一個前導詞來呼叫的功能——即時提供者的 `QueryTriggerKeywords`、動作的 `Keywords`、範圍的 `Keyword`、檔案篩選的觸發詞——都會透過 `Lertaro.PluginSdk.Services.TriggerWord` 來解析那個詞，因此宿主的移除步驟與外掛模組的比對不會各自跑偏。

| 輔助方法 | 比對 |
| :--- | :--- |
| `string Normalize(string? configured)` | 把一個已配置的詞還原成每個比對所期待的形態：去掉前後空白，`null` 或空白時為空字串。請在讀取時正規化——宿主會裁剪它所移除的那個詞，因此若拿一個未裁剪的值來比對會什麼都認不出，而宿主仍會把那個詞從檔案搜尋中移除。 |
| `bool TryMatch(string query, string? word, out string argument)` | 第一個 token **等於**那個詞（不分大小寫）。`argument` 是剩餘、已裁剪的文字，當查詢除了那個詞之外別無其他時為空——而那仍然算命中。一個只是以它開頭的較長詞並不算命中（`csreport` 不是 `cs`）。 |
| `bool TryMatchInvoked(...)` | 同上，但那個詞只有在它之後確實輸入了某些東西時才算數。當裸詞會讓一些沒人要求的列出現在畫面上時使用：單獨的 `cs` 仍是檔案搜尋，`cs ` 才是該提供者。 |
| `bool TryMatchAny(string query, IReadOnlyList<string> words, out string matchedWord, out string argument)` | 第一個比對成功的詞依清單順序取勝，並回報比對上的是**哪一個**——一個每提供者有多個關鍵字的網頁搜尋引擎需要這個來重新解析。 |
| `bool IsTypedPrefixOf(string query, string? word)` | 查詢是那個詞一個尚未完成的輸入（`m` 正通往 `mkdir` 的路上）。這是唯一允許在整個詞還沒輸入完之前就提供觸發的分支；一旦輸入任何分隔符即為 false。 |

`IInstantResultProvider.QueryTriggerKeywords`（預設為空）是宿主在自己的移除步驟中所讀取的內容，而 `PluginConfigField.IsTriggerWord` 標記那個存放此類詞的 `Text` 設定，讓設定頁能在另一個功能已經以同一個詞回應時提出警告——但不會封鎖儲存。否則，兩個功能共用一個詞是無聲無息的：檔案搜尋會遵循先註冊的那一個，而另一個的列就只是不再出現，也不會有任何東西告訴使用者該改命名哪一個。
