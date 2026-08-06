# 系統與對話方塊轉接

這些介面讓外掛可以和*其他*視窗整合——檔案總管、原生檔案選擇對話方塊、第三方檔案管理員——而不僅僅是 Lertaro 自己的搜尋視窗。

## `IActivePathCollector`

從目前作用中的前景視窗中擷取「目前目錄」，讓 Lertaro 知道該把搜尋範圍限定在哪裡(或者相對什麼路徑解析動作)。

```csharp
interface IActivePathCollector
{
    string Name { get; }
    string TargetName { get; }   // 目標應用程式/管理員的當地化名稱
    bool CanHandle(string className);
    string? TryGetPath(
        IntPtr activeHwnd, string activeClassName,
        IntPtr windowHwnd, string windowClassName,
        string processName);
}
```

作用中(取得焦點)的元素和它所在的視窗是分開傳入的，因為很多檔案管理員把實際路徑放在子控制項裡
(位址列、樹狀檢視的選取項目)，而不是最上層視窗本身。

## `IFileDialogAdapter`

讀取並驅動原生繪製的 Windows 開啟/儲存檔案對話方塊，讓 Lertaro 可以被嵌入其中(見下面的
[`IInlineSearchAdapter`](#iinlinesearchadapter))並保持雙方同步。

```csharp
interface IFileDialogAdapter
{
    string Name { get; }
    bool CanHandle(IntPtr hwnd, string className, string processName);
    string? GetCurrentPath(IntPtr hwnd);
    bool NavigateTo(IntPtr hwnd, string targetPath);
    bool TargetIsFolderOnly { get; } // 預設 false
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor); // 預設 true
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);
    bool RestoreFocus(IntPtr hwnd);
}
```

`TargetIsFolderOnly` 為 `true` 表示這個對話方塊的目標輸入框只能填資料夾——比如壓縮軟體的「解壓縮到」目標路徑——不像開啟/儲存對話方塊的檔案名稱輸入框那樣還能填具體檔案。宿主用它來判斷:如果使用者從搜尋結果裡選中的是一個檔案，需不需要在傳給 `NavigateTo` 之前先解析成它所在的資料夾，而不是把這個判斷留給 `NavigateTo` 自己——因為那個呼叫是在提升權限的 Hook 處理程序裡執行的，`File.Exists`/
`Directory.Exists` 在那裡沒辦法信任(使用者在非提升權限下對應的磁碟機，在那邊可能「不存在」)。如果目標輸入框本身就是能填具體檔案的，保持預設值 `false` 即可。

## `IInlineSearchAdapter`

把 Lertaro 搜尋列直接嵌入目標檔案對話方塊或檔案總管視窗(即使用者手冊裡說的「內嵌視窗」)，雙向保持選取狀態同步。

```csharp
interface IInlineSearchAdapter
{
    string Name { get; }
    bool IsFileExplorer { get; }   // 預設 false
    bool CanHandle(IntPtr hwnd, string className, string processName);
    bool CanTrigger(IntPtr focusedHwnd, string className);
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor); // 預設委派給 CanTrigger
    bool CanEnterActionsMode(IntPtr hwnd);
    string? GetSearchScope(IntPtr hwnd);
    bool ExecuteItem(IntPtr hwnd, string path, string searchInput);
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);
    IEnumerable<string> GetListItems(IntPtr hwnd);        // 可選
    void OnSelectionChanged(IntPtr hwnd, string path);    // 可選
    void OnSearchFinished(IntPtr hwnd, bool executed);    // 可選
}
```

`AdapterRect`(與 `IFileDialogAdapter` 共用)是一個簡單的 `{ Left, Top, Right, Bottom }` `int` 矩形。

## `IQuickNavigationProvider`

為快速導覽選單提供內容(通常是串接式選單)——見[熱鍵 → 快速導覽](../../user-guide/hotkeys#快速導覽-滑鼠)。選單該不該彈出由宿主決定，不是這個介面的職責:任何已被 `IInlineSearchAdapter`/`IFileDialogAdapter` 識別的視窗，觸發選單的工作已經有人做了，所以這個介面純粹是內容來源。

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

`GroupName`是顯示在這個 provider 自己根層級項目上方的分組標題，方便同時有多個快速導覽 provider
時區分各項目分別來自哪一個——跟 `IDynamicActionProvider.GroupName` 在動作選單裡的作用一樣。

`HeaderAction`(可選，預設 `null`)會在同一個根層級分組標題上加一個小按鈕——比如一個書籤類的
provider 可以用它做「新增目前資料夾」。回呼參數用的是 `GetMenuItems` 在根層級收到的同一個
`ISearchResult`;`HeaderActionTooltip` 設定這個按鈕的提示文字，`HeaderAction` 為空時會被忽略。巢狀的子選單(根層級以下的任意深度)沒有宿主繪製的標題列，所以 `HeaderAction` 的效果只到根層級為止
——想在子選單上做同樣的「+」按鈕，需要在該子選單的第一項裡回傳一個 `IsHeader = true` 的
`DynamicMenuItem`(見下文)，用它自己的 `OnExecute` 起同樣的作用。

`DynamicMenuItem` 與
[`IDynamicActionProvider`](./core-search-actions#idynamicactionprovider) 用的是同一個模型，包括子選單層級標題列用的 `IsHeader` 標記。
