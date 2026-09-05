# 宿主开放服务

`Lertaro.PluginSdk.Services` 命名空间下提供了一组高性能的静态基础设施服务。这些服务对宿主内部的核心算法、缓存与平台接口进行了轻量级封装，使插件能够以极简的代码直接复用宿主能力。

## 1. 核心静态服务一览

| 宿主服务 | 核心方法与签名 | 功能说明 |
| :--- | :--- | :--- |
| **`FuzzyMatchService`** | `bool IsMatch(string pattern, string text)`<br>`bool[]? GetHighlightMask(string text, string query)`<br>`double GetMatchScore(string text, string query)` | 运行与宿主完全一致的 fzf 模糊匹配引擎，计算字符级的高亮布尔掩码（自动支持汉字拼音多级兜底），并提供用于统一排序的匹配质量评分。 |
| **`TranslationService`** | `string Get(string key)`<br>`string Format(string key, params object[] args)`<br>`void LoadEmbeddedTranslations(...)`<br>`string GetCurrentCulture()`<br>`event Action<string>? CultureChanged` | 多语言动态解析与运行时变更广播。`GetCurrentCulture()` 返回用户在设置中心显式选择的界面语言代码（如 `"zh-CN"`）；订阅 `CultureChanged` 可在界面语言切换时动态刷新内部状态或重载字典。 |
| **`IconService`** | `ImageSource? GetIcon(string path, bool isDir)`<br>`ImageSource? GetThumbnail(string path, int size)` | 带内存与磁盘缓存的 Windows Shell 文件图标与缩略图提取服务。 |
| **`FavoritesService`** | `IReadOnlyList<FavoriteItem> GetFavorites()`<br>`bool IsFavorite(string path)`<br>`bool TryAddFavorite(FavoriteItem favorite)` | 读取收藏夹、检查路径是否已登记，并通过宿主桥接添加收藏项。 |
| **`HistoryService`** | `IReadOnlyList<HistoryEntry> GetHistoryEntries()` | 读取历史记录条目，按最近打开时间降序排列，包含关联的搜索关键词与文件类型。 |
| **`FileMetadataService`** | `Task<IReadOnlyDictionary<string, FileMetadata>> GetMetadataAsync(IEnumerable<string> paths)` | 批量查询外部路径的物理文件大小与时间戳（仅用于查询未出现在当前搜索结果集中的外部路径）。 |
| **`DirectoryIndexerService`** | `void RegisterDirectory(string pluginId, string path, bool recursive, string? filterPattern)`<br>`IDisposable WatchDirectories(string pluginId, Action onChanged)`<br>`IDisposable WatchDirectories(string pluginId, Action<IReadOnlyList<string>> onChanged)`<br>`IAsyncEnumerable<ISearchResult> EnumerateDirectoryAsync(...)` | 允许插件向宿主注册自定义目录，以进行基于宿主索引的搜索和变更监听。目录枚举只读取宿主文件索引并以流式返回；未被索引覆盖的目录会返回空序列，因此调用方必须确保目录被已配置的本地驱动器、网络或文件夹索引覆盖。宿主不会直接扫描文件系统。监听通知经过防抖处理，并可携带受影响目录；空列表表示宿主无法确定更窄的范围。 |
| **`MemoryMaintenanceService`** | `void RequestTrim()` | 插件完成一段临时内存分配密集型后台工作后，请求宿主延迟执行工作集维护。请求可能被合并或忽略，不会释放仍在使用的缓存。 |
| **`RecentFilesService`** | `Task<IReadOnlyList<ISearchResult>> GetRecentFilesAsync(IEnumerable<string> directories, int limit, int maxAgeMinutes, CancellationToken token)` | 利用内存索引快速提取指定目录列表下的最新修改文件集合（毫秒级应答，不产生物理磁盘 I/O）。 |
| **`ExplorerPathService`** | `string? GetLastActivePath()` | 获取用户最近一次在文件资源管理器或任意应用的文件选择对话框中浏览过的活动目录路径。 |
| **`PluginSettingsService`** | `T GetSetting<T>(string pluginId, string key, T defaultValue)`<br>`bool IsComponentEnabled(string dllName, string componentType, string componentName)`<br>`event Action<string, string>? SettingChanged`<br>`event Action? ComponentEnablementChanged` | 读取插件持久化配置以及宿主保存的组件级启用状态。 |
| **`SettingsSearchService`** | `IReadOnlyList<SettingsSearchEntryInfo> GetEntries()`<br>`void Invalidate()` | 读取宿主当前可搜索的设置条目，并在动态提供的条目发生变化时通知宿主刷新缓存快照。 |
| **`SettingsWindowService`** | `bool ShowWindow(string? targetSection = null)`<br>`bool ShowEntry(SettingsSearchEntryInfo? entry)` | 请求宿主显示主题化设置窗口，或直接跳转到可搜索的设置条目，不启动 URI 或其他进程。 |
| **`SearchRefreshService`** | `void RefreshIfMatches(Func<string, bool> queryMatches)` | 用于异步即时计算源完成后台数据获取后，通知宿主原地重跑当前匹配的搜索查询并刷新视图。 |
| **`UserDataService`** | `string GetUserDataDirectory()`<br>`string GetSharedDataDirectory()` | 获取当前用户的专属数据目录（存放私有配置）与机器级全局共享数据目录（共享 Python/Node 运行时）。 |
| **`Logger`** | `void Log(string message, LogLevel level = LogLevel.Info)` | 统一输出日志至 `app.log`，并在设置中心的实时日志查看器中同步呈现。 |
| **`PluginPromptService`** | `Task<Dictionary<string, object?>?> Prompt(string title, IEnumerable<PluginConfigField> fields, ...)` | 弹出基于 Schema 自动渲染的小型模态输入对话框，向用户请求一次性输入。 |
| **`PluginMessageBoxService`** | `MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult)` | 请求由宿主显示消息框，使插件能够使用宿主的主题化界面；未注册宿主处理器时回退到系统消息框。 |
| **`ExplorerService`** | `void OpenDirectory(string directoryPath, string? fileNameOrFilePath = null)` | 打开指定文件夹或定位指定文件，遵循宿主配置的第三方文件管理器（或资源管理器多标签页），未配置时回退到系统资源管理器。 |

`SettingsSearchService.GetEntries()` 返回的条目索引只在当前宿主进程中有效。将条目直接传给 `SettingsWindowService.ShowEntry(...)`，SDK 会调用宿主回调，不会构造或启动 `lertaro://` URI。

### 组件启用状态与高成本运行时

`PluginSettingsService.IsComponentEnabled(...)` 用于读取宿主保存的组件级开关。拥有目录监听器、后台工作线程、外部运行时或其他高成本状态的组件，应在初始化这些状态前先检查该开关，并订阅 `ComponentEnablementChanged`，在用户切换开关后启动或停止对应运行时。如果宿主没有注册回调或回调失败，该方法返回 `true`，从而保证插件在未接入完整宿主时仍可用。

## 2. Shell 原生文件操作封装

`Lertaro.PluginSdk.Shell.FileOperations` 封装了 Windows Shell 原生的 `IFileOperation` 接口。插件执行文件移动、复制与删除时，用户将获得与资源管理器完全一致的原生进度对话框、冲突替换提示与 `Ctrl+Z` 撤销支持：

```csharp
namespace Lertaro.PluginSdk.Shell.FileOperations;

// 批量粘贴或移动（合并为单次 Shell 操作）
public static class ShellPasteHelper
{
    public static void PasteAsync(
        IEnumerable<string> sourcePaths,
        string destinationFolder,
        bool move = false,
        Action? onCompleted = null);
}

// 安全放入回收站或永久删除
public static class ShellDeleteHelper
{
    public static void DeleteAsync(IEnumerable<string> paths, bool permanent = false);
}

// 虚拟文件与网页拖拽流提取
public static class VirtualFileExtractor
{
    public static bool HasVirtualFiles(IDataObject dataObject);
    public static Task<IReadOnlyList<string>> Extract(IDataObject dataObject, string targetFolder);
    public static string ResolveDestination(string folder, string name); // 重名自动加 (2) 规则
}
```

> [!TIP]
> 上述 Shell 异步帮助类均自动运行在 SDK 独立的专用 STA 工作线程（`ShellOperationStaWorker`）中，插件调用时无需自行创建 STA 线程套间。

## 3. 应用生命周期与主题化插件窗口

`AppLifecycleService.RequestRestart()` 请求宿主应用执行优雅重启。宿主会启动替代进程，等待当前实例完成正常退出后再结束；插件无需自行启动可执行文件或关闭宿主。宿主接受请求时该方法返回 `true`。

对于插件自有的 WPF 内容，`Lertaro.PluginSdk.Windows.PluginWindow` 提供统一的圆角主题窗口框架。将插件视图赋给 `ContentHostControl.Content`，并通过 `Footer` 添加底部按钮。普通任务栏窗口使用 `PluginWindowMode.Window`；需要置顶且从 Alt+Tab 隐藏的对话框使用 `PluginWindowMode.Dialog`。不传入图标时会使用宿主的默认应用图标。

```csharp
var window = new PluginWindow("我的工具", 720, 470, PluginWindowMode.Dialog);
window.ContentHostControl.Content = new MyView();
window.Footer.Children.Add(new Button { Content = "确定", IsDefault = true });
window.ShowDialog();
```
