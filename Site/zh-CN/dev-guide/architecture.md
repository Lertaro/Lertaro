# 架构设计

![Lertaro 架构图](/architecture-zh-CN.svg)

## 进程拆分

Lertaro 运行为三个独立进程，按权限级别和生命周期有意拆分:

- **`Lertaro.Service`** —— 一个以 `LocalSystem` 身份运行的 Windows 服务。它负责全部文件索引工作:读取 NTFS/ReFS 磁盘的 USN 日志与 MFT、直接遍历并监听其他本地文件系统(它们没有日志可读)、扫描并缓存网络共享，并通过命名管道回答搜索查询。在 SYSTEM 级别运行意味着它可以读取所有用户账户都被允许看到的原始卷元数据，而不需要让交互式的 App 进程获得它本不需要的提升权限。
- **`Lertaro.App`** —— 用户态、Session 级别的 WPF 应用:搜索窗口、设置窗口、热键处理、动作菜单/QuickLook 界面都在这里。它通过命名管道(`Core.Services` 里的 `SearchService`/
  `UsnServicePipeServer`)和 Service 通信，从不直接访问磁盘索引。它自己也额外托管了一条按用户区分的管道(`AppSearchPipeService`)，让 `lff` 命令行伴侣工具(参见[命令行搜索](../user-guide/cli))
  能复用 App 已经初始化好的搜索状态——已加载的别名/插件提供方、已配置的网络盘索引——而不用一个独立的客户端进程自己重新初始化一遍。
- **`Lertaro.Service --hook`** —— 一个独立的小进程，专门托管低层级全局键盘钩子，这样钩子崩溃或者某个前台应用行为异常都不会连累主 App 进程。它还会加载插件的窗口集成适配器，并在自己进程里执行这些调用——见下文[插件在架构中的位置](#插件在架构中的位置)。

## 共享的 Core

`Core` 是一个被 Service 和 App 同时引用的类库。它包含:

- 搜索引擎(`Core/SearchIndex/Fzf/*`)—— 一套仿照 `fzf` 命令行工具算法实现的模糊匹配引擎，配合一个查询解析器(`SearchQueryParser`)处理盘符定向和路径模式搜索。
- 运行时索引(`Core/IndexV2/*`)—— 由 USN/MFT 读取结果构建的内存映射列式快照格式，配合一个内存中的增量覆盖层记录快照之后的变更。
- IPC 契约(`SearchRequestMessage`、`SearchResponseBinarySerializer` 等)—— App 和 Service 两边完全共用同一份定义，保证双方对线路格式的理解始终一致。
- `Logger` —— 写入各进程独立的日志文件(`service.log`、`app.log`、`hook.log`)，都可以在 App 的设置 → 运行状态 日志查看器里读取(但不是都能直接写入)。

## 插件在架构中的位置

插件是引用 `PluginSdk` 的 `.dll` 程序集，由 App 进程加载(见[快速上手](./getting-started)和[打包与发布](./packaging))。Lertaro 自带内置插件作为一等公民示例——`Lertaro.Plugins.CoreExtensions`(内置文件动作与 Shell 右键菜单集成)、`Lertaro.Plugins.PinyinAlias`(中文文件名拼音别名)以及 `Lertaro.Plugins.FlowLauncherBridge`(桥接 C#、Python 3.12、Node.js v20 LTS 及可执行文件格式的第三方 Flow Launcher 插件并提供纯净隔离运行环境)——参见[插件示例](./examples)了解原生插件的详细走读。

插件从不直接和 Service 通信;它们通过插件 SDK 参考里记录的接口和 App 交互，如果需要索引自定义目录，则通过 `DirectoryIndexerService` 代为向 Service 转发请求。

窗口集成类适配器是"只在 App 里跑"这条规则的唯一例外:
[`IActivePathCollector`、`IFileDialogAdapter`、`IInlineSearchAdapter`](./sdk/system-adapters) 的实现会被再加载一份到 Hook 进程里，它们的调用实际在 Hook 进程里执行，而不是在 App 里。这也是为什么
Lertaro 能操作一个以管理员身份运行的文件资源管理器/文件对话框/第三方文件管理器窗口——即使 App
本身从来不提权:Windows 不允许低权限进程向高权限进程发送输入，所以这类调用必须从一个和目标窗口权限级别相同的进程发起。
