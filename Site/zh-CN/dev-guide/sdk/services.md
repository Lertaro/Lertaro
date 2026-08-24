# 宿主开放服务

`Lertaro.PluginSdk.Services` 命名空间下提供了一组高性能的静态基础设施服务。这些服务对宿主内部的核心算法、缓存与平台接口进行了轻量级封装，使插件能够以极简的代码直接复用宿主能力。

## 1. 核心静态服务一览

| 宿主服务 | 核心方法与签名 | 功能说明 |
| :--- | :--- | :--- |
| **`FuzzyMatchService`** | `bool IsMatch(string pattern, string text)`<br>`bool[]? GetHighlightMask(string text, string query)` | 运行与宿主完全一致的 fzf 模糊匹配引擎，并计算字符级的高亮布尔掩码（自动支持汉字拼音多级兜底）。 |
| **`TranslationService`** | `string Get(string key)`<br>`string Format(string key, params object[] args)`<br>`void LoadEmbeddedTranslations(...)`<br>`string GetCurrentCulture()` | 多语言动态解析。`GetCurrentCulture()` 返回用户在设置中心显式选择的界面语言代码（如 `"zh-CN"`），不受系统默认区域影响。 |
| **`IconService`** | `ImageSource? GetIcon(string path, bool isDir)`<br>`ImageSource? GetThumbnail(string path, int size)` | 带内存与磁盘缓存的 Windows Shell 文件图标与缩略图提取服务。 |
| **`FavoritesService`** | `IReadOnlyList<FavoriteItem> GetFavorites()` | 只读读取用户在设置中心保存的全部星标收藏项列表。 |
| **`HistoryService`** | `IReadOnlyList<HistoryEntry> GetHistoryEntries()` | 读取历史记录条目，按最近打开时间降序排列，包含关联的搜索关键词与文件类型。 |
| **`FileMetadataService`** | `Task<IReadOnlyDictionary<string, FileMetadata>> GetMetadataAsync(IEnumerable<string> paths)` | 批量查询外部路径的物理文件大小与时间戳（仅用于查询未出现在当前搜索结果集中的外部路径）。 |
| **`DirectoryIndexerService`** | `void RegisterDirectory(string pluginId, string path, bool recursive, string? filterPattern)`<br>`IDisposable WatchDirectories(string pluginId, Action onChanged)`<br>`IAsyncEnumerable<ISearchResult> EnumerateDirectoryAsync(...)` | 允许插件向后台服务注册专属自定义目录以进行自动索引与变更监听；提供流式免 I/O 目录遍历。 |
| **`RecentFilesService`** | `Task<IReadOnlyList<ISearchResult>> GetRecentFilesAsync(IEnumerable<string> directories, int limit, int maxAgeMinutes, CancellationToken token)` | 利用内存索引快速提取指定目录列表下的最新修改文件集合（毫秒级应答，不产生物理磁盘 I/O）。 |
| **`ExplorerPathService`** | `string? GetLastActivePath()` | 获取用户最近一次在文件资源管理器或任意应用的文件选择对话框中浏览过的活动目录路径。 |
| **`PluginSettingsService`** | `T GetSetting<T>(string pluginId, string key, T defaultValue)`<br>`event Action<string, string>? SettingChanged` | 读取插件持久化的配置项（优先读取用户修改值，其次读取 Schema 默认值，最后回退 defaultValue）。 |
| **`SearchRefreshService`** | `void RefreshIfMatches(Func<string, bool> queryMatches)` | 用于异步即时计算源完成后台数据获取后，通知宿主原地重跑当前匹配的搜索查询并刷新视图。 |
| **`UserDataService`** | `string GetUserDataDirectory()`<br>`string GetSharedDataDirectory()` | 获取当前用户的专属数据目录（存放私有配置）与机器级全局共享数据目录（共享 Python/Node 运行时）。 |
| **`Logger`** | `void Log(string message, LogLevel level = LogLevel.Info)` | 统一输出日志至 `app.log`，并在设置中心的实时日志查看器中同步呈现。 |
| **`PluginPromptService`** | `Task<Dictionary<string, object?>?> Prompt(string title, IEnumerable<PluginConfigField> fields, ...)` | 弹出基于 Schema 自动渲染的小型模态输入对话框，向用户请求一次性输入。 |

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
