# 系统架构设计

Lertaro 采用先进的多进程隔离架构与模块化分层设计，确保在实现系统级毫秒检索与全方位窗口集成的同时，兼顾最高级别的运行安全与稳定性。

![Lertaro 架构图](/architecture-zh-CN.svg)

## 1. 三进程隔离模型

为了彻底规避单个组件异常导致整个系统崩溃，并将 Windows 特权限制在最小范围内，Lertaro 的运行时被明确划分为三个独立的进程：

### 1. 后台索引服务（`Lertaro.Service`）

- **运行身份**：以 Windows 系统级 `LocalSystem` 身份常驻运行的 Windows 服务。
- **职责范围**：承担全盘文件索引与增量监听的核心重任。直接读取 NTFS / ReFS 磁盘底层的 USN 变更日志与 \$MFT 主文件表；实时监听 FAT32 / exFAT 磁盘变更；定时抓取并缓存 SMB / NAS 网络共享。
- **安全与性能考量**：运行在 SYSTEM 级别使服务无需弹出任何 UAC 提权弹窗即可直接读取原始磁盘卷的元数据；同时通过高性能命名管道向用户态 App 返回检索结果，彻底避免了让前台 UI 进程持有不必要的全局高权限。

### 2. 用户交互主程序（`Lertaro.App`）

- **运行身份**：标准 Windows 用户态、Session 隔离的 WPF 前台桌面应用程序。
- **职责范围**：承载快速搜索居中浮窗、完整主搜索窗口、设置中心、全局快捷键分发、动作菜单（`Ctrl+O`）以及 QuickLook 文件即时预览界面。
- **IPC 桥梁与 CLI 托管**：通过双向命名管道 `LertaroPipe` 与后台 Service 通信（`Core.Services.Search.SearchService` 是 App 侧的客户端）；同时，App 自身还托管了一条面向当前用户的专属命名管道服务（`AppSearchPipeService`），使外部伴随工具（如 `lff` 命令行工具）能直接复用 App 已经构建好的内存别名表、插件提供者与网络盘缓存，无需重复初始化。

### 3. 全局键盘钩子与窗口适配进程（`Lertaro.Service --hook`）

- **运行身份**：由后台服务拉起的独立辅助进程。它**仅在登录账户是真正的管理员时才提权启动**；否则它以当前用户自己的令牌运行，因此下文的权限突破只在这样的机器上可用。
- **职责范围**：托管低级全局键盘钩子（Low-Level Keyboard Hook）与鼠标全局监听。
- **UIPI 权限突破与崩溃隔离**：在 Windows 安全体系中，低完整性级别的用户态进程无法向以管理员身份运行的高权限窗口发送窗口消息或模拟输入（UIPI 隔离）。通过在该 Hook 进程中运行窗口集成适配器（[`IActivePathCollector`、`IFileDialogAdapter`、`IInlineSearchAdapter`](./sdk/system-adapters)），只要钩子进程是以提权身份启动的，Lertaro 就能读取并操作由管理员身份启动的文件资源管理器、Total Commander 或第三方对话框。同时，即使底层钩子因第三方游戏的反作弊模块产生异常，也不会影响主 App 进程的正常运行。

## 2. 共享核心层（Shared Core Library）

`Lertaro.Core` 是被 Service、App 和 Hook 进程同时引用的基础类库，主要包含以下关键模块：

- **自研 fzf 模糊匹配引擎（`Core/SearchIndex/Fzf/*`）**：高效复刻并优化了知名 `fzf` 算法的跳跃字符模糊匹配、子串分段与字符级高亮计算，配合 `SearchQueryParser` 实现盘符定向与路径模式切分。
- **列式内存索引（`Core/IndexV2/*`）**：采用内存映射列式快照（Columnar Snapshot）与内存增量覆盖层（Delta Overlay），实现亿级文件条目的亚毫秒级检索。
- **二进制 IPC 通信契约**：定义了 `SearchRequestMessage`、`SearchResponseBinarySerializer` 等标准二进制数据协议，确保多进程间零拷贝高效序列化。
- **统一多进程日志系统（`Logger`）**：分别输出至 `service.log`、`app.log` 与 `hook.log`，并由 App 的设置中心日志查看器统一代理读取与呈现。

## 3. 插件系统在架构中的定位

所有第三方及内置插件均基于 `Lertaro.PluginSdk` 构建，由 `Lertaro.App` 进程在启动时自动反射扫描并加载：

- **零特权直接通信**：插件自身的代码与加载它的进程同进程运行——通常是 App，但 Service 也会加载的两种类型见下一条。插件不会自行跨越进程边界，因此需要自定义目录索引的插件会通过 `DirectoryIndexerService` 把这项工作委托给宿主，而不是自己去遍历磁盘。
- **选择性双重加载**：搜索源、动作与界面扩展仅在 App 进程中运行。有三类组件会被同时加载到其他进程：窗口与文件对话框适配器（`IActivePathCollector`、`IFileDialogAdapter`、`IInlineSearchAdapter`）进入 Hook 进程以处理跨完整性级别的窗口自动化；`IAliasProvider` 与 `ITranslationProvider` 还会由 **Service** 额外加载（`ServicePluginLoader`），因为索引器要构建别名行，需要与界面呈现一致的转写与命名。
