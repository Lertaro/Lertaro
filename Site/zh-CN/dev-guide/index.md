# 开发者手册

欢迎查阅 Lertaro 开发者参考手册。Lertaro 采用先进的解耦架构与开放的插件化生态体系，提供了官方 SDK 程序集 `Lertaro.PluginSdk`。开发者可以通过引用该 SDK，为 Lertaro 贡献自定义搜索源、扩展右键上下文动作、深度适配第三方文件管理器与原生对话框，或者定制美化主题与文件预览组件。

## 1. 架构与开发流程

- **[系统架构设计](./architecture)** —— 详解 SYSTEM 级 Windows 索引服务、用户态 WPF 交互进程与独立键盘钩子进程的三进程隔离模型与命名管道 IPC 通信机制。
- **[快速上手指南](./getting-started)** —— 从零创建插件类库工程、引用 SDK、实现 `IPlugin` 入口以及本地调试的最佳实践。
- **[打包与分发](./packaging)** —— 插件程序集目录结构规范、第三方托管/原生依赖库打包、多语言 JSON 资源内嵌与 PostBuild 自动部署。
- **[官方插件范例](./examples)** —— 深度剖析随包开源的 `CoreExtensions`、`PinyinAlias` 与 `FlowLauncherBridge` 等真实插件的最佳实践代码。

## 2. 插件 SDK 接口参考

| SDK 模块分类 | 核心接口与服务 | 关键功能说明 |
| :--- | :--- | :--- |
| **[核心检索与动作](./sdk/core-search-actions)** | `ISearchableItemProvider`<br>`IInstantResultProvider`<br>`IFullSearchFileResultProvider`<br>`IAliasProvider`<br>`IQueryTokenProvider`<br>`ISearchResultAction`<br>`IDynamicActionProvider` | 贡献静态索引源、高频即时计算答案、完整搜索窗口文件结果、非 ASCII 别名转写引擎、尾部 Token 后缀处理器以及静态/动态上下文动作菜单。 |
| **[系统与对话框适配](./sdk/system-adapters)** | `IActivePathCollector`<br>`IFileDialogAdapter`<br>`IInlineSearchAdapter`<br>`IQuickNavigationProvider` | 探测前台管理器活动目录、挂载原生文件对话框、内嵌搜索条并双向同步选中状态、贡献鼠标快速导航级联菜单。 |
| **[界面与预览扩展](./sdk/ui-extensions)** | `ISidebarFilterProvider`<br>`IResultColumnProvider`<br>`IQuickPanelTabProvider`<br>`IFilePreviewProvider`<br>`IThumbnailProvider`<br>`IThemeProvider`<br>`ITranslationProvider` | 扩展侧边栏筛选分类、表格视图自定义列、快速面板动态工作区标签、QuickLook 自定义渲染器与缩略图提取、WPF 资源字典主题包与多语言 i18n。 |
| **[共享抽象契约](./sdk/abstractions)** | `ISearchResult`<br>`FileMetadata`<br>`IPluginSearchWindow`<br>`IConfigurable` | 检索结果只读数据契约、纳秒级文件时间戳与大小元数据、宿主窗口安全控制句柄与基于模式驱动的原生配置表单。 |
| **[宿主开放服务](./sdk/services)** | `FuzzyMatchService`<br>`TranslationService`<br>`IconService`<br>`FavoritesService`<br>`HistoryService`<br>`FileMetadataService`<br>`DirectoryIndexerService`<br>`RecentFilesService`<br>`ExplorerPathService`<br>`PluginSettingsService`<br>`SettingsSearchService`<br>`SettingsWindowService`<br>`SearchRefreshService`<br>`UserDataService`<br>`Logger` | 宿主暴露的高性能基础设施：fzf 模糊匹配与高亮掩码、多语言解析、带缓存图标提取、收藏管理与历史读取、后台目录索引代理、用户数据目录隔离及 Shell 原生文件操作。 |

> [!NOTE]
> 本手册所有接口签名、方法参数与行为契约均直接对照 `Lertaro.PluginSdk` 源码严格编写并校验。
