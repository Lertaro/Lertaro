# 共用抽象契約

本章節彙總了 `Lertaro.PluginSdk` 中跨多個介面複用的基礎資料模型、唯讀契約與設定驅動抽象。

## 1. 檢索結果模型 `ISearchResult`

在 Lertaro 的架構中，外掛模組對搜尋結果的觀察始終基於唯讀契約 `ISearchResult`，禁止直接篡改宿主底層的核心索引資料結構：

```csharp
namespace Lertaro.PluginSdk;

public interface ISearchResult
{
    string Name { get; }                  // 檔案或項目的顯示名稱（如 "Lertaro.exe"）
    string FullPath { get; }              // 絕對實體路徑（如 "C:\Program Files\Lertaro\Lertaro.exe"）
    string ContextDirectory { get; }      // 所在父級目錄路徑（如 "C:\Program Files\Lertaro"）
    bool IsDir { get; }                   // 是否為目錄/資料夾
    bool IsApplication { get; }           // 是否為可執行程式或捷徑
    FileMetadata Metadata { get; }        // 高效能檔案中繼資料（大小、修改時間等）
    bool[]? GetHighlightMask(string text, string query); // 字元級反白遮罩計算
}
```

> [!NOTE]
> `ISearchResult.Metadata` 包含的資料由宿主底層的 USN / MFT 記憶體索引直接注入，**讀取該屬性完全不產生任何磁碟 I/O 或 IPC 呼叫**。僅當你需要查詢不屬於目前結果集的外部路徑時，才需要呼叫 `FileMetadataService.GetMetadataAsync`。

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
    void HideWindow();                                // 隱藏目前搜尋視窗
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
| **`Text`** | 文字輸入框。支援設定 `RequireNonEmpty`，為空時自動回復為 `DefaultValue`。 |
| **`Integer`** | 數字微調輸入框。支援設定最小值與最大值範圍。 |
| **`Choice`** | 下拉式選單。透過 `Choices` 或 `ChoiceOptions` 清單指定可選項目。 |
| **`Hotkey`** | 專屬按鍵錄製框。可設定 `RequireModifier = true` 強制要求必須包含修飾鍵。 |
| **`FilePath` / `FolderPath`** | 附帶「瀏覽...」檔案/資料夾原生選取器按鈕的路徑輸入框。 |
| **`StringList`** | 支援多行編輯與項目增刪排序的多行清單方塊。 |
| **`Group`** | 包含子欄位清單（`SubFields`）的可折疊卡片分組。 |
| **`CustomControl`** | 允許外掛模組直接掛載一個自訂的 WPF `UIElement` 控制項執行個體。 |
| **`Button`** | 顯示操作按鈕並呼叫欄位的 `OnClick` 委派，不儲存設定值。 |

`PluginConfigSchema` 亦支援設定 `OnSave` 與 `OnRollback` 生命週期委派，在使用者按一下確認提交或離開頁面放棄修改時執行自訂持久化或狀態復原。

### 本地化選擇標籤

當選項需要本地化標籤，同時仍要儲存穩定的設定值時，應使用 `ChoiceOptions`。`PluginConfigChoice.Value` 會寫入外掛模組設定，`LabelKey` 會解析為介面顯示文字；如果儲存值與顯示文字相同，繼續使用舊的 `Choices` 清單即可。

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
}
```

主機只會在完整搜尋視窗的最終繪製階段呼叫 `GetFileResults`。外掛模組不處理目前查詢時應傳回空清單。傳回的每個 `InstantResultItem` 都必須對應一個實際存在的檔案或資料夾，這樣完整視窗的路徑、大小和類型欄位才有意義。此元件與外掛模組的即時結果提供者共用同一個啟用/停用開關。
