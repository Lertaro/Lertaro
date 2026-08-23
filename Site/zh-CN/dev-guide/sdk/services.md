# 宿主服务

`PluginSdk.Services` 里的静态服务，把宿主应用的功能暴露给插件——每一个都是包装了宿主启动时接好的委托的薄静态类，所以不管底层实际运行的是什么，插件的调用方式都一样。

| 服务 | 用途 |
|---|---|
| `FuzzyMatchService` | `IsMatch(pattern, text)` —— `text`(或它的某个别名)是否匹配 fzf 语法的 `pattern`,用的是和宿主自身搜索完全一致的匹配逻辑;`GetHighlightMask(text, query)` —— 对应这一对 (text, query) 的逐字符高亮掩码,用的是宿主自己那套字面量/模糊/别名多级兜底算法(含中文拼音),这样插件自己结果的高亮就能和其他结果保持一致,而不是只能处理简单的字面量子串匹配。 |
| `TranslationService` | `Get(key)` / `Format(key, args)` 在运行时按当前语言查询;`LoadEmbeddedTranslations(assembly, cultureKey, typeName)` 加载插件自己内嵌的 JSON 语言包;`GetSupportedCultures(assembly)`;`GetCurrentCulture()` —— 应用当前选定的界面语言(比如 `"zh-CN"`),这是一个独立于操作系统语言的用户设置。只有你确实需要拿到原始语言代码本身时才用它(比如塞进 HTTP 的 `Accept-Language` 请求头,或者决定翻译 API 的目标语言)——`CultureInfo.CurrentUICulture` 反映的是操作系统的语言,不是这个设置,一旦用户的 Windows 语言和应用内语言不一致,两者就会悄悄对不上。 |
| `IconService` | `GetIcon(path, isDir)` 和 `GetThumbnail(path, size)` —— 带缓存的 Shell 图标/缩略图提取，插件不需要自己调 Windows 图标 API。 |
| `FavoritesService` | `GetFavorites()` —— 只读访问用户的[收藏夹](../../user-guide/settings/favorites)列表(`FavoriteItem`:Name、Path)。 |
| `HistoryService` | `GetHistoryEntries()` —— 每一条已记录的[历史记录](../../user-guide/settings/history)条目,按最近打开优先排序,类型是 `HistoryEntry { Keyword, Path, Kind, Time }`(`Kind` 是 `HistoryEntryKind`:`File` / `Folder` / `Application`;`Keyword` 是打开时输入框里的搜索文字,没打字就直接打开的话(比如从快速面板的标签里点开)就是空字符串;`Time` 是 Unix 秒)。同一个路径最多只会出现一次,归属于最近一次带它进来的那个关键字。 |
| `FileMetadataService` | `GetMetadataAsync(paths)` —— 批量查询 Size/Created/Modified/Accessed([`FileMetadata`](./abstractions#filemetadata))，用于查询**不属于**你当前结果集的路径——每个 `ISearchResult` 本身就通过自己的 `Metadata` 属性免费携带这些数据(参见[共享抽象契约](./abstractions#isearchresult))，所以只有拿到的路径不是来自结果对象(比如来自你自己的配置)时才需要用这个服务。 |
| `DirectoryIndexerService` | `RegisterDirectory(pluginId, path, recursive, filterPattern)` / `UnregisterDirectories(pluginId)` / `SearchDirectoriesAsync(pluginId, query, token)` —— 让插件注册自己的目录进行后台索引和 USN 监听，而不用自己重新实现这套机制。`WatchDirectories(pluginId, onChanged)` 会在**你自己注册的**目录发生磁盘变化时回调你，返回一个 `IDisposable` 用于退订。它是按注册方投递而不是广播：没有 id 要比对，也不会误处理别人的变化——变化落在谁的注册上，宿主本来就知道。回调在后台线程触发，并且已经做过防抖(一次批量拷贝在目录安静下来后只回调一次，而不是每个文件一次)；只有宿主能确实归因到你的目录的变化才会通知，归因不了时(比如整树被重新扫描替换)宁可通知你也不会保持沉默。`EnumerateDirectoryAsync(path, recursive, filterPattern, limit, token)` 从同一份索引(而不是文件系统)列出某个目录的内容——宿主已索引的盘完全不产生磁盘 I/O，没索引的目录则自动改为实时遍历，调用方不需要自己判断属于哪种情况。它是流式的；`filterPattern` 筛的是**文件**(目录一律返回，不需要就按 `IsDir` 过滤)；隐藏和系统条目永远不返回；递归列举时值得设 `limit`——`EnumerateDirectoryAsync(@"C:\", recursive: true)` 会老老实实把整个卷的每一条都交给你。 |
| `RecentFilesService` | `GetRecentFilesAsync(directories, limit, maxAgeMinutes, token)` —— 一组目录下最新的条目，最近的在前，由宿主的内存索引回答而不是去读磁盘。是把这些目录当作**一份**合并列表，而不是每个目录一份。只含文件：文件夹自己的修改时间在里面增删任何东西时都会变，那会把「正在其中工作」的文件夹顶到一份本该显示「工作了什么」的列表最前面。`limit` 传 0 表示不限条数，`maxAgeMinutes` 传 0 表示不限时间，但两个都不设的话，一个闲置的文件夹会仅仅因为没有更新的东西就一直端出一个月前的文件。宿主没有索引的目录不会被实时遍历，而是干脆不贡献任何条目——这里要的是快答案，要么没有；慢的那条路请用 `DirectoryIndexerService.EnumerateDirectoryAsync`。 |
| `ExplorerPathService` | `GetLastActivePath()` —— 资源管理器窗口或文件对话框最后显示的那个文件夹，从来没有过则为 `null`。它由宿主自己的窗口跟踪填入，跟的是**所有**应用的文件对话框，而不只是 Lertaro 自己的界面，所以它的含义是「用户最后真正在看的那个文件夹」——这是插件自己算不出来的。方向和 [`IActivePathCollector`](./core-search-actions) 相反：那个是插件**告诉**宿主某个第三方文件管理器正在显示什么，这个是向宿主打听。不保证仍然存在：它记的是用户去过哪，而那个文件夹可能早已被删掉或拔掉了。 |
| `PluginSettingsService` | `GetSetting<T>(pluginId, key, defaultValue)` —— 从宿主的配置存储里只读访问插件自己持久化的设置。回退分三层:用户存过就用持久化的值;没存过就用你 `IConfigurable` schema 里该字段自己声明的 `DefaultValue`;两者都没有才轮到你传进来的 `defaultValue` 兜底——这样 schema 里声明的默认值就是唯一权威来源,调用方不需要在代码里再手写一份重复的默认值。如果你把某个设置缓存了起来而不是每次都重新读取,记得订阅 `SettingChanged(pluginId, key)` 事件,在它为你的插件触发时清空缓存——宿主是在设置页保存之后立刻触发这个事件的,这是唯一可靠的失效时机(不管是按键触发还是轮询检查,都要等到别的什么东西凑巧触发了才会看到变化,或者干脆永远看不到)。 |
| `SearchRefreshService` | `RefreshIfMatches(queryMatches)` —— 给数据是异步到达的 `IInstantResultProvider` 用的(参见 [`IInstantResultProvider`](./core-search-actions#iinstantresultprovider)):等你的后台请求完成、结果也缓存好之后，调用这个方法并传入一个基于当前查询文字的判断函数，宿主会把所有匹配这个判断的、正在进行的搜索重新跑一遍，这样刚缓存好的结果就能直接显示出来，不需要用户重新输入。 |
| `Logger` | `Log(message, level = LogLevel.Info)` —— 写入 App 的日志文件，和宿主自己的日志行一样，显示在**设置 → 运行状态 → App** 里。 |
| `PluginPromptService` | `Prompt(title, fields, initialValues?)` —— 弹出一个小的模态窗口，向用户询问给定[`PluginConfigField`](./abstractions#iconfigurable)字段的值(用的正是 `IConfigurable` 的配置对话框那套字段 schema/渲染逻辑)，按 `Key` 匹配从 `initialValues` 预填，没有就用各字段自己的 `DefaultValue`。返回按字段 `Key` 索引的填写结果，用户取消则返回 `null`——这些值不会读取或写入插件真正持久化的设置，所以可以放心复用某个配置字段的 schema 单纯做一次性输入(比如"添加前先给它起个名字")，不会碰到背后真实的那个设置项。 |
| `UserDataService` | `GetUserDataDirectory()` —— 返回当前用户的专属数据目录（如安装版的 `%LOCALAPPDATA%\Lertaro` 或便携版的 `Data\Users\<hash>`），用于存放用户私有配置与插件数据；`GetSharedDataDirectory()` —— 返回机器级全局共享数据目录（如安装版的 `%PROGRAMDATA%\Lertaro` 或便携版的 `Data\Machine`），用于跨多用户共享运行时（如 Python / Node.js 嵌入环境）与全局公共缓存。 |

`LogLevel` 是 `Error` / `Warn` / `Info` / `Debug`，与[运行状态日志查看器](../../user-guide/settings/service-status)里的等级过滤器一致。

## Shell 文件操作

`Lertaro.PluginSdk.Shell.FileOperations` —— 对 Windows shell 自己的 `IFileOperation` 的一层薄封装。插件搬动文件时，用户看到的是和资源管理器一模一样的进度对话框、「文件已存在」提示和撤销记录，而不是一次行为略有不同的 `System.IO` 调用。

| 帮助类 | 用途 |
|---|---|
| `ShellPasteHelper` | `PasteAsync(sourcePaths, destinationFolder, move, onCompleted?)` —— 把任意多个路径复制(或移动)进同一个文件夹，合并成**一次** shell 操作，所以跨盘的多选也只弹一个对话框，而不是每个文件一个。发出即返回：native 对话框可能被用户晾在那里，阻塞调用方只会把界面冻住。`onCompleted` 在操作结束时触发——不管是复制完了还是用户取消了，因为对一个正显示目标文件夹的视图来说，这两种情况的应对是同一个：回去重新看一眼。 |
| `ShellDeleteHelper` | `DeleteAsync(paths, permanent)` —— 放进回收站或永久删除，同样合并成一次操作、一次确认。同样是发出即返回。 |
| `VirtualFileExtractor` | `HasVirtualFiles(dataObject)` / `Extract(dataObject, targetFolder)` —— 把拖动携带的、磁盘上还不存在的文件写出来：从浏览器拖出的图片、从邮件客户端拖出的附件、从压缩包预览里拖出的文件。它们都不是路径，所以 `IDataObject.GetData(DataFormats.FileDrop)` 什么也拿不到；真正到来的是一份列出文件名的描述符，加上按索引一次给一个的字节流，这个类做的就是把它拆出来。刻意不按类型过滤：拒绝拖动方愿意交出来的东西，意味着要么信扩展名、要么嗅探字节，这两件事都不该由它来做。`ResolveDestination(folder, name)` 是它「重名就加 (2) 而不是覆盖」的那套命名规则，单独暴露出来给自己写文件的调用方用。 |

两个异步帮助类都跑在 SDK 自己的 STA 工作线程上(`ShellOperationStaWorker`，由宿主启动)——shell 的 COM 接口要求 STA，共用一条意味着插件不必自己开一个套间。
