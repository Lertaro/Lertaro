# 开发手册

Lertaro 提供了一套开放的插件 SDK(`Lertaro.PluginSdk`)，第三方程序集可以引用它来扩展搜索行为、添加右键菜单动作、与其他窗口集成，以及自定义界面。本手册记录了这套 SDK 的全部内容。

- **[架构设计](./architecture)** —— App、后台 Service 与插件之间是如何配合的。
- **[快速上手](./getting-started)** —— 搭建一个插件项目并加载它。
- **插件 SDK 参考**:
  - **[核心检索与动作](./sdk/core-search-actions)** —— 贡献搜索结果与结果动作。
  - **[系统与对话框适配](./sdk/system-adapters)** —— 与文件资源管理器、原生文件对话框及其他前台窗口集成。
  - **[界面与预览扩展](./sdk/ui-extensions)** —— 侧栏过滤器、结果列、文件预览、缩略图、主题与语言包。
  - **[共享抽象契约](./sdk/abstractions)** —— 插件收到的只读模型(`ISearchResult`、
    `IPluginSearchWindow`)以及配置模式(`IConfigurable`)。
  - **[宿主服务](./sdk/services)** —— 宿主暴露给插件的静态服务(图标、收藏夹、历史记录、文件元数据、目录索引、插件专属设置、日志)。
- **[插件示例](./examples)** —— 两个真实随附插件的案例分析。
- **[打包与发布](./packaging)** —— 编译好的插件 DLL 是如何被发现并加载的。

本手册里的每一个接口签名都直接对照当前 `PluginSdk` 源码核实过——如果发现和文档有出入，以代码为准。
