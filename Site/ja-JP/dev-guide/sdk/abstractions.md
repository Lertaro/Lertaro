# 共有抽象化

他の SDK ページのインターフェース全体で使われるモデルとサポート用のコントラクトです。

## `ISearchResult`

すべてのプラグインインターフェースが扱う、結果の読み取り専用ビューです——プラグインが変更可能な結果オブジェクトを受け取ることはなく、常にこれだけです。

```csharp
interface ISearchResult
{
    string Name { get; }
    string FullPath { get; }
    string ContextDirectory { get; }
    bool IsDir { get; }
    bool IsApplication { get; }
    FileMetadata Metadata { get; }
    bool[]? GetHighlightMask(string text, string query);
}
```

`Metadata` は、ホスト自身のファイルインデックスが生成したすべての結果について Size/Created/
Modified/Accessed を保持しています——これを読み取るのは無料です(ディスク I/O も IPC も発生しません)。これは `FileMetadataService.GetMetadataAsync`([ホストサービス](./services)を参照)とは異なり、後者は現在の結果に**まだ含まれていない**パスに対してのみ呼び出す価値があります。

## `FileMetadata`

```csharp
readonly record struct FileMetadata(long Size, DateTime Created, DateTime Modified, DateTime Accessed);
```

ローカル時刻です。`default`(すべてのフィールドがゼロ/`DateTime.MinValue`)は「利用不可」を意味します——これはファイルインデックスに裏付けられていない結果(例えば別のプラグインが生成したもの)です。`Metadata.Modified != default` をチェックすることで、これを本当に0バイトの正当なファイル(その
`Size` は本当に `0` ですが、タイムスタンプ自体は本物です)と区別できます。

## `IPluginSearchWindow`

`ISearchResultAction.Execute` などのコールバックに渡される、最小限のウィンドウ制御用のサーフェスです——意図的に小さく作られており、プラグインは実際のウィンドウを保持するのではなく、これを通じて結果に対する操作を行います。

```csharp
interface IPluginSearchWindow
{
    void LocateInExplorerExternal(string path);
    void OpenFileOrFolderExternal(string path);
    void OpenFileOrFolderAsAdminExternal(string path);
    void HideWindow();
}
```

## `IConfigurable`

`IPlugin` と一緒にこれを実装すると、**設定 → プラグイン → 設定** の下に自動生成される設定 UI が得られます——単純なケースであればカスタムの WPF を書く必要はありません。

```csharp
interface IConfigurable
{
    PluginConfigSchema GetConfigSchema();
}
```

`PluginConfigSchema` はフラットな `Fields: List<PluginConfigField>` です。各 `PluginConfigField`
は `Key`、任意の `GroupKey`/`LabelKey`/`DescriptionKey`(翻訳キーで、自前の `ITranslationProvider`
があればそれを通じて解決されます)、`FieldType`、`DefaultValue` を持ち、さらにタイプに応じて
`Choices`、ネストされた `SubFields`、または `RequireModifier`(`Hotkey` フィールドのみで使用され、修飾キーなしの単独キーを拒否します)を持ちます。

フィールド(典型的にはトリガーとなるキーワードの `Text` フィールド)に `RequireNonEmpty` を設定すると、保存時に空/空白のみの値を永続化する代わりに `DefaultValue` にフォールバックします——これがないと、ユーザーがキーワードフィールドを空にした場合、それに依存する機能が正常なデフォルトに戻るのではなく、静かに到達不能になってしまいます。

`ConfigFieldType` は次をカバーします:`Boolean`、`Text`、`Integer`、`Choice`、`Array`、`Object`、
`Group`、`StringList`、`Hotkey`、`FilePath`、`FolderPath`。ネストされたグループと `StringList` を使った実際のスキーマについては、[CoreExtensions](../examples#coreextensions-—-アクションとシェルのコンテキストメニュー)
を参照してください。

## レジストリ

`ActivePathCollectorRegistry`、`FileDialogAdapterRegistry`、`InlineSearchAdapterRegistry` は、対応する[システムアダプターインターフェース](./system-adapters)の読み込み済み実装をすべて実行時に一箇所へ集約する、ホスト側の仕組みです。プラグイン作者が通常これらと直接やり取りする必要はありません
——インターフェースを実装するだけで、ホストが自動的にあなたのプラグインを発見して登録します。
