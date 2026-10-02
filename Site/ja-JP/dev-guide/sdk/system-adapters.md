# システムとダイアログの統合

この章では、Windows エクスプローラー、標準ファイルダイアログ、およびサードパーティ製ファイラーにまたがって、ウィンドウへの深いドッキング、アクティブなディレクトリの抽出、インライン検索の統合を行うための `Lertaro.PluginSdk` アダプターインターフェイスを解説します。

これら 4 つはいずれも `Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters` に置かれ、`IPluginComponent` を継承します。ホストが **設定 → プラグイン** に並べる `Name` はそこから取得されます。これらのインターフェイス自身が `Name` を宣言することはありません。

> [!NOTE]
> `IActivePathCollector`、`IFileDialogAdapter`、`IInlineSearchAdapter` の実装は、管理者権限で実行されているウィンドウと対話する際に Windows の UIPI 隔離を越えられるよう、ホストによって **特権化した Hook ヘルパープロセス** にもロードされます。そのため、これらのメンバーは軽量で非対話的でなければなりません。ローレベルのキーボード／マウス Hook のコールバック上で実行され、システムの `LowLevelHooksTimeout` を超える遅延が発生すれば Hook は黙って破棄されるためです。

## 1. 開かれているフォルダーコレクター `IOpenedFolderCollector`

契約のうち読み取り専用の半分です。対象ファイラーが現在開いているすべてのフォルダーを報告します。[**クイックナビゲーション**](../../user-guide/hotkeys) とインライン検索一覧にある「現在開いているフォルダー」グループは、この結果から供給されます。

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

public readonly record struct OpenedFolder(string Path, IntPtr WindowHandle);

public interface IOpenedFolderCollector : IPluginComponent
{
    IReadOnlyList<OpenedFolder> GetOpenedFolders() => Array.Empty<OpenedFolder>();
}
```

アダプターは開いているウィンドウ 1 つにつき 1 項目を返すため、5 つのタブを持つファイラーは 5 つのフォルダーを報告します。レジストリはその一覧を **意図的に重複を残したまま** 引き渡します。2 つのコレクターに見えるフォルダー、または 1 つのコレクターに 2 度見えるフォルダーは 2 回現れます。重複のない集合が必要なら、呼び出し元が自身でパスにより重複排除してください。

## 2. アクティブパスコレクター `IActivePathCollector`

`IActivePathCollector` は `IOpenedFolderCollector` を**拡張します**。特定のウィンドウのフォルダーを名指しできるコレクターは、開かれているフォルダー一覧にも貢献できるのが普通で、継承した既定実装を満たすだけでその一覧を無料で得られます。

フォーカスのある最前面ウィンドウからアクティブな作業ディレクトリを抽出し、Lertaro がインライン検索の範囲を限定したり相対パスを解決したりできるようにします：

```csharp
public interface IActivePathCollector : IOpenedFolderCollector
{
    string TargetName { get; }   // 対象ファイラー名（例: "Directory Opus", "Total Commander"）

    // 3 つのオーバーロード。上になるほど細かい問いで、下になるほど粗い。クラス名の形式のみ
    // 必須で、ほかの 2 つはそれを既定とします。まだウィンドウを区別できないコレクターでも
    // あらゆる呼び出し元に対して正しく答えられます。
    bool CanHandle(string className);
    bool CanHandle(string windowClassName, string windowTitle) => CanHandle(windowClassName);
    bool CanHandle(IntPtr windowHwnd, string windowClassName, string processName) => CanHandle(windowClassName);

    string? TryGetPath(
        IntPtr activeHwnd, string activeClassName,   // フォーカスのあるコントロール
        IntPtr windowHwnd, string windowClassName,   // そのトップレベルウィンドウ
        string processName);
}
```

- フォーカスのあるコントロールと親ウィンドウが個別に渡されるため、ウィンドウ全体からだけでなく、入れ子になったコントロール（アドレスバー、ツリービュー）からパスを読み出せます。
- ウィンドウは認識できたがそのフォルダーが現時点では解決できない場合は `null` を返してください。失敗ではなく、ホストは前の範囲をそのまま残します。

## 3. 標準ファイルダイアログアダプター `IFileDialogAdapter`

Windows 標準の「開く／保存／フォルダー参照」ダイアログを検査・操作します：

```csharp
public interface IFileDialogAdapter : IPluginComponent
{
    bool CanHandle(IntPtr hwnd, string className, string processName);
    string? GetCurrentPath(IntPtr hwnd);
    bool NavigateTo(IntPtr hwnd, string targetPath);

    bool TargetIsFolderOnly => false;  // 対象の入力がフォルダーのみを受け付ける場合 true（例: 展開先ダイアログ）
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor) => true;

    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);          // カードをドッキングする位置

    // 配置用プローブ。ダイアログ自身のターゲット フィールドがどこにあり、ファイル一覧が
    // どこにあるかを教えます。インラインカードはこの 2 つを読んで自分の掛かる位置を決めます。
    // フィールドの下、一覧の上、または下に余地がないときの収まる場所。どちらかを false で
    // 返せば、ホストは GetDockBounds にフォールバックします。どちらも既定は「そのコントロール
    // は見えません」。
    bool TryGetTargetFieldBounds(IntPtr hwnd, out AdapterRect bounds) { bounds = default; return false; }
    bool TryGetFileListBounds(IntPtr hwnd, out AdapterRect bounds) { bounds = default; return false; }

    bool RestoreFocus(IntPtr hwnd);
}

public struct AdapterRect   // 物理ピクセル
{
    public int Left, Top, Right, Bottom;
}
```

- **`TargetIsFolderOnly`**：`true` の場合、ユーザーが検索結果でファイルを選択した際に、ホストは `NavigateTo` を呼ぶ前にその親フォルダーを自動解決します。
- **`TryGetTargetFieldBounds` / `TryGetFileListBounds`**：カードの配置専用です。配置側はダイアログのターゲット フィールドの下にカードを掛けることを優先し、ファイル一覧をフォールバックの基準位置として使います。いずれも解決できないダイアログには、そのまま `GetDockBounds` の矩形が当てられます。
- **`RestoreFocus`**：キーボードをダイアログ自身の編集フィールドへ返します。ホストはユーザーがインラインカードを離れるとき（`Escape`、または空のカードで呼び出しホットキーをもう一度押したとき）にこれを呼ぶため、他に何もアクティブにしてはいけません。

## 4. インライン検索アダプター `IInlineSearchAdapter`

Lertaro の検索カードを対象のファイルダイアログやエクスプローラーウィンドウに埋め込み、双方向の選択同期を維持します：

```csharp
public interface IInlineSearchAdapter : IPluginComponent
{
    bool IsFileExplorer => false;      // Windows エクスプローラーの場合は true

    bool CanHandle(IntPtr hwnd, string className, string processName);

    // 起動を伴わない認識。既定は CanHandle。ウィンドウが明らかにサポート対象のホストなのに
    // カードを呼び出してはならない場合にオーバーライドします。たとえばコマンドラインや
    // 名前変更の編集欄がフォーカスを持つ場合で、そこでの入力は Lertaro ではなくそのホストに
    // 属するためです。
    bool CanRecognizeHost(IntPtr hwnd, string className, string processName) => CanHandle(hwnd, className, processName);

    bool CanTrigger(IntPtr focusedHwnd, string className);
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor) => CanTrigger(hwndUnderCursor, classNameUnderCursor);
    bool CanEnterActionsMode(IntPtr hwnd);

    string? GetSearchScope(IntPtr hwnd);
    bool ExecuteItem(IntPtr hwnd, string path, string searchInput);
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);

    IEnumerable<string> GetListItems(IntPtr hwnd) => Array.Empty<string>();
    void OnSelectionChanged(IntPtr hwnd, string path) { }
    void OnSearchFinished(IntPtr hwnd, bool executed) { }

    // 0 より大きい値は、選択をミラーリングする間にフォーカスを奪い返すホスト向けに、選択変更が
    // 落ち着いてからそのミリ秒数後にホストがカード自身の入力欄を再びアクティブ化する意味です。
    // 0（既定）は二度と取り返さないことを意味します。
    int SelectionSyncFocusReclaimDelayMs => 0;
}
```

- **`GetDockBounds`**：実際にドッキングへ使用するコンテンツ領域の物理境界を返します。ホストはこのコンテナー矩形を使ってインライン検索ボックスのサイズと位置を決めます。解決できる場合は、無関係な外側のウィンドウではなく、アクティブなエクスプローラーのペインまたはダイアログのコンテンツ領域を返してください。
- **`CanTrigger`** は *あらゆる* キーストロークに対するゲートであるため、受け取ったクラス名だけで答える必要があります。Hook はここで UI Automation の往復をしている余裕を持ちません。
- **`GetListItems`**：現在表示されている行の名前で、選択のミラーリングに使われます。何も返さないのも問題ありません。対応済みのファイラーには行について空文字列を報告するものが複数あるため、ホストは行を名前だけで識別しません。
- **`CanEnterActionsMode`**：`false` にすると、このホストではアクションメニューが完全に消えます。右クリック、`Ctrl+O`、`→` はいずれも、空のパネルを開くのではなく中止されます。
- **`OnSearchFinished(hwnd, executed)`**：カードが閉じるときに、結果が実際に実行されたかどうかとともに呼ばれます。自身の UI（情報ツールチップや名前変更編集）を抑える必要のあったホストがそれを元へ戻すタイミングです。

## 5. クイックナビゲーションプロバイダー `IQuickNavigationProvider`

[**クイックナビゲーションメニュー**](../../user-guide/hotkeys) に動的なグループと項目を提供します：

```csharp
public enum MouseTriggerType { DoubleClick, MiddleClick }

public interface IQuickNavigationProvider : IPluginComponent
{
    string GroupName { get; }                                   // ルートグループの見出しテキスト
    string IPluginComponent.Name => GroupName;                   // 記述するのではなくマッピングされる

    Action<ISearchResult>? HeaderAction => null;                 // ヘッダー行の操作ボタン（例: "+"）
    string? HeaderActionTooltip => null;                         // そのボタンのツールチップ

    bool CanProvide(ISearchResult result);
    IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu);
    void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd);

    // 既定実装はなく、実装は必須。メニューが閉じるときに、メニューが確保したまま残した
    // もの（キャッシュした Shell の CDS ストリーム、ネイティブのアイコンハンドル）を破棄します。
    void ClearSession();
}
```

- **`HeaderAction`**：ルートグループヘッダーへ操作ボタンを追加します（例: ブックマークプロバイダーが「現在のフォルダーをピン留め」を足す）。リポジトリ内のフォルダーキャスケーダープラグインの「今いるフォルダーを保存する」`+` ボタンがこのメンバーです。
- **`DynamicMenuItem.IsHeader`**：入れ子のサブメニュー内で `IsHeader = true` の項目を返すと、操作ボタン付きの対話的なグループヘッダーが描画されます。
- **`MouseTriggerType`**：メニューを開ける 2 つのグローバルジェスチャーに名前を付けます。そのどちらが生きているかはユーザー設定で、プロバイダーの判断事項ではありません（[**ホットキー → クイックナビゲーション（マウス操作）**](../../user-guide/settings/hotkeys-page) 参照）。

## 6. レジストリ

ホストは `Lertaro.PluginSdk.Registries` の 4 つの静的レジストリ経由でアダプターを引き当てます。プラグインのコンポーネントが Hook プロセスへ届くのもこの仕組みです：

| レジストリ | メンバー |
| :--- | :--- |
| `ActivePathCollectorRegistry` | `Register(IActivePathCollector)`、`GetCollectors()`、`GetAllCollectors()` |
| `FileDialogAdapterRegistry` | `Register(IFileDialogAdapter)`、`GetMatchingAdapter(hwnd, className, processName)`、`GetAdapters()`、`GetAllAdapters()` |
| `InlineSearchAdapterRegistry` | `Register(IInlineSearchAdapter)`、`GetMatchingAdapter(hwnd, className, processName)`、`GetAdapters()`、`GetAllAdapters()` |
| `OpenedFolderCollectorRegistry` | `GetOpenedFolders()` — 有効なすべてのコレクターが報告したものを連結する。重複は**設計上そのまま保持**され、例外を投げたコレクターはスキップされるため、壊れたファイラーがスナップショット全体を沈めることはありません |

先頭の 3 つはそれぞれ、ホストが代入する `Func<T, bool> FilterFunc` を公開します。ホストはこれをユーザーが有効にしたコンポーネントへ絞り込むため、`GetCollectors()` / `GetAdapters()` は絞り込み後のビューを、`GetAllCollectors()` / `GetAllAdapters()` は登録されたすべてを返します。プラグインがこれを代入することは一切ありません。一致判定の順序は登録順で、`CanHandle` が `true` と答えた最初のアダプターがそのウィンドウを占有します。汎用の `#32770` ダイアログ用アダプターが、特化したアダプターがすでにカバーしているウィンドウを自分のものとして宣言してはいけないのはこのためです。ダイアログのレジストリはこの上に拒否権を 1 つ追加しています。アダプターがウィンドウを占有したあとでも、タイトルがブロックリストに載っているとその検索は `null` を返し、次のアダプターへは通り抜けないため、そのウィンドウにはどのアダプターも付きません。
