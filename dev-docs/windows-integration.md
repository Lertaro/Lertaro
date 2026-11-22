# Windows 宿主与管道集成

本文记录 Hook 恢复、跨进程调用和搜索管道的现行约束。AutoCAD 与唤醒现象来自 2026 年 10 月的宿主报告，保留作复现线索。

## Hook 恢复

[HookResumeWatcher](../App/Services/HookResumeWatcher.cs) 在 App 消息线程订阅电源事件，非 Suspend 事件请求 re-arm。[HookProcess](../Core/Hook/Ipc/HookProcess.cs) 将消息投递到安装钩子的原生线程，Stop + Start 键盘/鼠标钩子并重置 `ExplorerStaInvoker` 弃读预算。

唤醒后无响应时，分别确认托盘/UI 响应、键鼠回调计数和 IPC。钩子句柄非零时，回调仍可能已经停止。

历史冻结目标窗口的探针中，跨进程顶层标题 `GetWindowText` 返回缓存标题，显式 `SendMessageTimeout(WM_GETTEXT)` 则超时。因此该复现应继续调查具体调用，不能只凭函数名认定无界阻塞。

## 跨进程调用

- UI 与低级钩子回调中的宿主读取须有超时、隔离或异步结果路径，尤其是 UIA、COM 和无响应窗口。
- Shell COM 对象遵守所属 STA 与生命周期；共享对象时需明确公寓和串行访问。
- 系统字符串消息 `WM_GETTEXT / WM_SETTEXT / CB_GETLBTEXT / LB_GETTEXT` 使用调用方缓冲区；`CDM_GETFOLDERPATH` 等私有消息需要目标进程缓冲区。消息有回应但结果为空时，先核对编组。
- 提权 Hook 进程未必看见用户会话的映射盘。对话框导航与 `File.Exists / Directory.Exists` 分别验证。
- 快速切换使用近似 Z 序；真实激活历史须由焦点事件判断。置顶、最小化和不激活挪窗都会改变枚举顺序。

## 完整路径的目录跳转

[DirectoryJumpInstantProvider](../Plugins/CoreExtensions/Providers/InstantAnswers/DirectoryJumpInstantProvider.cs) 读取原始查询，去成对外引号并展开环境变量、Shell 路径，确认目录可达后提供打开行。支持完整盘符路径、UNC 和虚拟路径；相对路径没有可靠锚点，直接拒绝。裸 `%TEMP%` 交给环境变量 provider，带后续路径时才尝试目录跳转。

打开行通过 `ExplorerService.OpenFolder` 执行，保留虚拟路径和默认文件管理器的路由。完整 UNC 的可达性探测仍受原生 SMB 超时约束。回归入口：[DirectoryJumpInstantProviderTests](../Tests/Plugins/CoreExtensions/Providers/InstantAnswers/DirectoryJumpInstantProviderTests.cs)。

## 搜索管道

[AppSearchPipeListener](../App/Services/Pipe/AppSearchPipeListener.cs) 使用带用户 SID 和会话后缀的管道名，并由 SID ACL 限定当前用户，支持同账户的普通与提权客户端。无法构造 ACL 时停止监听，避免扩大可访问范围。

App 搜索管道最多接纳 16 个客户端，超额连接立即关闭，处理器退出后释放槽位。每次请求读取限时 30 秒，覆盖空闲连接及半包；该限时用于读取请求，搜索期间由断开监视器取消查询。

[UsnServicePipeRequestProcessor](../Core/Services/UsnServicePipeRequestProcessor.cs) 从内核获取客户端 PID，验证当前安装的 App 身份。Initialize 与 ClearPathCaches 需 App 身份；重建、删除/取消索引、写机器设置及读/清服务日志还需管理员账户，包含 UAC 受限的管理员令牌。普通搜索沿用调用者可见性检查。拒绝时返回 Error 并记录原因，初始化调用也读取并记录该响应。

| 协议 | 当前版本与行为 |
| --- | --- |
| [SearchRequestBinarySerializer](../Core/Wire/SearchRequestBinarySerializer.cs) | v12，读取时拒绝版本不一致 |
| [SearchResponseBinarySerializer](../Core/Wire/SearchResponseBinarySerializer.cs) | v7，服务正则超时使用独立失败帧 |
| [SearchResultWithHighlightBinarySerializer](../Core/Wire/SearchResultWithHighlightBinarySerializer.cs) | v3，App→CLI 传递正则超时与查询错误 |

Service、App 与 CLI 使用配套版本。失败帧在读取端转为查询异常，窗口清除部分结果。CLI 非交互搜索成功结束后输出结果，失败返回非零退出码；交互搜索失败时清空当前结果并显示错误。

回归入口：[ConnectionSlotGateTests](../Tests/App/Services/Pipe/ConnectionSlotGateTests.cs)、[SearchRequestReadCancellationTests](../Tests/Core/Wire/SearchRequestReadCancellationTests.cs)、[UsnServicePipeAuthorizationTests](../Tests/Core/Services/UsnServicePipeAuthorizationTests.cs)、[SearchRegexFailureTests](../Tests/Core/Wire/SearchRegexFailureTests.cs)。

## AutoCAD 路径读取与导航

[AutoCADFileDialogAdapter](../Plugins/AutoCAD/AutoCADFileDialogAdapter.cs) 通过 `CDM_GETFOLDERPATH` 读取目录。2026-10-17/18 的自制另存为模板能响应消息却复制 0 字符，手输目录后回车能够导航。读取失败和导航成功需分别处理。

该模板的文件名 Edit 为 1001，另有 FolderView 和“保存于”ComboBox 13001；`#32770` 只说明窗口类。当前未从 13001 回退读取目录，后续实现须先验证选中项是实际路径还是显示名。

文件名回填要求框内仍为本次写入路径且读数稳定；用户改动、空框或宿主改写后的文本均保留。`ClassifyReading` 的精确比较用于防止覆盖用户输入。

宿主几何在后台读取，首帧可能使用回退位置。缓存身份包含 HWND 与宿主矩形，位置变化后重新获取。

## 验证入口

[HookResumeWatcherTests](../Tests/App/Services/HookResumeWatcherTests.cs) 和 [AutoCADFileDialogAdapterTests](../Tests/Plugins/AutoCAD/AutoCADFileDialogAdapterTests.cs) 验证机制。真实宿主覆盖保存/选目录、已有/不存在路径、已输入文件名与映射盘，分别读取 `app.log` 和提权进程的 `hook.log`。

权限与 junction 迁移见 [settings-permissions.md](settings-permissions.md)，首帧和 DPI 见 [window-rendering.md](window-rendering.md)。
