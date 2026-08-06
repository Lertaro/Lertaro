# システム / ダイアログアダプター

これらのインターフェースは、プラグインが Lertaro を*他の*ウィンドウ——File Explorer、ネイティブのファイル選択ダイアログ、サードパーティ製ファイルマネージャー——と統合できるようにするもので、
Lertaro 自身の検索ウィンドウだけを対象とするものではありません。

## `IActivePathCollector`

現在アクティブなフォアグラウンドウィンドウから「現在のディレクトリ」を抽出し、Lertaro が検索のスコープをどこに絞るべきか(あるいは相対的なアクションの解決先)を把握できるようにします。

```csharp
interface IActivePathCollector
{
    string Name { get; }
    string TargetName { get; }   // localized name of the app/manager this targets
    bool CanHandle(string className);
    string? TryGetPath(
        IntPtr activeHwnd, string activeClassName,
        IntPtr windowHwnd, string windowClassName,
        string processName);
}
```

多くのファイルマネージャーでは、実際のパスがトップレベルウィンドウ自体ではなく子コントロール(アドレスバー、ツリービューの選択項目)に入っているため、アクティブな(フォーカスのある)要素とそれを含むウィンドウは別々に渡されます。

## `IFileDialogAdapter`

ネイティブに描画された Windows の開く/保存ファイルダイアログを読み取り、操作します。これにより
Lertaro をそれらに埋め込み(下記の [`IInlineSearchAdapter`](#iinlinesearchadapter) を参照)、同期を保つことができます。

```csharp
interface IFileDialogAdapter
{
    string Name { get; }
    bool CanHandle(IntPtr hwnd, string className, string processName);
    string? GetCurrentPath(IntPtr hwnd);
    bool NavigateTo(IntPtr hwnd, string targetPath);
    bool TargetIsFolderOnly { get; } // default: false
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor); // default: true
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);
    bool RestoreFocus(IntPtr hwnd);
}
```

`TargetIsFolderOnly` は、ターゲットフィールドがフォルダーしか保持できないダイアログ(たとえば圧縮ツールの「展開先」の指定先)に対して `true` を指定します——Open/Save ダイアログのファイル名ボックスとは異なり、特定のファイルを指すことは決してありません。ホストはこれを使って、選択された検索結果がファイルだった場合、`NavigateTo` に到達する前に含まれるフォルダーへ解決する必要があるかどうかを判断します。この判断を `NavigateTo` 自体に任せないのは、その呼び出しが昇格された Hook プロセス内で実行され、対話的なユーザーが非昇格状態でマップしたドライブに対しては `File.Exists`/
`Directory.Exists` を信頼できないためです。実際のファイル名ボックスを持つものについては、デフォルトの `false` のままにしておいてください。

## `IInlineSearchAdapter`

Lertaro の検索バーをターゲットのファイルダイアログや File Explorer ウィンドウ(ユーザーマニュアルでいう「インラインウィンドウ」)に直接埋め込み、両方向で選択状態を同期させます。

```csharp
interface IInlineSearchAdapter
{
    string Name { get; }
    bool IsFileExplorer { get; }   // default false
    bool CanHandle(IntPtr hwnd, string className, string processName);
    bool CanTrigger(IntPtr focusedHwnd, string className);
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor); // default: delegates to CanTrigger
    bool CanEnterActionsMode(IntPtr hwnd);
    string? GetSearchScope(IntPtr hwnd);
    bool ExecuteItem(IntPtr hwnd, string path, string searchInput);
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);
    IEnumerable<string> GetListItems(IntPtr hwnd);        // optional
    void OnSelectionChanged(IntPtr hwnd, string path);    // optional
    void OnSearchFinished(IntPtr hwnd, bool executed);    // optional
}
```

`AdapterRect`(`IFileDialogAdapter` と共有)は、単純な `{ Left, Top, Right, Bottom }` の `int` による矩形です。

## `IQuickNavigationProvider`

Quick Navigation ポップアップ([ホットキー → クイックナビゲーション](../../user-guide/hotkeys#クイックナビゲーション-マウス)を参照)にコンテンツ(通常はカスケードメニュー)を供給します。特定のクリックでポップアップが実際に開くかどうかを決めるのはこのインターフェースではなくホストです。`IInlineSearchAdapter`/
`IFileDialogAdapter` にすでに認識されているウィンドウであれば自動的にトリガーされるため、これは純粋にコンテンツソースにすぎません。

```csharp
interface IQuickNavigationProvider
{
    string GroupName { get; }
    Action<ISearchResult>? HeaderAction => null;
    string? HeaderActionTooltip => null;
    bool CanProvide(ISearchResult result);
    IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu);
    void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd);
    void ClearSession();
}
```

`GroupName` は、このプロバイダー自身のルートレベル項目の上に表示されるセクション見出しにラベルを付けます。これにより、複数の Quick Navigation プロバイダーが有効なユーザーでも、どの項目がどのプロバイダー由来かを判別できます——アクションメニューにおける `IDynamicActionProvider.GroupName` と同じ役割です。

`HeaderAction`(任意、デフォルト `null`)は、そのルートレベルのグループ見出しに小さなボタンを追加します——例えば、ブックマーク型のプロバイダーなら「現在のフォルダーを追加」といった用途に使えます。これは `GetMenuItems` 自体がルートレベルで受け取るのと同じ `ISearchResult` を引数として呼び出されます。`HeaderActionTooltip` はそのボタンのツールチップを設定するもので、`HeaderAction` が
null の場合は無視されます。ネストされたサブメニュー(ルートより深い任意の階層)にはホストが描画する見出し自体が存在しないため、`HeaderAction` の効果はルートレベルまでにとどまります。サブメニューに同じ「+」ボタンを付けたいプロバイダーは、代わりにそのサブメニューの最初の項目として
`IsHeader = true` の `DynamicMenuItem`(下記参照)を返し、独自の `OnExecute` に同じ役割を持たせてください。

`DynamicMenuItem` は
[`IDynamicActionProvider`](./core-search-actions#idynamicactionprovider) と同じモデルで、サブメニューレベルの見出し行を表す `IsHeader` フラグも含めて共通です。
