# ホストが提供する各種サービス

`Lertaro.PluginSdk.Services` 名前空間では、ホスト内部のアルゴリズム、キャッシュ、プラットフォーム機能をプラグインから直接利用できる高性能な静的サービス群を提供しています。

## 1. 主要な静的サービス一覧

| サービス名 | 主要メソッドとシグネチャ | 機能説明 |
| :--- | :--- | :--- |
| **`FuzzyMatchService`** | `bool IsMatch(string pattern, string text)`<br>`bool[]? GetHighlightMask(string text, string query)` | ホストと同一の fzf あいまい一致エンジンを実行し、文字単位のハイライトマスク（ピンイン多階層フォールバック対応）を計算。 |
| **`TranslationService`** | `string Get(string key)`<br>`string Format(string key, params object[] args)`<br>`void LoadEmbeddedTranslations(...)`<br>`string GetCurrentCulture()`<br>`event Action<string>? CultureChanged` | 多言語動的解決と実行時言語変更ブロードキャスト。`GetCurrentCulture()` は OS の言語ではなく設定画面で明示的に選択されている言語コード（例: `"ja-JP"`）を返却；`CultureChanged` を購読することで UI 言語変更時に内部状態の更新や辞書の再読み込みが可能。 |
| **`IconService`** | `ImageSource? GetIcon(string path, bool isDir)`<br>`ImageSource? GetThumbnail(string path, int size)` | メモリおよびディスクキャッシュ付きの Windows Shell ファイルアイコン・サムネイル抽出。 |
| **`FavoritesService`** | `IReadOnlyList<FavoriteItem> GetFavorites()` | ユーザーが設定画面で登録したお気に入り項目一覧の読み取り。 |
| **`HistoryService`** | `IReadOnlyList<HistoryEntry> GetHistoryEntries()` | 最近のアクセス順に並んだ履歴項目（検索キーワード、ファイル種別を含む）の読み取り。 |
| **`FileMetadataService`** | `Task<IReadOnlyDictionary<string, FileMetadata>> GetMetadataAsync(IEnumerable<string> paths)` | 検索結果セットに含まれない外部パスのファイルサイズやタイムスタンプを一括取得。 |
| **`DirectoryIndexerService`** | `void RegisterDirectory(string pluginId, string path, bool recursive, string? filterPattern)`<br>`IDisposable WatchDirectories(string pluginId, Action onChanged)`<br>`IAsyncEnumerable<ISearchResult> EnumerateDirectoryAsync(...)` | バックグラウンドサービスにカスタムディレクトリを登録して自動インデックス・監視；I/O なしのストリーム列挙を提供。 |
| **`RecentFilesService`** | `Task<IReadOnlyList<ISearchResult>> GetRecentFilesAsync(IEnumerable<string> directories, int limit, int maxAgeMinutes, CancellationToken token)` | インメモリインデックスから指定フォルダー群の最新更新ファイルをミリ秒単位で集約抽出。 |
| **`ExplorerPathService`** | `string? GetLastActivePath()` | エクスプローラーや各アプリのファイルダイアログで最後に開かれた作業ディレクトリパスを取得。 |
| **`PluginSettingsService`** | `T GetSetting<T>(string pluginId, string key, T defaultValue)`<br>`event Action<string, string>? SettingChanged` | プラグイン設定の読み取り（ユーザー値 > スキーマ既定値 > フォールバック値の優先順位）。 |
| **`SearchRefreshService`** | `void RefreshIfMatches(Func<string, bool> queryMatches)` | 非同期処理の完了後に、一致するアクティブな検索結果の再評価とビューの即時更新をホストへ通知。 |
| **`UserDataService`** | `string GetUserDataDirectory()`<br>`string GetSharedDataDirectory()` | ユーザー専用データフォルダー（個別設定用）およびマシン共通データフォルダー（Python/Node ランタイム等）を取得。 |
| **`Logger`** | `void Log(string message, LogLevel level = LogLevel.Info)` | `app.log` にログを出力し、設定画面のログビューアーにリアルタイム同期。 |
| **`PluginPromptService`** | `Task<Dictionary<string, object?>?> Prompt(string title, IEnumerable<PluginConfigField> fields, ...)` | スキーマに基づいて自動生成される軽量なモーダル入力ダイアログを表示。 |
| **`PluginMessageBoxService`** | `MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult)` | ホスト管理のメッセージボックスを表示し、プラグインがホストのテーマ UI を利用できるようにします；ホストのハンドラーが未登録の場合はシステムのメッセージボックスへフォールバックします。 |
| **`ExplorerService`** | `void OpenDirectory(string directoryPath, string? fileNameOrFilePath = null)` | 指定されたディレクトリを開くかファイルを特定し、ホスト設定のサードパーティ製ファイルマネージャー（またはエクスプローラーのタブ）を尊重します。未設定時はシステムのエクスプローラーにフォールバックします。 |

## 2. Windows Shell ファイル操作ヘルパー

`Lertaro.PluginSdk.Shell.FileOperations` は Windows Shell の `IFileOperation` COM インターフェイスをラップしており、進捗ダイアログ、上書き確認、`Ctrl+Z` 元に戻す操作をネイティブにサポートします。

```csharp
namespace Lertaro.PluginSdk.Shell.FileOperations;

// 複数ファイルの一括貼り付け・移動
public static class ShellPasteHelper
{
    public static void PasteAsync(
        IEnumerable<string> sourcePaths,
        string destinationFolder,
        bool move = false,
        Action? onCompleted = null);
}

// ごみ箱への安全な削除または完全削除
public static class ShellDeleteHelper
{
    public static void DeleteAsync(IEnumerable<string> paths, bool permanent = false);
}

// ドラッグ＆ドロップされた仮想ファイルストリームの抽出
public static class VirtualFileExtractor
{
    public static bool HasVirtualFiles(IDataObject dataObject);
    public static Task<IReadOnlyList<string>> Extract(IDataObject dataObject, string targetFolder);
    public static string ResolveDestination(string folder, string name); // 重複時の (2) 自動付与
}
```

> [!TIP]
> 上記の Shell ヘルパーは SDK 内部の専用 STA スレッド（`ShellOperationStaWorker`）で非同期実行されるため、呼び出し元で COM アパートメントスレッドを意識する必要はありません。

## 3. アプリケーションのライフサイクルとテーマ対応プラグインウィンドウ

`AppLifecycleService.RequestRestart()` はホストに正常な再起動を要求します。ホストは後継プロセスを起動し、現在のインスタンスが通常の終了処理を完了してから終了するため、プラグインが実行ファイルを起動したりホストを終了したりする必要はありません。ホストが要求を受け付けた場合は `true` を返します。

プラグイン独自の WPF コンテンツには、`Lertaro.PluginSdk.Windows.PluginWindow` がホストと同じ角丸テーマのウィンドウフレームを提供します。`ContentHostControl.Content` にプラグインのビューを設定し、`Footer` から下部ボタンを追加できます。通常のタスクバーウィンドウには `PluginWindowMode.Window`、最前面に表示し Alt+Tab から隠すダイアログには `PluginWindowMode.Dialog` を使用します。アイコンを省略するとホストの既定のアプリアイコンが使われます。

```csharp
var window = new PluginWindow("ツール", 720, 470, PluginWindowMode.Dialog);
window.ContentHostControl.Content = new MyView();
window.Footer.Children.Add(new Button { Content = "OK", IsDefault = true });
window.ShowDialog();
```
