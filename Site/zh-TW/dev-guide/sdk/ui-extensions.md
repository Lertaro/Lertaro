# 介面與預覽擴充

本章節介紹 `Lertaro.PluginSdk` 中用於擴充搜尋視窗側邊欄、新增自訂表格資料欄、提供快速面板動態工作區索引標籤、建置 QuickLook 檔案預覽器與縮圖擷取器，以及發佈 WPF 主題與 i18n 當地語系化套件的各項介面。

這一切都位於 `Lertaro.PluginSdk.Abstractions.Plugins`（預覽提供者位於 `…Abstractions.Plugins.Preview`）之下，而且每一個介面都衍生自 `IPluginComponent`，後者提供宿主在**設定 → 外掛模組**中顯示的 `Name`。

## 1. 側邊欄篩選提供者 `ISidebarFilterProvider`

將自訂篩選分類插入搜尋視窗的左側側邊欄：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface ISidebarFilterProvider : IPluginComponent
{
    IEnumerable<SidebarFilterGroup> GetFilterGroups();

    // 排序權重；數值較小的先繪製。
    int SortOrder => 100;
}

public class SidebarFilterGroup
{
    // 宿主針對已知分組辨識用的選填穩定識別碼（例如內建結果類型篩選的 "Type"）。
    // 當分組完全由外掛模組自訂時留空。
    public string Id { get; set; } = string.Empty;
    public string Header { get; set; } = string.Empty;
    public List<SidebarFilterItem> Items { get; set; } = new();

    // 此分組中是否可以同時有多個項目處於啟用狀態。
    public bool AllowMultiSelect { get; set; }
}

public class SidebarFilterItem
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    // 兩條圖示路徑，兩者都感測主題：IconData 是以作用中主題的文字色彩繪製的字形，
    // IconKey 則指定宿主已擁有的資源。兩者都留 null 表示沒有圖示。
    public string? IconData { get; set; }
    public string? IconKey { get; set; }

    // 結果必須滿足、此項目才算命中的述詞。預設為「什麼都不命中」，
    // 因此從未設定它的項目會顯示，卻永遠無法選取任何東西。
    public Func<ISearchResult, bool> MatchPredicate { get; set; } = _ => false;
}
```

群組與項目都是可變類別，不是 record：填上你需要的屬性，其餘留在預設值即可。

## 2. 自訂表格資料欄提供者 `IResultColumnProvider`

替完整搜尋視窗的「詳細資料」表格檢視追加自訂資料欄（例如媒體時長、程式碼行數、Git 分支）。提供者一次性描述它的欄位，並依需求回答每個儲存格的值：

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

    // 選填：對不適用該欄位的結果隱藏此欄位。
    public Func<ISearchResult, bool>? VisibilityPredicate { get; set; }

    // 選填：點擊表頭時的自訂排序。x < y 時為負數，x > y 時為正數。
    public Func<ISearchResult, ISearchResult, int>? SortComparer { get; set; }

    // 選填：在完整視窗中雙擊此欄位的儲存格。不設定時，雙擊該儲存格的行為
    // 就如同雙擊該列的其他任何位置。
    public Action<ISearchResult>? OnDoubleClick { get; set; }
}
```

`GetCellValue` 會在清單繪製期間被呼叫，因此必須非常廉價；請交回事先算好的值或讀取快取，而不要去碰磁碟。

## 3. 快速面板索引標籤提供者 `IQuickPanelTabProvider`

為[**快速面板**](../../user-guide/settings/quick-panel)貢獻一個動態工作區索引標籤：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IQuickPanelTabProvider : IPluginComponent
{
    // 目前要顯示的項目。每次面板被呼出時都會呼叫。
    Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default);
}
```

那單一方法就是全部契約——沒有拖曳接收、重新排序或動作上下文需要實作。

- 面板關閉時 `CancellationToken` 會被取消。只有該索引標籤自己的清單會觀察它；你的外掛模組其他部分完全不受影響。
- 來源知道修改時間時，請填入 `ISearchResult` 的 `Metadata.Modified`，因為預設的最新優先排序會使用它。保留預設值時，項目會維持你返回時的順序。
- 什麼都不返回的提供者不會有索引標籤，這也無需任何設定。
- 只要外掛模組存在，索引標籤就存在，這與使用者必須自行新增的資料夾不同。它可以從標籤列關閉，並在**設定 → 快速面板**重新開啟；這與在**設定 → 外掛模組**停用該元件（那會讓它完全不載入）是兩回事。

## 4. 檔案預覽與縮圖

### 自訂檔案預覽提供者 `IFilePreviewProvider`

在 QuickLook 面板內轉譯預覽，使用者以 `Alt+P` 或在可預覽列上點擊滑鼠中鍵開啟該面板（見[**動作與預覽**](../../user-guide/actions-and-preview)）：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins.Preview;

public interface IFilePreviewProvider : IPluginComponent
{
    // 只負責打破平手。先套用使用者自行設定的提供者順序（設定 → 一般 → 預覽與縮圖），
    // Priority 只在該順序內部排序，數值高的排前面。
    int Priority => 0;

    bool CanPreview(string path, bool isDir);
    UIElement CreatePreview(string path, bool isDir);

    // 當提供者宿主自己的一個外部視窗，而非回傳要在面板內排版的 WPF 內容時為真
    // （QuickLook 橋接外掛模組就是這麼做的）。
    bool RendersExternally => false;
}
```

#### 預覽生命週期與複用契約

當你的**提供者**實作下面第一個選填契約，或你所返回的 `UIElement` 實作第二個契約時，宿主會最佳化預覽的生命週期：

- **`IPreviewSessionAware`** — 宿主把轉型套用在**提供者**身上，而不是它回傳的那個控制項：`void EndPreviewSession();`。提供者擁有一個真正的外部視窗（`HwndHost`、原生 `IPreviewHandler` 及其 `prevhost` 代理常駐程序），而不只是程序內的控制項，因此當它的擁有視窗關閉時便會被告知結束工作階段；而就程序內轉譯的提供者而言——只在預覽面板隱藏或整個預覽工作階段結束時才通知。若沒有這個契約，宿主的視窗會滯留卻沒有任何東西指向它。
- **`IReusablePreview`** — 宿主把轉型套用在返回的那個元件上：`bool TrySetTarget(string path, bool isDir);`。當使用者用方向鍵在相似檔案間移動時，宿主會要求同一個控制項重新鎖定目標，而不是銷毀並重建它，這正是消除閃爍的方法。當新目標不適合此執行個體時返回 `false`，宿主便退回建立一個全新的預覽。
- **`IReceivesPreviewPanelBounds`** — `void OnPreviewPanelBoundsAvailable(int left, int top, int width, int height);`。會自己宿主一個外部視窗的提供者需要知道面板所佔據的矩形，才能把自己掛進去或在其中定位；實作這個介面，該矩形一旦確定就會交給你。

### 自訂縮圖提供者 `IThumbnailProvider`

為沒有原生 Shell 處理程式的格式（`.blend`、`.psd`、`.dwg`）擷取縮圖：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins.Preview;

public interface IThumbnailProvider : IPluginComponent
{
    // 與預覽相同的規則：使用者自行設定的縮圖提供者順序先決定，
    // Priority 只在該順序內部排序。
    int Priority => 0;

    bool CanProvideThumbnail(string path, bool isDir);

    // 同步執行，因為它跑在結果清單的繪製路徑上——請保持快速。
    // 你**不必**自行記憶化：宿主會快取你返回的內容（實體或虛擬項目以路徑為索引，
    // 其餘情況則以副檔名為索引）。兩個附帶結果：`size` 是宿主自己的選擇，取自 Shell
    // 的影像清單，因此別指望某個特定的數值；而且提供者永遠不會被問到目錄。
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
```

```csharp
namespace Lertaro.PluginSdk.Abstractions;   // 注意：主題介面本身位於高一層的命名空間

public interface ITheme
{
    string Id { get; }
    string DisplayName { get; }
    bool IsDark { get; }
    ResourceDictionary GetResources();

    // 小於 1.0 時，經由宿主的階層式表面輔助方法建立的視窗會成為階層式、半透明的視窗，
    // 其圓角必須改為繪製並裁剪；等於 1.0 時它保持不透明、由視窗管理員圓角，並保留
    // ClearType。這個選擇在視窗的建構子中一次決定，因為 AllowsTransparency 在控制代碼
    // 存在之後就無法再更改。目前只套用到通知視窗；視窗還在畫面上時切換主題並不會重建它。
    double WindowOpacity => 1.0;
}
```

一個提供者可以貢獻任意數量的主題，而每個主題自帶亮色或暗色的旗標，而不是由提供者暴露一個暗色變體。

### 當地語系化提供者 `ITranslationProvider`

動態提供翻譯字典：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface ITranslationProvider : IPluginComponent
{
    // 此提供者能服務的文化程式碼，讓宿主在任何東西載入之前就能在設定中提供它們。
    // 預設為空，代表「從被要求的內容中探索」。
    IReadOnlyList<string> SupportedCultures => Array.Empty<string>();

    IReadOnlyDictionary<string, string> GetTranslations(string cultureName);
}
```
