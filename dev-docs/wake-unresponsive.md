# 休眠唤醒后"无响应"调查

## 适用范围与核对基线

调查对象是用户报告的"休眠唤醒后程序无响应、无法输入"。探针实测完成与修复提交在 2026-10-15（`f6d3a14f`，当时写在 `feat/pinyin` 分支上，`App/Services/HookResumeWatcher.cs` 等文件现在都在 `main` 的树里）。根因仍未证实。

恢复路径的落地状态原先按 `main` = `20692d47` 复核过，本文又按 `9a6b9769` 重新核对了一遍，那份复核的结论全部仍然成立。文中 `文件.cs:行号` 按 `9a6b9769` 标注。

那些数字来自一个一次性探针：每个 case 起一个子进程，卡住 8 s 强杀，脚本没有入库。

## 结论与现行恢复路径

用户看到的"程序无响应、无法输入"几乎肯定等于**提权 Hook 进程里的低级键盘/鼠标钩子不再被调用**，而程序原本没有任何机制能发现或恢复它。已落地的是恢复机制，原因仍然没有实测证据。

现在的恢复链路是：`App/Services/HookResumeWatcher.cs` 监听 `SystemEvents.PowerModeChanged`（`:26`），发出无载荷的 `IpcMessageId.RearmHooks` = 48（`Core/Wire/IpcMessage.cs:73`）；`HookProcess.RearmHooks()` 把它 `PostThreadMessage` 到 `_nativeThreadId`（`Core/Hook/Ipc/HookProcess.cs:87`），而那个线程就是 `RunMessageLoop` 安装钩子的线程（`:273`），消息在它的 `GetMessage` 循环里被认出来（`:447`）。低级钩子派发到安装线程，从管道线程重装会再次掉钩，所以重装只能走这条路。`RearmHooksNow` 先 `Stop` 再 `Start` 键盘与鼠标两个钩子，顺带复位 `ExplorerStaInvoker` 的弃读预算（`:90`、`:98`）。

监听落在 App 侧，hook 进程里没有这段订阅，理由写在 `HookResumeWatcher.cs:14` 的注释里：`SystemEvents` 需要只有这个项目引用的桌面框架，而 App 是钩子没了之后还活着的那一半；订阅还得落在有泵的消息线程上。

全仓的电源事件处理到 `9a6b9769` 也只有这一处，`WM_POWERBROADCAST` 与 `SetThreadExecutionState` 仍然 0 命中。其余 `SystemEvents` 订阅是 `SessionSwitch`（锁屏/解锁，服务通知卡片，`App/Services/Notifications/NotificationService.cs:284`）、同一个文件里的 `DisplaySettingsChanged`（`:285`）和主题用的 `UserPreferenceChanged`（`App/Services/Theme/SystemThemeWatcher.cs:24`）。

## 已排除的 P0 理论

原推断：按键回调里 `ExplorerWindowClassifier` 的 `GetWindowText(rootHwnd)` 会向"唤醒后还没开始泵消息"的前台进程同步发 `WM_GETTEXT`，从而卡死钩子线程 → 超过 `LowLevelHooksTimeout` → 钩子被静默摘除。这处调用现在还在（`Core/Hook/ExplorerIntegration/ExplorerWindowClassifier.cs:137`）。

实测（目标窗口线程要么 `Sleep(Infinite)` 要么 `NtSuspendProcess` 全进程冻结，永不回消息）：

| 目标状态 | 调用 | 结果 |
|---|---|---|
| 空标题 + 不泵消息 | `GetWindowText` | 18 ms，len=0 |
| 有标题 + 不泵消息 | `GetWindowText` | 17 ms，返回缓存标题 |
| 空标题 + 全进程冻结 | `GetWindowText` / `GetWindowTextLength` | 18 ms / 8 ms |
| 冻结 + `IsHungAppWindow=False` | `GetWindowText` | 4–7 ms |
| 冻结 | `SendMessageTimeout(WM_GETTEXT, 50ms)` | 79 ms，`ret=0` `ERROR_TIMEOUT` |

顶层窗口读的是 USER 共享内存里的标题缓存，**不发** `WM_GETTEXT`；只有显式 send 才会等不回。这条路径没有无上限跨进程阻塞，"握锁永久卡死"不成立。

顺带测到 `IsHungAppWindow` 在窗口停止泵消息后约 1–3 s 才翻 True；本机 `powercfg -a` 只有 **S3**（无 S0ix、休眠未启用）；`LowLevelHooksTimeout` 在 HKCU 与 `HKU\.DEFAULT` 都未设置。

## 仍成立的机制事实

1. 钩子句柄被系统摘掉后看起来仍然有效。`KeyboardHookService.Start()` 见 `_hookId != 0` 直接 return（`Core/Hook/InlineSearch/KeyboardHookService.cs:92`），所以进程内没有任何东西能区分"摘掉的"和"装着的"，重装靠的是整条 Stop+Start。这就是"只能重启程序"的**永久性**来源。App 侧重启钩子的判据到 `9a6b9769` 仍然是"进程退出或管道报错"（`HookIpcClient.RunLoop`，`Core/Hook/Ipc/HookIpcClient.cs:273`），所以掉钩这件事只有那个进程外面的一方察觉得到，现在的 re-arm 正是 App 发进来的。
2. 键盘钩子和鼠标钩子装在同一个线程（`HookProcess.RunMessageLoop`），一起死。
3. `Win32Api.TrimWorkingSet()` = 两次 blocking 全量 GC + LOH 压缩 + `SetProcessWorkingSetSize(-1,-1)`（`Core/Win32Api.cs:267`）。它曾经挂在 Hook 进程里每次 Explorer 失活都跑一次，等于把硬缺页塞进每一次按键回调。这条已经停掉（`Core/Hook`、`Service` 下 0 调用点）。**App 侧的 `TrimWorkingSet` + EcoQoS 隐藏降速仍未动**（`App/App.xaml.cs:201`、`App/Services/IdleWorkingSetTrimmer.cs:69` 等），下面第二条猜测因此还开着。
4. `ExplorerStaInvoker` 的弃读预算（4 worker / 上限 8，`Core/Hook/ExplorerIntegration/ExplorerStaInvoker.cs:25,30`）只有卡住的读**返回**才减，否则是单向闩锁：一个跨过睡眠没回来的读会拒绝之后所有 shell 读取，一直到进程结束。现在由 re-arm 路径复位（`:50`）。

## 未证实的原因（按可能性排，都还缺数据）

- 某次回调超过 `LowLevelHooksTimeout` 的具体来源未知：唤醒瞬间磁盘争用（首屏一堆进程同时换页回来）+ 本进程刚被清空工作集 + EcoQoS 把隐藏进程整体降速（`PowerThrottlingHelper` 只在有窗口可见时关掉，`App/Services/PowerThrottlingHelper.cs:43`，Hook 进程完全没有参与这个开关）。
- 唤醒时计时雪崩（静默期 / USN 水位 / 网络盘重试 / 提醒大 delta 补发）与第一次呼出抢同一块盘。
- 也不排除是 App 侧 UI 线程问题（用户报告只说"无响应"，没说托盘图标是否还响应）。

## 要定死需要的那一次测量

真跑一次 S3：在钩子里加"回调进入计数 + 最后进入时间戳"的 volatile 心跳，和一个每 2 s 打一行（最近 N 秒是否收到回调 / 分类在飞阶段 / IPC 事件队列深度）的看门狗；睡之前记录计数，醒来后第一次呼出用 SendInput 触发，读 `app.log` / `hook.log` / `service.log` 三份时间线。

只有"唤醒后计数不再增长"被拍到，原因才算落地；本次的恢复修复不依赖它，可以先走。

## Win32 与 .NET 的坑

`Microsoft.Win32.PowerModes` 在 .NET 10 里只有 `StatusChange`/`Suspend`/`Resume` 三个成员，`PBT_APMRESUMEAUTOMATIC`(0x12) 根本不会送到 handler，所以判定规则写成"除 Suspend 之外都当唤醒处理"（`App/Services/HookResumeWatcher.cs:47`），没有列举 resume 成员。一次多余的 re-arm 只是两对 Unhook/SetWindowsHookEx，多吸收几个事件不花钱，而这个最早到达的 resume 正是用户还没碰任何东西时的那一次。
