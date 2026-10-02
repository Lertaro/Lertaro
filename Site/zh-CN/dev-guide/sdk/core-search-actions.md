# 核心检索与动作

本章节详细介绍 `Lertaro.PluginSdk` 中用于贡献搜索数据源、即时计算答案、非 ASCII 别名转写引擎、查询后缀 Token 处理器以及静态/动态上下文动作菜单的核心接口与数据结构。

## 1. 基础组件规范 `IPluginComponent` 与 `IPlugin`

所有插件扩展组件均直接或间接继承自 `IPluginComponent`，用于向宿主声明组件的元数据：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IPluginComponent
{
    string Name => GetType().Name;      // 组件显示名称（默认取类名）
    string Description => string.Empty; // 功能描述，在设置界面中作为 ToolTip 提示气泡呈现
}

public interface IPlugin : IPluginComponent
{
    // 插件主程序集入口点标识。两个 Website 成员都是可选的：
    // WebsiteUrl 为 null 时，设置卡片完全不显示链接。
    string? WebsiteUrl => null;
    string? WebsiteLabel => null;
}
```

## 2. 贡献搜索结果

### 静态可缓存条目源 `ISearchableItemProvider`

适用于内容相对静态、枚举耗时但不需要随每次击键实时变化的场景（例如：开始菜单快捷方式、浏览器书签、系统控制面板项等）。

```csharp
public interface ISearchableItemProvider : IPluginComponent
{
    bool EnableAlias => true;           // 是否允许对此数据源应用拼音等别名转写
    event Action? ItemsChanged;         // 当数据源发生变动时触发，通知宿主重新拉取并更新索引
    IEnumerable<SearchableItem> GetSearchableItems();
}
```

### 动态即时计算源 `IInstantResultProvider`

在用户每次敲击键盘时即时触发，适合形态由查询字符串本身决定的结果（例如：数学计算器、进制转换、环境变量展开、网页即时跳转等）。

```csharp
public interface IInstantResultProvider : IPluginComponent
{
    IEnumerable<InstantResultItem> GetInstantResults(string query);
    bool[]? GetHighlightMask(string text, string query) => null; // 自定义匹配高亮掩码

    // 唤起该提供者的那些词，宿主自己的剥离步骤读的就是它，这样宿主的剥离
    // 与你的匹配就不会各自漂移。见第 6 节“触发词”。
    IReadOnlyList<string> QueryTriggerKeywords => [];
}
```

> [!TIP]
> `GetInstantResults` 为同步调用以保障打字流畅度。若需要发起网络请求（如在线翻译或搜索建议）：可先立即返回一个占位结果项，通过 `Task.Run` 在后台异步获取数据并缓存，请求完成后调用 `SearchRefreshService.RefreshIfMatches` 通知宿主就地刷新当前搜索结果。

### 非 ASCII 别名转写引擎 `IAliasProvider`

用于为中文文件名等非 ASCII 文本生成额外的可索引别名，支持混合拼音输入匹配：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IAliasProvider : IPluginComponent   // Name 来自 IPluginComponent
{
    bool CanHandle(string text);
    IReadOnlyList<(char Start, char End)> InputRanges { get; }  // 源字符范围（如 CJK 表意文字）
    IReadOnlyList<(char Start, char End)> OutputRanges { get; } // 生成别名字符范围（如 a-z）

    // 多音节引擎（拼音）才需要设置：生成的别名里用来连接各音节的字符。
    // '\0'（默认值）表示该引擎完全不输出任何分隔符。
    char SyllableSeparator => '\0';

    IEnumerable<string> GetAliases(string text);

    int Version => 1;                                           // 规则更新时递增以触发重新索引
    IEnumerable<string> GetQueryForms(string term) => Array.Empty<string>(); // 查询侧改写（如拼音音节边界切分）
    int[]? MapAliasToSourceIndices(string text, string alias) => null;       // 映射别名命中位置至原文以供高亮

    // 供索引器热路径使用的零分配 UTF-8 构建器。默认实现把 GetAliases() 转发进 sink，
    // 并且只对确实含有大写字母的别名做小写化，因此只有当引擎能以比字符串更廉价的方式
    // 产出字节时，才需要改写这个方法。
    void GetAliasesUtf8(string text, AliasByteSink dest);
}
```

### 查询后缀 Token 处理器 `IQueryTokenProvider`

用于认领并处理搜索框尾部的特定 Token 标记（例如 `report :size`、`doc :@today` 或 `image ::"hello world"`），对初步匹配的结果列表进行流式二次变换（过滤、重排序等）：

```csharp
public interface IQueryTokenProvider : IPluginComponent
{
    bool CanHandle(string token);
    Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results);

    // 该 token 从查询中被吃掉之后，仍要在结果行里保持高亮的文本。
    // null（默认值）表示不去改动宿主自己的高亮。
    string? GetHighlightText(string token) => null;
}
```

## 3. 结果上的上下文动作

### 动作提供者容器 `IActionProvider`

```csharp
public interface IActionProvider
{
    IEnumerable<ISearchResultAction> GetActions();
    IEnumerable<IDynamicActionProvider> GetDynamicActionProviders();
}
```

### 静态动作契约 `ISearchResultAction`

表示一个明确的静态操作（如“复制完整路径”、“以管理员身份运行”等），呈现在 `Ctrl+O` 动作菜单中或绑定为快捷键：

```csharp
namespace Lertaro.PluginSdk.Abstractions;

public interface ISearchResultAction : Plugins.IPluginComponent
{
    string GroupName { get; }           // 动作菜单中的分组标题
    string DisplayName { get; }         // 动作显示文本
    // 动作是以显示名来寻址的，因此 Name 是映射出来的、不是手写的：
    string Plugins.IPluginComponent.Name => DisplayName;

    // 非可空、带默认值。空字符串表示“无快捷键”，破坏性文件操作在重新拿回资源管理器的默认
    // 键位之前，正是这样保持未绑定状态的。
    string Hotkey => string.Empty;
    IReadOnlyList<string> Keywords => Array.Empty<string>();
    IReadOnlyList<string> Parameters => Array.Empty<string>();

    // 动作出现在哪里。默认：在搜索中可见，且只有在它没有认领关键词时才在菜单中
    // 可见（认领关键词就是一个动作改为以一行结果出现的办法）。
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => true;
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => Keywords.Count == 0;

    ImageSource? Icon { get; }          // 动作图标；为 null 时绘制分组默认图标
    bool CanExecute(IReadOnlyList<ISearchResult> results);
    void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view);
}
```

### 动态菜单构建器 `IDynamicActionProvider`

在运行时动态构建深层嵌套或系统级菜单（例如将 Windows Shell 原生右键菜单注入到 Lertaro 中）：

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IDynamicActionProvider : IPluginComponent
{
    string GroupName { get; }
    string IPluginComponent.Name => GroupName;    // 与 ISearchResultAction 相同的映射

    int Priority => 0;                            // 菜单排序权重，不是可空类型
    IReadOnlyList<string> Keywords => Array.Empty<string>();
    IReadOnlyList<string> Parameters => Array.Empty<string>();

    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => true;
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => Keywords.Count == 0;

    void Init() { }                               // 一次性预热，每个进程最多一次，从第一个菜单开始展开时触发
    bool CanProvide(IReadOnlyList<ISearchResult> results);

    // 若要在即时结果（一个窗口标题、一个进程行）之上也构建菜单，必须显式选择加入。默认为 false，
    // 因为大多数提供者依赖的是这些行所不具备的文件路径。
    bool CanProvideForInstantResults => false;

    IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> results, IntPtr hMenu);
    IEnumerable<(string Hotkey, Action Execute)> GetHotkeyActions(IReadOnlyList<ISearchResult> results)
        => Array.Empty<(string, Action)>();
    void ExecuteCommand(IReadOnlyList<ISearchResult> results, uint commandId, IntPtr ownerHwnd);

    // 没有默认实现：必须实现它。菜单被销毁时调用，
    // 因此持有原生句柄或缓存了 shell CDS 流的提供者可以在此释放它们。
    void ClearSession();
}
```

## 4. 辅助数据结构

- **`SearchableItem`**：包含 `Title`、`Description`、`IconData`、`IconColor`、`ActionType`（`"Copy"` / `"Execute"` / `"None"`）、`ActionArgument`、`TabCompletion`、`HBitmapIcon`（宿主自动接管释放）、`ResultKind`（由插件自选的标记，宿主的筛选器与列都可以依据它来匹配），以及两个执行回调：`OnExecute`（`Action`）用于发出去就不管，或当动作需要汇报成功与否时用 `OnExecuteFunc`（`Func<bool>`）——宿主会用这个答复来决定例如是否关闭窗口。`InstantResultItem` 带有同样的展示与回调成员，**只是不含 `ResultKind`**，那个只有可搜索条目模型才有。
- **`DynamicMenuItem`**：包含 `Text`、`CommandId`、`IsSeparator`、`HasSubMenu`、`SubMenuHandle`、`IsDisabled`、`OnExecute`、`IsActionable`（默认 `true`；`false` 标记的是只用于打开子菜单的那一行）、`HBitmapItem`（来自被镜像的 Shell 菜单的原生图标句柄）、`ShortcutHint`（助记键所匹配的字母）、`IsContinuation`（一个分页游标：这一批延续的是宿主仍在填充的菜单，只要它置位，宿主就会接着向你要下一批），以及 `IsHeader`（渲染为带可选操作按钮的分组标题行）。
- **`SearchWindowType`**：枚举值包括 `Main`（主搜索窗口）、`Quick`（居中快速浮窗）与 `Inline`（嵌入式文件对话框）。

## 5. 具名搜索作用范围 `ISearchScopeProvider`

一个**作用范围（scope）**就是一个关键词前缀加上一组目录：键入 `tf report` 会让宿主在那些文件夹之内执行它正常的索引搜索来查找 `report`。它是在既有索引之上的第二级筛选，而不是第二个搜索引擎。

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface ISearchScopeProvider : IPluginComponent
{
    // 每次击键分发时都会被查询：请返回一个缓存的列表，只在配置变化时才重建。
    // 关键词为空或没有文件夹的作用范围会被宿主忽略。
    IReadOnlyList<SearchScope> GetSearchScopes();
}

public sealed class SearchScope
{
    public string Keyword { get; init; } = string.Empty;            // 不区分大小写的第一个 token，如 "tf"
    public IReadOnlyList<string> Folders { get; init; } = Array.Empty<string>();
    public string FilterPattern { get; init; } = "*";               // 以 ';' 分隔、作用于文件名的 Win32 通配符
}
```

与 `ISearchableItemProvider` 不同，作用范围提供者从不枚举、也从不实体化文件，因此无论配置的文件夹有多大，内存开销与每次击键的开销都是平的。没有被任何宿主索引覆盖的文件夹会被跳过并记录一条警告，而不是现场遍历——这与索引辅助方法遵循的是同一条规则：先用已配置的本地驱动器索引、网络索引或文件夹索引覆盖该文件夹。目录一律视为通过 `FilterPattern` 的检查。

仓库内的实现是文件筛选（File Filters）插件。

## 6. 触发词 `TriggerWord`

任何由用户键入首部单词来调用的功能——即时提供者的 `QueryTriggerKeywords`、动作的 `Keywords`、作用范围的 `Keyword`、文件筛选器的触发符——都通过 `Lertaro.PluginSdk.Services.TriggerWord` 来解析这个词，宿主的剥离与插件的匹配因此不可能各走各路。

| 辅助方法 | 匹配的是 |
| :--- | :--- |
| `string Normalize(string? configured)` | 把配置的词整理成所有比较都期望的形态：去掉首尾空白，`null` 或空白则得到空字符串。读取时就归一化——宿主会剥掉它自己剥离的那个词，所以拿未去空白的值去比较什么也认不出来，而宿主照样会把那个词从文件检索里移除。 |
| `bool TryMatch(string query, string? word, out string argument)` | 第一个 token **等于**该词（不区分大小写）。`argument` 是剩余并被裁剪过的文本，当整个查询就只有这个词时为空——而这种情况仍然算匹配。仅仅以该词开头的更长词不算匹配（`csreport` 不是 `cs`）。 |
| `bool TryMatchInvoked(...)` | 与上相同，但只有在该词之后确实键入了内容时才算数。当光秃秃的一个词会在屏幕上放上没人索取的行时使用它：单独的 `cs` 仍是文件检索，`cs ` 才是那个提供者。 |
| `bool TryMatchAny(string query, IReadOnlyList<string> words, out string matchedWord, out string argument)` | 按列表顺序第一个匹配上的词胜出，并报告匹配上的是**哪一个**——一个提供者配有多个关键词的网页搜索引擎需要这个信息才能重新解析。 |
| `bool IsTypedPrefixOf(string query, string? word)` | 查询是这个单词尚未打完的输入过程（朝着 `mkdir` 输入的 `m`）。这是唯一被允许在整词到齐之前就提供该触发器的分支；一旦键入了任何分隔符就返回 false。 |

`IInstantResultProvider.QueryTriggerKeywords`（默认为空）正是宿主为自己的剥离步骤所读取的内容，而 `PluginConfigField.IsTriggerWord` 标记住承载这类词的那个 `Text` 设置项，使设置页面能在另一个功能已经对该词作出响应时给出提醒——但不会阻止保存。除此之外，两个功能共用一个词是无声无息的：文件检索跟随最先注册的那一个，另一个的行就此不再出现，而没有任何东西告诉用户该去重命名哪一个。
