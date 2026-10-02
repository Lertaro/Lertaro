# 介面與預覽擴充

本章節介紹 `Lertaro.PluginSdk` 中用於擴充主搜尋視窗側邊欄、追加自訂表格資料欄、提供快速面板動態工作區標籤頁、建置 QuickLook 檔案預覽器與縮圖擷取器，以及發布 WPF 主題包與 i18n 當地語系化語系包的介面。

這些介面全數位於 `Lertaro.PluginSdk.Abstractions.Plugins` 命名空間下（預覽提供者位於 `…Abstractions.Plugins.Preview`），而且每一項都繼承自 `IPluginComponent`，宿主在**設定 → 外掛模組**中顯示的 `Name` 就由該基礎介面提供。

## 1. 側邊欄篩選提供者 `ISidebarFilterProvider`

將自訂的篩選分類注入主搜尋視窗的左側側邊欄：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface ISidebarFilterProvider : IPluginComponent
{
    IEnumerable<SidebarFilterGroup> GetFilterGroups();

    // 排序權重；數值較小的分類先呈現。
    int SortOrder => 100;
}

public class SidebarFilterGroup
{
    // 宿主對已知識別碼所認用的選填穩定識別碼（例如對應內建結果類型篩選的 "Type"）。
    // 分組完全由外掛模組自訂時留空。
    public string Id { get; set; } = string.Empty;
    public string Header { get; set; } = string.Empty;
    public List<SidebarFilterItem> Items { get; set; } = new();

    // 此分組內是否可以同時有多個項目生效。
    public bool AllowMultiSelect { get; set; }
}

public class SidebarFilterItem
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    // 兩條圖示路徑，兩者都具備主題感知能力：IconData 是以作用中主題的文字顏色繪製的字形，
    // IconKey 則指向宿主已經擁有的資源。兩者都留空表示不顯示圖示。
    public string? IconData { get; set; }
    public string? IconKey { get; set; }

    // 結果必須滿足這個判斷準則，該項目才算命中。預設為「一律不命中」，
    // 因此從未設定它的項目會顯示出來，卻永遠選不到任何東西。
    public Func<ISearchResult, bool> MatchPredicate { get; set; } = _ => false;
}
```

分組與項目都是可變更的類別，不是 record：填上你需要的屬性，其餘保持預設值即可。

## 2. 自訂表格資料欄提供者 `IResultColumnProvider`

在完整搜尋視窗的「詳細資料」多列表格檢視中追加自訂資料欄（例如：影音時長、程式碼行數、Git 存放庫分支）：提供者一次性描述自己的資料欄，並按需回答個別儲存格的內容：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IResultColumnProvider : IPluginComponent
{
    IEnumerable<ResultColumnDefinition> GetColumns();
    string GetCellValue(ISearchResult result, string columnId);
}

public class ResultColumnDefinition
{
    public string ColumnId { get; set; } = string.Empty;
    public string HeaderText { get; set; } = string.Empty;
    public double Width { get; set; } = 120;

    // 選填：對該欄不適用的結果隱藏此資料欄。
    public Func<ISearchResult, bool>? VisibilityPredicate { get; set; }

    // 選填：按一下表頭時的自訂排序。x < y 時為負數，x > y 時為正數。
    public Func<ISearchResult, ISearchResult, int>? SortComparer { get; set; }

    // 選填：在完整視窗中按兩下此欄的儲存格。留空不設時，按兩下該儲存格的行為
    // 與按兩下該列其他任何位置完全相同。
    public Action<ISearchResult>? OnDoubleClick { get; set; }
}
```

`GetCellValue` 是在列表渲染過程中呼叫的，因此必須極為廉價；請交回事前算好的值或改為讀取快取，而不是去碰磁碟。

## 3. 快速面板標籤頁提供者 `IQuickPanelTabProvider`

為[**快速面板**](../../user-guide/settings/quick-panel)貢獻一個動態工作區標籤頁：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IQuickPanelTabProvider : IPluginComponent
{
    // 當下要顯示的項目。每次呼出面板時都會呼叫。
    Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default);
}
```

這單一方法就是全部契約——沒有任何拖放接收、項目重排或動作上下文需要實作。

- `CancellationToken` 會在面板關閉時被取消。只有該標籤頁自己的清單會觀察它；你的外掛模組其他行為不會因此改變。
- 只要來源知道修改時間，就填上 `ISearchResult` 的 `Metadata.Modified`，因為預設的最新優先排序使用的就是它。讓它維持預設值時，項目會保持你返回時的順序。
- 沒有返回任何項目的提供者不會取得標籤頁，而這件事也沒有任何設定可以調整。
- 只要外掛模組存在，標籤頁就存在，不像資料夾需要使用者自行新增。它可以從標籤列上關閉，並在**設定 → 快速面板**中重新開啟；這與在**設定 → 外掛模組**中停用該元件是兩個不同的問題（後者會讓它完全不再載入）。

## 4. 檔案預覽與縮圖

### 自訂檔案預覽提供者 `IFilePreviewProvider`

在 QuickLook 面板內轉譯預覽內容；使用者按 `Alt+P` 或在可預覽的列上按一下滑鼠中鍵即可開啟該面板（詳見[**動作選單與預覽**](../../user-guide/actions-and-preview)）：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins.Preview;

public interface IFilePreviewProvider : IPluginComponent
{
    // 多個提供者都宣稱同一個檔案時的決勝依據；數值較大者勝出。
    int Priority => 0;

    bool CanPreview(string path, bool isDir);
    UIElement CreatePreview(string path, bool isDir);

    // 當提供者自行承載一個外部視窗，而不是返回要排在面板內轉譯的 WPF 內容時為 true
    // （QuickLook 橋接外掛模組就是如此）。
    bool RendersExternally => false;
}
```

#### 預覽生命週期與複用契約

當你返回的 `UIElement` 實作了下列任一選填契約時，宿主會對預覽生命週期進行最佳化：

- **`IPreviewSessionAware`** — `void EndPreviewSession();` 提供者持有的是真正的外部視窗（`HwndHost`、原生 `IPreviewHandler` 及其 `prevhost` 代理），而不只是程序內的控制項，因此在面板隱藏或整個預覽會話結束時，宿主會通知它結束自己的會話。缺少這個成員時，宿主的視窗會滯留在那裡而沒有任何物件指向它。
- **`IReusablePreview`** — `bool TrySetTarget(string path, bool isDir);` 使用者用方向鍵在同類檔案間逐行移動時，宿主會要求同一個控制項改換目標，而不是先銷毀再重建，閃爍正是由這個步驟消除的。新目標不適合這個實例時返回 `false`，宿主會退回建立一個全新的預覽。

### 自訂縮圖提供者 `IThumbnailProvider`

為沒有原生 Shell 處理程序的格式（`.blend`、`.psd`、`.dwg`）擷取縮圖：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins.Preview;

public interface IThumbnailProvider : IPluginComponent
{
    int Priority => 0;

    bool CanProvideThumbnail(string path, bool isDir);

    // 同步呼叫，因為它跑在結果列表的渲染路徑上。請保持快速，並自行依路徑與尺寸做快取
    // ——宿主不會替你記住結果。
    ImageSource? GetThumbnail(string path, int size);
}
```

## 5. 主題與當地語系化

### 主題提供者 `IThemeProvider`

貢獻色彩配置與 WPF 資源字典：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IThemeProvider : IPluginComponent
{
    IEnumerable<ITheme> GetThemes();
}

public interface ITheme
{
    string Id { get; }
    string DisplayName { get; }
    bool IsDark { get; }
    ResourceDictionary GetResources();

    // 低於 1.0 時，宿主會以分層、半透明的載體渲染它的無邊框視窗；為 1.0 時視窗保持不透明
    // 並保留 ClearType。這個數值決定了視窗以哪一種形式建立，而視窗一旦存在就再也無法改變它。
    double WindowOpacity => 1.0;
}
```

單一提供者可以貢獻任意數量的主題，而每個主題都自帶淺色或深色的旗標，而不是由提供者另外暴露一個深色變體。

### 當地語系化提供者 `ITranslationProvider`

動態提供翻譯字典：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface ITranslationProvider : IPluginComponent
{
    // 這個提供者能服務的文化代碼，讓宿主在任何東西載入之前就能在設定中提供它們。
    // 預設為空白清單，代表「依被請求到的內容自行發掘」。
    IReadOnlyList<string> SupportedCultures => Array.Empty<string>();

    IReadOnlyDictionary<string, string> GetTranslations(string cultureName);
}
```
