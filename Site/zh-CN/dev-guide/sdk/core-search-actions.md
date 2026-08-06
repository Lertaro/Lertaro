# 核心检索与动作

## `IPluginComponent` 与 `IPlugin`

所有插件组件（包括插件入口）都需要继承自 `IPluginComponent`。该接口提供了组件的名称和描述：

```csharp
interface IPluginComponent
{
    string Name => GetType().Name;       // 组件的显示名称，默认返回具体类名
    string Description => string.Empty;  // 组件的功能描述，宿主会在配置/设置界面中作为 ToolTip 提示气泡展示
}
```

每个插件都必须实现 `IPlugin` 接口（继承自 `IPluginComponent`）作为插件的主入口，另外再加上其他按需实现的接口：

```csharp
interface IPlugin : IPluginComponent
{
}
```

## 贡献搜索结果

### `ISearchableItemProvider`

返回一份完整的、可缓存的条目列表，供索引使用——适合内容是静态的或者枚举较慢、但不会随每次按键变化的场景(例如开始菜单快捷方式、书签列表)。

```csharp
interface ISearchableItemProvider : IPluginComponent
{
    bool EnableAlias { get; } // 默认 true
    event Action? ItemsChanged;
    IEnumerable<SearchableItem> GetSearchableItems();
}
```

### `IInstantResultProvider`

每次按键都会运行一次，直接返回结果——适合像计算器、URL 快捷方式这类"结果形状由查询本身决定"的内容，而不是需要提前建好索引的东西。

```csharp
interface IInstantResultProvider : IPluginComponent
{
    IEnumerable<InstantResultItem> GetInstantResults(string query);
    bool[]? GetHighlightMask(string text, string query); // 可选的匹配高亮
}
```

`GetInstantResults`只有同步这一种形态——没有异步/可取消令牌的重载。如果你的数据需要走一次网络请求
(比如翻译文字、拉取搜索引擎的联想建议)，做法是:立刻返回一个占位结果项，用 `Task.Run` 在后台去真正干活，拿到结果后缓存起来，再调用 `SearchRefreshService.RefreshIfMatches`(参见[宿主服务](./services))
让宿主把当前 query 会命中你缓存的那些搜索重新跑一遍——可以参考 WebSearch 插件的建议拉取逻辑
(`Plugins/WebSearch/WebSearchInstantProvider.cs`)作为完整示例。

### `IAliasProvider`

为非 ASCII 文本生成额外的可搜索字符串——中文文件名的拼音别名就是这样实现的(见
[PinyinAlias](../examples#pinyinalias-中文文件名拼音别名))。

```csharp
interface IAliasProvider
{
    string Name { get; }
    bool CanHandle(string text);
    IReadOnlyList<(char Start, char End)> InputRanges { get; }
    IReadOnlyList<(char Start, char End)> OutputRanges { get; }
    IEnumerable<string> GetAliases(string text);

    int Version { get; } // 默认 1
    int[]? MapAliasToSourceIndices(string text, string alias); // 默认 null
    void GetAliasesUtf8(string text, AliasByteSink dest); // 默认:内部转调 GetAliases
    IEnumerable<string> GetQueryForms(string term); // 默认:不返回任何形式
}
```

`InputRanges` 和 `OutputRanges`没有默认实现——每个 provider 都必须自己声明。`InputRanges` 是这个
provider 转写的**源**字符范围(比如拼音对应的是 CJK 表意文字区块);`OutputRanges` 是它生成的别名所使用的字符范围(比如拼音就是小写 `a`-`z`)。宿主会用这两个范围,把一个同时混用了某个 provider
自己的输入、输出两种字母表的查询词(比如用"大cj"匹配"大长今")切分成一段按候选项原文匹配的字面片段,和一段按这个 provider 生成的别名匹配的别名语法片段,而不用去猜测"是不是非 ASCII"。

`Version`、`MapAliasToSourceIndices`、`GetAliasesUtf8` 都有默认实现——绝大多数 provider 都不需要碰它们:

- **`Version`**:当这个 provider 对同一个输入的输出可能发生变化时(算法修复、新增规则、更新了数据表)就把它加一。索引靠这个值判断这个 provider 之前生成的别名已经过期，需要重新生成。
- **`MapAliasToSourceIndices`**:把命中别名的位置(比如命中了哪几个拼音字母)映射回原始文本上用于高亮，否则因为查询词从没在未转写的原文里逐字出现过，就会完全高亮不出来。返回 `null`(默认值)表示这个别名不是这个 provider 针对这段文本生成的，或者不支持映射——宿主会把这种情况当成
  "这个 provider 高亮不了"，而不是错误。
- **`GetAliasesUtf8`**:宿主批量建索引时用的字节原生版本，别名最终是按 UTF-8 字节存储的。默认实现就是内部转调 `GetAliases`，所以现有的 provider 不用改也能正常工作；只有当你的 provider 生成的别名量特别大、字符串分配开销确实成为实际瓶颈时，才需要重写它来完全跳过字符串具现化。
- **`GetQueryForms`**:`GetAliases` 的查询侧对应版本——把用户输入的某一个查询词，改写成这个
  provider 自己的别名所使用的那种带分隔结构的形式，这样一段用户按普通字符连续打出来的查询词，依然能保留宿主本身理解不了的内部结构(比如拼音的音节边界，这正是阻止查询跨越两个不相关音节误匹配的关键)。默认不返回任何形式，意味着"这个词根本不在我的字母表里"——这正是防止一个这个
  provider 无法表达的查询词，误命中本不该命中的别名。每条查询里每个词只会调用一次，不会按候选项逐个调用，所以在这里做一些实际工作是划算的——但每返回一种形式，就会多出一个要拿去跟每个候选项比对的备选项，所以返回得越多，代价也越大。

### `IQueryTokenProvider`

从查询里认领一个尾部 token(例如 `report :size`、`report ::"hello world"` 或 `report ::hello\ world`)，并对已经匹配好的结果列表做变换——排序、过滤，或者在一次普通搜索之上做其他组合处理。支持在双引号 (`"..."`)、单引号 (`'...'`) 或使用转义空格 (`\ `) 的 Token 中包含空格。

```csharp
interface IQueryTokenProvider : IPluginComponent
{
    bool CanHandle(string token);
    Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results);
}
```

## 结果上的动作

### `IActionProvider`

插件用来暴露静态和动态动作的容器接口:

```csharp
interface IActionProvider
{
    IEnumerable<ISearchResultAction> GetActions();
    IEnumerable<IDynamicActionProvider> GetDynamicActionProviders();
}
```

### `ISearchResultAction`

一个单独的静态动作(例如"复制路径")，出现在动作菜单或快速窗口的动作热键里:

```csharp
interface ISearchResultAction : IPluginComponent
{
    string GroupName { get; }
    string DisplayName { get; }
    string? Hotkey { get; }              // 可选的默认热键
    IReadOnlyList<string>? Keywords { get; }
    IReadOnlyList<string>? Parameters { get; }
    ImageSource Icon { get; }
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool CanExecute(IReadOnlyList<ISearchResult> selection);
    void Execute(IReadOnlyList<ISearchResult> selection, IPluginSearchWindow window);
}
```

### `IDynamicActionProvider`

在运行时构建菜单项，而不是返回一份固定列表——真正的 Windows Shell 右键菜单(含级联子菜单)之所以能出现在 Lertaro 的动作菜单里，用的就是这个机制；参见
[ShellMenuActionProvider](../examples#coreextensions-——-动作与-shell-右键菜单)。

```csharp
interface IDynamicActionProvider
{
    string GroupName { get; }
    int? Priority { get; }
    IReadOnlyList<string>? Keywords { get; }
    IReadOnlyList<string>? Parameters { get; }
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    void Init();
    bool CanProvide(IReadOnlyList<ISearchResult> selection);
    IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> selection, IntPtr hMenu);
    IEnumerable<(string Hotkey, Action Execute)> GetHotkeyActions(IReadOnlyList<ISearchResult> selection);
    void ExecuteCommand(IReadOnlyList<ISearchResult> selection, uint commandId, IntPtr ownerHwnd);
    void ClearSession();
}
```

`Init()`由宿主在整个进程生命周期内最多调用一次——在任何一次动作菜单真正打开之前，即
`CanProvide`/`GetMenuItems` 被调用之前触发。"最多一次"这个保证由宿主负责，具体实现不需要自己防止重复调用。适合用来做那种值得抢占先机的慢速一次性初始化(比如预热一个原生工作线程)，而不是和自己的 `CanProvide`/`GetMenuItems` 调用(紧随其后、没有任何提前量)抢时间——不能阻塞，真正耗时的工作要放到后台线程里做。默认实现是空操作。

`Priority` 决定这个 provider 在动作菜单的动态(按 provider 分组)分组里排在哪——数值越小越靠前，默认 `0`。不过这只是个兜底信号:用户可以在[设置 → 通用 → 完整搜索窗口](../../user-guide/settings/general#完整搜索窗口)里手动拖拽/调整这些分组的顺序，用户已经手动排过序的分组会保持在那个位置，不再受 `Priority` 影响。

## 支持模型

- **`SearchableItem`** / **`InstantResultItem`** —— 两者共有 Title、Description、IconData、IconColor、
  ActionType(`"Copy"` / `"Execute"` / `"None"`)、ActionArgument、TabCompletion，以及 `HBitmapIcon`
  (预先准备好的 GDI 位图句柄，设置后优先级高于 IconData——宿主会接管所有权，用完自己调用
  DeleteObject，所以交出去之后不要再复用或释放这个句柄；具体用法可以参考窗口切换器插件自己的窗口内容截图实现)。`SearchableItem` 还额外多了 `OnExecute`(直接调用委托)和 `ResultKind`
  (覆盖结果类型，比如 `"Application"`/`"File"`)。
- **`DynamicMenuItem`** —— Text、CommandId、IsSeparator、HasSubMenu、SubMenuHandle、IsDisabled、
  HBitmapItem、OnExecute、ShortcutHint、IsHeader。`IsHeader` 把这一项渲染成不可点击的分组标题行
  (就像快速导航子菜单自己的分组名一样)，而不是普通的一行——Text 就是标题文字，如果同时设置了
  `OnExecute`，标题行末尾会出现一个小按钮来调用它;`IsHeader` 为 true 时其余字段都会被忽略。这是
  [`IQuickNavigationProvider.HeaderAction`](./system-adapters#iquicknavigationprovider)在子菜单深度上的等价物，`HeaderAction` 本身只覆盖根层级。
- **`SearchWindowType`** 枚举 —— `Main`、`Quick`、`Inline`。可以让动作或提供者根据当前显示在[用户手册](../../user-guide/getting-started#三种窗口)里说的三种窗口的哪一种而表现不同。
