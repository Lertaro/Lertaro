# Windows 宿主集成

合并 Hook 唤醒、对话框适配和快速切换记录中的现行经验。相关源码已按目录首页基线核对；AutoCAD 和唤醒现象来自 2026 年 10 月报告，本次没有真机复测。

## Hook 恢复不等于根因已证实

[HookResumeWatcher](../App/Services/HookResumeWatcher.cs) 在 App 消息线程订阅电源事件，非 Suspend 事件请求 re-arm。[HookProcess](../Core/Hook/Ipc/HookProcess.cs) 把消息投递到安装钩子的原生线程，再 Stop + Start 键盘/鼠标钩子并重置 `ExplorerStaInvoker` 弃读预算。不能在管道线程直接重装。

钩子句柄非零不能证明回调仍在到达；恢复机制已存在，但“唤醒后无响应”仍不能一概归为掉钩。复现时分别确认托盘/UI 是否响应、键鼠回调计数是否增长、IPC 是否正常，再决定调查哪一层。

历史冻结目标窗口探针中，跨进程顶层标题 `GetWindowText` 仍可返回缓存标题，显式 `SendMessageTimeout(WM_GETTEXT)` 则超时。这个实验否定了那一条无界阻塞假设，不能外推成任意窗口文本读取都不会阻塞。

## 跨进程调用边界

- UI 与低级钩子回调不承担无界宿主调用；UIA、COM 和目标窗口无响应时，读取要有超时、隔离或异步结果路径。
- Shell COM 对象按所属 STA 和生命周期使用；放进一个“STA 池”不等于串行化，也不能假定跨公寓共享对象安全。
- 系统字符串消息 `WM_GETTEXT / WM_SETTEXT / CB_GETLBTEXT / LB_GETTEXT` 使用调用方缓冲区；`CDM_GETFOLDERPATH` 这类私有消息需要目标进程缓冲区。消息有回应但结果为空时先核对编组边界。
- 适配器可能在提权 Hook 进程运行，用户会话的映射盘未必可见。`File.Exists / Directory.Exists` 返回 false 不能单独证明对话框无法导航；存在性判断要考虑令牌与会话差异。
- 快速切换的 Z 序只是近似，不等于真实激活历史。置顶、最小化和不激活挪窗都会改变顺序；不能把枚举排名当作焦点事件。

## AutoCAD：先分开路径读取与导航

[AutoCADFileDialogAdapter](../Plugins/AutoCAD/AutoCADFileDialogAdapter.cs) 仍通过 `CDM_GETFOLDERPATH` 读取目录。2026-10-17/18 报告中的自制另存为模板能响应消息，却复制 0 字符；手输目录后回车能够导航。这说明导航成功不证明当前目录可读，也不支持为修读取而改掉提交动作。

该模板的文件名 Edit 为 1001，另有 FolderView 和“保存于”ComboBox 13001；不能只凭 `#32770` 认作标准 comdlg32 模板。现有实现没有读取 13001 的目录回退。若继续处理，先验证选中项包含真实路径还是仅显示名，再决定实现，不能猜控件文本就是路径。

文件名回填只在框内仍是本次写入的路径且读数稳定时进行；用户改动、空框或宿主改写后的文本都应保留。当前 `ClassifyReading` 的精确比较有意选择保守退化，不能为了减少残留而覆盖用户输入。

宿主几何在后台读取，第一帧可能先用回退位置。若复用几何，缓存身份至少包含 HWND 和宿主矩形，位置变化后重新获取；不能把其他宿主或旧会话的测量直接搬过来。

## 验证方式

机制测试入口：[HookResumeWatcherTests](../Tests/App/Services/HookResumeWatcherTests.cs)、[AutoCADFileDialogAdapterTests](../Tests/Plugins/AutoCAD/AutoCADFileDialogAdapterTests.cs)。真实宿主验证至少包含保存/选目录、已有/不存在路径、已输入文件名和映射盘；日志要同时区分 `app.log` 与提权进程的 `hook.log`。

权限与 junction 迁移见 [settings-permissions.md](settings-permissions.md)，首帧和 DPI 见 [window-rendering.md](window-rendering.md)。
