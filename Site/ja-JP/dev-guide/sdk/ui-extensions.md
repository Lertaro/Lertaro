# UI とプレビューの拡張

この章では、検索ウィンドウのサイドバー拡張、カスタムテーブル列の追加、クイックパネルの動的タブ提供、QuickLook ファイルプレビューアとサムネイル抽出器の構築、WPF テーマおよび i18n 多言語パックの同梱に関する `Lertaro.PluginSdk` のインターフェイスを解説します。

これらのうちすべては `Lertaro.PluginSdk.Abstractions.Plugins` 配下（プレビュー系プロバイダーは `…Abstractions.Plugins.Preview` 配下）に置かれ、いずれも `IPluginComponent` を継承します。`IPluginComponent` が、ホストが **設定 → プラグイン** で表示する `Name` を供給します。

## 1. サイドバーフィルタープロバイダー `ISidebarFilterProvider`

検索ウィンドウの左サイドバーへカスタムのフィルターカテゴリを注入します：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface ISidebarFilterProvider : IPluginComponent
{
    IEnumerable<SidebarFilterGroup> GetFilterGroups();

    // 並び順の重み。値が小さいほど先に描画される。
    int SortOrder => 100;
}

public class SidebarFilterGroup
{
    // ホストが既知のグループに対して認識する任意の安定 ID（例: 組み込みの結果種別
    // フィルターなら "Type"）。グループが完全にプラグイン定義の場合は空にする。
    public string Id { get; set; } = string.Empty;
    public string Header { get; set; } = string.Empty;
    public List<SidebarFilterItem> Items { get; set; } = new();

    // このグループ内で複数の項目を同時に有効にできるかどうか。
    public bool AllowMultiSelect { get; set; }
}

public class SidebarFilterItem
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    // 2 とおりのアイコン経路。どちらもテーマ対応で、IconData はアクティブテーマの
    // 文字色で描画されるグリフ、IconKey はホストが既に所有するリソース名。
    // どちらも null にすればアイコンなし。
    public string? IconData { get; set; }
    public string? IconKey { get; set; }

    // この項目に一致するために結果が満たすべき述語。既定は「何も一致しない」ため、
    // 一度も設定しない項目は表示されるものの、何も選択できません。
    public Func<ISearchResult, bool> MatchPredicate { get; set; } = _ => false;
}
```

グループと項目は record ではなく可変クラスです。必要なプロパティだけを埋め、残りは既定値のままにしてください。

## 2. テーブルカスタム列プロバイダー `IResultColumnProvider`

フル検索ウィンドウの「詳細」テーブルビューへカスタムのデータ列を追加します（例: メディアの再生時間、コード行数、Git ブランチ）。プロバイダーは自身の列を一度だけ説明し、セル単位の値は要求に応じて返します：

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

    // 任意：適用先の合わない結果ではこの列を非表示にする。
    public Func<ISearchResult, bool>? VisibilityPredicate { get; set; }

    // 任意：列ヘッダーのクリックによるカスタムソート。x < y なら負、x > y なら正。
    public Func<ISearchResult, ISearchResult, int>? SortComparer { get; set; }

    // 任意：フルウィンドウにおいてこの列のセルをダブルクリックしたときの動作。
    // 未設定なら、セルのダブルクリックは行の他の場所と同じ挙動になります。
    public Action<ISearchResult>? OnDoubleClick { get; set; }
}
```

`GetCellValue` は一覧の描画中に呼び出されるため、低コストである必要があります。ディスクに触れるのではなく、事前に計算した値を返すかキャッシュを読んでください。

## 3. クイックパネルタブプロバイダー `IQuickPanelTabProvider`

[**クイックパネル**](../../user-guide/settings/quick-panel) へ動的なワークスペースタブを追加します：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IQuickPanelTabProvider : IPluginComponent
{
    // 今まさに表示すべき項目。パネルが呼び出されるたびに実行される。
    Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default);
}
```

この 1 つのメソッドが契約のすべてです。ドロップ受け入れ、並び替え、アクションコンテキストを実装する必要はありません。

- `CancellationToken` はパネルが閉じられるとキャンセルされます。これを観測するのはタブ自身のリストだけで、プラグインの他の挙動は変わりません。
- ソースが更新日時を知っているなら `ISearchResult` の `Metadata.Modified` を埋めてください。既定の新しい物先順の並びはこの値を使うためです。既定値のままにすると、返した順序が保たれます。
- 何も返さないプロバイダーにはタブが現れません。その動作に設定項目はありません。
- ユーザーが追加する必要のあるフォルダーとは違い、タブはプラグインと同じ時点で存在します。ストリップから閉じることができ、**設定 → クイックパネル** で再度開けます。これは **設定 → プラグイン** でコンポーネントを無効化するのとは別問題です（後者は読み込み自体を止めます）。

## 4. ファイルプレビューとサムネイル

### カスタムファイルプレビュープロバイダー `IFilePreviewProvider`

QuickLook パネル内でのプレビューを描画します。パネルはユーザーが `Alt+P`、またはプレビュー可能な行の中クリックで開きます（[**アクションメニューと即時プレビュー**](../../user-guide/actions-and-preview) 参照）：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins.Preview;

public interface IFilePreviewProvider : IPluginComponent
{
    // タイブレークにのみ使われます。まずユーザーが設定したプロバイダーの順序
    // （設定 → 一般 → プレビューとサムネイル）が適用され、Priority はその中で
    // 高いものから並べる役割を持ちます。
    int Priority => 0;

    bool CanPreview(string path, bool isDir);
    UIElement CreatePreview(string path, bool isDir);

    // パネル内に配置される WPF コンテンツを返すのではなく、外部ウィンドウを自前で
    // ホストする場合に true（QuickLook ブリッジプラグインがこの形です）。
    bool RendersExternally => false;
}
```

#### プレビューのライフサイクルと再利用の契約

**プロバイダー**が以下の 1 つ目の契約を実装する場合、または返した `UIElement` が 2 つ目の契約を実装する場合、ホストはプレビューのライフサイクルを最適化します：

- **`IPreviewSessionAware`** — 返されたコントロールではなく **プロバイダー** に対してキャストされます：`void EndPreviewSession();` プロバイダーがプロセス内のコントロールではなく実際の外部ウィンドウ（`HwndHost`、ネイティブの `IPreviewHandler` とその `prevhost` サロゲート）を所有しているため、オーナーウィンドウが閉じるときにセッションを終了するよう通知されます。プロセス内で描画するプロバイダーの場合は、パネルが隠れるかセッションが終わったときのみです。これがないと、ホストのウィンドウが何も参照しないまま残り続けます。
- **`IReusablePreview`** — 返された要素に対してキャストされます：`bool TrySetTarget(string path, bool isDir);` ユーザーが矢印キーで同種のファイル間を移動するとき、ホストはコントロールを破棄・再生成せず同じコントロールへ再ターゲットを依頼します。これがチラつきを除去します。新しい対象がこのインスタンスに合わなければ `false` を返し、ホストは新しいプレビューを構築するほうへフォールバックします。
- **`IReceivesPreviewPanelBounds`** — `void OnPreviewPanelBoundsAvailable(int left, int top, int width, int height);` 自前のプロセス外ウィンドウをホストするプロバイダーは、パネルが占める矩形を知ってそこに親設定や配置を行う必要があります。このメンバーを実装すれば、矩形が確定した時点で受け取れます。

### カスタムサムネイルプロバイダー `IThumbnailProvider`

ネイティブの Shell ハンドラーがない形式（`.blend`、`.psd`、`.dwg`）のサムネイルを抽出します：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins.Preview;

public interface IThumbnailProvider : IPluginComponent
{
    // プレビューと同じ規則です。まずユーザーが設定したサムネイル プロバイダーの
    // 順序が決まり、Priority はその中での並べ替えにのみ使われます。
    int Priority => 0;

    bool CanProvideThumbnail(string path, bool isDir);

    // 同期的。結果一覧の描画パスで実行されるため、高速に保ってください。
    // メモ化は不要です。ホストが返り値をキャッシュします（実在または仮想の
    // アイテムはパスをキーに、それ以外は拡張子をキーにします）。つまり
    // `size` はホストが Shell のイメージリストから選ぶ値なので、特定の数を
    // 期待しないでください。また、ディレクトリについてプロバイダーが尋ねられることは
    // 一切ありません。
    ImageSource? GetThumbnail(string path, int size);
}
```

## 5. テーマと多言語化

### テーマプロバイダー `IThemeProvider`

カラーパレットと WPF リソースディクショナリを提供します：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IThemeProvider : IPluginComponent
{
    IEnumerable<ITheme> GetThemes();
}
```

```csharp
namespace Lertaro.PluginSdk.Abstractions;   // 注意：テーマ自体は 1 つ上位の名前空間です

public interface ITheme
{
    string Id { get; }
    string DisplayName { get; }
    bool IsDark { get; }
    ResourceDictionary GetResources();

    // 1.0 未満では、ホストのレイヤード サーフェス用ヘルパー経由で構築されたウィンドウが
    // レイヤード型の半透明ウィンドウになり、角は自分で描画してクリップする必要があります。
    // 1.0 なら不透明のまま、ウィンドウマネージャーによって角が丸められ、ClearType を保ちます。
    // 判定はウィンドウのコンストラクターで 1 度だけ行われます。AllowsTransparency は
    // ハンドル生成後に変更できないためです。
    // 現時点で適用されるのは通知ウィンドウだけです。テーマ切り替えが画面に出ている
    // ウィンドウがあっても、そのウィンドウは再構築されません。
    double WindowOpacity => 1.0;
}
```

1 つのプロバイダーが任意の数のテーマを提供でき、ダーク版をプロバイダーが別に出す形ではなく、各テーマが自身のライト／ダークフラグを持ちます。

### 多言語化プロバイダー `ITranslationProvider`

翻訳ディクショナリを動的に提供します：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface ITranslationProvider : IPluginComponent
{
    // このプロバイダーが供給できるカルチャーコード。何も読み込む前にホストが
    // 設定画面で提示できるようになります。既定は空で、「要求された内容から発見する」ことを意味します。
    IReadOnlyList<string> SupportedCultures => Array.Empty<string>();

    IReadOnlyDictionary<string, string> GetTranslations(string cultureName);
}
```
