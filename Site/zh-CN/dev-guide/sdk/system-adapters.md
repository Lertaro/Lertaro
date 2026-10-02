# 系统与对话框适配

本章节介绍 `Lertaro.PluginSdk` 中用于深度窗口停靠、活动目录提取，以及在 Windows 文件资源管理器、原生文件对话框与第三方文件管理器之间实现内嵌搜索集成的适配器接口。

这四类接口全部位于 `Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters`，并都派生自 `IPluginComponent`——宿主在**设置 → 插件**中列出的 `Name` 就来自它；这些接口自己都不声明 `Name`。

> [!NOTE]
> `IActivePathCollector`、`IFileDialogAdapter` 与 `IInlineSearchAdapter` 的实现会被宿主加载进**特权 Hook 辅助进程**，以便在与以管理员身份运行的窗口交互时跨越 Windows UIPI 隔离。这也正是它们的成员必须廉价且不可交互的原因：它们运行在低级键盘/鼠标钩子回调上，任何超过系统 `LowLevelHooksTimeout` 的停顿都会把钩子静默丢弃。

## 1. 已打开文件夹收集器 `IOpenedFolderCollector`

这份契约的只读那一半：报告目标管理器当前打开的全部文件夹，[**快速导航**](../../user-guide/hotkeys)以及内嵌搜索列表中的**当前打开的文件夹**分组就靠它供数据。

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

public readonly record struct OpenedFolder(string Path, IntPtr WindowHandle);

public interface IOpenedFolderCollector : IPluginComponent
{
    IReadOnlyList<OpenedFolder> GetOpenedFolders() => Array.Empty<OpenedFolder>();
}
```

适配器为每个打开的窗口返回一条记录，因此开了五个标签页的管理器会报告五个文件夹。注册表把这份列表**刻意带着重复原样**交下去——被两个收集器看到、或被同一个收集器看到两次的文件夹会出现两次；想要集合的调用方自己按路径去重。

## 2. 活动目录收集器 `IActivePathCollector`

`IActivePathCollector` **继承**自 `IOpenedFolderCollector`：一个能够说出某个具体窗口所属文件夹的收集器，通常同样会贡献已打开文件夹列表，而它只需实现继承来的默认实现就能免费得到。

从获得焦点的前台窗口提取活动工作目录，使 Lertaro 能够界定内嵌搜索的作用范围或解析相对路径：

```csharp
public interface IActivePathCollector : IOpenedFolderCollector
{
    string TargetName { get; }   // 目标管理器名称（如 "Directory Opus"、"Total Commander"）

    // 三个重载，一个问题比一个问题更粗。只有类名形式是强制实现的；其余两个默认转给它，
    // 因此还分辨不了窗口的收集器也能为所有调用方给出正确答案。
    bool CanHandle(string className);
    bool CanHandle(string windowClassName, string windowTitle) => CanHandle(windowClassName);
    bool CanHandle(IntPtr windowHwnd, string windowClassName, string processName) => CanHandle(windowClassName);

    string? TryGetPath(
        IntPtr activeHwnd, string activeClassName,   // 获得焦点的子控件
        IntPtr windowHwnd, string windowClassName,   // 它所属的顶层窗口
        string processName);
}
```

- 获得焦点的控件与父窗口分开传入，好让路径能从嵌套控件（地址栏、树视图）里读出来，而不只能取自整个窗口。
- 窗口认得出来、但那一刻无法解析它的文件夹时返回 `null`——这不算失败，宿主只是保留先前的作用范围。

## 3. 原生文件对话框适配器 `IFileDialogAdapter`

检查并控制 Windows 原生的打开 / 保存 / 浏览对话框：

```csharp
public interface IFileDialogAdapter : IPluginComponent
{
    bool CanHandle(IntPtr hwnd, string className, string processName);
    string? GetCurrentPath(IntPtr hwnd);
    bool NavigateTo(IntPtr hwnd, string targetPath);

    bool TargetIsFolderOnly => false;  // 目标的输入是否只接受文件夹（如压缩解压目录）
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor) => true;

    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);          // 卡片停靠到哪里

    // 摆放探针：对话框自己的目标字段在哪里、它的文件列表在哪里。内嵌卡片两个都读，
    // 用来决定把自己挂在字段下方、列表上方，还是下方放不下时放得下的地方。
    // 任一探针返回 false，宿主就退回 GetDockBounds。两者的默认回答都是“看不见那个控件”。
    bool TryGetTargetFieldBounds(IntPtr hwnd, out AdapterRect bounds) { bounds = default; return false; }
    bool TryGetFileListBounds(IntPtr hwnd, out AdapterRect bounds) { bounds = default; return false; }

    bool RestoreFocus(IntPtr hwnd);
}

public struct AdapterRect   // 物理像素
{
    public int Left, Top, Right, Bottom;
}
```

- **`TargetIsFolderOnly`**：为 `true` 时，若用户从搜索结果里选中了一个文件，宿主会在调用 `NavigateTo` 之前先解析出它所在的父文件夹。
- **`TryGetTargetFieldBounds` / `TryGetFileListBounds`**：只用于卡片摆放。定位器优先把卡片挂在对话框的目标字段下方，并把文件列表当作兜底锚点；两个都解析不出来的对话框，拿到的就是 `GetDockBounds` 的矩形。
- **`RestoreFocus`**：把键盘交还给对话框自己的编辑框。宿主在用户离开内嵌卡片时调用它（`Escape`，或卡片内容为空时再按一次呼出快捷键），因此它绝不能再激活别的东西。

## 4. 内嵌搜索适配器 `IInlineSearchAdapter`

把 Lertaro 的搜索卡片嵌入目标文件对话框或资源管理器窗口，并维持双向的选中状态同步：

```csharp
public interface IInlineSearchAdapter : IPluginComponent
{
    bool IsFileExplorer => false;      // 系统文件资源管理器为 true

    bool CanHandle(IntPtr hwnd, string className, string processName);

    // 只识别、不触发。默认转给 CanHandle；当窗口显然就是你支持的宿主、却又不能呼出卡片时
    // 才改写它——例如命令行或重命名编辑框正持有焦点，那里打字属于宿主，不属于 Lertaro。
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

    // > 0：选中变化稳定这么多毫秒后，宿主把焦点重新交给卡片自己的输入框，用于那些在镜像
    // 选中状态时会把焦点抢回去的宿主。0（默认）表示永不重新夺取。
    int SelectionSyncFocusReclaimDelayMs => 0;
}
```

- **`GetDockBounds`**：返回真正用于停靠的内容区域的物理边界。宿主用这个容器矩形来计算内嵌搜索框的尺寸与位置；适配器应当返回当前的资源管理器窗格或对话框内容区域，而不是无关的外层窗口——前提是这些边界能够解析出来。
- **`CanTrigger`** 是**每一次**击键都要过的闸门，所以它必须凭拿到的类名作答——钩子在那个位置上承担不起一次 UI Automation 往返。
- **`GetListItems`**：当前显示的各行的名称，用于选中状态镜像。返回空也没关系；若干受支持的管理器给自己的行返回空字符串，这正是宿主不单凭名称来识别某一行的原因。
- **`CanEnterActionsMode`**：`false` 会把该宿主的动作菜单整个去掉——右键、`Ctrl+O` 与 `→` 一律退场，而不是打开一个空面板。
- **`OnSearchFinished(hwnd, executed)`**：卡片关闭时调用，并告知是否真的执行了某个结果；为了显示卡片而压制过自身 UI（信息提示、重命名编辑框）的宿主，应当在此时把它们放回去。

## 5. 快速导航提供者 `IQuickNavigationProvider`

为[**快速导航级联菜单**](../../user-guide/hotkeys)贡献动态分组与条目：

```csharp
public enum MouseTriggerType { DoubleClick, MiddleClick }

public interface IQuickNavigationProvider : IPluginComponent
{
    string GroupName { get; }                                   // 根层级分组标题
    string IPluginComponent.Name => GroupName;                   // 是映射来的，不是手写的

    Action<ISearchResult>? HeaderAction => null;                 // 分组标题栏上的操作按钮（如 "+"）
    string? HeaderActionTooltip => null;                         // 该按钮的 ToolTip 提示

    bool CanProvide(ISearchResult result);
    IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu);
    void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd);

    // 无默认实现：必须实现它。菜单关闭时调用，用来释放菜单留下的分配物
    // （缓存的 shell CDS 流、原生图标句柄）。
    void ClearSession();
}
```

- **`HeaderAction`**：在根分组标题上追加一个操作按钮（例如书签提供者用它添加“固定当前文件夹”）。文件夹级联插件那个“把你所在的文件夹存下来”的 `+` 按钮就是这个成员。
- **`DynamicMenuItem.IsHeader`**：在嵌套子菜单中，返回 `IsHeader = true` 的条目会渲染成带操作按钮的可交互分组标题行。
- **`MouseTriggerType`**：命名能够打开该菜单的两种全局手势。其中哪几种生效是用户的设置，而不是提供者的决定——见[**快捷键 → 快速导航鼠标触发**](../../user-guide/settings/hotkeys-page)。

## 6. 注册表

宿主通过 `Lertaro.PluginSdk.Registries` 中的四个静态注册表查找适配器，插件的组件也正是借此到达 Hook 进程的：

| 注册表 | 成员 |
| :--- | :--- |
| `ActivePathCollectorRegistry` | `Register(IActivePathCollector)`、`GetCollectors()`、`GetAllCollectors()` |
| `FileDialogAdapterRegistry` | `Register(IFileDialogAdapter)`、`GetMatchingAdapter(hwnd, className, processName)`、`GetAdapters()`、`GetAllAdapters()` |
| `InlineSearchAdapterRegistry` | `Register(IInlineSearchAdapter)`、`GetMatchingAdapter(hwnd, className, processName)`、`GetAdapters()`、`GetAllAdapters()` |
| `OpenedFolderCollectorRegistry` | `GetOpenedFolders()` —— 把每个已启用收集器所报告的内容依次拼接起来；重复项是**有意保留**的，而抛出异常的那个收集器会被跳过，这样一个坏掉的文件管理器就无法拖垮整份快照 |

前三个各自暴露一个由宿主赋值的 `Func<T, bool> FilterFunc`：宿主把它收窄到用户已启用的那些组件，因此 `GetCollectors()` / `GetAdapters()` 返回过滤后的视图，而 `GetAllCollectors()` / `GetAllAdapters()` 返回全部注册项。插件绝不给它赋值。匹配顺序就是注册顺序，第一个 `CanHandle` 回答 `true` 的适配器拥有该窗口——这也是为什么一个通用的 `#32770` 对话框适配器不能去认领已被某个专门适配器覆盖的窗口。对话框注册表在此之上还多加了一道否决：某个适配器认领窗口之后，如果该窗口的标题位于屏蔽名单里，查找会返回 `null` 而不是继续落到下一个适配器，于是没有任何适配器为那个窗口服务。
