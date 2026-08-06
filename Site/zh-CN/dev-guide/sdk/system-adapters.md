# 系统与对话框适配

这些接口让插件可以和*其他*窗口集成——文件资源管理器、原生文件选择对话框、第三方文件管理器——而不仅仅是 Lertaro 自己的搜索窗口。

## `IActivePathCollector`

从当前活动的前台窗口中提取"当前目录"，让 Lertaro 知道该把搜索范围限定在哪里(或者相对什么路径解析动作)。

```csharp
interface IActivePathCollector
{
    string Name { get; }
    string TargetName { get; }   // 目标应用/管理器的本地化名称
    bool CanHandle(string className);
    string? TryGetPath(
        IntPtr activeHwnd, string activeClassName,
        IntPtr windowHwnd, string windowClassName,
        string processName);
}
```

活动(获得焦点)的元素和它所在的窗口是分开传入的，因为很多文件管理器把实际路径放在子控件里(地址栏、树形视图的选中项)，而不是顶层窗口本身。

## `IFileDialogAdapter`

读取并驱动原生渲染的 Windows 打开/保存文件对话框，让 Lertaro 可以被嵌入其中(见下面的
[`IInlineSearchAdapter`](#iinlinesearchadapter))并保持双方同步。

```csharp
interface IFileDialogAdapter
{
    string Name { get; }
    bool CanHandle(IntPtr hwnd, string className, string processName);
    string? GetCurrentPath(IntPtr hwnd);
    bool NavigateTo(IntPtr hwnd, string targetPath);
    bool TargetIsFolderOnly { get; } // 默认 false
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor); // 默认 true
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);
    bool RestoreFocus(IntPtr hwnd);
}
```

`TargetIsFolderOnly` 为 `true` 表示这个对话框的目标输入框只能填文件夹——比如压缩软件的"解压到"
目标路径——不像 Open/Save 对话框的文件名输入框那样还能填具体文件。宿主用它来判断:如果用户从搜索结果里选中的是一个文件，需不需要在传给 `NavigateTo` 之前先解析成它所在的文件夹，而不是把这个判断留给 `NavigateTo` 自己——因为那个调用是在提升权限的 Hook 进程里执行的，`File.Exists`/
`Directory.Exists` 在那里没法信任(用户在非提升权限下映射的驱动器，在那边可能"不存在")。如果目标输入框本身就是能填具体文件的，保持默认值 `false` 即可。

## `IInlineSearchAdapter`

把 Lertaro 搜索栏直接嵌入目标文件对话框或文件资源管理器窗口(即用户手册里说的"内嵌窗口")，双向保持选中状态同步。

```csharp
interface IInlineSearchAdapter
{
    string Name { get; }
    bool IsFileExplorer { get; }   // 默认 false
    bool CanHandle(IntPtr hwnd, string className, string processName);
    bool CanTrigger(IntPtr focusedHwnd, string className);
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor); // 默认委托给 CanTrigger
    bool CanEnterActionsMode(IntPtr hwnd);
    string? GetSearchScope(IntPtr hwnd);
    bool ExecuteItem(IntPtr hwnd, string path, string searchInput);
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);
    IEnumerable<string> GetListItems(IntPtr hwnd);        // 可选
    void OnSelectionChanged(IntPtr hwnd, string path);    // 可选
    void OnSearchFinished(IntPtr hwnd, bool executed);    // 可选
}
```

`AdapterRect`(与 `IFileDialogAdapter` 共用)是一个简单的 `{ Left, Top, Right, Bottom }` `int` 矩形。

## `IQuickNavigationProvider`

为快速导航菜单提供内容(通常是级联菜单)——见[热键 → 快速导航](../../user-guide/hotkeys#快速导航鼠标)。菜单该不该弹出由宿主决定，不是这个接口的职责:任何已被 `IInlineSearchAdapter`/`IFileDialogAdapter` 识别的窗口，触发菜单的工作已经有人做了，所以这个接口纯粹是内容来源。

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

`GroupName`是显示在这个 provider 自己根层级条目上方的分组标题，方便同时有多个快速导航 provider
时区分各条目分别来自哪一个——跟 `IDynamicActionProvider.GroupName` 在动作菜单里的作用一样。

`HeaderAction`(可选，默认 `null`)会在同一个根层级分组标题上加一个小按钮——比如一个书签类的
provider 可以用它做"添加当前文件夹"。回调参数用的是 `GetMenuItems` 在根层级收到的同一个
`ISearchResult`;`HeaderActionTooltip` 设置这个按钮的提示文字，`HeaderAction` 为空时会被忽略。嵌套的子菜单(根层级以下的任意深度)没有宿主渲染的标题栏，所以 `HeaderAction` 的效果只到根层级为止
——想在子菜单上做同样的"+"按钮，需要在该子菜单的第一项里返回一个 `IsHeader = true` 的
`DynamicMenuItem`(见下文)，用它自己的 `OnExecute` 起同样的作用。

`DynamicMenuItem` 与
[`IDynamicActionProvider`](./core-search-actions#idynamicactionprovider) 用的是同一个模型，包括子菜单层级标题行用的 `IsHeader` 标记。
