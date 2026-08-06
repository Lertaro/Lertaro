# コア検索とアクション

## `IPluginComponent` と `IPlugin`

すべてのプラグインコンポーネント(プラグインのエントリクラス自身も含む)は `IPluginComponent` を継承しなければなりません。このインターフェースはコンポーネントの名前と説明を提供します。

```csharp
interface IPluginComponent
{
    string Name => GetType().Name;       // Component display name, defaults to type name
    string Description => string.Empty;  // Component description/tooltip shown in settings UI
}
```

すべてのプラグインは、主エントリポイントとして(`IPluginComponent` を継承する)`IPlugin` インターフェースを実装しなければならず、それに加えて必要な他のインターフェースを実装します。

```csharp
interface IPlugin : IPluginComponent
{
}
```

## 検索結果の提供

### `ISearchableItemProvider`

インデックスに組み込むための、完全でキャッシュ可能な項目リストを返します——静的だったり列挙が遅かったりするものの、キー入力のたびには変化しないコンテンツ向けです(例:スタートメニューのショートカット、ブックマークのリスト)。

```csharp
interface ISearchableItemProvider : IPluginComponent
{
    bool EnableAlias { get; } // default true
    event Action? ItemsChanged;
    IEnumerable<SearchableItem> GetSearchableItems();
}
```

### `IInstantResultProvider`

すべてのキー入力のたびに実行され、結果を直接返します——電卓や URL ショートカットのような、クエリの形そのものが結果になるコンテンツ向けであり、あらかじめインデックスしておくようなものではありません。

```csharp
interface IInstantResultProvider : IPluginComponent
{
    IEnumerable<InstantResultItem> GetInstantResults(string query);
    bool[]? GetHighlightMask(string text, string query); // optional match highlighting
}
```

`GetInstantResults` は同期のみです——非同期/キャンセルトークンを取るオーバーロードはありません。データがネットワークの往復を必要とする場合(テキストの翻訳、検索エンジンの候補取得など)は、まず即座にプレースホルダー項目を返し、`Task.Run` で実際の処理を開始し、結果が届いたらキャッシュし、
`SearchRefreshService.RefreshIfMatches`([ホストサービス](./services)を参照)を呼び出してください。これにより、現在のクエリがキャッシュにヒットするようになった検索をホストが再実行してくれます
——具体例は WebSearch プラグインの候補取得(`Plugins/WebSearch/WebSearchInstantProvider.cs`)を参照してください。

### `IAliasProvider`

非 ASCII テキストに対して追加の検索可能な文字列を生成します——中国語ファイル名向けのピンインエイリアスがこの仕組みで動作しています([PinyinAlias](../examples#pinyinalias-—-中国語ファイル名向けのピンインエイリアス)
を参照)。

```csharp
interface IAliasProvider
{
    string Name { get; }
    bool CanHandle(string text);
    IReadOnlyList<(char Start, char End)> InputRanges { get; }
    IReadOnlyList<(char Start, char End)> OutputRanges { get; }
    IEnumerable<string> GetAliases(string text);

    int Version { get; } // default 1
    int[]? MapAliasToSourceIndices(string text, string alias); // default null
    void GetAliasesUtf8(string text, AliasByteSink dest); // default: adapts GetAliases
    IEnumerable<string> GetQueryForms(string term); // default: none
}
```

`InputRanges` と `OutputRanges` にはデフォルト実装がありません——すべてのプロバイダーがこれらを宣言する必要があります。`InputRanges` はこのプロバイダーが変換*元とする*文字範囲です(例えばピンインなら CJK 表意文字ブロック)。`OutputRanges` は生成されるエイリアスを構成する範囲です(例えば小文字の `a`-`z`)。ホストはこの2つを組み合わせて、あるプロバイダー自身の入力・出力の両方のアルファベットを混在させたクエリ項(例えば候補 `大长今` に対する `大cj`)を、候補自身のテキストに対して照合するリテラルの区間と、このプロバイダーのエイリアスに対して照合するエイリアス構文の区間とに分割します。ASCII かどうかを推測するのではなく、この方式で処理します。

`Version`、`MapAliasToSourceIndices`、`GetAliasesUtf8` はすべてデフォルト実装が用意されており、ほとんどのプロバイダーはこれらに触れる必要はありません。

- **`Version`**:このプロバイダーの出力が同じ入力に対して変化しうる場合(アルゴリズムの修正、新しいルール、データテーブルの更新など)に増やしてください。インデックスはこの値を使って、このプロバイダーが以前に生成したエイリアスが古くなり、再生成が必要であることを検知します。
- **`MapAliasToSourceIndices`**:エイリアスに対して見つかった一致(例えばどのピンイン文字が一致したか)を、元のテキストへハイライト用にマッピングし直します。これがないと、クエリが変換前のテキストにそのままの形で一切現れないため、何もハイライトできなくなってしまいます。このエイリアスがこのテキストに対してこのプロバイダーによって生成されたものではない場合、あるいはマッピングがサポートされていない場合は `null`(デフォルト)を返してください——ホストはこれをエラーとしてではなく「このプロバイダー経由ではハイライトできない」として扱います。
- **`GetAliasesUtf8`**:ホストの一括インデックス作成経路で使われる、バイトネイティブなバリアントです。そこではエイリアスは最終的に UTF-8 バイトとして保存されます。デフォルト実装は `GetAliases`
  を内部で呼び出すため、既存のプロバイダーは変更なしでそのまま動作します。プロバイダーが非常に大量のエイリアスを生成し、その文字列生成のコストが実際に問題になる場合にのみ、文字列の実体化を完全に省略するためオーバーライドしてください。
- **`GetQueryForms`**:`GetAliases` のクエリ側に対応するものです——ユーザーが入力したクエリ項を、このプロバイダー自身のエイリアスが使っているのと同じ区切り構造の形に書き換えます。これにより、ユーザーが単なる文字の並びとして入力したクエリ項でも、ホストには理解できない構造(例えばピンインの音節境界。これがあるおかげで、クエリが 2 つの無関係な音節をまたいで一致してしまうことを防げます)を保持したままにできます。何も返さない(デフォルト)ことは「この項は自分のアルファベットにまったく存在しない」ことを意味し、これによって、このプロバイダーが表現できないクエリが、本来一致するはずのないエイリアスに一致してしまうのを防いでいます。クエリごと・項ごとに 1 回だけ呼び出され、候補ごとには呼び出されないため、ここで実質的な処理を行っても問題ありません——ただし返す形の数が増えるほど、それぞれが候補と照合される追加の候補になるため、コストがかさみます。

### `IQueryTokenProvider`

クエリの末尾のトークン（例：`report :size`、`report ::"hello world"`、`report ::hello\ world`）を取得し、一致した結果リストを変換（ソート、フィルタリングなど）します。ダブルクォーテーション（`"..."`）、シングルクォーテーション（`'...'`）、またはエスケープスペース（`\ `）を使用してトークン内にスペースを含めることができます。

```csharp
interface IQueryTokenProvider : IPluginComponent
{
    bool CanHandle(string token);
    Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results);
}
```

## 結果に対するアクション

### `IActionProvider`

プラグインが静的・動的の両方のアクションを公開するために実装するコンテナです。

```csharp
interface IActionProvider
{
    IEnumerable<ISearchResultAction> GetActions();
    IEnumerable<IDynamicActionProvider> GetDynamicActionProviders();
}
```

### `ISearchResultAction`

アクションメニューやクイックウィンドウのアクションホットキーに表示される、単一の静的なアクション
(例:「パスをコピー」)です。

```csharp
interface ISearchResultAction : IPluginComponent
{
    string GroupName { get; }
    string DisplayName { get; }
    string? Hotkey { get; }              // optional default hotkey
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

固定リストを返すのではなく、実行時にメニュー項目を構築します——これが、実際の Windows シェルの右クリックメニュー(ネストされたカスケードサブメニューを含む)が Lertaro のアクションメニューの中に表示される仕組みです。[ShellMenuActionProvider](../examples#coreextensions-—-アクションとシェルのコンテキストメニュー)
を参照してください。

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

`Init()` は、プロセスにつき最大1回だけ、最初にいずれかのアクションメニューが開かれたタイミングでホストから呼び出されます——`CanProvide`/`GetMenuItems` が実際に呼ばれるより前です。この「最大1回」はホストが保証するため、実装側で繰り返し呼び出しに対するガードを自前で用意する必要はありません。時間的な余裕を活かせる遅い一度きりのセットアップ(ネイティブのワーカースレッドのウォームアップなど)に使ってください。直後に前置き時間なしで続く自分自身の `CanProvide`/`GetMenuItems` の呼び出しと競合させるべきではありません——ブロックしてはならないので、実際に時間のかかる処理はバックグラウンドスレッドで行ってください。デフォルト実装は何もしません。

`Priority` は、アクションメニューの動的な(プロバイダーごとの)グループの中で、このプロバイダー自身のセクションがどこに表示されるかを制御します——値が小さいほど先に表示され、デフォルトは `0` です。ただし、これはあくまでフォールバックにすぎません。ユーザーは[設定 → 一般 → 完全検索ウィンドウ](../../user-guide/settings/general#フル検索ウィンドウ)からこれらのセクションをドラッグして自由に並べ替えることができ、ユーザーが明示的に並べ替えたセクションは
`Priority` の値に関わらずその位置を保ちます。

## 補助的なモデル

- **`SearchableItem`** / **`InstantResultItem`** — どちらも Title、Description、IconData、
  IconColor、ActionType(`"Copy"` / `"Execute"` / `"None"`)、ActionArgument、TabCompletion、そして
  `HBitmapIcon`(あらかじめ読み込まれた GDI の HBITMAP で、設定されている場合は IconData より優先されます——ホストが所有権を引き継ぎ、使い終わったら自分で DeleteObject を呼ぶため、渡した後は自分でそのハンドルを再利用したり解放したりしないでください。実例としては Window Switcher プラグインのウィンドウサムネイルキャプチャを参照してください)を持ちます。`SearchableItem` にはさらに
  `OnExecute`(直接呼び出すためのデリゲート)と `ResultKind`(`"Application"`/`"File"` などの上書き)があります。
- **`DynamicMenuItem`** — Text、CommandId、IsSeparator、HasSubMenu、SubMenuHandle、IsDisabled、
  HBitmapItem、OnExecute、ShortcutHint、IsHeader を持ちます。`IsHeader` は、通常の行ではなく、
  (Quick Navigation のサブメニュー自体のグループ名のような)クリックできないセクション見出し行としてこの項目を描画します——Text が見出しのラベルとなり、`OnExecute` も設定されている場合は見出しの末尾に小さなボタンが表示されそれを呼び出します。`IsHeader` が true のときは他のすべてのフィールドは無視されます。これは
  [`IQuickNavigationProvider.HeaderAction`](./system-adapters#iquicknavigationprovider)(ルートレベルのみをカバーする)の、サブメニューの深さに対応する等価物です。
- **`SearchWindowType`** 列挙型 — `Main`、`Quick`、`Inline`。[ユーザーマニュアル](../../user-guide/getting-started#_3-つのウィンドウ)に記載されている3種類のウィンドウのうち、どれに表示されているかに応じて、アクションやプロバイダーの挙動を変えることができます。
