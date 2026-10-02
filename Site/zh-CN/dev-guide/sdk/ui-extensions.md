# 界面与预览扩展

本章节介绍 `Lertaro.PluginSdk` 中用于扩展主搜索窗口侧边栏、追加自定义表格列、提供快速面板动态工作区标签页、构建 QuickLook 文件预览器与缩略图提取器，以及发布 WPF 主题与 i18n 语言包的接口。

这些接口全部位于 `Lertaro.PluginSdk.Abstractions.Plugins` 命名空间下（预览类提供者位于 `…Abstractions.Plugins.Preview`），并且每一个都派生自 `IPluginComponent`，后者提供宿主在**设置 → 插件**中显示的 `Name`。

## 1. 侧边栏筛选分类提供者 `ISidebarFilterProvider`

向主搜索窗口的左侧侧边栏注入自定义筛选分类：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface ISidebarFilterProvider : IPluginComponent
{
    IEnumerable<SidebarFilterGroup> GetFilterGroups();

    // 排序权重；值越小越先渲染。
    int SortOrder => 100;
}

public class SidebarFilterGroup
{
    // 宿主认识的可选稳定分组标识，用于已知分组（例如内置的结果类型筛选对应 "Type"）。
    // 分组完全由插件自定义时留空即可。
    public string Id { get; set; } = string.Empty;
    public string Header { get; set; } = string.Empty;
    public List<SidebarFilterItem> Items { get; set; } = new();

    // 该分组内是否允许同时有多个条目生效。
    public bool AllowMultiSelect { get; set; }
}

public class SidebarFilterItem
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    // 两条图标路径，都能感知主题：IconData 是按当前主题文字颜色绘制的字形，
    // IconKey 指向宿主已经拥有的资源。两者都留 null 即不显示图标。
    public string? IconData { get; set; }
    public string? IconKey { get; set; }

    // 结果必须满足该委托，此条目才算命中。默认“永不命中”，
    // 因此从不设置它的条目会显示出来，却永远选不到任何内容。
    public Func<ISearchResult, bool> MatchPredicate { get; set; } = _ => false;
}
```

分组与条目是可变的 class，而不是 record：填好你需要的属性，其余保持默认值即可。

## 2. 结果表格自定义列提供者 `IResultColumnProvider`

为完整搜索窗口的“详细信息”多列表格视图追加自定义数据列（例如媒体时长、代码行数、Git 分支）。提供者一次性描述自己的各列，并按需回答每个单元格的值：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IResultColumnProvider : IPluginComponent
{
    IEnumerable<ResultColumnDefinition> GetColumns();
    string GetCellValue(ISearchResult result, string columnId);
}

public class ResultColumnDefinition
{
    public string ColumnId { get; set; } = string.Empty;
    public string HeaderText { get; set; } = string.Empty;
    public double Width { get; set; } = 120;

    // 可选：对不适用的结果隐藏该列。
    public Func<ISearchResult, bool>? VisibilityPredicate { get; set; }

    // 可选：点击表头时的自定义排序。x < y 时为负，x > y 时为正。
    public Func<ISearchResult, ISearchResult, int>? SortComparer { get; set; }

    // 可选：在完整窗口中双击该列单元格时触发。不设置它，双击该单元格
    // 就与双击这一行上任何其他位置的行为一致。
    public Action<ISearchResult>? OnDoubleClick { get; set; }
}
```

`GetCellValue` 在列表渲染期间被调用，因此必须足够廉价；请交回预先算好的值或读取缓存，而不是去碰磁盘。

## 3. 快速面板标签页提供者 `IQuickPanelTabProvider`

为[**快速面板**](../../user-guide/settings/quick-panel)贡献一个动态工作区标签页：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IQuickPanelTabProvider : IPluginComponent
{
    // 此刻要展示的条目。每次面板被呼出时都会调用。
    Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default);
}
```

这唯一的方法就是全部契约——没有拖拽接收、重排序或动作上下文要去实现。

- `CancellationToken` 会在面板关闭时被取消。只有该标签页自己的列表观察它；你的插件其他一切都不会因此改变。
- 数据源知道 `ISearchResult` 的 `Metadata.Modified` 时就把它填上，因为默认的“最新在前”排序用的就是它。保持默认值，条目就会按你返回时的顺序排列。
- 什么都不返回的提供者不会得到标签页，也没有任何配置项为此存在。
- 标签页随插件存在而存在，这与用户必须自己添加的文件夹不同。它可以在标签条上关闭，并在**设置 → 快速面板**里重新打开，这与在**设置 → 插件**里禁用该组件是两个不同的问题（后者会让它彻底不再加载）。

## 4. 文件预览与缩略图

### 自定义文件预览提供者 `IFilePreviewProvider`

在 QuickLook 面板内渲染预览，用户通过 `Alt+P` 或在可预览的行上点击鼠标中键打开该面板（见[**动作菜单与预览**](../../user-guide/actions-and-preview)）：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins.Preview;

public interface IFilePreviewProvider : IPluginComponent
{
    // 只用于打破平手。先应用用户配置的提供者顺序（设置 → 通用 → 预览与缩略图），
    // Priority 只在这一顺序之内排序，值大的在前。
    int Priority => 0;

    bool CanPreview(string path, bool isDir);
    UIElement CreatePreview(string path, bool isDir);

    // 为 true 表示提供者挂载的是它自己的外部窗口，而不是交回 WPF 内容让面板排布
    // （QuickLook 桥接插件就是这种）。
    bool RendersExternally => false;
}
```

#### 预览生命周期与复用契约

当你的**提供者**实现下面第一个契约，或你返回的 `UIElement` 实现第二个时，宿主会对预览生命周期做进阶优化：

- **`IPreviewSessionAware`** —— 强转的对象是**提供者**，而不是它返回的那个控件：`void EndPreviewSession();`。提供者拥有的是一个真正的外部窗口（一个 `HwndHost`、一个原生 `IPreviewHandler` 及其 `prevhost` 代理进程），而不仅仅是进程内的控件，因此所属窗口关闭时它就会被通知结束会话——而对在进程内渲染的提供者来说，只有到预览面板隐藏或整个会话结束时才收到通知。没有它，宿主留下的窗口将没有任何东西指向它。
- **`IReusablePreview`** —— 强转的对象是返回的那个元素：`bool TrySetTarget(string path, bool isDir);`。当用户用上/下方向键在同类文件之间移动时，宿主会请求同一个控件就地更换目标，而不是销毁并重建它，正是这一点消除了闪烁。新目标不适合这个实例时返回 `false`，宿主退回构建一个全新的预览。
- **`IReceivesPreviewPanelBounds`** —— `void OnPreviewPanelBoundsAvailable(int left, int top, int width, int height);`。承载自己那个跨进程窗口的提供者需要知道面板所占的矩形，才能把自己的窗口挂到它之下或在其中定位；实现这个契约，矩形一旦确定就会被交给你。

### 自定义缩略图提供者 `IThumbnailProvider`

为没有原生 Shell 处理器的格式（`.blend`、`.psd`、`.dwg`）提取缩略图：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins.Preview;

public interface IThumbnailProvider : IPluginComponent
{
    // 与预览同一条规则：先由用户配置的缩略图提供者顺序决定，Priority 只在该顺序之内排序。
    int Priority => 0;

    bool CanProvideThumbnail(string path, bool isDir);

    // 同步方法，因为它运行在结果列表的渲染路径上——请保持快速。
    // 你不需要自己记住结果：宿主会缓存你返回的内容（物理条目或虚拟条目按路径记，
    // 其余按扩展名记）。由此有两点：`size` 是宿主自己从 Shell 图像列表里取的，
    // 别指望它是某个特定数值；而且提供者根本不会被问到目录。
    ImageSource? GetThumbnail(string path, int size);
}
```

## 5. 主题与多语言

### 主题提供者 `IThemeProvider`

贡献配色方案与 WPF 资源字典：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IThemeProvider : IPluginComponent
{
    IEnumerable<ITheme> GetThemes();
}
```

```csharp
namespace Lertaro.PluginSdk.Abstractions;   // 注意：主题接口本身在上一层命名空间

public interface ITheme
{
    string Id { get; }
    string DisplayName { get; }
    bool IsDark { get; }
    ResourceDictionary GetResources();

    // 小于 1.0 时，经由宿主分层表面辅助方法构建的窗口会成为分层的半透明窗口，其圆角需要
    // 自己绘制并裁剪；等于 1.0 时窗口保持不透明，由窗口管理器负责圆角，并保住 ClearType。
    // 这个选择在窗口的构造函数里只做一次，因为 AllowsTransparency 在句柄存在之后就无法改变。
    // 目前它作用在两个通知窗口上；窗口还在屏幕上时切换主题并不会重建它。
    double WindowOpacity => 1.0;
}
```

一个提供者可以贡献任意数量的主题，每个主题各自携带自己的明/暗标志，而不是由提供者对外暴露一个暗色变体。

### 本地化提供者 `ITranslationProvider`

动态提供翻译字典：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface ITranslationProvider : IPluginComponent
{
    // 该提供者能够服务的语言代码，以便宿主在加载任何东西之前就把它们列进设置界面。
    // 默认为空，含义是“按被问到的内容自行发现”。
    IReadOnlyList<string> SupportedCultures => Array.Empty<string>();

    IReadOnlyDictionary<string, string> GetTranslations(string cultureName);
}
```
