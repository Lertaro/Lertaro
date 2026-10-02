# 共享抽象契约

本章节汇总了 `Lertaro.PluginSdk` 中跨多个接口复用的基础数据模型、只读契约与配置驱动抽象。

## 1. 检索结果模型 `ISearchResult`

在 Lertaro 的架构中，插件对搜索结果的观察始终基于只读契约 `ISearchResult`，禁止直接篡改宿主底层的核心索引数据结构：

```csharp
namespace Lertaro.PluginSdk.Abstractions;

public interface ISearchResult
{
    string Name { get; }                  // 文件或条目的显示名称（如 "Lertaro.exe"）
    string FullPath { get; }              // 绝对物理路径（如 "C:\Program Files\Lertaro\Lertaro.exe"）
    string ContextDirectory { get; }      // 所在父级目录路径（如 "C:\Program Files\Lertaro"）
    bool IsDir { get; }                   // 是否为目录/文件夹
    bool IsApplication { get; }           // 是否为可执行程序或快捷方式
    bool[]? GetHighlightMask(string text, string query) => null; // 字符级高亮掩码计算
    FileMetadata Metadata => default;     // 高性能文件元数据（大小、修改时间等）
    string? InstantActionArgument => null; // 即时结果真正指向的目标
}
```

`FullPath` 是大多数动作据以工作的身份标识，因此一个作用于“并非路径之物”的即时结果——`activatewindow:12345`、`kill:4321`、一段自定义命令的载荷——会把那个目标改放在 `InstantActionArgument` 里，提供者也从那里读取它。对其他任何种类的行它都保持 `null`：普通的文件与文件夹结果、插件搜索动作、历史记录条目。

> [!NOTE]
> `ISearchResult.Metadata` 包含的数据由宿主底层的 USN / MFT 内存索引直接注入，**读取该属性完全不产生任何磁盘 I/O 或 IPC 调用**。仅当你需要查询不属于当前结果集的外部路径时，才需要调用 `FileMetadataService.GetMetadataAsync`。

## 2. 文件元数据结构 `FileMetadata`

```csharp
public readonly record struct FileMetadata(
    long Size,
    DateTime Created,
    DateTime Modified,
    DateTime Accessed
);
```

- 时间戳均为**本地时间（Local Time）**。
- 若 `Metadata == default`（即各字段均为 0 或 `DateTime.MinValue`），表示该结果并非由物理文件索引生成（例如由某个即时计算插件动态生成）。
- 可通过 `Metadata.Modified != default` 准确区分“元数据不可用”与“大小恰好为 0 字节的合法真实文件”。

## 3. 宿主安全控制接口 `IPluginSearchWindow`

当动作执行回调（如 `ISearchResultAction.Execute`）被触发时，宿主会传入 `IPluginSearchWindow` 实例，供插件安全调度宿主窗口：

```csharp
public interface IPluginSearchWindow
{
    void LocateInExplorerExternal(string path);       // 在资源管理器或配置的文件管理器中高亮定位
    void OpenFileOrFolderExternal(string path);       // 使用关联程序普通启动
    void OpenFileOrFolderAsAdminExternal(string path);// 提权以管理员身份启动
    void HideWindow();                                // 隐藏当前搜索窗口
}
```

## 4. 模式驱动的配置体系 `IConfigurable`

如果你的插件需要提供个性化设置项，只需在插件类上实现 `IConfigurable` 接口，宿主便会在**设置 → 插件 → 配置**中自动根据 Schema 渲染出原生美观的表单界面，无需手写任何 XAML：

```csharp
public interface IConfigurable
{
    PluginConfigSchema GetConfigSchema();
}
```

### 核心字段类型 `ConfigFieldType`

| 字段类型 | 渲染控件与说明 |
| :--- | :--- |
| **`Boolean`** | 切换开关（Toggle Switch）或复选框。 |
| **`Text`** | 文本输入框。用户把它清空时，`RequireNonEmpty` 会回退为 `DefaultValue`；`MaxLength` 限定最大长度（为 0 或未设置表示不限长）；`SelectionStart` / `SelectionLength` 设定该字段的输入对话框打开时从 0 开始计算的初始选中范围。 |
| **`Integer`** | 数字微调输入框。支持配置最小值与最大值范围。 |
| **`Choice`** | 下拉选择框。通过 `Choices` 或 `ChoiceOptions` 列表指定可选条目。 |
| **`Array`** | 列表值。带 `SubFields` 时它是**记录**列表，渲染为主从明细编辑器（每个条目一张嵌套表单——文件筛选、自定义命令与网页搜索插件用的正是这种形态）；不带 `SubFields` 时它是普通的标量列表，渲染为紧凑的单列编辑器。SDK 根本不给 `DefaultValue` 设默认值（声明里是 `object?`、`null!`），这就是仓库内每个插件都用 `new List<object>()` 来表示空列表的原因。 |
| **`Object`** | 单个结构化值，通过其 `SubFields` 编辑，但不具备 `Array` 的那些列表操作能力。 |
| **`Group`** | 包含子字段列表（`SubFields`）的可折叠卡片分组。 |
| **`StringList`** | 支持多行编辑、条目增删排序与自动折行的多行列表框；真实换行仅以视觉标记显示，不会写入配置值。 |
| **`Hotkey`** | 专属按键录制框。可配置 `RequireModifier = true` 强制要求必须包含修饰键。 |
| **`FilePath` / `FolderPath`** | 附带“浏览...”文件/文件夹原生选择器按钮的路径输入框。 |
| **`CustomControl`** | 允许插件直接挂载一个自定义的 WPF `UIElement` 控件实例（经由 `CustomControl` 成员提供）。 |
| **`Button`** | 渲染操作按钮并调用字段的 `OnClick` 委托，不存储设置值。 |

`PluginConfigField` 其余的成员，是宿主围绕这些类型负责渲染或持久化的部分：`Key`（持久化用的设置名）、`GroupKey`（该字段位于哪张 `Group` 卡片里）、`LabelKey` / `DescriptionKey`（翻译键，而非字面文本）、`RequireNonEmpty`、`Choices` / `ChoiceOptions` / `SubFields`、`IsTriggerWord`（见 [**核心检索与动作**](./core-search-actions) 的“触发词”一节）、`MaxLength`、`SelectionStart` / `SelectionLength`、`CustomControl`、`OnClick`，以及两个让插件把值存到宿主设置存储之外的委托：`Func<object?>? GetValue` 与 `Action<object?>? SetValue`。

### 图标字段

将字段的 Schema 键设为 `Icon` 并使用 `Text` 类型时，宿主会显示图标预览并支持直接输入 WPF Path Data。粘贴完整 SVG/XML 文档时，宿主会提取并合并所有 `<path d>` 值，只保存转换后的 WPF Path Data；图标内容无效时会清空并通过主题化错误对话框提示。不需要图标时，空值仍然有效。

`PluginConfigSchema` 亦支持配置 `OnSave` 与 `OnRollback` 生命周期委托：用户点击**确定/应用**提交时触发 `OnSave` 执行自定义持久化，取消或回滚时触发 `OnRollback` 恢复状态。

### 本地化选择标签

当选项需要使用本地化标签，同时还要保存稳定的配置值时，应使用 `ChoiceOptions`。`PluginConfigChoice.Value` 会写入插件设置，`LabelKey` 会解析为界面显示文本；如果保存值和显示文本相同，继续使用旧的 `Choices` 列表即可。

```csharp
new PluginConfigField
{
    Key = "DisplayMode",
    FieldType = ConfigFieldType.Choice,
    DefaultValue = "FriendlyName",
    ChoiceOptions =
    [
        new PluginConfigChoice
        {
            Value = "FriendlyName",
            LabelKey = "DisplayMode_FriendlyName"
        }
    ]
}
```

## 5. 完整搜索窗口文件结果 `IFullSearchFileResultProvider`

如果插件需要向完整搜索窗口贡献真实的文件或文件夹行，可以实现 `IFullSearchFileResultProvider`：

```csharp
public interface IFullSearchFileResultProvider : IPluginComponent
{
    IReadOnlyList<InstantResultItem> GetFileResults(string query, int limit);

    // 可选。默认实现会遍历 GetFileResults，所以在这个成员出现之前写的提供者无需改动。
    IEnumerable<InstantResultItem> GetFileResultsStreamed(string query, int limit);
}
```

宿主在完整搜索窗口自身的文件搜索仍在流式返回时，于后台线程调用提供者，并在其结果一到达就绘制，而不是等到搜索结束。插件不处理当前查询时应返回空列表。返回的每个 `InstantResultItem` 都必须对应一个实际存在的文件或文件夹，这样完整窗口的路径、大小和类型列才有意义。若提供者的回答需要数秒（例如全文索引遍历），可以改写 `GetFileResultsStreamed`，边找到边交出命中，让前几行先上屏、其余继续查找；由于接口的默认实现只是遍历 `GetFileResults`，改写是可选的。该组件在**设置 → 插件**下拥有**属于它自己**的启用/禁用开关，按它自身的组件类型记账——把插件的即时结果提供者关掉并不会连带关掉它，反之亦然。

## 6. 用户配置路径解析 `UserPathResolver`

当插件接受用户输入或配置中的路径时，应使用 `Lertaro.PluginSdk.Helpers.UserPathResolver`，在调用文件系统 API 前统一处理环境变量和 Windows Shell 虚拟路径：

```csharp
string expanded = UserPathResolver.Expand(rawPath);            // 接收 string?，返回 string
bool isVirtual = UserPathResolver.IsVirtualPath(expanded);
string resolved = UserPathResolver.Resolve(rawPath);           // 可选的第二个参数见下方说明

// Resolve 与 ResolveForNavigation 都接受一个可选的 Func<string, string>?，
// 它在向文件系统发问之前，把一个无法解析的虚拟标记改写成一个真实路径；
// 既没给出该委托、又没有可解析的内容时，作为最后兜底原样返回传入的值。

// 当这个路径即将被打开或浏览时，请用 ResolveForNavigation 而不是 Resolve：
// 它还会把虚拟 Shell 项规范化为宿主能够导航过去的文件系统目标。
string target = UserPathResolver.ResolveForNavigation(rawPath);
```

`Expand` 会去除首尾空白并展开 `%USERPROFILE%` 等环境变量。`Resolve` 会先展开环境变量，再尽可能将 `shell:Downloads` 或 `::{CLSID}` 等标记解析为物理路径。对于 `shell:AppsFolder` 这类没有物理路径的虚拟文件夹，`Resolve` 会改而返回其规范名 `::{CLSID}`，使同一文件夹的各种写法彼此相等；该结果仍然是虚拟路径。只有 Shell 完全无法解析的标记才会原样返回。传给文件系统 API 前应使用 `IsVirtualPath` 检查结果。目录索引 API 只有在路径解析为真实且被索引覆盖的文件夹后才能枚举内容。
