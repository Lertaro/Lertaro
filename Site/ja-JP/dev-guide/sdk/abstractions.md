# 共通データ構造と契約

この章では、`Lertaro.PluginSdk` 全体で共有されるデータモデル、読み取り専用契約、および設定スキーマの抽象化についてまとめます。

## 1. 検索結果モデル `ISearchResult`

プラグインが検索結果を参照する際は、常に読み取り専用インターフェイス `ISearchResult` を使用します。

```csharp
namespace Lertaro.PluginSdk;

public interface ISearchResult
{
    string Name { get; }                  // 表示名（例: "Lertaro.exe"）
    string FullPath { get; }              // 絶対パス（例: "C:\Program Files\Lertaro\Lertaro.exe"）
    string ContextDirectory { get; }      // 親フォルダーパス（例: "C:\Program Files\Lertaro"）
    bool IsDir { get; }                   // ディレクトリかどうか
    bool IsApplication { get; }           // 実行ファイルまたはショートカットかどうか
    FileMetadata Metadata { get; }        // 高精度なファイルメタデータ（サイズ、更新日時等）
    bool[]? GetHighlightMask(string text, string query); // 文字単位のハイライトマスク
}
```

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
| **`Text`** | テキストボックス。`RequireNonEmpty` を有効にすると、空欄時に `DefaultValue` へ自動フォールバック。 |
| **`Integer`** | 最小値・最大値を指定可能な数値スピンボックス。 |
| **`Choice`** | `Choices` または `ChoiceOptions` の一覧から選ぶドロップダウンリスト。 |
| **`Hotkey`** | キー入力登録コントロール（`RequireModifier = true` で修飾キーを必須化可能）。 |
| **`FilePath` / `FolderPath`** | 参照ダイアログボタン付きのパス入力コントロール。 |
| **`StringList`** | 項目の追加・削除・並び替えが可能な複数行リスト。 |
| **`Group`** | 折りたたみ可能なカード形式のサブフィールドグループ（`SubFields`）。 |
| **`CustomControl`** | プラグインが作成したカスタム WPF `UIElement` を直接埋め込み。 |
| **`Button`** | 操作ボタンを表示し、フィールドの `OnClick` デリゲートを呼び出します。設定値は保存しません。 |

### アイコンフィールド

スキーマキーが `Icon` のテキストフィールドにはアイコンのプレビューが表示されます。WPF Path Data を直接入力でき、完全な SVG/XML を貼り付けるとホストがすべての `<path d>` 値を抽出して結合し、変換後の WPF Path Data だけを保存します。無効なアイコン内容は消去され、テーマ対応のエラーダイアログで通知されます。アイコンを指定しない場合は空の値も有効です。

`PluginConfigSchema` では `OnSave` や `OnRollback` デリゲートを設定し、保存や破棄時のカスタム処理をフックできます。

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
}
```

ホストはフル検索ウィンドウの最終描画時だけ `GetFileResults` を呼び出します。現在のクエリを処理しない場合は空のリストを返してください。返す各 `InstantResultItem` は実在するファイルまたはフォルダーを表す必要があります。これにより、フル検索ウィンドウのパス、サイズ、種類の列を正しく表示できます。このコンポーネントは、プラグインのインスタント結果プロバイダーと同じ有効化・無効化スイッチで管理されます。
