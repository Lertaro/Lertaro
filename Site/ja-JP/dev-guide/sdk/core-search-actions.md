# 検索コアとアクション

この章では、`Lertaro.PluginSdk` における検索データソースの提供、即時計算クエリ、非 ASCII エイリアス変換エンジン、クエリ末尾トークンハンドラー、およびコンテキストメニューに関する主要なインターフェイスを解説します。

## 1. 基本コンポーネント仕様 `IPluginComponent` と `IPlugin`

すべてのプラグインコンポーネントは `IPluginComponent` を継承してメタデータをホストに提供します。

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IPluginComponent
{
    string Name => GetType().Name;      // コンポーネントの表示名（既定はクラス名）
    string Description => string.Empty; // 設定画面でツールチップとして表示される説明文
}

public interface IPlugin : IPluginComponent
{
    // プラグインアセンブリのメインエントリポイント。ウェブサイト関連のメンバーは
    // どちらも省略可能で、WebsiteUrl が null なら設定カードにリンク自体が表示されません。
    string? WebsiteUrl => null;
    string? WebsiteLabel => null;
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

    // このプロバイダーを起動するワード。ホスト自身の除去処理がこの値を読むため、
    // ホスト側の除去とプラグイン側の一致判定がずれません。第 6 章「トリガーワード」を参照。
    IReadOnlyList<string> QueryTriggerKeywords => [];
}
```

> [!TIP]
> `GetInstantResults` はスムーズなタイピングのため同期呼び出しされます。非同期ネットワーク処理（翻訳やサジェスト取得等）を行う場合は、仮のプレースホルダーを即座に返し、`Task.Run` でバックグラウンド取得した後に `SearchRefreshService.RefreshIfMatches` を呼び出してホスト側の結果を再描画してください。

### 非 ASCII エイリアス変換エンジン `IAliasProvider`

中国語ファイル名など非 ASCII 文字列に対してインデックス用のエイリアスを生成します。

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IAliasProvider : IPluginComponent   // Name は IPluginComponent に由来する
{
    bool CanHandle(string text);
    IReadOnlyList<(char Start, char End)> InputRanges { get; }  // 入力文字範囲（CJK統合漢字等）
    IReadOnlyList<(char Start, char End)> OutputRanges { get; } // 出力文字範囲（a-z等）

    // 複数音節エンジン（ピンイン）で設定する、生成されるエイリアス内で音節を繋ぐ文字。
    // '\0'（既定）はエンジンが区切り文字を一切出力しないことを意味する。
    char SyllableSeparator => '\0';

    IEnumerable<string> GetAliases(string text);

    int Version => 1;                                           // ルール変更時にインクリメント
    IEnumerable<string> GetQueryForms(string term) => Array.Empty<string>(); // クエリ側の音節分割等の展開
    int[]? MapAliasToSourceIndices(string text, string alias) => null;       // ハイライト位置の逆マッピング

    // インデクサーのホットパス向けのゼロアロケーション UTF-8 ビルダー。既定の実装は
    // GetAliases() をシンクへ転送し、実際に大文字を含むエイリアスだけを小文字化するため、
    // エンジンは文字列より低コストでバイトを生成できる場合のみこれをオーバーライドする。
    void GetAliasesUtf8(string text, AliasByteSink dest);
}
```

### クエリ末尾トークンハンドラー `IQueryTokenProvider`

検索語の末尾にあるトークン（例: `report :size`, `doc :@today`, `image ::"hello world"`）を処理し、結果一覧にフィルターやソートを適用します。

```csharp
public interface IQueryTokenProvider : IPluginComponent
{
    bool CanHandle(string token);
    Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results);

    // このトークンがクエリから消費された後に、結果行内でハイライトし続けるテキスト。
    // null（既定）はホスト自身のハイライトをそのまま残す。
    string? GetHighlightText(string token) => null;
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
namespace Lertaro.PluginSdk.Abstractions;

public interface ISearchResultAction : Plugins.IPluginComponent
{
    string GroupName { get; }           // アクションメニュー内のグループ見出し
    string DisplayName { get; }         // アクションのタイトル
    // アクションは表示名で識別されるため、Name は記述するのではなくマッピングされる:
    string Plugins.IPluginComponent.Name => DisplayName;

    // 既定値を持つ非 Nullable。空文字列は「ショートカットなし」を意味する。
    // 破壊的なファイルアクションが、エクスプローラーのキー組み合わせを割り戻されるまで
    // 未割当のままだったのはこの仕組みによる。
    string Hotkey => string.Empty;
    IReadOnlyList<string> Keywords => Array.Empty<string>();
    IReadOnlyList<string> Parameters => Array.Empty<string>();

    // アクションが表示される位置。既定では検索に表示され、キーワードを持たない場合のみ
    // メニューに表示される（キーワードを持つ場合、代わりに行として表示されるため）。
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => true;
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => Keywords.Count == 0;

    ImageSource? Icon { get; }          // アクションのアイコン。null はグループの既定アイコンを描画
    bool CanExecute(IReadOnlyList<ISearchResult> results);
    void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view);
}
```

### 動的メニュービルダー `IDynamicActionProvider`

実行時に動的にメニューを構築します（Windows Shell の右クリックメニューの埋め込みなど）。

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IDynamicActionProvider : IPluginComponent
{
    string GroupName { get; }
    string IPluginComponent.Name => GroupName;    // ISearchResultAction と同じマッピング

    int Priority => 0;                            // メニュー内の並び順の重み（非 Nullable）
    IReadOnlyList<string> Keywords => Array.Empty<string>();
    IReadOnlyList<string> Parameters => Array.Empty<string>();

    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => true;
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => Keywords.Count == 0;

    void Init() { }                               // 初回のメニュー表示とは別に呼ばれる 1 度だけのウォームアップ
    bool CanProvide(IReadOnlyList<ISearchResult> results);

    // インスタント結果（ウィンドウタイトルやプロセス行）に対するメニューを有効化するかどうか。
    // 既定は false。多くのプロバイダーはこれらの行が持たないファイルパスを基準にするため。
    bool CanProvideForInstantResults => false;

    IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> results, IntPtr hMenu);
    IEnumerable<(string Hotkey, Action Execute)> GetHotkeyActions(IReadOnlyList<ISearchResult> results)
        => Array.Empty<(string, Action)>();
    void ExecuteCommand(IReadOnlyList<ISearchResult> results, uint commandId, IntPtr ownerHwnd);

    // 既定実装は存在しないため、実装は必須。メニューが破棄されるときに呼ばれ、
    // ナティブハンドルやキャッシュした Shell の CDS ストリームを保持するプロバイダーが
    // それらを解放できるようにする。
    void ClearSession();
}
```

## 4. 補助データ構造

- **`SearchableItem`**：`Title`、`Description`、`IconData`、`IconColor`、`ActionType`（`"Copy"` / `"Execute"` / `"None"`）、`ActionArgument`、`TabCompletion`、`HBitmapIcon`（ホストが自動解放）、`ResultKind`（プラグインが選択するタグで、ホストのフィルターや列がこれを手がかりにする）、および 2 つの実行コールバックを保持します。使い捨てであれば `OnExecute`（`Action`）、アクションが成否を報告する必要がある場合は `OnExecuteFunc`（`Func<bool>`）を使い、ホストはその戻り値を、たとえばウィンドウを閉じるかどうかの判断に利用します。`InstantResultItem` は表示用・コールバック系のメンバーを同じく持ちますが、**`ResultKind` だけは除かれます**。これは検索アイテム側のモデルだけに存在するメンバーです。
- **`DynamicMenuItem`**：`Text`、`CommandId`、`IsSeparator`、`HasSubMenu`、`SubMenuHandle`、`IsDisabled`、`OnExecute`、`IsActionable`（既定は `true`。`false` はサブメニューを開くだけの行を示す）、`HBitmapItem`（ミラー対象の Shell メニューから取得するネイティブのアイコンハンドル）、`ShortcutHint`（ニーモニックキーが一致させる文字）、`IsContinuation`（ページング用カーソル。このバッチがホストがまだ埋め途中のメニューの続きであることを示し、値が立っている間ホストは要求を続けます）、`IsHeader`（操作ボタン付きのグループヘッダー行として描画）を保持します。
- **`SearchWindowType`**：`Main`（メイン検索窓）、`Quick`（クイック検索バー）、`Inline`（インラインダイアログ）の列挙型。

## 5. 名前付き検索スコープ `ISearchScopeProvider`

**スコープ** はキーワードの接頭辞とディレクトリ集合の組み合わせです。`tf report` と入力すると、ホストは通常どおりインデックス検索を `report` について実行し、その対象を指定フォルダーに限定します。既存インデックスに対する第二段階のフィルターであり、別の検索エンジンではありません。

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface ISearchScopeProvider : IPluginComponent
{
    // キーストロークごとのディスパッチで参照されるため、キャッシュした一覧を返し、
    // 設定が変わったときだけ再構築する。キーワードが空、またはフォルダーを持たない
    // スコープはホストに無視される。
    IReadOnlyList<SearchScope> GetSearchScopes();
}

public sealed class SearchScope
{
    public string Keyword { get; init; } = string.Empty;            // 大文字小文字を区別しない先頭トークン（例: "tf"）
    public IReadOnlyList<string> Folders { get; init; } = Array.Empty<string>();
    public string FilterPattern { get; init; } = "*";               // ファイル名に対する ';' 区切りの Win32 ワイルドカード
}
```

`ISearchableItemProvider` とは異なり、スコーププロバイダーはファイルを列挙も実体化もしないため、設定したフォルダーがどれほど大きくてもメモリとキーストロークあたりのコストは一定に保たれます。いずれのホストインデックスにも覆われていないフォルダーは、その場で走査せず警告をログに出してスキップします。これはインデクサーヘルパーと同じ規則で、まず設定済みのローカルドライブ・ネットワーク・フォルダーインデックスで対象フォルダーを覆っておく必要があります。ディレクトリは常に `FilterPattern` を通過します。

リポジトリ内の実装はファイルフィルタープラグインです。

## 6. トリガーワード `TriggerWord`

ユーザーが冒頭の単語を入力して起動する機能——インスタントプロバイダーの `QueryTriggerKeywords`、アクションの `Keywords`、スコープの `Keyword`、ファイルフィルターのトリガー——は、いずれも単語を `Lertaro.PluginSdk.Services.TriggerWord` 経由で解決します。これにより、ホスト側の除去処理とプラグイン側の一致判定がずれなくなります。

| ヘルパー | 一致条件 |
| :--- | :--- |
| `string Normalize(string? configured)` | 設定された単語を、すべての比較が想定する形式に整えます。前後の空白を除去し、`null` または空なら空文字列になります。読み取り時に正規化してください。ホストは除去する単語をトリムするため、未トリムの値と比較しても何も一致せず、ホストはファイル検索からその単語を取り除き続けてしまいます。 |
| `bool TryMatch(string query, string? word, out string argument)` | 先頭のトークンが単語と**一致**（大文字小文字を区別しない）します。`argument` は残りのトリム済みテキストで、クエリが単語だけのときは空文字列になります。この場合も一致します。単に始まりの部分文字列が同じだけのより長い単語は一致しません（`csreport` は `cs` ではない）。 |
| `bool TryMatchInvoked(...)` | 上記と同じですが、単語の後に実際に何かが入力された時点で初めて単語が成立します。単語だけでは誰も求めていない行が画面に出てしまう場合に使用します。`cs` 単体はファイル検索のまま、`cs ` になった時点でプロバイダーが起動します。 |
| `bool TryMatchAny(string query, IReadOnlyList<string> words, out string matchedWord, out string argument)` | 一覧の順に最初に一致した単語が採用され、**どれ**が一致したかを返します。1 つのプロバイダーに複数のキーワードを持たせる Web 検索エンジンは、再解析のためにどれが一致したかを知る必要があります。 |
| `bool IsTypedPrefixOf(string query, string? word)` | クエリが入力途中の単語である状態（`mkdir` への途中で `m` を入力）。単語が揃う前にトリガーを提示することが許される唯一の分岐で、区切り文字が入力された時点で false になります。 |

`IInstantResultProvider.QueryTriggerKeywords`（既定は空）はホスト自身の除去ステップが読み取る項目です。また `PluginConfigField.IsTriggerWord` は、そのような単語を保持する `Text` 設定を印します。これにより設定画面は、他の機能がすでに同じ単語で応答している場合に保存をブロックせずに警告できます。1 つの単語を 2 つの機能が使う争いは、これを設定しないと黙って起きます。ファイル検索は先に登録されたほうが優先され、もう一方の行は単に表示されなくなりますが、ユーザーにはどちらを書き換えるべきか伝わらないためです。
