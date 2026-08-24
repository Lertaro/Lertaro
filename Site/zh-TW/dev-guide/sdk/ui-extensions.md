# 介面與預覽擴充

本章節介紹 `Lertaro.PluginSdk` 中用於深度擴充主介面側邊欄、表格自訂資料欄、快速面板動態工作區索引標籤、QuickLook 自訂檔案預覽器、縮圖擷取、WPF 資源字典主題包以及多語言當地語系化的 UI 擴充介面。

## 1. 側邊欄篩選分類 `ISidebarFilterProvider`

用於在主搜尋視窗的左側側邊欄中插入自訂的分類篩選樹：

```csharp
namespace Lertaro.PluginSdk;

public interface ISidebarFilterProvider : IPluginComponent
{
    IEnumerable<SidebarFilterGroup> GetFilterGroups();
}

public sealed class SidebarFilterGroup
{
    public required string GroupName { get; init; }
    public required IReadOnlyList<SidebarFilterItem> FilterItems { get; init; }
}

public sealed class SidebarFilterItem
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public ImageSource? Icon { get; init; }
    public required Func<ISearchResult, bool> FilterFunc { get; init; } // 命中判斷委派
}
```

## 2. 結果表格自訂資料欄 `IResultColumnProvider`

在主搜尋視窗的「詳細資料」多列表格檢視中追加自訂資料欄（例如：擷取並展示影音時長、程式碼行數或 Git 存放庫分支名稱）：

```csharp
public interface IResultColumnProvider : IPluginComponent
{
    string ColumnId { get; }
    string HeaderText { get; }
    double DefaultWidth => 120;
    double MinWidth => 40;
    bool IsVisibleByDefault => false;
    string? GetCellText(ISearchResult result);
    int Compare(ISearchResult a, ISearchResult b) => 0; // 按一下表頭時的排序列排序規則
}
```

## 3. 快速面板動態索引標籤 `IQuickPanelTabProvider`

為置中快速浮動視窗底部的[**快速面板**](../../user-guide/settings/quick-panel)提供動態工作區索引標籤：

```csharp
public interface IQuickPanelTabProvider : IPluginComponent
{
    string TabId { get; }
    string Title { get; }
    string? IconPath => null;
    Task<IReadOnlyList<ISearchResult>> GetItemsAsync(CancellationToken token);

    // 拖曳檔案/連結移入該索引標籤時的接收邏輯
    bool CanHandleDragOver(IDataObject data) => false;
    Task HandleDropAsync(IDataObject data, CancellationToken token) => Task.CompletedTask;

    // 是否支援使用者手動拖曳調整項目順序
    bool SupportsReorder => false;
    Task SaveOrderAsync(IReadOnlyList<ISearchResult> orderedItems) => Task.CompletedTask;

    // 自訂該索引標籤專用的快顯動作功能表上下文
    DynamicActionContext CreateActionContext() => DynamicActionContext.Default;
}
```

## 4. 檔案即時預覽與縮圖

### 自訂檔案預覽器 `IFilePreviewProvider`

接管並自訂特定檔案類型在 QuickLook（空白鍵預覽）浮動視窗中的視覺化轉譯邏輯：

```csharp
public interface IFilePreviewProvider : IPluginComponent
{
    bool CanPreview(string filePath);
    int Priority => 0;                  // 多外掛模組衝突時的仲裁優先順序（值越大越優先）
    FrameworkElement CreatePreviewControl(string filePath);
}
```

#### 預覽生命週期與複用最佳化契約

若外掛模組返回的 WPF `FrameworkElement` 實作了以下可選契約，宿主會在預覽生命週期內執行進階最佳化：

- **`IPreviewSessionAware`**：實作 `void OnPreviewClosed()`，在使用者關閉預覽視窗或切換到其他不相符的檔案時觸發，用於安全釋放影音播放器控制代碼、WebView2 執行個體或大檔案串流。
- **`IReusablePreview`**：實作 `void UpdatePreview(string filePath)`。當使用者按下上下方向鍵連續在同類檔案間切換時，宿主不會銷毀並重建控制項，而是直接呼叫此方法就地更新內容，消除介面白屏與閃爍。

### 自訂縮圖擷取器 `IThumbnailProvider`

為未安裝系統 Shell 縮圖擴充的專有檔案格式（如 `.blend`、`.psd`、`.dwg`）擷取高解析度縮圖：

```csharp
public interface IThumbnailProvider : IPluginComponent
{
    bool CanProvide(string filePath);
    Task<ImageSource?> GetThumbnailAsync(string filePath, int targetSize, CancellationToken token);
}
```

## 5. 外觀主題與多語言

### 自訂主題包 `IThemeProvider`

為 Lertaro 貢獻自訂的色彩配置與 WPF 資源字典：

```csharp
public interface IThemeProvider : IPluginComponent
{
    string ThemeId { get; }
    string DisplayName { get; }
    ResourceDictionary GetResourceDictionary(bool isDark);
}
```

### 多語言當地語系化 `ITranslationProvider`

為外掛模組自身及宿主貢獻動態多語言鍵值對：

```csharp
public interface ITranslationProvider : IPluginComponent
{
    IReadOnlyDictionary<string, string> GetTranslations(string cultureName);
}
```
