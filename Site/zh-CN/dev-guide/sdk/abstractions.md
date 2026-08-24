# 共享抽象契约

本章节汇总了 `Lertaro.PluginSdk` 中跨多个接口复用的基础数据模型、只读契约与配置驱动抽象。

## 1. 检索结果模型 `ISearchResult`

在 Lertaro 的架构中，插件对搜索结果的观察始终基于只读契约 `ISearchResult`，禁止直接篡改宿主底层的核心索引数据结构：

```csharp
namespace Lertaro.PluginSdk;

public interface ISearchResult
{
    string Name { get; }                  // 文件或条目的显示名称（如 "Lertaro.exe"）
    string FullPath { get; }              // 绝对物理路径（如 "C:\Program Files\Lertaro\Lertaro.exe"）
    string ContextDirectory { get; }      // 所在父级目录路径（如 "C:\Program Files\Lertaro"）
    bool IsDir { get; }                   // 是否为目录/文件夹
    bool IsApplication { get; }           // 是否为可执行程序或快捷方式
    FileMetadata Metadata { get; }        // 高性能文件元数据（大小、修改时间等）
    bool[]? GetHighlightMask(string text, string query); // 字符级高亮掩码计算
}
```

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
| **`Text`** | 文本输入框。支持配置 `RequireNonEmpty`，为空时自动回退为 `DefaultValue`。 |
| **`Integer`** | 数字微调输入框。支持配置最小值与最大值范围。 |
| **`Choice`** | 下拉选择框。通过 `Choices` 列表指定可选条目。 |
| **`Hotkey`** | 专属按键录制框。可配置 `RequireModifier = true` 强制要求必须包含修饰键。 |
| **`FilePath` / `FolderPath`** | 附带“浏览...”文件/文件夹原生选择器按钮的路径输入框。 |
| **`StringList`** | 支持多行编辑与条目增删排序的多行列表框。 |
| **`Group`** | 包含子字段列表（`SubFields`）的可折叠卡片分组。 |
| **`CustomControl`** | 允许插件直接挂载一个自定义的 WPF `UIElement` 控件实例。 |

`PluginConfigSchema` 亦支持配置 `OnSave` 与 `OnRollback` 生命周期委托，在用户点击确认提交或离开页面放弃修改时执行自定义持久化或状态复原。
