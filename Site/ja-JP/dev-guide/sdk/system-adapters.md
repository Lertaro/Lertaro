# システムとダイアログの統合

この章では、Windows エクスプローラー、標準ファイルダイアログ、およびサードパーティ製ファイラーと連携するための `Lertaro.PluginSdk` アダプターインターフェイスを解説します。

> [!NOTE]
> `IActivePathCollector`、`IFileDialogAdapter`、`IInlineSearchAdapter` の実装は、管理者権限のウィンドウとの UIPI 制限を越えて安全に対話するため、ホストによって **特権 Hook プロセス** 側にもロードされて実行されます。

## 1. アクティブパスコレクター `IActivePathCollector`

フォーカスのあるアクティブウィンドウから現在の作業ディレクトリを抽出し、インライン検索の絞り込み範囲や相対パスの解決に利用します。

```csharp
namespace Lertaro.PluginSdk;

public interface IActivePathCollector
{
    string Name { get; }
    string TargetName { get; }   // 対象ファイラー名（例: "Directory Opus", "Total Commander"）
    bool CanHandle(string className);
    string? TryGetPath(
        IntPtr activeHwnd, string activeClassName,
        IntPtr windowHwnd, string windowClassName,
        string processName);
}
```

- フォーカスのあるコントロール（`activeHwnd`）と親ウィンドウ（`windowHwnd`）が個別に渡されるため、アドレスバーやツリービューのパスを柔軟に取得できます。

## 2. 標準ファイルダイアログアダプター `IFileDialogAdapter`

Windows 標準のファイルを開く/保存ダイアログを検出・操作します。

```csharp
public interface IFileDialogAdapter
{
    string Name { get; }
    bool CanHandle(IntPtr hwnd, string className, string processName);
    string? GetCurrentPath(IntPtr hwnd);
    bool NavigateTo(IntPtr hwnd, string targetPath);
    bool TargetIsFolderOnly => false;  // フォルダー選択専用ダイアログかどうか
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor) => true;
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);
    bool RestoreFocus(IntPtr hwnd);
}
```

- **`TargetIsFolderOnly`**：`true` の場合、ユーザーが検索結果でファイルを選択した際に、`NavigateTo` 呼び出し前に自動で親フォルダーへ展開されます。
- **`AdapterRect`**：ピクセル単位の物理境界 `{ Left, Top, Right, Bottom }` を保持。

## 3. インライン検索アダプター `IInlineSearchAdapter`

ファイルダイアログやエクスプローラー内部に Lertaro の検索バーを直接埋め込み、双方向の選択同期を実現します。

```csharp
public interface IInlineSearchAdapter
{
    string Name { get; }
    bool IsFileExplorer => false;      // Windows エクスプローラーかどうか
    bool CanHandle(IntPtr hwnd, string className, string processName);
    bool CanTrigger(IntPtr focusedHwnd, string className);
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor) => CanTrigger(hwndUnderCursor, classNameUnderCursor);
    bool CanEnterActionsMode(IntPtr hwnd);
    string? GetSearchScope(IntPtr hwnd);
    bool ExecuteItem(IntPtr hwnd, string path, string searchInput);
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);
    IEnumerable<string> GetListItems(IntPtr hwnd) => [];
    void OnSelectionChanged(IntPtr hwnd, string path) { }
    void OnSearchFinished(IntPtr hwnd, bool executed) { }
}
```

## 4. クイックナビゲーションプロバイダー `IQuickNavigationProvider`

マウス操作で表示される [**クイックナビゲーションメニュー**](../../user-guide/hotkeys#3-クイックナビゲーションマウス操作) に動的な項目やグループを提供します。

```csharp
public interface IQuickNavigationProvider
{
    string GroupName { get; }           // ルートグループの見出し名
    Action<ISearchResult>? HeaderAction => null; // ヘッダー行末尾の操作ボタン（例: "+" ボタン）
    string? HeaderActionTooltip => null;// ヘッダーボタンのツールチップ
    bool CanProvide(ISearchResult result);
    IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu);
    void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd);
    void ClearSession() { }
}
```

- **`HeaderAction`**：ルートグループヘッダーの右側にボタンを配置できます（例: ブックマークプロバイダーによる「現在のフォルダーを追加」）。
- **`DynamicMenuItem.IsHeader`**：サブメニュー内において `IsHeader = true` の項目を返すことで、サブメニュー側にも操作ボタン付きの見出し行を描画できます。
