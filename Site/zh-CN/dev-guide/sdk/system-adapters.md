# 系统与对话框适配

本章节介绍 `Lertaro.PluginSdk` 中用于与外部前台窗口（Windows 文件资源管理器、原生文件打开/保存对话框、第三方文件管理器等）进行深度挂载、目录探测与内嵌搜索交互的适配器接口。

> [!NOTE]
> 本页面中的 `IActivePathCollector`、`IFileDialogAdapter` 与 `IInlineSearchAdapter` 组件会被宿主加载至**特权 Hook 辅助进程**中执行，以确保能够跨越 UIPI 隔离与以管理员身份启动的文件窗口安全交互。

## 1. 活动目录收集器 `IActivePathCollector`

用于从当前获得焦点的活动窗口中精准提取“当前工作目录路径”，使 Lertaro 能够正确界定内嵌搜索的作用范围或执行相对路径定位：

```csharp
namespace Lertaro.PluginSdk;

public interface IActivePathCollector
{
    string Name { get; }
    string TargetName { get; }   // 目标管理器名称（如 "Directory Opus"、"Total Commander"）
    bool CanHandle(string className);
    string? TryGetPath(
        IntPtr activeHwnd, string activeClassName,
        IntPtr windowHwnd, string windowClassName,
        string processName);
}
```

- 将获得焦点的子控件（`activeHwnd`）与顶层主窗口（`windowHwnd`）分开传入，以便适配器从深层地址栏或树形控件中抓取路径。

## 2. 原生文件对话框适配器 `IFileDialogAdapter`

用于读取并驱动 Windows 原生文件打开/保存对话框（Common Item Dialog 或经典对话框）：

```csharp
public interface IFileDialogAdapter
{
    string Name { get; }
    bool CanHandle(IntPtr hwnd, string className, string processName);
    string? GetCurrentPath(IntPtr hwnd);
    bool NavigateTo(IntPtr hwnd, string targetPath);
    bool TargetIsFolderOnly => false;  // 目标是否仅接受文件夹（如压缩解压目录）
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor) => true;
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);
    bool RestoreFocus(IntPtr hwnd);
}
```

- **`TargetIsFolderOnly`**：当设为 `true` 时，若用户在搜索结果中选中了一个具体文件，宿主会在调用 `NavigateTo` 前自动将其解析为其所在的父级文件夹，避免输入框格式错误。
- **`AdapterRect`**：包含 `{ Left, Top, Right, Bottom }` 物理像素边界。

## 3. 内嵌搜索适配器 `IInlineSearchAdapter`

将 Lertaro 的搜索栏直接挂载至目标窗口或文件对话框内部，实现实时的内嵌搜索与选中状态双向同步：

```csharp
public interface IInlineSearchAdapter
{
    string Name { get; }
    bool IsFileExplorer => false;      // 是否为系统文件资源管理器
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

## 4. 鼠标快速导航提供者 `IQuickNavigationProvider`

为鼠标双击或中键呼出的[**快速导航级联菜单**](../../user-guide/hotkeys#3-快速导航鼠标触发)贡献动态分组与条目：

```csharp
public interface IQuickNavigationProvider
{
    string GroupName { get; }           // 根层级分组标题
    Action<ISearchResult>? HeaderAction => null; // 分组标题栏尾部的附加操作按钮（如 "+" 按钮）
    string? HeaderActionTooltip => null;// 标题栏操作按钮的 ToolTip 提示
    bool CanProvide(ISearchResult result);
    IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu);
    void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd);
    void ClearSession() { }
}
```

- **`HeaderAction`**：可在根层级分组标题右侧添加小图标按钮（例如书签提供者利用此按钮实现“添加当前文件夹”）。
- **`DynamicMenuItem.IsHeader`**：在嵌套子菜单中，可通过返回带有 `IsHeader = true` 的菜单项实现子菜单内部带有操作按钮的分组标题行。
