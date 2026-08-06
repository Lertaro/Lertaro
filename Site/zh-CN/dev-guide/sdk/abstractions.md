# 共享抽象契约

其他 SDK 文档页面里用到的模型和支持性契约。

## `ISearchResult`

每一个插件接口操作的都是这份结果的只读视图——插件永远拿不到可变的结果对象，只有这个:

```csharp
interface ISearchResult
{
    string Name { get; }
    string FullPath { get; }
    string ContextDirectory { get; }
    bool IsDir { get; }
    bool IsApplication { get; }
    FileMetadata Metadata { get; }
    bool[]? GetHighlightMask(string text, string query);
}
```

`Metadata` 携带了宿主自己的文件索引为每个结果生成的 Size/Created/Modified/Accessed——读取它是免费的(不涉及磁盘 I/O 或 IPC),不像 `FileMetadataService.GetMetadataAsync`(参见[宿主服务](./services))
那样,只有在查询**不属于**你当前结果集的路径时才值得调用。

## `FileMetadata`

```csharp
readonly record struct FileMetadata(long Size, DateTime Created, DateTime Modified, DateTime Accessed);
```

本地时间。`default`(每个字段都是零/`DateTime.MinValue`)表示"不可用"——这种结果不是由文件索引生成的(比如来自另一个插件)。用 `Metadata.Modified != default` 来区分这种"确实不知道"的情况和一个真实的、合法的零字节文件——后者 `Size` 确实是 `0`,但时间戳仍然是真实的。

## `IPluginSearchWindow`

传给 `ISearchResultAction.Execute` 等回调的最小窗口控制接口——刻意保持精简;插件应该通过它来操作结果，而不是持有真实窗口的引用:

```csharp
interface IPluginSearchWindow
{
    void LocateInExplorerExternal(string path);
    void OpenFileOrFolderExternal(string path);
    void OpenFileOrFolderAsAdminExternal(string path);
    void HideWindow();
}
```

## `IConfigurable`

和 `IPlugin` 一起实现这个接口，就能在**设置 → 插件 → 配置**里自动获得一个配置界面——简单场景下不需要自己写 WPF。

```csharp
interface IConfigurable
{
    PluginConfigSchema GetConfigSchema();
}
```

`PluginConfigSchema` 是一份扁平的 `Fields: List<PluginConfigField>`。每个 `PluginConfigField`
有一个 `Key`，可选的 `GroupKey`/`LabelKey`/`DescriptionKey`(翻译 key，如果你有自己的
`ITranslationProvider` 就通过它解析)，一个 `FieldType`，一个 `DefaultValue`，以及——取决于类型
——`Choices`、嵌套的 `SubFields`，或者 `RequireModifier`(仅 `Hotkey` 字段,拒绝没有修饰键的单个按键)。

给字段(通常是触发关键词这类 `Text` 字段)设置 `RequireNonEmpty`，保存时如果值为空/纯空白就会回退到 `DefaultValue`，而不是把空值持久化下去——否则用户把关键词字段清空后，依赖它的功能会悄无声息地变得不可触发，而不是回退到一个正常的默认值。

`ConfigFieldType` 涵盖:`Boolean`、`Text`、`Integer`、`Choice`、`Array`、`Object`、`Group`、
`StringList`、`Hotkey`、`FilePath`、`FolderPath`。参见
[CoreExtensions](../examples#coreextensions-——-动作与-shell-右键菜单) 里一个用到嵌套分组和
`StringList` 的真实配置模式。

## 注册表

`ActivePathCollectorRegistry`、`FileDialogAdapterRegistry`、`InlineSearchAdapterRegistry` 是宿主把所有已加载的对应[系统适配接口](./system-adapters)实现汇总到一处的方式。插件作者通常不需要直接和这些注册表打交道——只要实现对应接口，宿主就会自动发现并注册你的插件。
