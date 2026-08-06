# UI とプレビューの拡張

## 結果の表示

### `ISidebarFilterProvider`

結果サイドバーに分類用のフィルターグループを追加します(例:日付範囲やサイズの区分)。

```csharp
interface ISidebarFilterProvider
{
    int SortOrder { get; } // default 100; lower renders first
    IEnumerable<SidebarFilterGroup> GetFilterGroups();
}
```

`SidebarFilterGroup` は `Header`、`AllowMultiSelect` フラグ(デフォルト `false`。有効にすると、そのグループで複数項目を同時に選択でき、OR で組み合わされます——重なり合う/累積する日付範囲のように、一度に1つだけ選ぶ意味しか持たない項目については無効のままにしてください)、そして
`SidebarFilterItem` のリスト(Id、DisplayName、任意のアイコン、現在の結果リストに対する任意の非同期 `FilterPredicate`)を持ちます。ホストはグループに選択がある時点でクリアボタンを表示するため、プロバイダー側で独自の「すべて」/「いずれか」の疑似項目を用意する必要はありません。

### `IResultColumnProvider`

結果のグリッドビューに追加の列を挿入します(ファイルサイズ、更新日、カスタムメタデータなど)。

```csharp
interface IResultColumnProvider
{
    IEnumerable<ResultColumnDefinition> GetColumns();
    string GetCellValue(ISearchResult result, string columnId);
}
```

`ResultColumnDefinition` は列 ID、ヘッダーテキスト、幅、そして任意の
`VisibilityPredicate`/`SortComparer` デリゲートを持ちます。

## クイックパネル

### `IQuickPanelTabProvider`

[クイックパネル](../../user-guide/settings/quick-panel) — 前面のウィンドウに重ねてドッキングするフローティングパネル — にタブを丸ごと1つ提供します。タブはコンポーネントの名前を持ち、中には一覧が1つ入ります。項目はホスト自身の結果行で描画されるので、アイコン・オープン・サムネイル・アクションメニューはすべて無料でついてきます。CoreExtensions には5つ同梱されています:お気に入り、履歴、Windows の最近使った項目、最後のフォルダー、最近使ったファイル。

```csharp
interface IQuickPanelTabProvider : IPluginComponent
{
    Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default);
}
```

誰かのタブの中のグループではなく、タブそのものです:プロバイダーが返すのは一揃いのコレクションで、ワークスペースが集めるフォルダー群とは直交しているため、1つずつ各ワークスペースにチェックを入れて回るのではなく、それらと並んで置かれます。

`GetEntriesAsync()` はパネルが呼び出されるたびに呼ばれ、ストリーミングではなく完成した集合を返します:パネルは項目を**集合として**並べ替えて打ち切る(新しい順、最大何件)ため、到着のたびに並べ替え直さずに半分だけ表示することはできません。これはレイテンシの代償を伴いません — 各タブはそれぞれのタスクで読み込まれ、パネルは最初に到着したものから開くので、探しに行く必要のあるプロバイダーが遅らせるのは自分のタブだけです。それでもトークンは尊重してください:パネルが閉じるときにキャンセルされます。

ソースが更新日時を知っているなら `ISearchResult.Metadata` の `Modified` を埋めてください — 既定の新しい順がそれを使い、更新日時のない項目は返した順のままになります。何も返さなかったプロバイダーにはタブができず、例外を投げたプロバイダーは自分のタブだけを失い、他には影響しません。

タブは既定でサムネイルタイルとして開きます。設定 → クイックパネル → プラグインタブでそのタブに**一覧で表示**をチェックした場合を除きます。パネル側の見出しの表示切り替えは、開いている間に限りこれを上書きします。タブを **×** で閉じることと、設定 → プラグインでコンポーネントを無効にすることは意図的に別物です:前者はストリップから外すだけ(同じページでチェックを戻せます)、後者はそもそも読み込ませません。ホストは閉じた状態と表示の選択のどちらもコンポーネント id を安定キーとして保存するため、プラグインをオフにしている間に閉じたタブは、プラグインが戻ってきても閉じたままです。

## プレビューとサムネイル

### `IFilePreviewProvider`

特別に扱いたいファイルタイプについて、QuickLook プレビューペイン([アクションメニューとプレビュー
→ QuickLook プレビュー](../../user-guide/actions-and-preview#quicklook-プレビュー)を参照)にカスタムの WPF `UIElement` を描画します。

```csharp
interface IFilePreviewProvider
{
    string Name { get; }
    int Priority { get; } // default 0; higher runs first
    bool CanPreview(string path, bool isDir);
    UIElement CreatePreview(string path, bool isDir);
    bool RendersExternally { get; } // default false
}
```

`Priority` はあくまで*デフォルトの*順序です——ユーザーは 設定 → 一般 →
[プレビューとサムネイル](../../user-guide/settings/general#プレビューとサムネイル)から、あなたのプロバイダーとの相対順を含めて自由に並べ替えることができ、その設定が `Priority` の返す値に優先します。自分のプロバイダーが宣言した優先度が、実際に実行される順序だと思い込まないでください。

プレビューの挙動を細かく調整する、任意の2つの補助インターフェースがあります。

- **`IPreviewSessionAware`** — プレビュープロバイダー自身がコストの高いプロセス外リソース(ホストされたネイティブハンドラー、ファイルロックなど)を保持している場合、プレビュープロバイダー自体にこれを実装してください。`EndPreviewSession()` はプレビューセッション全体が終了したときに一度だけ呼び出され、個々のプレビュー切り替えのたびには呼び出されません。ただし1つ例外があります:
  `RendersExternally` が true のプロバイダーについては、ホストはそのプロバイダーから切り替わるたびに、セッション終了時だけでなく毎回これを呼び出します——詳しくは下記を参照してください。
- **`IReusablePreview`** — `CreatePreview` が返す `UIElement` が、ゼロから再構築するのではなく新しいファイルを指し直せる場合、その `UIElement` 側にこれを実装してください。`TrySetTarget(path,
  isDir)` は、変更をその場で処理できた場合は `true` を、代わりに新しいプレビューを構築するようホストに指示する場合は `false` を返します。

`RendersExternally` は、実際のプレビュー表示面が `CreatePreview` が返す `UIElement` ではなく、別の外部管理されたウィンドウであるプロバイダー向けです——例えばファイルを丸ごと別のアプリケーションに引き渡すような場合です。勝ち残ったプロバイダーがこれを設定している場合、ホストは `CreatePreview`
の内容を表示する代わりに自身のプレビューパネルを非表示にします(その内容は結局表示されないため、単なるプレースホルダーで構いません)。**`IReceivesPreviewPanelBounds`** と組み合わせることで、ホスト自身のパネルが本来占めるはずだった正確な画面上の矩形(物理ピクセル)を取得でき、外部ウィンドウをそれ以外の場所ではなくその位置に配置できます。

```csharp
interface IReceivesPreviewPanelBounds
{
    void OnPreviewPanelBoundsAvailable(int left, int top, int width, int height);
}
```

同梱されている(実験的な)QuickLook Bridge プラグインが実際の例です。これは自身の名前付きパイプ経由で外部の [QuickLook](https://github.com/QL-Win/QuickLook) アプリを検出し、接続できればそのウィンドウをすべてのファイル/フォルダーに対してホストパネルの位置にドッキングします——ユーザー向けの挙動については[アクションメニューとプレビュー → QuickLook 経由の外部プレビュー](../../user-guide/actions-and-preview#quicklook-による外部プレビュー-任意)を参照してください。なお、これは Lertaro 自身に内蔵されているプレビューパネル(このコードベースやドキュメントの中で非公式に「QuickLook」とも呼ばれています)とは別物である点に注意してください。

### `IThumbnailProvider`

マッチした結果に表示されるアイコン/サムネイルを上書きします。

```csharp
interface IThumbnailProvider : IPluginComponent
{
    int Priority { get; } // default 0; higher runs first
    bool CanProvideThumbnail(string path, bool isDir);
    ImageSource? GetThumbnail(string path, int size);
}
```

上記の `IFilePreviewProvider.Priority` と同じ注意点です:これもデフォルトの順序にすぎず、ユーザーは 設定 → 一般 →
[プレビューとサムネイル](../../user-guide/settings/general#プレビューとサムネイル)(両方のプロバイダーの並び順リストが同じタブにあります)から上書きできます。

## テーマとローカライズ

### `IThemeProvider` / `ITheme`

1つ以上のカスタム WPF リソースディクショナリを、選択可能なテーマとして登録します(**設定 → 一般
→ インターフェイスのテーマ**に表示されます)。

```csharp
interface IThemeProvider
{
    string Name { get; }
    IEnumerable<ITheme> GetThemes();
}

interface ITheme
{
    string Id { get; }
    string DisplayName { get; }
    bool IsDark { get; }
    double WindowOpacity { get; } // default 1.0
    ResourceDictionary GetResources();
}
```

### `ITranslationProvider`

指定されたカルチャに対する UI 文字列を提供します——プラグイン自身の UI のためであったり、
`PinyinAlias` のように単に自分の表示名だけのためであったりします。このインターフェースを、関連のない別のインターフェースと同じクラスに実装しているプラグインについては、[サンプルプラグイン](../examples)を参照してください。

```csharp
interface ITranslationProvider
{
    string Name { get; }
    IReadOnlyList<string> SupportedCultures { get; } // e.g. "zh-CN", "en-US"
    IReadOnlyDictionary<string, string> GetTranslations(string cultureName);
}
```

`TranslationService.LoadEmbeddedTranslations`([ホストサービス](./services)を参照)が、プラグインの DLL に埋め込まれた JSON ファイルでこれを支える標準的な方法です。
