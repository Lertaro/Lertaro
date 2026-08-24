# 検索コアとアクション

この章では、`Lertaro.PluginSdk` における検索データソースの提供、即時計算クエリ、非 ASCII エイリアス変換エンジン、クエリ末尾トークンハンドラー、およびコンテキストメニューに関する主要なインターフェイスを解説します。

## 1. 基本コンポーネント仕様 `IPluginComponent` と `IPlugin`

すべてのプラグインコンポーネントは `IPluginComponent` を継承してメタデータをホストに提供します。

```csharp
namespace Lertaro.PluginSdk;

public interface IPluginComponent
{
    string Name => GetType().Name;      // コンポーネントの表示名（既定はクラス名）
    string Description => string.Empty; // 設定画面でツールチップとして表示される説明文
}

public interface IPlugin : IPluginComponent
{
    // プラグインアセンブリのメインエントリポイント
}
```

## 2. 検索結果の提供

### 静的キャッシュ可能アイテムプロバイダー `ISearchableItemProvider`

キーストロークごとに変化せず、事前にインデックス化しておく用途に適しています（スタートメニューのショートカット、ブックマーク等）。

```csharp
public interface ISearchableItemProvider : IPluginComponent
{
    bool EnableAlias => true;           // ピンイン等のエイリアス変換を許可するかどうか
    event Action? ItemsChanged;         // データ変更時に再インデックスを要求するイベント
    IEnumerable<SearchableItem> GetSearchableItems();
}
```

### 動的即時計算プロバイダー `IInstantResultProvider`

ユーザーの入力ごとにリアルタイム実行され、検索語自体から答えを導く機能に適しています（計算機、進数変換、URL ジャンプ等）。

```csharp
public interface IInstantResultProvider : IPluginComponent
{
    IEnumerable<InstantResultItem> GetInstantResults(string query);
    bool[]? GetHighlightMask(string text, string query) => null; // カスタムハイライトマスク
}
```

> [!TIP]
> `GetInstantResults` はスムーズなタイピングのため同期呼び出しされます。非同期ネットワーク処理（翻訳やサジェスト取得等）を行う場合は、仮のプレースホルダーを即座に返し、`Task.Run` でバックグラウンド取得した後に `SearchRefreshService.RefreshIfMatches` を呼び出してホスト側の結果を再描画してください。

### 非 ASCII エイリアス変換エンジン `IAliasProvider`

中国語ファイル名など非 ASCII 文字列に対してインデックス用のエイリアスを生成します。

```csharp
public interface IAliasProvider
{
    string Name { get; }
    bool CanHandle(string text);
    IReadOnlyList<(char Start, char End)> InputRanges { get; }  // 入力文字範囲（CJK統合漢字等）
    IReadOnlyList<(char Start, char End)> OutputRanges { get; } // 出力文字範囲（a-z等）
    IEnumerable<string> GetAliases(string text);

    int Version => 1;                                           // ルール変更時にインクリメント
    int[]? MapAliasToSourceIndices(string text, string alias) => null; // ハイライト位置の逆マッピング
    void GetAliasesUtf8(string text, AliasByteSink dest);       // ゼロアロケーション UTF-8 生成
    IEnumerable<string> GetQueryForms(string term);             // クエリ側の音節分割等の展開
}
```

### クエリ末尾トークンハンドラー `IQueryTokenProvider`

検索語の末尾にあるトークン（例: `report :size`, `doc :@today`, `image ::"hello world"`）を処理し、結果一覧にフィルターやソートを適用します。

```csharp
public interface IQueryTokenProvider : IPluginComponent
{
    bool CanHandle(string token);
    Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results);
}
```

## 3. 結果に対するコンテキストアクション

### アクションコンテナ `IActionProvider`

```csharp
public interface IActionProvider
{
    IEnumerable<ISearchResultAction> GetActions();
    IEnumerable<IDynamicActionProvider> GetDynamicActionProviders();
}
```

### 静的アクション契約 `ISearchResultAction`

`Ctrl+O` メニューやショートカットキーに登録される静的アクション（パスクリップボードコピー、管理者として実行など）を定義します。

```csharp
public interface ISearchResultAction : IPluginComponent
{
    string GroupName { get; }           // アクションのグループ名
    string DisplayName { get; }         // 表示名
    string? Hotkey { get; }             // 既定のショートカットキー（例: "Ctrl+Shift+C"）
    IReadOnlyList<string>? Keywords { get; }
    IReadOnlyList<string>? Parameters { get; }
    ImageSource Icon { get; }           // アイコン
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool CanExecute(IReadOnlyList<ISearchResult> selection);
    void Execute(IReadOnlyList<ISearchResult> selection, IPluginSearchWindow window);
}
```

### 動的メニュービルダー `IDynamicActionProvider`

実行時に動的にメニューを構築します（Windows Shell の右クリックメニューの埋め込みなど）。

```csharp
public interface IDynamicActionProvider
{
    string GroupName { get; }
    int? Priority => 0;                 // メニュー内での表示優先度
    IReadOnlyList<string>? Keywords { get; }
    IReadOnlyList<string>? Parameters { get; }
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    void Init() { }                     // プロセス生存期間中に 1 度だけ呼ばれるウォームアップ処理
    bool CanProvide(IReadOnlyList<ISearchResult> selection);
    IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> selection, IntPtr hMenu);
    IEnumerable<(string Hotkey, Action Execute)> GetHotkeyActions(IReadOnlyList<ISearchResult> selection);
    void ExecuteCommand(IReadOnlyList<ISearchResult> selection, uint commandId, IntPtr ownerHwnd);
    void ClearSession() { }
}
```

## 4. 補助データ構造

- **`SearchableItem` / `InstantResultItem`**：`Title`、`Description`、`IconData`、`IconColor`、`ActionType`、`ActionArgument`、`TabCompletion`、`HBitmapIcon`（自動解放）、`OnExecute` 等を保持。
- **`DynamicMenuItem`**：`Text`、`CommandId`、`IsSeparator`、`HasSubMenu`、`SubMenuHandle`、`IsDisabled`、`OnExecute`、`IsHeader`（操作ボタン付きのグループヘッダー行として描画可能）を保持。
- **`SearchWindowType`**：`Main`（メイン検索窓）、`Quick`（クイック検索バー）、`Inline`（インラインダイアログ）の列挙型。
