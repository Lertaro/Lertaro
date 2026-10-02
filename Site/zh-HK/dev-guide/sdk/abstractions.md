# 共用抽象契約

本章節彙總了 `Lertaro.PluginSdk` 中跨多個介面複用的基礎資料模型、唯讀契約與設定驅動抽象。

## 1. 檢索結果模型 `ISearchResult`

在 Lertaro 的架構中，外掛模組對搜尋結果的觀察始終基於唯讀契約 `ISearchResult`，禁止直接篡改宿主底層的核心索引資料結構：

```csharp
namespace Lertaro.PluginSdk.Abstractions;

public interface ISearchResult
{
    string Name { get; }                  // 檔案或項目的顯示名稱（如 "Lertaro.exe"）
    string FullPath { get; }              // 絕對實體路徑（如 "C:\Program Files\Lertaro\Lertaro.exe"）
    string ContextDirectory { get; }      // 所在父級目錄路徑（如 "C:\Program Files\Lertaro"）
    bool IsDir { get; }                   // 是否為目錄/資料夾
    bool IsApplication { get; }           // 是否為可執行程式或捷徑
    bool[]? GetHighlightMask(string text, string query) => null; // 字元級反白遮罩計算
    FileMetadata Metadata => default;     // 高效能檔案中繼資料（大小、修改時間等）
    string? InstantActionArgument => null; // 即時結果所指向的對象
}
```

`FullPath` 是多數動作據以操作的身分，因此當即時結果作用的對象並不是路徑時——`activatewindow:12345`、`kill:4321`、自訂命令的載荷——該目標改由 `InstantActionArgument` 承載，提供者也在那裡讀取它。其他任何形式的列它都保持 `null`：普通的檔案與資料夾結果、外掛模組搜尋動作、歷史記錄項目。

> [!NOTE]
> `ISearchResult.Metadata` 包含的資料由宿主底層的 USN / MFT 記憶體索引直接注入，**讀取該屬性完全不產生任何磁碟 I/O 或 IPC 調用**。僅當你需要查詢不屬於當前結果集的外部路徑時，才需要調用 `FileMetadataService.GetMetadataAsync`。

## 2. 檔案中繼資料結構 `FileMetadata`

```csharp
public readonly record struct FileMetadata(
    long Size,
    DateTime Created,
    DateTime Modified,
    DateTime Accessed
);
```

- 時間戳記均為**本機時間（Local Time）**。
- 若 `Metadata == default`（即各欄位均為 0 或 `DateTime.MinValue`），表示該結果並非由實體檔案索引產生（例如由某個即時計算外掛模組動態產生）。
- 可透過 `Metadata.Modified != default` 準確區分「中繼資料不可用」與「大小恰好為 0 位元組的合法真實檔案」。

## 3. 宿主安全控制介面 `IPluginSearchWindow`

當動作執行回呼（如 `ISearchResultAction.Execute`）被觸發時，宿主會傳入 `IPluginSearchWindow` 執行個體，供外掛模組安全調度宿主視窗：

```csharp
public interface IPluginSearchWindow
{
    void LocateInExplorerExternal(string path);       // 在檔案總管或設定的檔案管理器中反白定位
    void OpenFileOrFolderExternal(string path);       // 使用關聯程式普通啟動
    void OpenFileOrFolderAsAdminExternal(string path);// 提權以管理員身分啟動
    void HideWindow();                                // 隱藏當前搜尋視窗
}
```

## 4. 結構描述驅動的設定體系 `IConfigurable`

如果你的外掛模組需要提供個人化設定項目，只需在外掛模組類別上實作 `IConfigurable` 介面，宿主便會在**設定 → 外掛模組 → 設定**中自動根據 Schema 轉譯出原生美觀的表單介面，無需手寫任何 XAML：

```csharp
public interface IConfigurable
{
    PluginConfigSchema GetConfigSchema();
}
```

### 核心欄位類型 `ConfigFieldType`

| 欄位類型 | 轉譯控制項與說明 |
| :--- | :--- |
| **`Boolean`** | 切換開關（Toggle Switch）或核取方塊。 |
| **`Text`** | 文字輸入框。使用者清空時 `RequireNonEmpty` 會退回 `DefaultValue`；`MaxLength` 限制長度（為 0 或未設定代表不設上限）；`SelectionStart` / `SelectionLength` 設定為這個欄位開啟的提示編輯器中從 0 開始計算的初始選取範圍。 |
| **`Integer`** | 數字微調輸入框。支援設定最小值與最大值範圍。 |
| **`Choice`** | 下拉選擇框。透過 `Choices` 或 `ChoiceOptions` 列表指定可選項目。 |
| **`Array`** | 一個列表值。帶有 `SubFields` 時它是一份**記錄**清單，以主從式編輯器轉譯（每個項目一個巢狀表單——正是檔案篩選、自訂命令與網頁搜尋外掛模組採用的形態）；沒有 `SubFields` 時它只是純量清單，以精簡的單欄編輯器轉譯。SDK 完全沒有給 `DefaultValue` 預設值（宣告中是 `object?`、`null!`），這就是倉庫內每個外掛模組都要為空列表傳入 `new List<object>()` 的原因。 |
| **`Object`** | 單一結構化值，透過它的 `SubFields` 編輯，但不具備 `Array` 的列表操作介面。 |
| **`Group`** | 包含巢狀 `SubFields` 的可折疊卡片分組。 |
| **`StringList`** | 支援多行編輯、項目增刪排序與自動換行的多行列表框；實際換行只以視覺標記顯示，不會寫入設定值。 |
| **`Hotkey`** | 專屬按鍵錄製框。可設定 `RequireModifier = true` 強制要求必須包含修飾鍵。 |
| **`FilePath` / `FolderPath`** | 附帶「瀏覽...」檔案/資料夾原生選取器按鈕的路徑輸入框。 |
| **`CustomControl`** | 允許外掛模組直接掛載一個自訂的 WPF `UIElement` 控制項執行個體（也可經由 `CustomControl` 成員達成）。 |
| **`Button`** | 顯示操作按鈕並呼叫欄位的 `OnClick` 委派，不儲存設定值。 |

`PluginConfigField` 其餘的成員，是宿主圍繞這些類型用來轉譯或持久化的項目：`Key`（實際儲存的設定名稱）、`GroupKey`（該欄位位於哪張 `Group` 卡片）、`LabelKey` / `DescriptionKey`（翻譯鍵，而非字面文字）、`RequireNonEmpty`、`Choices` / `ChoiceOptions` / `SubFields`、`IsTriggerWord`（見[**核心檢索與動作**](./core-search-actions)的「觸發詞」）、`MaxLength`、`SelectionStart` / `SelectionLength`、`CustomControl`、`OnClick`，以及兩個讓外掛模組把值存到宿主設定儲存之外的委派：`Func<object?>? GetValue` 與 `Action<object?>? SetValue`。

### 圖示欄位

將欄位的 Schema 鍵設為 `Icon` 並使用 `Text` 類型時，宿主會顯示圖示預覽並支援直接輸入 WPF Path Data。貼上完整 SVG/XML 文件時，宿主會擷取並合併所有 `<path d>` 值，只儲存轉換後的 WPF Path Data；圖示內容無效時會清空並透過主題化錯誤對話方塊提示。不需要圖示時，空值仍然有效。

`PluginConfigSchema` 亦支援設定 `OnSave` 與 `OnRollback` 生命週期委派：使用者按下**確定/套用**提交時觸發 `OnSave` 執行自訂持久化，取消或還原時觸發 `OnRollback` 復原狀態。

### 本地化選擇標籤

當選項需要本地化標籤，同時仍要儲存穩定的設定值時，應使用 `ChoiceOptions`。`PluginConfigChoice.Value` 會寫入外掛模組設定，`LabelKey` 會解析為介面顯示文字；如果儲存值與顯示文字相同，繼續使用舊的 `Choices` 列表即可。

```csharp
new PluginConfigField
{
    Key = "DisplayMode",
    FieldType = ConfigFieldType.Choice,
    DefaultValue = "FriendlyName",
    ChoiceOptions =
    [
        new PluginConfigChoice
        {
            Value = "FriendlyName",
            LabelKey = "DisplayMode_FriendlyName"
        }
    ]
}
```

## 5. 完整搜尋視窗檔案結果 `IFullSearchFileResultProvider`

如果外掛模組需要向完整搜尋視窗提供真實的檔案或資料夾列，可以實作 `IFullSearchFileResultProvider`：

```csharp
public interface IFullSearchFileResultProvider : IPluginComponent
{
    IReadOnlyList<InstantResultItem> GetFileResults(string query, int limit);

    // 選填。預設實作會走訪 GetFileResults，所以在這個成員出現之前寫的提供者無需改動。
    IEnumerable<InstantResultItem> GetFileResultsStreamed(string query, int limit);
}
```

宿主在完整搜尋視窗自身的檔案搜尋仍在串流返回時，於背景執行緒呼叫提供者，並在結果一到就渲染，而不是等到搜尋完結。外掛模組不處理目前查詢時應返回空列表。返回的每個 `InstantResultItem` 都必須對應一個實際存在的檔案或資料夾，這樣完整視窗的路徑、大小和類型欄位才有意義。若提供者的回答需要數秒（例如全文索引遍歷），可改寫 `GetFileResultsStreamed`，邊找到邊交出命中，讓前幾列先上螢幕、其餘繼續查詢；因為介面的預設實作只是走訪 `GetFileResults`，改寫是選填的。這個元件在**設定 → 外掛模組**下有**屬自己的**啟用/停用開關，以它自己的元件類型為索引——關掉外掛模組的即時結果提供者並不會把這個一起關掉，反之亦然。

## 6. 使用者配置路徑解析 `UserPathResolver`

當外掛模組接受使用者輸入或設定中的路徑時，應使用 `Lertaro.PluginSdk.Helpers.UserPathResolver`，在呼叫檔案系統 API 前統一處理環境變數和 Windows Shell 虛擬路徑：

```csharp
string expanded = UserPathResolver.Expand(rawPath);            // 接受 string?，返回 string
bool isVirtual = UserPathResolver.IsVirtualPath(expanded);
string resolved = UserPathResolver.Resolve(rawPath);           // 選填的第二個引數見下方

// Resolve 與 ResolveForNavigation 都接受一個選填的 Func<string, string>?，用於在詢問檔案系統
// 之前把無法解析的虛擬標記換成真實路徑；沒有解析委派、又無虛擬標記可解析時，輸入會作為最後
// 手段原樣返回。

// 當路徑接著要被開啟或瀏覽時，請用 ResolveForNavigation 而不是 Resolve：它還會額外把虛擬的
// Shell 項目正規化為宿主得以前往的檔案系統目標。
string target = UserPathResolver.ResolveForNavigation(rawPath);
```

`Expand` 會移除前後空白並展開 `%USERPROFILE%` 等環境變數。`Resolve` 會先展開環境變數，再盡可能將 `shell:Downloads` 或 `::{CLSID}` 等標記解析為實體路徑。對於 `shell:AppsFolder` 這類沒有實體路徑的虛擬資料夾，`Resolve` 會改為傳回其標準名 `::{CLSID}`，讓同一個資料夾的各種寫法彼此相等；該結果仍然是虛擬路徑。只有 Shell 完全無法解析的標記才會原樣返回。傳給檔案系統 API 前應使用 `IsVirtualPath` 檢查結果。目錄索引 API 只有在路徑解析為真實且被索引涵蓋的資料夾後才能列舉內容。
