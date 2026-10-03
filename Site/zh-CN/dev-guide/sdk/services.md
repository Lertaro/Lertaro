# 宿主开放服务

`Lertaro.PluginSdk.Services` 命名空间下提供了一组高性能的静态基础设施服务。它们轻量封装了宿主内部的核心算法、缓存与平台接口，使插件能够以少量代码直接复用宿主能力。

## 1. 核心静态服务一览

| 宿主服务 | 核心方法与签名 | 功能说明 |
| :--- | :--- | :--- |
| **`FuzzyMatchService`** | `bool IsMatch(string pattern, string text)`<br>`bool[]? GetHighlightMask(string text, string query)`<br>`double GetMatchScore(string text, string query)` | 运行与宿主完全一致的 fzf 模糊匹配引擎，计算字符级的高亮布尔掩码（自动支持汉字拼音多级兜底），并提供用于统一排序的匹配质量评分。 |
| **`TranslationService`** | `string Get(string key)`<br>`bool TryGet(string key, out string result)`<br>`string Format(string key, params object[] args)`<br>`string GetCurrentCulture()`<br>`IReadOnlyList<string> GetSupportedCultures(Assembly assembly)`<br>`Dictionary<string, string> LoadEmbeddedTranslations(Assembly assembly, string cultureKey, string typeName)`<br>`event Action<string>? CultureChanged` | 多语言动态解析与运行时变更广播。`GetCurrentCulture()` 返回用户在设置中心显式选择的界面语言代码（如 `"zh-CN"`），与系统默认值无关；订阅 `CultureChanged` 可在界面语言切换时动态刷新内部状态或重载字典。`TryGet` 回答某个键是否被解析成功，而 `Get` 在未命中时回退为可见的 `[key]` 占位文本而不是抛出异常。`GetSupportedCultures(assembly)` 列出某个程序集的内嵌资源覆盖了哪些语言；`LoadEmbeddedTranslations(assembly, cultureKey, typeName)` 返回其中一种语言的字典。 |
| **`IconService`** | `ImageSource? GetIcon(string path, bool isDir)`<br>`ImageSource? GetThumbnail(string path, int size)`<br>`ImageSource? GetIconFromCacheOnly(string path, bool isDir, out bool needsLoad)` | 带内存与磁盘缓存的 Windows Shell 文件图标与缩略图提取服务。`GetIconFromCacheOnly` 完全不碰 Shell：它只返回已经缓存的内容，并通过 `needsLoad` 报告是否仍然需要一次真正的加载，列表正是靠这一点先画出图标、事后再补齐。 |
| **`FavoritesService`** | `IEnumerable<FavoriteItem> GetFavorites()`<br>`bool IsFavorite(string path)`<br>`bool TryAddFavorite(FavoriteItem favorite)` | 读取收藏夹、检查路径是否已登记，并通过宿主桥接添加收藏项。 |
| **`HistoryService`** | `IEnumerable<HistoryEntry> GetHistoryEntries()` | 读取历史记录条目，按最近打开时间降序排列，包含关联的搜索关键词、文件类型与单条记录的使用次数。同一物理路径最多出现一次，并归属于最近一次打开它时使用的关键词。 |
| **`FileMetadataService`** | `Task<IReadOnlyDictionary<string, FileMetadata>> GetMetadataAsync(IReadOnlyList<string> paths)` | 批量查询外部路径的物理文件大小与时间戳（仅用于查询未出现在当前搜索结果集中的外部路径）。 |
| **`DirectoryIndexerService`** | `void RegisterDirectory(string pluginId, string directoryPath, bool recursive = true, string filterPattern = "*")`<br>`void UnregisterDirectories(string pluginId)`<br>`IDisposable WatchDirectories(string pluginId, Action onChanged)`<br>`IDisposable WatchDirectories(string pluginId, Action<IReadOnlyList<string>> onChanged)`<br>`void NotifyDirectoryChanged(string pluginId)`<br>`void NotifyDirectoryChanged(string pluginId, IReadOnlyList<string> changedDirectories)`<br>`Task<List<ISearchResult>> SearchDirectoriesAsync(string pluginId, string query, CancellationToken token = default)`<br>`IAsyncEnumerable<ISearchResult> EnumerateDirectoryAsync(string directoryPath, bool recursive = false, string filterPattern = "*", int limit = 0, CancellationToken token = default)` | 允许插件向宿主注册自定义目录，以进行基于宿主索引的搜索和变更监听。目录枚举只读取宿主文件索引并以流式返回；未被索引覆盖的目录会返回空序列，因此调用方必须确保目录被已配置的本地驱动器、网络或文件夹索引覆盖。宿主不会直接扫描文件系统。请注意这处刻意造成的不对称：`RegisterDirectory` 默认递归，而 `EnumerateDirectoryAsync` 默认不递归。监听通知经过防抖处理，并可携带受影响目录；空列表表示宿主无法确定更窄的范围。 |
| **`MemoryMaintenanceService`** | `void RequestTrim()` | 插件完成一段临时内存分配密集型后台工作后，请求宿主延迟执行工作集维护。请求可能被合并或忽略，不会释放仍在使用的缓存。 |
| **`RecentFilesService`** | `Task<IReadOnlyList<ISearchResult>> GetRecentFilesAsync(IReadOnlyList<string> directories, int limit, int maxAgeMinutes, CancellationToken cancellationToken = default)` | 通过查询宿主的内存索引，汇总所配置目录中的最近文件。 |
| **`ExplorerPathService`** | `string? GetLastActivePath()`<br>`IReadOnlyList<string> GetOpenedFolderPaths()` | 获取用户最近一次在文件资源管理器或任意应用的文件选择对话框中浏览过的活动目录路径，以及其中当前已打开的文件夹。 |
| **`PluginSettingsService`** | `T GetSetting<T>(string pluginId, string key, T defaultValue)`<br>`void SetSetting(string pluginId, string key, object? value)`<br>`bool IsComponentEnabled(string dllName, string componentType, string componentName)`<br>`void NotifySettingChanged(string pluginId, string key, object? value = null)`<br>`event Action<string, string>? SettingChanged`<br>`event Action<string, string, object?>? SettingChangedWithValue`<br>`event Action? ComponentEnablementChanged` | 读取并写入插件持久化配置，以及宿主保存的组件级启用状态。`SettingChanged` 只点明改的是什么；`SettingChangedWithValue` 还一并带上新的值，监听方因此不必再把设置回读一次。 |
| **`SettingsSearchService`** | `IReadOnlyList<SettingsSearchEntryInfo> GetEntries()`<br>`void Invalidate()` | 读取宿主当前可搜索的设置条目，并在动态提供的条目发生变化时通知宿主刷新缓存快照。 |
| **`SettingsWindowService`** | `bool ShowWindow(string? targetSection = null)`<br>`bool ShowEntry(SettingsSearchEntryInfo? entry)` | 请求宿主显示主题化设置窗口，或直接跳转到可搜索的设置条目，不启动 URI 或其他进程。 |
| **`SearchRefreshService`** | `void RefreshIfMatches(Func<string, bool> queryMatches)` | 用于异步即时计算源完成后台数据获取后，通知宿主原地重跑当前匹配的搜索查询并刷新视图。 |
| **`UserDataService`** | `string? GetUserDataDirectory()`<br>`string? GetSharedDataDirectory()` | 获取当前用户的专属数据目录（存放私有配置）与机器级全局共享数据目录。两者的返回类型都是可空的：宿主可能解析不出对应的目录，因此要检查 `null`，而不是假定路径一定存在。共享目录仅由服务写入；插件可读取，但自身文件须存放在用户数据目录。 |
| **`Logger`** | `void Log(string message, LogLevel level = LogLevel.Info)` | 统一输出日志至 `app.log`，并在设置中心的实时日志查看器中同步呈现。它位于根命名空间 `Lertaro.PluginSdk`，**不在** `Lertaro.PluginSdk.Services` 中。 |
| **`PluginPromptService`** | `IReadOnlyDictionary<string, object?>? Prompt(string title, IReadOnlyList<PluginConfigField> fields, IReadOnlyDictionary<string, object?>? initialValues = null)` | 弹出基于 Schema 自动渲染的小型模态输入对话框，向用户请求一次性输入。它是同步的：直接返回提交下来的值，用户取消时返回 `null`。不要对它 `await`。 |
| **`PluginNotificationService`** | `INotificationHandle Show(NotificationRequest request)`<br>`Task<NotificationResult> ShowAsync(NotificationRequest request)`<br>`bool Show(string title, string text, Action? onClick = null)` | 由宿主自己的窗口显示后台通知：右下角的卡片堆（`NotificationPosition.CardStack`），或屏幕下方居中的一行提示（`BottomNotice`）。宿主会把请求的时长裁剪到该位置允许的范围，按调用方程序集标注发送者（插件无法伪造自己的来源），并在独占全屏应用占据屏幕时把卡片降级成那一行提示。它不会把异常抛进插件的后台线程，句柄的任务也一定会完成，包括什么都没显示出来的情况；`bool` 重载只表示宿主是否受理了请求。需要用户应答而不是单纯告知时，改用 `PluginMessageBoxService`。完整契约——时长与上限、按 `Id` 替换、失败原因、点击语义、摆放与线程——见 [**通知**](./notifications)。 |
| **`PluginMessageBoxService`** | `MessageBoxResult Show(string messageBoxText, string caption = "", MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.OK)` | 请求由宿主显示消息框，使插件能够使用宿主的主题化界面；未注册宿主处理器时回退到系统消息框。 |
| **`ExplorerService`** | `void OpenDirectory(string directoryPath, string? fileNameOrFilePath = null)`<br>`void OpenFolder(string? folderPath)` | 打开指定文件夹或定位指定文件，遵循宿主配置的第三方文件管理器（或资源管理器多标签页），未配置时回退到系统资源管理器。`OpenFolder` 是“打开这个文件夹，没东西可打开就聚焦资源管理器”这种朴素形式，并且接受 `null`。 |

`SettingsSearchService.GetEntries()` 返回的条目索引只在当前宿主进程中有效。将条目直接传给 `SettingsWindowService.ShowEntry(...)`，SDK 会调用宿主回调，不会构造或启动 `lertaro://` URI。

`HistoryEntry` 提供 `Keyword`、`Path`、`Kind`、`Time`（Unix 秒）和 `Count`（项目被打开的次数）字段。`HistoryService.GetHistoryEntries()` 按最近打开顺序返回记录。

### 组件启用状态与高成本运行时

`PluginSettingsService.IsComponentEnabled(...)` 用于读取宿主保存的组件级开关。拥有目录监听器、后台工作线程、外部运行时或其他高成本状态的组件，应在初始化这些状态前先检查该开关，并订阅 `ComponentEnablementChanged`，在用户切换开关后启动或停止对应运行时。如果宿主没有注册回调或回调失败，该方法返回 `true`，从而保证插件在未接入完整宿主时仍可用。

## 2. Shell 原生文件操作封装

`Lertaro.PluginSdk.Shell.FileOperations` 封装了 Windows Shell 原生的 `IFileOperation` 接口。插件执行文件移动、复制与删除时，用户将获得与资源管理器完全一致的原生进度对话框、冲突替换提示与 `Ctrl+Z` 撤销支持：

```csharp
namespace Lertaro.PluginSdk.Shell.FileOperations;

// 批量粘贴或移动，合并为单次原子 Shell 操作。`move` 没有默认值：请说清你要的是哪一种。
public static class ShellPasteHelper
{
    public static void PasteAsync(
        IReadOnlyList<string> sourcePaths,
        string destinationFolder,
        bool move,
        Action? onCompleted = null);
}

// 安全放入回收站或永久删除。`permanent` 同样没有默认值。
public static class ShellDeleteHelper
{
    public static void DeleteAsync(IReadOnlyList<string> paths, bool permanent);
}

// 重命名单个已存在的文件或文件夹
public static class ShellRenameHelper
{
    public static void RenameAsync(string path, string newName);
}

// 从拖拽数据流中提取虚拟文件与网页
public static class VirtualFileExtractor
{
    public static bool HasVirtualFiles(IDataObject? data);                        // 是否存在 FileGroupDescriptorW
    public static List<string> Extract(IDataObject? data, string targetFolder);   // 同步；返回它写出的文件
    public static string? ResolveDestination(string targetFolder, string name);   // 输入为空白时返回 null；重名自动加 (2)
}
```

三个操作帮助类全都是**发出即忘的 `void`**，而不是 `Task`：`PasteAsync` 接收一个可选的 `onCompleted` 回调，其余的什么都不回报。参数集合为空或全为空白时会被忽略，而不是抛出异常。

> [!TIP]
> 帮助类会自行调度到**由 SDK 自己拥有、并在插件进程内启动**的 STA 工作线程上（`ShellOperationStaWorker`——它属于 SDK 内部实现，既不由你注册，也不由你配置），因此插件执行 Shell 操作时无需自己管理 COM 套间线程。刻意不提供取消，也不提供可供 await 的结果：原生的确认与进度对话框本身就是交互，需要知道结果的插件应当事后再去读取目标位置。

## 3. 应用生命周期与主题化插件窗口

`AppLifecycleService.RequestRestart()` 请求宿主应用执行优雅重启。宿主会启动替代进程，等待当前实例完成正常退出后再结束；插件无需自行启动可执行文件或关闭宿主。宿主接受请求时该方法返回 `true`。

对于插件自有的 WPF 内容，`Lertaro.PluginSdk.Windows.PluginWindow` 提供统一的圆角主题窗口框架。将插件视图赋给 `ContentHostControl.Content`，并通过 `Footer` 添加底部按钮。普通任务栏窗口使用 `PluginWindowMode.Window`；需要置顶且从 Alt+Tab 隐藏的对话框使用 `PluginWindowMode.Dialog`。不传入图标时会使用宿主的默认应用图标。

```csharp
var window = new PluginWindow("我的工具", 720, 470, PluginWindowMode.Dialog);
window.ContentHostControl.Content = new MyView();
window.Footer.Children.Add(new Button { Content = "确定", IsDefault = true });
window.ShowDialog();
```

`PluginWindow.ShowFooter` 在一个工具没有按钮时把整行底部栏关掉；省略图标参数时回退到宿主的默认应用图标。

## 4. 窗口、查询、主题与预览基础设施

| 宿主服务 | 核心方法与签名 | 功能说明 |
| :--- | :--- | :--- |
| **`SearchWindowService`** | `bool IsWindowVisible()`<br>`void ShowWindow(string? query = null)`<br>`void HideWindow()`<br>`void FocusQueryTextBox()` | 供插件查询并驱动宿主的搜索窗口，包括把一个起始查询交给它。 |
| **`SearchQueryService`** | `void ChangeQuery(string query, bool requery = false)`<br>`string StripQueryTokens(string query)` | 写入当前活动窗口的查询，可选择重新跑一次搜索，并剥掉宿主的尾部 Token，使插件看到的就是用户键入的纯文本。 |
| **`ThemeService`** | `bool IsDarkTheme` | 自绘内容的插件只需要这一个标志。宿主委托缺席时它会去读正在运行的 WPF 应用程序的资源，因此在启动器之外渲染的插件照样得到一个答案，而不是一个异常。 |
| **`PluginPreviewCache`** | `string Register(string title, string pluginName, Lazy<UserControl> factory, Func<object?>? iconProvider = null)`<br>`PluginPreviewEntry? GetEntry(string key)`<br>`UIElement? GetPreview(string key)` | 以惰性方式注册一个插件自己拥有的预览控件，并把宿主据以查找的键交给它，于是控件是在真正第一次被展示时才构建，而不是在加载时。 |
| **`PreviewActivationSignal`** | `void Begin()`<br>`void End()`<br>`bool IsActive`<br>`event Action? FocusStolen` / `void NotifyFocusStolen()` | 只要**跨进程**的原生处理程序处于被承载状态，它就一直置位——是整个查看会话，而不只是它的冷启动，因为与其渲染出来的内容交互可能弹出真正的顶层窗口。嵌套的 `Begin`/`End` 深度会被计数，因此提供者必须成对调用。*当前宿主行为，并非契约：*信号置位期间，搜索窗口不再把自己失活当作“用户点开了别处”。 |
| **`PreviewDialogSignal`** | `void NotifyDialogOpened()`<br>`void NotifyDialogClosed()`<br>`event Action? DialogOpened` / `DialogClosed` | 由提供者在原生处理程序自己的弹窗出现时触发（它存在所要应对的情形，就是预览加密文件时 Word 弹出的“输入密码”提示）。*当前宿主行为，并非契约：*对话框悬挂期间，快速窗口及其预览会被隐藏，好让用户能够到那个对话框，随后再把它们恢复。 |
| **`LocalSendTransferService`** | `void OpenSendWindow(IReadOnlyList<string>? files, string? text)` | 打开宿主的 LocalSend 窗口并预先装入一份文件列表或一段文本载荷，插件因此可以转手一次传输而完全不必自己拥有任何界面。 |
| **`ToolRunService`** | `Func<string, string, Task<string?>>? RunDopusPathsFunc`（由宿主赋值） | 在真正能够应答它的进程里运行外部工具。宿主的键盘钩子是提权的，而提权的 `dopusrt.exe` 永远等不到未提权的 Directory Opus 的回应（UIPI 会拦下回应），反之把进程降级需要钩子令牌并不具备的特权。App 运行在用户自己的权限级别上，因此钩子把请求转交给它。读取该委托并把 `null` 视为不可用；调用方要先创建输出文件，因为该工具填写的是一个已经存在的文件。 |
