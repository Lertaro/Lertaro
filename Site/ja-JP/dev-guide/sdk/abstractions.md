# 共通データ構造と契約

この章では、`Lertaro.PluginSdk` 全体で共有されるデータモデル、読み取り専用契約、および設定スキーマの抽象化についてまとめます。

## 1. 検索結果モデル `ISearchResult`

プラグインが検索結果を参照する際は、常に読み取り専用インターフェイス `ISearchResult` を使用します。

```csharp
namespace Lertaro.PluginSdk.Abstractions;

public interface ISearchResult
{
    string Name { get; }                  // 表示名（例: "Lertaro.exe"）
    string FullPath { get; }              // 絶対パス（例: "C:\Program Files\Lertaro\Lertaro.exe"）
    string ContextDirectory { get; }      // 親フォルダーパス（例: "C:\Program Files\Lertaro"）
    bool IsDir { get; }                   // ディレクトリかどうか
    bool IsApplication { get; }           // 実行ファイルまたはショートカットかどうか
    bool[]? GetHighlightMask(string text, string query) => null; // 文字単位のハイライトマスク
    FileMetadata Metadata => default;     // 高精度なファイルメタデータ（サイズ、更新日時等）
    string? InstantActionArgument => null; // インスタント結果が対象とするもの
}
```

`FullPath` は多くのアクションが識別に用いる基準です。そのため、パスではない対象に対して働くインスタント結果（`activatewindow:12345`、`kill:4321`、カスタムコマンドのペイロードなど）は、そのターゲットを代わりに `InstantActionArgument` に保持し、プロバイダーはそこから読み取ります。その他の種の行——通常のファイル・フォルダー結果、プラグインの検索アクション、履歴項目——では常に `null` のままです。

> [!NOTE]
> `ISearchResult.Metadata` はインメモリインデックスから直接提供されるため、**アクセス時にディスク I/O や IPC 呼び出しは一切発生しません**。結果セットに含まれない外部パスの情報を取得する場合のみ `FileMetadataService.GetMetadataAsync` を使用してください。

## 2. ファイルメタデータ構造体 `FileMetadata`

```csharp
public readonly record struct FileMetadata(
    long Size,
    DateTime Created,
    DateTime Modified,
    DateTime Accessed
);
```

- タイムスタンプはすべて **ローカル時間（Local Time）** です。
- `Metadata == default`（値が 0 や `DateTime.MinValue`）の場合、ファイルインデックス由来ではない結果（プラグインが動的生成したアイテム等）を示します。
- `Metadata.Modified != default` で、メタデータ未取得の状態と実在する 0 バイトファイルを正確に区別できます。

## 3. ホストウィンドウ制御 `IPluginSearchWindow`

アクション実行時（`ISearchResultAction.Execute` 等）に渡されるホストウィンドウの制御ハンドルです。

```csharp
public interface IPluginSearchWindow
{
    void LocateInExplorerExternal(string path);       // エクスプローラー等でファイルを選択表示
    void OpenFileOrFolderExternal(string path);       // 関連付けられたアプリで通常起動
    void OpenFileOrFolderAsAdminExternal(string path);// 管理者権限で起動
    void HideWindow();                                // 検索ウィンドウを非表示にする
}
```

## 4. スキーマ駆動型設定 `IConfigurable`

プラグインで独自の設定項目を提供する場合、`IConfigurable` を実装するだけで、XAML を記述することなく **設定 → プラグイン → 設定** にネイティブな設定フォームが自動生成されます。

```csharp
public interface IConfigurable
{
    PluginConfigSchema GetConfigSchema();
}
```

### サポートされているフィールド型 `ConfigFieldType`

| フィールド型 | UI コントロールと動作 |
| :--- | :--- |
| **`Boolean`** | トグルスイッチまたはチェックボックス。 |
| **`Text`** | テキストボックス。ユーザーが空欄にすると `RequireNonEmpty` が `DefaultValue` へフォールバックさせ、`MaxLength` が文字数を制限します（0 または未設定なら無制限）。`SelectionStart` / `SelectionLength` は、このフィールドのために開く入力エディターにおける 0 始まりの初期選択範囲を指定します。 |
| **`Integer`** | 最小値・最大値を指定可能な数値スピンボックス。 |
| **`Choice`** | `Choices` または `ChoiceOptions` の一覧から選ぶドロップダウンリスト。 |
| **`Array`** | 一覧の値。`SubFields` を伴う場合は**レコード**のリストであり、マスター／ディテールエディター（1 項目につき 1 つの入れ子フォーム）として描画されます（ファイルフィルター、カスタムコマンド、Web 検索プラグインがこの形を使います）。`SubFields` がなければスカラーの素朴なリストとなり、コンパクトな単一カラムのエディターとして描画されます。SDK は `DefaultValue` に既定値をまったく与えていません（宣言では `object?`、`null!`）。そのため本リポジトリーのプラグインはすべて、空のリストには `new List<object>()` を渡しています。 |
| **`Object`** | `SubFields` 経由で編集する単一の構造化値。`Array` が備える一覧操作機能はありません。 |
| **`Group`** | 折りたたみ可能なカード形式のサブフィールドグループ（`SubFields`）。 |
| **`StringList`** | 項目の追加・削除・並び替えと自動折り返しに対応した複数行リスト。実際の改行は表示上のマーカーで示され、設定値には含まれません。 |
| **`Hotkey`** | キー入力登録コントロール（`RequireModifier = true` で修飾キーを必須化可能）。 |
| **`FilePath` / `FolderPath`** | 参照ダイアログボタン付きのパス入力コントロール。 |
| **`CustomControl`** | プラグインが作成したカスタム WPF `UIElement` を直接埋め込み（`CustomControl` 経由でも到達できます）。 |
| **`Button`** | 操作ボタンを表示し、フィールドの `OnClick` デリゲートを呼び出します。設定値は保存しません。 |

その他の `PluginConfigField` のメンバーは、ホストがこれらの型のまわりで描画・永続化に使うものです。`Key`（永続化される設定名）、`GroupKey`（フィールドが収まる `Group` カード）、`LabelKey` / `DescriptionKey`（リテラルの文字列ではなく翻訳キー）、`RequireNonEmpty`、`Choices` / `ChoiceOptions` / `SubFields`、`IsTriggerWord`（[**検索コアとアクション**](./core-search-actions) の「トリガーワード」参照）、`MaxLength`、`SelectionStart` / `SelectionLength`、`CustomControl`、`OnClick`、そしてプラグインが値をホストの設定ストア以外の場所に保存できるようにする 2 つのデリゲート `Func<object?>? GetValue` と `Action<object?>? SetValue` です。

### アイコンフィールド

スキーマキーが `Icon` のテキストフィールドにはアイコンのプレビューが表示されます。WPF Path Data を直接入力でき、完全な SVG/XML を貼り付けるとホストがすべての `<path d>` 値を抽出して結合し、変換後の WPF Path Data だけを保存します。無効なアイコン内容は消去され、テーマ対応のエラーダイアログで通知されます。アイコンを指定しない場合は空の値も有効です。

`PluginConfigSchema` では `OnSave` や `OnRollback` デリゲートを設定できます。`OnSave` はユーザーが**OK/適用**を押して変更を確定したときに実行され、`OnRollback` はキャンセルまたは破棄時に状態を復元します。

### 選択肢のローカライズラベル

ローカライズされたラベルを表示しながら安定した設定値を保存したい場合は、`ChoiceOptions` を使用します。`PluginConfigChoice.Value` がプラグイン設定に保存され、`LabelKey` が表示用テキストに解決されます。保存値と表示テキストが同じ場合は、従来の `Choices` コレクションを使用できます。

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

## 5. フル検索ウィンドウのファイル結果 `IFullSearchFileResultProvider`

フル検索ウィンドウに実在するファイルやフォルダーの行を追加するプラグインは、`IFullSearchFileResultProvider` を実装できます。

```csharp
public interface IFullSearchFileResultProvider : IPluginComponent
{
    IReadOnlyList<InstantResultItem> GetFileResults(string query, int limit);

    // 省略可。既定の実装は GetFileResults を走査するため、このメンバーが導入される前に書かれたプロバイダーはそのまま動きます。
    IEnumerable<InstantResultItem> GetFileResultsStreamed(string query, int limit);
}
```

ホストはフル検索ウィンドウ自身のファイル検索がまだストリーミング中に、バックグラウンドスレッドでプロバイダーを呼び出し、検索が確定するのを待たずに、行が到着した順に描画します。現在のクエリを処理しない場合は空のリストを返してください。返す各 `InstantResultItem` は実在するファイルまたはフォルダーを表す必要があります。これにより、フル検索ウィンドウのパス、サイズ、種類の列を正しく表示できます。応答に数秒かかるプロバイダー（全文インデックスの走査など）は `GetFileResultsStreamed` をオーバーライドして、見つけた候補をその場で渡すことができます。最初の数行が画面に出た後も残りの検索が続きます。既定の実装は `GetFileResults` を走査するだけなので、オーバーライドは省略できます。このコンポーネントは **設定 → プラグイン** 配下に**独自の**有効化・無効化スイッチを持ち、スイッチはそのコンポーネント型をキーにします。プラグインのインスタント結果プロバイダーを無効にしてもこのコンポーネントは無効にならず、その逆も同じです。

## 6. ユーザー設定パスの解決 `UserPathResolver`

ユーザーが入力したパスや設定に保存されたパスを受け取るプラグインは、ファイルシステム API を呼び出す前に `Lertaro.PluginSdk.Helpers.UserPathResolver` を使用して、環境変数と Windows Shell 仮想パスを同じ規則で処理してください。

```csharp
string expanded = UserPathResolver.Expand(rawPath);            // 引数は string?、戻り値は string
bool isVirtual = UserPathResolver.IsVirtualPath(expanded);
string resolved = UserPathResolver.Resolve(rawPath);           // 2 番目の引数は省略可（下記）

// Resolve と ResolveForNavigation はどちらも、省略可能な Func<string, string>? を受け取ります。
// これは、ファイルシステムに尋ねる前に、解析できない仮想トークンを実在するパスへ変換するために使われます。
// リゾルバーを渡さず解析できるものもない場合、最後の手段として入力がそのまま返されます。

// パスを開いたり参照したりする直前なら、Resolve ではなく ResolveForNavigation を使います。
// こちらはさらに、仮想シェル アイテムをホストがナビゲートできるファイルシステムのターゲットへ正規化します。
string target = UserPathResolver.ResolveForNavigation(rawPath);
```

`Expand` は前後の空白を取り除き、`%USERPROFILE%` などの環境変数を展開します。`Resolve` は展開後、`shell:Downloads` や `::{CLSID}` などのトークンを可能な場合に物理パスへ解決します。`shell:AppsFolder` のように物理パスを持たない仮想フォルダーは、代わりに正規の `::{CLSID}` 名へ解決されるため、同じフォルダーのさまざまな書き方が一致します。その結果は依然として仮想パスです。Shell がまったく解析できないトークンのみがそのまま返されます。ファイルシステム API に渡す前に `IsVirtualPath` で結果を確認してください。ディレクトリインデックス API で列挙できるのは、実在し、インデックス対象になっているフォルダーへ解決されたパスだけです。
