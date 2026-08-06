# 界面与预览扩展

## 结果展示

### `ISidebarFilterProvider`

给结果侧栏添加分类过滤分组(例如日期区间或文件大小档位)。

```csharp
interface ISidebarFilterProvider
{
    int SortOrder { get; } // 默认 100;数值越小越靠前渲染
    IEnumerable<SidebarFilterGroup> GetFilterGroups();
}
```

`SidebarFilterGroup` 有一个 `Header`、一个 `AllowMultiSelect` 开关(默认 `false`;打开后这个分组允许同时选中多项,用 OR 组合——如果分组里的选项只在单选时才有意义(比如互相重叠/累进的日期区间),就不要打开它),以及一份 `SidebarFilterItem` 列表(Id、DisplayName、可选图标，以及一个可选的、对当前结果列表做异步过滤的 `FilterPredicate`)。宿主会在分组有选中项时自动显示一个清空按钮,
所以 provider 不需要自己维护一个"全部"/"任意"伪选项。

### `IResultColumnProvider`

给结果表格视图注入额外的列(文件大小、修改日期、自定义元数据等等)。

```csharp
interface IResultColumnProvider
{
    IEnumerable<ResultColumnDefinition> GetColumns();
    string GetCellValue(ISearchResult result, string columnId);
}
```

`ResultColumnDefinition` 携带列 id、表头文字、宽度，以及可选的 `VisibilityPredicate`/
`SortComparer` 委托。

## 快速面板

### `IQuickPanelTabProvider`

给[快速面板](../../user-guide/settings/quick-panel)贡献一整个标签——那个停靠在前台窗口上的浮动面板。标签以组件命名，里面装一份清单，条目由宿主用它自己的结果行渲染，所以图标、打开、缩略图和动作菜单都是白送的。CoreExtensions 自带五个：收藏夹、历史记录、Windows 历史记录、上次目录和最近文件。

```csharp
interface IQuickPanelTabProvider : IPluginComponent
{
    Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default);
}
```

是一个标签，而不是塞进别人标签里的一个分组：提供器给出的是一整份清单，它和某个工作区收集的文件夹是正交的，所以它跟那些文件夹并排放，而不必被逐个勾进每一个工作区。

`GetEntriesAsync()` 在面板每次被呼出时调用，并且返回的是一份完整结果而不是流式的：面板要把条目当作一个**整体**来排序和截断(最新在前，且最多多少条)，所以它没法只显示其中一半而不在每次新条目到达时重排。这并不会带来延迟——每个标签都在各自的任务上加载，面板在第一个到达时就打开，所以一个需要去慢慢找的提供器只会拖慢自己那个标签。但仍然请遵守令牌：面板关闭时它会被取消。

来源知道修改时间的话，就填进 `ISearchResult.Metadata` 的 `Modified`——默认的「最新在前」会用它，没有修改时间的条目则维持你返回时的顺序。什么都没返回的提供器不会有标签；抛异常的提供器只赔上自己这一个标签，不影响其他。

标签默认以缩略图平铺打开，除非用户在设置 → 快速面板 → 插件标签里为它勾上**以列表显示**；面板自己标题栏上的视图开关在面板打开期间仍然可以覆盖它。用 **×** 关闭一个标签和在设置 → 插件里禁用该组件是刻意区分开的两件事：前者只是把它移出标签栏(在同一个页面上勾回来即可)，后者则让它压根不再加载。宿主用组件 id 作为稳定 Key 来记住关闭状态和显示方式，所以插件被关掉期间关闭的标签，插件回来时依然是关着的。

## 预览与缩略图

### `IFilePreviewProvider`

在 QuickLook 预览面板里渲染自定义的 WPF `UIElement`(见[动作菜单与预览 → QuickLook 预览](../../user-guide/actions-and-preview#quicklook-预览))，用于你想特殊处理的文件类型。

```csharp
interface IFilePreviewProvider
{
    string Name { get; }
    int Priority { get; } // 默认 0;数值越大越先运行
    bool CanPreview(string path, bool isDir);
    UIElement CreatePreview(string path, bool isDir);
    bool RendersExternally { get; } // 默认 false
}
```

`Priority`只是*默认*的顺序——用户可以在 设置 → 通用 →
[预览与缩略图](../../user-guide/settings/general#预览与缩略图)里自由调整各个提供者的顺序(包括相对于你的这个 provider)，这个用户配置会覆盖 `Priority` 返回的值。不要假设你的 provider 声明的优先级就是它实际运行的顺序。

两个可选的配套接口可以进一步优化预览行为:

- **`IPreviewSessionAware`** —— 如果预览提供者自身持有开销较大的进程外资源(托管的原生处理程序、文件锁)，就在预览提供者本身上实现这个接口;`EndPreviewSession()` 只在整个预览会话结束时调用一次，而不是每次切换预览目标都调用。唯一的例外:如果这个 provider 的 `RendersExternally`
  为 true，宿主会在每次从它切换走的时候都调用一次，不只是会话真正结束的时候——见下文。
- **`IReusablePreview`** —— 如果 `CreatePreview` 返回的 `UIElement` 能够重新指向一个新文件，而不需要从头重建，就在它上面实现这个接口:`TrySetTarget(path, isDir)` 返回 `true` 表示已经原地处理好了变更，返回 `false` 则告诉宿主需要重新构建一个新的预览。

`RendersExternally` 适用于真正的预览内容渲染在一个独立的、由外部管理的窗口里、而不是
`CreatePreview` 返回的那个 `UIElement` 上的场景——比如把文件整个交给另一个应用程序去处理。当胜出的 provider 设置了这个属性，宿主会隐藏自己的预览面板，而不是显示 `CreatePreview` 的内容(反正也不会真的显示出来，所以可以随便返回一个占位用的空内容)。配合 **`IReceivesPreviewPanelBounds`**
使用，可以拿到宿主自己那个预览面板本该占据的屏幕矩形(物理像素)，这样外部窗口就能被摆到那个位置，而不是随便出现在别的地方:

```csharp
interface IReceivesPreviewPanelBounds
{
    void OnPreviewPanelBoundsAvailable(int left, int top, int width, int height);
}
```

内置的(实验性)QuickLook 桥接插件就是一个真实例子:它通过命名管道探测一个外部的
[QuickLook](https://github.com/QL-Win/QuickLook) 应用，如果能连上，就把它的窗口停靠到宿主面板原本的位置，覆盖所有文件/文件夹——具体的用户可见行为见[动作菜单与预览 → 通过 QuickLook 的外部预览](../../user-guide/actions-and-preview#通过-quickLook-的外部预览可选)。注意这和 Lertaro
自己内置的预览面板是两回事——本代码库和文档里也习惯把那个内置面板非正式地称为"QuickLook"。

### `IThumbnailProvider`

覆盖匹配结果显示的图标/缩略图。

```csharp
interface IThumbnailProvider : IPluginComponent
{
    int Priority { get; } // 默认 0;数值越大越先运行
    bool CanProvideThumbnail(string path, bool isDir);
    ImageSource? GetThumbnail(string path, int size);
}
```

跟上面 `IFilePreviewProvider.Priority` 的说明一样:这只是默认顺序,用户可以在 设置 → 通用 →
[预览与缩略图](../../user-guide/settings/general#预览与缩略图)里覆盖它(这两种 provider 的排序列表在同一个标签页里)。

## 主题与本地化

### `IThemeProvider` / `ITheme`

注册一个或多个自定义 WPF 资源字典，作为可选主题(显示在**设置 → 通用 → 界面主题**里)。

```csharp
interface IThemeProvider
{
    string Name { get; }
    IEnumerable<ITheme> GetThemes();
}

interface ITheme
{
    string Id { get; }
    string DisplayName { get; }
    bool IsDark { get; }
    double WindowOpacity { get; } // 默认 1.0
    ResourceDictionary GetResources();
}
```

### `ITranslationProvider`

为给定文化提供界面字符串——可以是插件自己的界面文本，也可以像 `PinyinAlias` 那样，仅仅是它自己的显示名称。参见[插件示例](../examples)了解一个把这个接口和另一个不相关接口实现在同一个类上的插件。

```csharp
interface ITranslationProvider
{
    string Name { get; }
    IReadOnlyList<string> SupportedCultures { get; } // 例如 "zh-CN"、"en-US"
    IReadOnlyDictionary<string, string> GetTranslations(string cultureName);
}
```

`TranslationService.LoadEmbeddedTranslations`(见[宿主服务](./services))是用内嵌在插件 DLL 里的 JSON 文件支撑这个接口的标准做法。
