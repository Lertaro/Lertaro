# 通知

`Lertaro.PluginSdk.Services.PluginNotificationService` 让插件在后台通过宿主自己的窗口获得用户注意——不需要额外进程，不走系统 toast 通道，也不与宿主 UI 程序集耦合。任何东西在屏幕上停留多久、停在哪里，由宿主决定，而不是插件。

## 1. 入口方法

| 签名 | 返回 | 何时使用 |
| :--- | :--- | :--- |
| `INotificationHandle Show(NotificationRequest request)` | 立刻拿到一个句柄 | 你希望之后替换或取消这条通知 |
| `Task<NotificationResult> ShowAsync(NotificationRequest request)` | 最终结局 | 你只关心它是怎么结束的 |
| `bool Show(string title, string text, Action? onClick = null)` | 是否有宿主受理了它 | 旧的两参数形式；返回 `false` 意味着没有接入宿主，因此你还能走兜底方案 |

每个入口方法从插件自己的后台线程调用都是安全的，并且**永不抛异常**——逸出的异常会把调用方的循环一起带崩。当完全没有指派宿主委托时（插件运行在启动器之外），调用只是一次安静的空操作，答复是一个已经以 `Unavailable` 结束的句柄；只有当宿主自己的委托抛出异常时，这个请求才会以 `Warn` 记录。

`INotificationHandle` 恰好有两个成员：`Task<NotificationResult> Completion` 与 `void Dismiss()`。`Completion` **一定**会结束，包括那些从未到达屏幕的通知，所以对它的 `await` 不可能把线程停在那里。`Dismiss()` 会像用户亲自关闭那样关掉一张已显示的卡片，卡片消失之后它是空操作，并且绝不会扭转一个已经交付的结果。

需要自己拥有的窗口的插件请改用 [`Windows.PluginWindow`](./services)；需要的是*一个回答*而不是一条告知的插件请改用 `PluginMessageBoxService`。

## 2. `NotificationRequest`

| 属性 | 默认值 | 行为 |
| :--- | :--- | :--- |
| `string? Id` | `null` | 相同 `Id` 的两个请求指向同一条通知。见**按 Id 替换**（§4）。 |
| `string Title` | `string.Empty` | 卡片标题。`BottomNotice` 会忽略它，因为它没有标题行。 |
| `string Message` | `string.Empty` | 正文。`Title` 与 `Message` 二者都为空白或仅含空白时，请求会被判为 `InvalidRequest` 而拒绝——没有任何可展示的内容。 |
| `NotificationLevel Level` | `Info` | `Info` / `Warn` / `Error`。只决定图标与语义色，其余一概不影响。 |
| `NotificationPosition Position` | `CardStack` | `CardStack` 或 `BottomNotice`。各自有自己的队列和自己的规则，彼此绝不互相阻塞。 |
| `double? DurationSeconds` | `null` | `null` 采用该位置自己的默认值。超出该位置允许范围的显式取值会被**裁剪到最近的边界**，而不会被拒绝；`NaN` 或负数落在较短的那一端。 |
| `Action? OnClick` | `null` | 在关闭之外额外执行。见**点击回调**（§6）。 |

## 3. 两个位置

| | `CardStack`（右下角） | `BottomNotice`（底部居中） |
| :--- | :--- | :--- |
| 形态 | 带标题、来源名称、✕ 与“全部标记为已读”的卡片 | 一行文字；没有标题、没有来源、没有控件 |
| 同时存在 | 5 张可见，其余排队 | 1 条——新的立刻替换当前那条，没有任何收尾要等 |
| 默认时长 | 8 秒 | 4 秒 |
| 裁剪区间 | 2 – 30 秒 | 2 – 10 秒 |
| `OnClick` | 生效 | 忽略（没有任何可交互的东西） |
| 全屏应用独占屏幕 | 降级成那一行提示，上限 5 秒 | 不受影响 |

批量发出通知时，还有两个节流数字值得知道：

- 宿主只跑**一个 100 ms 的 tick**，而每个 tick **最多**把一张排队中的卡片提升到屏幕上。因此一波突发会自己分散到达时刻；而每张卡片都从自己出现的那一刻开始计时，分散了到达也就分散了到期——一波突发不再一次性吃掉卡片堆里的五个位置。
- **单个插件最多有 10 个被受理的卡片请求**（在屏幕上的*和*仍在排队的合计）。第 11 个会作为 `QueueFull` 被丢弃并记录。这条上限只在卡片准入这条路径上强制：`BottomNotice` 请求永远不计入它，因为那一行提示只有一个槽位、是自己覆盖自己的。插件之间没有全局的待处理上限，所以一个插件不会被另一个插件的突发饿死，但失控的生产者同样不受全局限制。

## 4. 按 Id 替换

给请求带上 `Id`，就是反复出现的状态更新避免把整屏铺满自己副本的办法：新请求的 `Id` 与某张**可见**卡片相同时，会就地覆盖那张卡片——仍占着堆里原来的位置，并不会被顶到最上面——而先前那个调用方的句柄以 `Replaced` 结束。正是那些需要知道自己已被取代的调用方才会握着句柄不放。

有一个限制要留意：去重只拿当前屏幕上的卡片来匹配，**不**拿仍在队列里等待的卡片匹配。屏幕已经占满时，一个插件连续发出同一个 `Id` 的五份，会五份全部排队。

## 5. 结果与失败原因

`NotificationResult` 是 `Succeeded` 加上一个可选的 `NotificationFailure`。第一个到达某条通知的终局生效；此后没有任何东西会改写它。

| `NotificationFailure` | 何时出现 |
| :--- | :--- |
| `Unavailable` | 没有宿主服务该请求——插件运行在启动器之外，或宿主委托抛了异常。 |
| `InvalidRequest` | `Title` 与 `Message` 二者都为空白或仅含空白。 |
| `QueueFull` | 该插件已经有 10 个卡片请求被受理。 |
| `Replaced` | 更新的请求带着同一个 `Id`。`BottomNotice` 的覆盖也会让被取代的那条请求以此结束，**即使两条都没有 `Id`**，因为那一行提示是始终互相覆盖的单个槽位。 |
| `CancelledByPluginUnload` | 插件在自己的卡片正在显示或仍在排队时被停用或卸载。 |
| `HostShuttingDown` | 启动器正在关闭，并把通知一起带走。 |

已经到达屏幕并走完时间的通知，以及被用户或被 `Dismiss()` 关掉的通知，都以 `NotificationResult.Success` 结束——那些是结局，不是失败。

## 6. 点击回调

`OnClick` 在**任何由用户发起的关闭**时运行，代码把这些当作同一种结果：点击卡片本体与点击 ✕ 走的是同一条路径。倒计时自然走完**不会**运行它，§5 里的任何失败同样不会。

回调是插件代码，所以其中的异常会被捕获、连同来源名称一起记录，并且绝不会带回启动器的输入管线里。可能会失败的实际工作请放到回调之外去做；回调的职责是把用户带到你的窗口（仓库内的例子是 `Plugins/Calendar`，它用它来激活日历视图）。

## 7. 插件无法伪造的来源归属

卡片上打印的名称来自 `Assembly.GetCallingAssembly()`，在外观类的每个**公开**入口处读取，再交由宿主映射：名为 `Lertaro.App*` 的程序集显示为 `Lertaro`，已注册的插件按其托管显示名展示，其余一律回退到程序集名并剥掉 `Lertaro.Plugins.` 前缀。以字符串形式传入来源名称是刻意不提供的——这是插件最不该由自己选择的那一项归属。

## 8. 位置与表面

- **哪一块屏幕**：前台窗口所在的屏幕，其次是光标所在的屏幕，最后是主屏。工作区会经由该显示器的 DPI 换算成 DIP，所以在 200 % 缩放的面板上，卡片与在 100 % 时是同样的物理尺寸。
- **堆叠顺序**：右下角，按到达次序排列；被移除卡片上方的卡片会重新滑动归位（这是唯一的动画，约 200 ms，入场时下落 40 DIP）。
- **尺寸**：卡片最高不超过工作区高度的一半（`min(260 DIP, 50 %)`），因此一条长消息无法吃掉整块小屏幕。
- **拖动**：卡片只能凭标题栏拖动，被拖过的卡片保有自己的那个角。显示设置变化会让卡片堆重新锚定，这会丢掉用户拖动过的位置——比起让卡片跑到屏幕之外，这是更划算的取舍。
- **表面种类**：在构造时依据当前主题的 `WindowOpacity` 一次决定。完全不透明的主题得到由窗口管理器做圆角的普通窗口，从而保住 ClearType；半透明主题得到分层窗口，其圆角必须改为自己绘制并裁剪。因此跨主题切换显示的卡片，会一直保持它开始时的哪一种，直到它消失。
- **Topmost** 始终开启，而且这些窗口不出现在 Alt+Tab 中（`WS_EX_TOOLWINDOW`）。通知没有淡入淡出：没有任何东西动画 `Window.Opacity`，因为那会牺牲 ClearType，并且每帧多一次逐像素合成。到达提示是一个已经不透明的窗口内部的一次边框闪烁（约 750 ms）。

## 9. 线程与生命周期

- 可以从任何线程调用。宿主把所有通知入口串行化在同一把闸门上，因此 `Show` 是一次简短的阻塞调用——不要在逐项处理的热循环里驱动它；改成发一条汇总卡片。
- 窗口以 `DispatcherPriority.Background` 呈现，因此一波通知突发不会饿到启动器自己的输入工作。
- **会话被锁定时**（屏保、`Win+L`），倒计时冻结、卡片隐藏；会话解锁后它们回来，并且仍然带着自己原本被给到的时间。
- 停用或卸载插件会取消它正在显示*和*仍在排队的卡片（`CancelledByPluginUnload`）；启动器的有序关闭对所有未完成的通知做同样的事（`HostShuttingDown`）。
- 这个构建里**没有任何面向用户的通知设置**：没有勿扰模式、没有按插件的开关，上面那些位置与时长规则都属于宿主、不可配置。请据此设计你的请求——一个不停说话的插件应该自己安静下来，而不是请用户把它静音。

## 10. 示例

```csharp
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;

// 每条提醒一张卡片；若它还在屏幕上，就替换它而不是重复它。
var handle = PluginNotificationService.Show(new NotificationRequest
{
    Id = $"reminder-{due.Item.Id}",
    Title = TranslationService.Get("Reminder_Title"),
    Message = due.Item.Text,
    Level = NotificationLevel.Warn,
    DurationSeconds = 12,               // 由宿主裁剪进 2..30
    OnClick = () => CalendarView.ShowOrActivate(),
});

// 只在确实需要终局的地方才 await 它；这个任务总会完成。
_ = handle.Completion.ContinueWith(t =>
{
    if (t.Result.Failure is NotificationFailure.Replaced)
        Logger.Log($"Superseded by a newer reminder: {due.Item.Id}", LogLevel.Debug);
});
```

> [!NOTE]
> 本页的签名、默认值与限制均对照 `PluginSdk/Abstractions/NotificationRequest.cs`、`PluginSdk/Abstractions/INotificationHandle.cs`、`PluginSdk/Services/PluginNotificationService.cs`，以及 `App/Services/Notifications/` 与 `App/Views/Notifications/` 下的宿主侧代码读取确认。
