# 核心检索与动作

本章节详细介绍 `Lertaro.PluginSdk` 中用于贡献搜索数据源、即时计算答案、非 ASCII 别名转写引擎、查询后缀 Token 处理器以及静态/动态上下文动作菜单的核心接口与数据结构。

## 1. 基础组件规范 `IPluginComponent` 与 `IPlugin`

所有插件扩展组件均直接或间接继承自 `IPluginComponent`，用于向宿主声明组件的元数据：

```csharp
namespace Lertaro.PluginSdk;

public interface IPluginComponent
{
    string Name => GetType().Name;      // 组件显示名称（默认取类名）
    string Description => string.Empty; // 功能描述，在设置界面中作为 ToolTip 提示气泡呈现
}

public interface IPlugin : IPluginComponent
{
    // 插件主程序集入口点标识
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
}
```

> [!TIP]
> `GetInstantResults` 为同步调用以保障打字流畅度。若需要发起网络请求（如在线翻译或搜索建议）：可先立即返回一个占位结果项，通过 `Task.Run` 在后台异步获取数据并缓存，请求完成后调用 `SearchRefreshService.RefreshIfMatches` 通知宿主就地刷新当前搜索结果。

### 非 ASCII 别名转写引擎 `IAliasProvider`

用于为中文文件名等非 ASCII 文本生成额外的可索引别名，支持混合拼音输入匹配：

```csharp
public interface IAliasProvider
{
    string Name { get; }
    bool CanHandle(string text);
    IReadOnlyList<(char Start, char End)> InputRanges { get; }  // 源字符范围（如 CJK 表意文字）
    IReadOnlyList<(char Start, char End)> OutputRanges { get; } // 生成别名字符范围（如 a-z）
    IEnumerable<string> GetAliases(string text);

    int Version => 1;                                           // 规则更新时递增以触发重新索引
    int[]? MapAliasToSourceIndices(string text, string alias) => null; // 映射别名命中位置至原文以供高亮
    void GetAliasesUtf8(string text, AliasByteSink dest);       // 零分配字节原生构建
    IEnumerable<string> GetQueryForms(string term);             // 查询侧改写（如拼音音节边界切分）
}
```

### 查询后缀 Token 处理器 `IQueryTokenProvider`

用于认领并处理搜索框尾部的特定 Token 标记（例如 `report :size`、`doc :@today` 或 `image ::"hello world"`），对初步匹配的结果列表进行流式二次变换（过滤、重排序等）：

```csharp
public interface IQueryTokenProvider : IPluginComponent
{
    bool CanHandle(string token);
    Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results);
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

表示一个明确的静态操作（如“复制完整路径”、“以管理员身份运行”等），呈现在 `Ctrl+O` 动作菜单中或绑定为全局动作热键：

```csharp
public interface ISearchResultAction : IPluginComponent
{
    string GroupName { get; }           // 动作所属分组名称
    string DisplayName { get; }         // 动作显示文本
    string? Hotkey { get; }             // 默认快捷键（如 "Ctrl+Shift+C"）
    IReadOnlyList<string>? Keywords { get; }
    IReadOnlyList<string>? Parameters { get; }
    ImageSource Icon { get; }           // 动作图标
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool CanExecute(IReadOnlyList<ISearchResult> selection);
    void Execute(IReadOnlyList<ISearchResult> selection, IPluginSearchWindow window);
}
```

### 动态菜单构建器 `IDynamicActionProvider`

在运行时动态构建深层嵌套或系统级菜单（例如将 Windows Shell 原生右键菜单注入到 Lertaro 中）：

```csharp
public interface IDynamicActionProvider
{
    string GroupName { get; }
    int? Priority => 0;                 // 在动作菜单中的默认展示优先级
    IReadOnlyList<string>? Keywords { get; }
    IReadOnlyList<string>? Parameters { get; }
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    void Init() { }                     // 进程生命周期内最多触发一次的预热初始化
    bool CanProvide(IReadOnlyList<ISearchResult> selection);
    IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> selection, IntPtr hMenu);
    IEnumerable<(string Hotkey, Action Execute)> GetHotkeyActions(IReadOnlyList<ISearchResult> selection);
    void ExecuteCommand(IReadOnlyList<ISearchResult> selection, uint commandId, IntPtr ownerHwnd);
    void ClearSession() { }
}
```

## 4. 辅助数据结构

- **`SearchableItem` / `InstantResultItem`**：包含 `Title`、`Description`、`IconData`、`IconColor`、`ActionType`（`"Copy"` / `"Execute"` / `"None"`）、`ActionArgument`、`TabCompletion`、`HBitmapIcon`（GDI 位图句柄，宿主自动接管释放）以及 `OnExecute` 执行委托。
- **`DynamicMenuItem`**：包含 `Text`、`CommandId`、`IsSeparator`、`HasSubMenu`、`SubMenuHandle`、`IsDisabled`、`OnExecute`、`IsHeader`（设为 true 时渲染为带可选操作按钮的分组标题行）。
- **`SearchWindowType`**：枚举值包括 `Main`（主搜索窗口）、`Quick`（居中快速浮窗）与 `Inline`（嵌入式文件对话框）。
