# UI とプレビューの拡張

この章では、メインウィンドウのサイドバー、詳細テーブルのカスタム列、クイックパネルの動的タブ、QuickLook プレビュー描画、サムネイル抽出、WPF テーマ、多言語対応に関する `Lertaro.PluginSdk` の UI 拡張インターフェイスを解説します。

## 1. サイドバーフィルタープロバイダー `ISidebarFilterProvider`

メイン検索ウィンドウの左側サイドバーに独自のカテゴリフィルターツリーを追加します。

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
    public required Func<ISearchResult, bool> FilterFunc { get; init; } // 一致判定デリゲート
}
```

## 2. テーブルカスタム列プロバイダー `IResultColumnProvider`

メイン検索ウィンドウの「詳細」テーブルビューにカスタム列を追加します（例: メディア再生時間、コード行数、Git ブランチ名等）。

```csharp
public interface IResultColumnProvider : IPluginComponent
{
    string ColumnId { get; }
    string HeaderText { get; }
    double DefaultWidth => 120;
    double MinWidth => 40;
    bool IsVisibleByDefault => false;
    string? GetCellText(ISearchResult result);
    int Compare(ISearchResult a, ISearchResult b) => 0; // 列ヘッダーのソート比較デリゲート
}
```

## 3. クイックパネル動的タブプロバイダー `IQuickPanelTabProvider`

クイック検索バー下部の [**クイックパネル**](../../user-guide/settings/quick-panel) に動的なワークスペースタブを追加します。

```csharp
public interface IQuickPanelTabProvider : IPluginComponent
{
    string TabId { get; }
    string Title { get; }
    string? IconPath => null;
    Task<IReadOnlyList<ISearchResult>> GetItemsAsync(CancellationToken token);

    // ドラッグ＆ドロップ受け入れ処理
    bool CanHandleDragOver(IDataObject data) => false;
    Task HandleDropAsync(IDataObject data, CancellationToken token) => Task.CompletedTask;

    // ドラッグによる並び替えのサポート
    bool SupportsReorder => false;
    Task SaveOrderAsync(IReadOnlyList<ISearchResult> orderedItems) => Task.CompletedTask;

    // タブ専用のアクションコンテキスト
    DynamicActionContext CreateActionContext() => DynamicActionContext.Default;
}
```

## 4. ファイルプレビューとサムネイル

### カスタムファイルプレビュープロバイダー `IFilePreviewProvider`

QuickLook（スペースキー）プレビューウィンドウでの描画ロジックを独自に実装します。

```csharp
public interface IFilePreviewProvider : IPluginComponent
{
    bool CanPreview(string filePath);
    int Priority => 0;                  // 複数の一致がある場合の優先度
    FrameworkElement CreatePreviewControl(string filePath);
}
```

#### プレビューのライフサイクルと再利用

WPF `FrameworkElement` が以下のインターフェイスを実装している場合、ホスト側で表示処理が最適化されます。

- **`IPreviewSessionAware`**：`void OnPreviewClosed()` を実装し、プレビュー終了時や別形式ファイルへの切り替え時にメディアプレーヤーや WebView2 インスタンス、ファイルハンドルを安全に解放します。
- **`IReusablePreview`**：`void UpdatePreview(string filePath)` を実装し、同種ファイル間を上下キーで移動する際にコントロールを破棄・再生成せずインプレース更新してチラつきを防ぎます。

### カスタムサムネイルプロバイダー `IThumbnailProvider`

Shell 拡張が未登録の特殊な形式（`.blend`, `.psd`, `.dwg` 等）の高解像度サムネイルを生成・抽出します。

```csharp
public interface IThumbnailProvider : IPluginComponent
{
    bool CanProvide(string filePath);
    Task<ImageSource?> GetThumbnailAsync(string filePath, int targetSize, CancellationToken token);
}
```

## 5. テーマと多言語化

### テーマプロバイダー `IThemeProvider`

独自の配色と WPF リソースディクショナリを提供します。

```csharp
public interface IThemeProvider : IPluginComponent
{
    string ThemeId { get; }
    string DisplayName { get; }
    ResourceDictionary GetResourceDictionary(bool isDark);
}
```

### 多言語化プロバイダー `ITranslationProvider`

動的な多言語翻訳ディクショナリを提供します。

```csharp
public interface ITranslationProvider : IPluginComponent
{
    IReadOnlyDictionary<string, string> GetTranslations(string cultureName);
}
```
