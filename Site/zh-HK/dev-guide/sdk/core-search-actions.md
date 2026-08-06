# 核心檢索與動作

## `IPluginComponent` 與 `IPlugin`

所有插件組件（包括插件入口）都需要繼承自 `IPluginComponent`。該接口提供了組件的名稱和描述：

```csharp
interface IPluginComponent
{
    string Name => GetType().Name;       // 組件的顯示名稱，預設返回具體類名
    string Description => string.Empty;  // 組件的功能描述，宿主會在配置/設定介面中作為 ToolTip 提示氣泡展示
}
```

每個插件都必須實現 `IPlugin` 接口（繼承自 `IPluginComponent`）作為插件的主入口，另外再加上其他按需實現的接口：

```csharp
interface IPlugin : IPluginComponent
{
}
```

## 貢獻搜尋結果

### `ISearchableItemProvider`

返回一份完整的、可快取的條目列表，供索引使用——適合內容是靜態的或者枚舉較慢、但不會隨每次按鍵變化的場景(例如開始功能表捷徑、書籤列表)。

```csharp
interface ISearchableItemProvider : IPluginComponent
{
    bool EnableAlias { get; } // 預設 true
    event Action? ItemsChanged;
    IEnumerable<SearchableItem> GetSearchableItems();
}
```

### `IInstantResultProvider`

每次按鍵都會運行一次，直接返回結果——適合像計算器、URL 捷徑這類"結果形狀由查詢本身決定"的內容，而不是需要提前建好索引的東西。

```csharp
interface IInstantResultProvider : IPluginComponent
{
    IEnumerable<InstantResultItem> GetInstantResults(string query);
    bool[]? GetHighlightMask(string text, string query); // 可選的匹配高亮
}
```

`GetInstantResults`只有同步這一種形態——沒有異步/可取消令牌的重載。如果你的資料需要走一次網絡請求
(比如翻譯文字、拉取搜尋引擎的聯想建議)，做法是:立刻返回一個佔位結果項，用 `Task.Run` 在後臺去真正幹活，拿到結果後快取起來，再調用 `SearchRefreshService.RefreshIfMatches`(參見[宿主服務](./services))
讓宿主把當前 query 會命中你快取的那些搜尋重新跑一遍——可以參考 WebSearch 插件的建議拉取邏輯
(`Plugins/WebSearch/WebSearchInstantProvider.cs`)作為完整示例。

### `IAliasProvider`

為非 ASCII 文本生成額外的可搜尋字串——中文檔案名的拼音別名就是這樣實現的(見
[PinyinAlias](../examples#pinyinalias-——-中文檔案名拼音別名))。

```csharp
interface IAliasProvider
{
    string Name { get; }
    bool CanHandle(string text);
    IReadOnlyList<(char Start, char End)> InputRanges { get; }
    IReadOnlyList<(char Start, char End)> OutputRanges { get; }
    IEnumerable<string> GetAliases(string text);

    int Version { get; } // 預設 1
    int[]? MapAliasToSourceIndices(string text, string alias); // 預設 null
    void GetAliasesUtf8(string text, AliasByteSink dest); // 預設:內部轉調 GetAliases
    IEnumerable<string> GetQueryForms(string term); // 預設:不返回任何形式
}
```

`InputRanges` 和 `OutputRanges`沒有預設實現——每個 provider 都必須自己聲明。`InputRanges` 是這個
provider 轉寫的**源**字元範圍(比如拼音對應的是 CJK 表意文字區塊);`OutputRanges` 是它生成的別名所使用的字元範圍(比如拼音就是小寫 `a`-`z`)。宿主會用這兩個範圍,把一個同時混用了某個 provider
自己的輸入、輸出兩種字母表的查詢詞(比如用"大cj"匹配"大長今")切分成一段按候選項原文匹配的字面片段,和一段按這個 provider 生成的別名匹配的別名語法片段,而不用去猜測"是不是非 ASCII"。

`Version`、`MapAliasToSourceIndices`、`GetAliasesUtf8` 都有預設實現——絕大多數 provider 都不需要碰它們:

- **`Version`**:當這個 provider 對同一個輸入的輸出可能發生變化時(算法修復、新增規則、更新了資料表)就把它加一。索引靠這個值判斷這個 provider 之前生成的別名已經過期，需要重新生成。
- **`MapAliasToSourceIndices`**:把命中別名的位置(比如命中了哪幾個拼音字母)映射回原始文本上用於高亮，否則因為查詢詞從沒在未轉寫的原文裏逐字出現過，就會完全高亮不出來。返回 `null`(預設值)表示這個別名不是這個 provider 針對這段文本生成的，或者不支援映射——宿主會把這種情況當成
  "這個 provider 高亮不了"，而不是錯誤。
- **`GetAliasesUtf8`**:宿主批量建索引時用的字節原生版本，別名最終是按 UTF-8 字節存儲的。預設實現就是內部轉調 `GetAliases`，所以現有的 provider 不用改也能正常工作；只有當你的 provider 生成的別名量特別大、字串分配開銷確實成為實際瓶頸時，才需要重寫它來完全跳過字串具現化。
- **`GetQueryForms`**:`GetAliases` 的查詢端對應版本——把用戶輸入的某一個查詢詞，改寫成這個
  provider 自己的別名所使用的那種帶分隔結構的形式，這樣一段用戶按普通字元連續打出來的查詢詞，依然能保留宿主本身理解不了的內部結構(比如拼音的音節邊界，這正是阻止查詢跨越兩個不相關音節誤匹配的關鍵)。預設不返回任何形式，代表"這個詞根本不在我的字母表裏"——這正是防止一個這個
  provider 無法表達的查詢詞，誤命中本不該命中的別名。每條查詢裏每個詞只會調用一次，不會按候選項逐一調用，所以在這裏做一些實際工作是划算的——但每返回一種形式，就會多出一個要拿去跟每個候選項匹配的備選項，所以返回得越多，代價也越大。

### `IQueryTokenProvider`

從查詢裏認領一個尾部 token(例如 `report :size`、`report ::"hello world"` 或 `report ::hello\ world`)，並對已經匹配好的結果列表做變換——排序、過濾，或者在一次普通搜尋之上做其他組合處理。支援在雙引號 (`"..."`)、單引號 (`'...'`) 或使用轉義空格 (`\ `) 的 Token 中包含空格。

```csharp
interface IQueryTokenProvider : IPluginComponent
{
    bool CanHandle(string token);
    Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results);
}
```

## 結果上的動作

### `IActionProvider`

插件用來暴露靜態和動態動作的容器接口:

```csharp
interface IActionProvider
{
    IEnumerable<ISearchResultAction> GetActions();
    IEnumerable<IDynamicActionProvider> GetDynamicActionProviders();
}
```

### `ISearchResultAction`

一個單獨的靜態動作(例如"複製路徑")，出現在動作選單或快速視窗的動作熱鍵裏:

```csharp
interface ISearchResultAction : IPluginComponent
{
    string GroupName { get; }
    string DisplayName { get; }
    string? Hotkey { get; }              // 可選的預設熱鍵
    IReadOnlyList<string>? Keywords { get; }
    IReadOnlyList<string>? Parameters { get; }
    ImageSource Icon { get; }
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool CanExecute(IReadOnlyList<ISearchResult> selection);
    void Execute(IReadOnlyList<ISearchResult> selection, IPluginSearchWindow window);
}
```

### `IDynamicActionProvider`

在運行時構建選單項，而不是返回一份固定列表——真正的 Windows Shell 右鍵選單(含級聯子選單)之所以能出現在 Lertaro 的動作選單裏，用的就是這個機制；參見
[ShellMenuActionProvider](../examples#coreextensions-——-動作與-shell-右鍵選單)。

```csharp
interface IDynamicActionProvider
{
    string GroupName { get; }
    int? Priority { get; }
    IReadOnlyList<string>? Keywords { get; }
    IReadOnlyList<string>? Parameters { get; }
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    void Init();
    bool CanProvide(IReadOnlyList<ISearchResult> selection);
    IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> selection, IntPtr hMenu);
    IEnumerable<(string Hotkey, Action Execute)> GetHotkeyActions(IReadOnlyList<ISearchResult> selection);
    void ExecuteCommand(IReadOnlyList<ISearchResult> selection, uint commandId, IntPtr ownerHwnd);
    void ClearSession();
}
```

`Init()`由宿主在整個處理程序生命週期內最多調用一次——在任何一次動作選單真正打開之前，即
`CanProvide`/`GetMenuItems` 被調用之前觸發。"最多一次"這個保證由宿主負責，具體實現不需要自己防止重複調用。適合用來做那種值得搶佔先機的慢速一次性初始化(比如預熱一個原生工作執行緒)，而不是和自己的 `CanProvide`/`GetMenuItems` 調用(緊隨其後、沒有任何提前量)搶時間——不能阻塞，真正耗時的工作要放到後臺執行緒裏做。預設實現是空操作。

`Priority` 決定這個 provider 在動作選單的動態(按 provider 分組)分組裏排在哪——數值越小越靠前，預設 `0`。不過這只是個兜底信號:使用者可以在[設定 → 通用 → 完整搜尋視窗](../../user-guide/settings/general#完整搜尋視窗)裏手動拖曳/調整這些分組的順序，使用者已經手動排過序的分組會保持在那個位置，不再受 `Priority` 影響。

## 支援模型

- **`SearchableItem`** / **`InstantResultItem`** —— 兩者共有 Title、Description、IconData、IconColor、
  ActionType(`"Copy"` / `"Execute"` / `"None"`)、ActionArgument、TabCompletion，以及 `HBitmapIcon`
  (預先準備好的 GDI 位圖句柄，設定後優先級高於 IconData——宿主會接管所有權，用完自己調用
  DeleteObject，所以交出去之後不要再複用或釋放這個句柄；具體用法可以參考視窗切換器插件自己的視窗內容截圖實現)。`SearchableItem` 還額外多了 `OnExecute`(直接調用委託)和 `ResultKind`
  (覆蓋結果類型，比如 `"Application"`/`"File"`)。
- **`DynamicMenuItem`** —— Text、CommandId、IsSeparator、HasSubMenu、SubMenuHandle、IsDisabled、
  HBitmapItem、OnExecute、ShortcutHint、IsHeader。`IsHeader` 把這一項渲染成不可點擊的分組標題行
  (就像快速導航子選單自己的分組名一樣)，而不是普通的一行——Text 就是標題文字，如果同時設定了
  `OnExecute`，標題行末尾會出現一個小按鈕來調用它;`IsHeader` 為 true 時其餘欄位都會被忽略。這是
  [`IQuickNavigationProvider.HeaderAction`](./system-adapters#iquicknavigationprovider)在子選單深度上的等價物，`HeaderAction` 本身只覆蓋根層級。
- **`SearchWindowType`** 枚舉 —— `Main`、`Quick`、`Inline`。可以讓動作或提供者根據當前顯示在[使用者手冊](../../user-guide/getting-started#三種視窗)裏說的三種視窗的哪一種而表現不同。
