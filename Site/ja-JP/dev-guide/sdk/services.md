# ホストが提供する各種サービス

`Lertaro.PluginSdk.Services` 名前空間では、ホスト内部のアルゴリズム、キャッシュ、プラットフォーム機能をプラグインから直接利用できる高性能な静的サービス群を提供しています。

## 1. 主要な静的サービス一覧

| サービス名 | 主要メソッドとシグネチャ | 機能説明 |
| :--- | :--- | :--- |
| **`FuzzyMatchService`** | `bool IsMatch(string pattern, string text)`<br>`bool[]? GetHighlightMask(string text, string query)`<br>`double GetMatchScore(string text, string query)` | ホストと同一の fzf あいまい一致エンジンを実行し、文字単位のハイライトマスク（ピンイン多階層フォールバック対応）を計算し、結果の一貫した並べ替えに使える一致品質スコアを提供。 |
| **`TranslationService`** | `string Get(string key)`<br>`bool TryGet(string key, out string result)`<br>`string Format(string key, params object[] args)`<br>`string GetCurrentCulture()`<br>`IReadOnlyList<string> GetSupportedCultures(Assembly assembly)`<br>`Dictionary<string, string> LoadEmbeddedTranslations(Assembly assembly, string cultureKey, string typeName)`<br>`event Action<string>? CultureChanged` | 多言語動的解決と実行時言語変更ブロードキャスト。`GetCurrentCulture()` は OS の言語ではなく設定画面で明示的に選択されている言語コード（例: `"ja-JP"`）を返却；`CultureChanged` を購読することで UI 言語変更時に内部状態の更新や辞書の再読み込みが可能。`TryGet` はキーが解決できたかどうかを返し、`Get` は例外を投げずに目に見える `[key]` プレースホルダーへフォールバックします。`GetSupportedCultures(assembly)` はそのアセンブリの埋め込みリソースがカバーするロケールを一覧化し、`LoadEmbeddedTranslations(assembly, cultureKey, typeName)` は 1 つのロケールの辞書を返します。 |
| **`IconService`** | `ImageSource? GetIcon(string path, bool isDir)`<br>`ImageSource? GetThumbnail(string path, int size)`<br>`ImageSource? GetIconFromCacheOnly(string path, bool isDir, out bool needsLoad)` | メモリおよびディスクキャッシュ付きの Windows Shell ファイルアイコン・サムネイル抽出。`GetIconFromCacheOnly` は Shell に一切触れず、キャッシュ済みのものを返し、`needsLoad` 経由で実際の読み込みがまだ必要かどうかを報告します。これにより一覧は先に描画され、アイコンは後から埋められます。 |
| **`FavoritesService`** | `IEnumerable<FavoriteItem> GetFavorites()`<br>`bool IsFavorite(string path)`<br>`bool TryAddFavorite(FavoriteItem favorite)` | お気に入り一覧の取得、パスの登録済み確認、ホストブリッジ経由でのお気に入り追加を提供。 |
| **`HistoryService`** | `IEnumerable<HistoryEntry> GetHistoryEntries()` | 最近のアクセス順に並んだ履歴項目（検索キーワード、ファイル種別、項目ごとの使用回数を含む）を読み取ります。同じ物理パスは、最後に開いたときのキーワードの下に最大 1 件だけ表示されます。 |
| **`FileMetadataService`** | `Task<IReadOnlyDictionary<string, FileMetadata>> GetMetadataAsync(IReadOnlyList<string> paths)` | 検索結果セットに含まれない外部パスのファイルサイズやタイムスタンプを一括取得。 |
| **`DirectoryIndexerService`** | `void RegisterDirectory(string pluginId, string directoryPath, bool recursive = true, string filterPattern = "*")`<br>`void UnregisterDirectories(string pluginId)`<br>`IDisposable WatchDirectories(string pluginId, Action onChanged)`<br>`IDisposable WatchDirectories(string pluginId, Action<IReadOnlyList<string>> onChanged)`<br>`void NotifyDirectoryChanged(string pluginId)`<br>`void NotifyDirectoryChanged(string pluginId, IReadOnlyList<string> changedDirectories)`<br>`Task<List<ISearchResult>> SearchDirectoriesAsync(string pluginId, string query, CancellationToken token = default)`<br>`IAsyncEnumerable<ISearchResult> EnumerateDirectoryAsync(string directoryPath, bool recursive = false, string filterPattern = "*", int limit = 0, CancellationToken token = default)` | ホスト側のインデックス検索と変更監視のためにカスタムディレクトリを登録します。列挙はホストのファイルインデックスだけを読み取り、ストリームで返します。対象となるインデックスがないディレクトリは空のシーケンスになるため、ローカルドライブ、ネットワーク、またはフォルダーインデックスで対象にする必要があります。ホストはファイルシステムを直接スキャンしません。意図的な非対称に注意してください。`RegisterDirectory` は既定で再帰的に走査しますが、`EnumerateDirectoryAsync` は再帰しません。監視通知はデバウンスされ、影響を受けたディレクトリを含められます。空のリストは、より狭い範囲を特定できなかったことを示します。 |
| **`MemoryMaintenanceService`** | `void RequestTrim()` | 一時メモリを大量に使用するバックグラウンド処理の完了後、ホストに遅延したワーキングセット整理を要求します。要求はまとめられるか無視される場合があり、使用中のキャッシュは解放しません。 |
| **`RecentFilesService`** | `Task<IReadOnlyList<ISearchResult>> GetRecentFilesAsync(IReadOnlyList<string> directories, int limit, int maxAgeMinutes, CancellationToken cancellationToken = default)` | ホストのインメモリインデックスに問い合わせ、指定フォルダー群の最近のファイルを集約します。 |
| **`ExplorerPathService`** | `string? GetLastActivePath()`<br>`IReadOnlyList<string> GetOpenedFolderPaths()` | エクスプローラーや各アプリのファイルダイアログで最後に開かれた作業ディレクトリパス、およびそれらで現在開かれているフォルダーを取得。 |
| **`PluginSettingsService`** | `T GetSetting<T>(string pluginId, string key, T defaultValue)`<br>`void SetSetting(string pluginId, string key, object? value)`<br>`bool IsComponentEnabled(string dllName, string componentType, string componentName)`<br>`void NotifySettingChanged(string pluginId, string key, object? value = null)`<br>`event Action<string, string>? SettingChanged`<br>`event Action<string, string, object?>? SettingChangedWithValue`<br>`event Action? ComponentEnablementChanged` | プラグイン設定と、ホストが保存するコンポーネント単位の有効状態を読み書きします。`SettingChanged` は何が変わったかを名前で通知し、`SettingChangedWithValue` は新しい値も運ぶため、購読側は値を読み直す必要がありません。 |
| **`SettingsSearchService`** | `IReadOnlyList<SettingsSearchEntryInfo> GetEntries()`<br>`void Invalidate()` | ホストが現在提供している検索可能な設定項目を取得し、動的な項目が変化したときにホストのキャッシュ済みスナップショットを更新可能。 |
| **`SettingsWindowService`** | `bool ShowWindow(string? targetSection = null)`<br>`bool ShowEntry(SettingsSearchEntryInfo? entry)` | テーマ対応の設定画面を表示するか、検索可能な設定項目へ直接移動するようホストに要求。URI や別プロセスは起動しません。 |
| **`SearchRefreshService`** | `void RefreshIfMatches(Func<string, bool> queryMatches)` | 非同期処理の完了後に、一致するアクティブな検索結果の再評価とビューの即時更新をホストへ通知。 |
| **`UserDataService`** | `string? GetUserDataDirectory()`<br>`string? GetSharedDataDirectory()` | ユーザー専用データフォルダー（個別設定用）およびマシン共通データフォルダーを取得。どちらも nullable です。ホストが該当フォルダーを解決できない場合があり得るため、パスがあると決め付けず `null` をチェックしてください。共通フォルダーへの書き込みはサービスのみで、プラグインは読み取りのみ可能です。自身のファイルはユーザーフォルダーに保存してください。 |
| **`Logger`** | `void Log(string message, LogLevel level = LogLevel.Info)` | `app.log` にログを出力し、設定画面のログビューアーにリアルタイム同期。`Lertaro.PluginSdk.Services` ではなく、最上位の `Lertaro.PluginSdk` 名前空間に属します。 |
| **`PluginPromptService`** | `IReadOnlyDictionary<string, object?>? Prompt(string title, IReadOnlyList<PluginConfigField> fields, IReadOnlyDictionary<string, object?>? initialValues = null)` | スキーマに基づいて自動生成される軽量なモーダル入力ダイアログを表示。同期メソッドで、確定された値、ユーザーがキャンセルした場合は `null` を返します。`await` しないでください。 |
| **`PluginNotificationService`** | `INotificationHandle Show(NotificationRequest request)`<br>`Task<NotificationResult> ShowAsync(NotificationRequest request)`<br>`bool Show(string title, string text, Action? onClick = null)` | ホスト自身のウィンドウで背景通知を表示します。右下のカードスタック（`NotificationPosition.CardStack`）と、画面下部中央の1行表示（`BottomNotice`）があります。ホストがその位置で許される範囲に表示時間を収め、呼び出し元アセンブリから送信者を表記するので、プラグイン自身が帰属を偽ることはできません。排他的な全画面アプリが画面を占有している間はカードが1行表示に縮み、例外がプラグインのバックグラウンドスレッドへ投げ返されることもありません。何も画面に出なかった場合も含めてハンドル待ちのタスクは必ず完了し、`bool` のオーバーロードはホストが要求を受け付けたかどうかだけを返します。返答が必要なら `PluginMessageBoxService` を使ってください。表示時間と上限、`Id` による置換、失敗理由、クリックの意味、配置とスレッド処理を含む完全な仕様は [**通知表示**](./notifications) にあります。 |
| **`PluginMessageBoxService`** | `MessageBoxResult Show(string messageBoxText, string caption = "", MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.OK)` | ホスト管理のメッセージボックスを表示し、プラグインがホストのテーマ UI を利用できるようにします；ホストのハンドラーが未登録の場合はシステムのメッセージボックスへフォールバックします。 |
| **`ExplorerService`** | `void OpenDirectory(string directoryPath, string? fileNameOrFilePath = null)`<br>`void OpenFolder(string? folderPath)` | 指定されたディレクトリを開くかファイルを特定し、ホスト設定のサードパーティ製ファイルマネージャー（またはエクスプローラーのタブ）を尊重します。未設定時はシステムのエクスプローラーにフォールバックします。`OpenFolder` は「開くフォルダーを開く、開く対象がなければエクスプローラーをフォーカスする」というシンプルな形式で、`null` を受け付けます。 |

`SettingsSearchService.GetEntries()` が返す項目のインデックスは、現在のホストプロセス内でのみ有効です。項目をそのまま `SettingsWindowService.ShowEntry(...)` に渡すと、SDK はホストのコールバックを呼び出し、`lertaro://` URI の生成や起動は行いません。

`HistoryEntry` は `Keyword`、`Path`、`Kind`、`Time`（Unix 秒）、`Count`（項目を開いた回数）を公開します。`HistoryService.GetHistoryEntries()` は最後に開いた項目から順に返します。

### コンポーネントの有効状態と高コストなランタイム状態

`PluginSettingsService.IsComponentEnabled(...)` はホストが管理するコンポーネント単位のスイッチを読み取ります。ディレクトリ監視、バックグラウンドワーカー、外部ランタイム、その他の高コストな状態を所有するコンポーネントは、その状態を初期化する前にスイッチを確認し、`ComponentEnablementChanged` を購読してユーザーがスイッチを変更したときに対応するランタイムを開始または停止してください。ホストのコールバックが登録されていない場合、またはコールバックが失敗した場合、このメソッドは `true` を返すため、完全なホストの外でもプラグインを利用できます。

## 2. Windows Shell ファイル操作ヘルパー

`Lertaro.PluginSdk.Shell.FileOperations` は Windows Shell の `IFileOperation` COM インターフェイスをラップしており、進捗ダイアログ、上書き確認、`Ctrl+Z` 元に戻す操作をネイティブにサポートします。

```csharp
namespace Lertaro.PluginSdk.Shell.FileOperations;

// 単一のアトミックな Shell 操作として複数ファイルを一括貼り付け・移動。`move` には既定値がないため、
// どちらを意図しているか必ず指定してください。
public static class ShellPasteHelper
{
    public static void PasteAsync(
        IReadOnlyList<string> sourcePaths,
        string destinationFolder,
        bool move,
        Action? onCompleted = null);
}

// ごみ箱への安全な削除または完全削除。`permanent` にも同様に既定値はありません。
public static class ShellDeleteHelper
{
    public static void DeleteAsync(IReadOnlyList<string> paths, bool permanent);
}

// 存在するファイルまたはフォルダー 1 件の名前を変更
public static class ShellRenameHelper
{
    public static void RenameAsync(string path, string newName);
}

// ドラッグ＆ドロップされた仮想ファイルストリームの抽出
public static class VirtualFileExtractor
{
    public static bool HasVirtualFiles(IDataObject? data);                        // FileGroupDescriptorW が存在するか
    public static List<string> Extract(IDataObject? data, string targetFolder);   // 同期処理。書き込んだファイル一覧
    public static string? ResolveDestination(string targetFolder, string name);   // 入力が空なら null、重複時は (2) 自動付与
}
```

3 つの操作ヘルパーはいずれも **fire-and-forget の `void`** であり、`Task` を返しません。`PasteAsync` はオプションの `onCompleted` コールバックを受け取りますが、他は何も報告しません。引数集合が空または空白の場合は、例外を投げずに無視されます。

> [!TIP]
> ヘルパー自身が、**SDK** が所有しプラグイン自身のプロセス内で起動する STA ワーカースレッド（`ShellOperationStaWorker` —— SDK の内部型で、登録や設定は不要）へ処理を転送するため、プラグイン側でシェル操作の COM アパートメントスレッドを管理する必要はありません。キャンセルも、待機可能な戻り値も、意図的に提供されていません。ネイティブの確認ダイアログと進捗ダイアログそのものが相互作用であり、結果を知りたいプラグインはあとで出力先を読み取ってください。

## 3. アプリケーションのライフサイクルとテーマ対応プラグインウィンドウ

`AppLifecycleService.RequestRestart()` はホストに正常な再起動を要求します。ホストは後継プロセスを起動し、現在のインスタンスが通常の終了処理を完了してから終了するため、プラグインが実行ファイルを起動したりホストを終了したりする必要はありません。ホストが要求を受け付けた場合は `true` を返します。

プラグイン独自の WPF コンテンツには、`Lertaro.PluginSdk.Windows.PluginWindow` がホストと同じ角丸テーマのウィンドウフレームを提供します。`ContentHostControl.Content` にプラグインのビューを設定し、`Footer` から下部ボタンを追加できます。通常のタスクバーウィンドウには `PluginWindowMode.Window`、最前面に表示し Alt+Tab から隠すダイアログには `PluginWindowMode.Dialog` を使用します。アイコンを省略するとホストの既定のアプリアイコンが使われます。

```csharp
var window = new PluginWindow("ツール", 720, 470, PluginWindowMode.Dialog);
window.ContentHostControl.Content = new MyView();
window.Footer.Children.Add(new Button { Content = "OK", IsDefault = true });
window.ShowDialog();
```

`PluginWindow.ShowFooter` は、ボタンを持たないツールでフッター行をオフにします。アイコン引数を省略した場合はホストの既定のアプリアイコンにフォールバックします。

## 4. ウィンドウ・クエリ・テーマ・プレビューの基盤サービス

| サービス名 | 主要メソッドとシグネチャ | 機能説明 |
| :--- | :--- | :--- |
| **`SearchWindowService`** | `bool IsWindowVisible()`<br>`void ShowWindow(string? query = null)`<br>`void HideWindow()`<br>`void FocusQueryTextBox()` | プラグインからホストの検索ウィンドウを照会・操作します。開始用のクエリを渡すこともできます。 |
| **`SearchQueryService`** | `void ChangeQuery(string query, bool requery = false)`<br>`string StripQueryTokens(string query)` | アクティブなウィンドウのクエリを書き込み、必要に応じて検索を再実行します。ホストの末尾トークンを除去するため、プラグインはユーザーが入力した素のテキストだけを扱えます。 |
| **`ThemeService`** | `bool IsDarkTheme` | 自身の内容を描画するプラグイン向けの単一のフラグ。ホスト側デリゲートが存在しない場合は、実行中の WPF アプリケーションのリソースを読むため、ランチャー外で描画されるプラグインでも例外ではなく答えを得られます。 |
| **`PluginPreviewCache`** | `string Register(string title, string pluginName, Lazy<UserControl> factory, Func<object?>? iconProvider = null)`<br>`PluginPreviewEntry? GetEntry(string key)`<br>`UIElement? GetPreview(string key)` | プラグイン所有のプレビューコントロールを遅延生成で登録し、ホストが参照するためのキーを返します。コントロールは読み込み時ではなく、実際に初めて表示された時点で構築されます。 |
| **`PreviewActivationSignal`** | `void Begin()`<br>`void End()`<br>`bool IsActive`<br>`event Action? FocusStolen` / `void NotifyFocusStolen()` | **プロセス外**のネイティブハンドラーが実際にホストされている間セットされます。コールドスタートではなく閲覧セッション全体に及ぶのがポイントで、描画済みコンテンツとの操作が本物のトップレベルウィンドウを開く可能性があるためです。入れ子の `Begin`/`End` の深さはカウントされるため、プロバイダーは呼び出しを必ず対にしてください。 *現状のホストの動作であり、契約ではありません：*シグナルが立っている間、検索ウィンドウは自身の非活性化を「ユーザーがよそをクリックした」とは扱わなくなります。 |
| **`PreviewDialogSignal`** | `void NotifyDialogOpened()`<br>`void NotifyDialogClosed()`<br>`event Action? DialogOpened` / `DialogClosed` | プロバイダーによって、ネイティブハンドラー自身のポップアップが現れたときに発行されます（存在意義そのものが、プレビュー中の暗号化ファイルに対する Word の「パスワードを入力」プロンプトです）。*現状のホストの動作であり、契約ではありません：*ダイアログ表示中、ダイアログに届けるためクイックウィンドウとそのプレビューが隠され、その後復元されます。 |
| **`LocalSendTransferService`** | `void OpenSendWindow(IReadOnlyList<string>? files, string? text)` | ファイル一覧またはテキストペイロードをあらかじめ読み込んだ状態でホストの LocalSend ウィンドウを開くため、プラグインは UI を一切所有せずに転送を引き継げます。 |
| **`ToolRunService`** | `Func<string, string, Task<string?>>? RunDopusPathsFunc`（ホストが代入） | 外部ツールを、実際に応答できるプロセス内で実行します。ホストのキーボードフックは特権化されており、特権化された `dopusrt.exe` に対しては非特権の Directory Opus が UIPI に応答を遮られるため、決して応答できません。一方でプロセスの権限を下げると、フックのトークンが持たない特権が必要になります。App はユーザー自身のレベルで実行されるため、フックは要求をそこへ転送します。デリゲートを読み取り、`null` は利用不可として扱ってください。出力ファイルは呼び出し元が先に作成してください。ツールは既存のファイルを埋めるためです。 |
