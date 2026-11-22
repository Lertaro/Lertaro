# 窗口渲染、位置与会话

实现入口：[WindowParking](../App/Helpers/Visuals/WindowParking.cs)、[AppTypography](../App/Services/AppTypography.cs)、[DwmWindowCorners](../App/Helpers/Visuals/DwmWindowCorners.cs)。硬件测量来自 2026 年 10 月，用于解释现有取舍；通知会话按正则分支的当前实现核对。

## 文字与轮廓

- 有内容的窗口采用不透明表面，减少分层透明窗口的文字渲染代价。具体文字渲染方式仍由硬件和 WPF 决定。
- 不再全局强制 `TextFormattingMode=Display`。文本测量与排版使用同一模式，避免 `FormattedText` 宽度与实际显示不一致。
- 根节点通过 `AppTypography` 开启 `UseLayoutRounding / SnapsToDevicePixels`；计算字号使用 `SnapFontSize` 向上取档或取整。文字模糊时还需检查容器缩放、位图缓存与分数滚动偏移。
- 顶层轮廓使用 `CornerRadiusWindow=8`、内层带区 7 的资源。最大化后还原也读取资源；DWM 调用在 HWND 建立后的 `SourceInitialized` 执行。
- 不透明 Popup 覆盖整个客户区并使用方角，避免圆角空区变黑。
- 首帧处理使用停靠；动画限于窗口内元素与位置，避免整窗淡出改变合成方式。

## 首帧与位置

`Show()` 完成显示调用，`UpdateLayout()` 完成布局；像素提交仍须等待绘制。新增不透明窗口需检查首次显示、重建 HWND、改变尺寸和 DPI。

| 场景 | 当前做法 |
| --- | --- |
| 尚无目标位置 | `Park` 同时设置 Left/Top；Left 为 NaN 时只写 Top 会触发系统级联位置 |
| 调用方定位的卡片 | 先定横坐标，再 `ShowThenPlace`；保留目标列，只抬起纵坐标 |
| WPF 定位的大窗或模态窗 | Show/ShowDialog 前安装 `ParkUntilPainted`，在 `SourceInitialized` 捕获位置，再延后恢复 |
| 跨 DPI 落位 | 同步 WPF 的 DIP 状态，用 HWND 物理坐标落位；旧 DPI 的坐标先换算 |
| 隐藏的菜单锚点 | 复用无内容、全透明窗口；零尺寸仍可能占有屏幕像素 |

`AfterFrame` 经过 Render 和 Loaded 两次 dispatcher 调度。离屏等待 `ContentRendered` 的历史探针共 32 次，全部超时。停靠减少冷布局暴露，但多等 0–260 ms 后仍测得约 22–31 ms 的落位残差。

快速、内嵌和通知卡片经 `CardRenderMode` 使用软件合成，依据来自本机对照。QuickLook 与 PreviewOverlay 也先停靠再显示。

## 通知会话与窗口复用

[NotificationService](../App/Services/Notifications/NotificationService.cs) 在插件产生通知前订阅会话事件，再读取当前锁屏状态。[NotificationSessionState](../App/Services/Notifications/NotificationSessionState.cs) 读取 WTS 状态；WTS 不可用时通过输入桌面判断，暂停期间定期复查。锁屏隐藏窗口并冻结倒计时，解锁重置计时基点，从剩余时间继续。

| 场景 | 当前处理与回归要点 |
| --- | --- |
| 同 ID 的可见卡片替换 | 保留 Sequence 和窗口，旧 handle 以 Replaced 结束；取消尚未呈现的替换项时，也能找到并关闭原槽位 |
| 卡片高度超出工作区 | 回到待显示队列，保留测量高度与剩余时间；有容量后再呈现，handle 保持待完成 |
| Alt+F4 等外部关闭 | `Closed` 移除 runner 并通知队列；批量主动关闭只报告一次批次 |
| 插件的全部组件被禁用 | 先关闭通知接纳入口，再结束已显示和排队请求；重启生产者前重新开放。卸载中的迟到提交会被拒绝 |
| 显示器变化或开始拖动 | 变化时清除拖动锚点并重新排列，保持剩余时间；拖动开始先停止原位置动画 |

[NotificationWindowManager](../App/Services/Notifications/NotificationWindowManager.cs) 将新窗放到离屏位置，完成布局并等待绘制调度后排列。位置动画完成时核对仍是当前动画，避免旧回调覆盖新位置。

回归入口：[NotificationSessionStateTests](../Tests/App/Services/Notifications/NotificationSessionStateTests.cs)、[NotificationQueueTests](../Tests/App/Services/Notifications/NotificationQueueTests.cs)、[NotificationWindowManagerTests](../Tests/App/Services/Notifications/NotificationWindowManagerTests.cs)。

## 复用和尺寸的经验

- 内嵌窗口 dismiss 使用 Hide + Park，Dispose 才 Close。复用时重置 owner、会话、键盘布局、菜单与宿主尺寸缓存。
- 停靠或全透明窗口仍可能为 `IsVisible=true`。托盘与菜单锚点声明 `ITransientHostWindow`，对话框延迟居中前重新检查 owner，避免落到屏幕外。
- 上屏前先定宽度再量高度。同步召唤阶段的尺寸计算须允许执行，避免 `IsSearching` 等异步早退条件冻结空结果高度。
- 首次显示问题先查初始化，持续黑边先查布局。`SizeToContent` 被最大尺寸夹住时，量完固定实际尺寸，再让内容行分配剩余空间。
- 停靠状态的守卫放在实际定位入口，覆盖预热、DPIChanged 和位置更新。
- 设置页跳转先选择目标再显示或还原窗口，滚动与高亮待布局后执行，避免先闪现上一页面。

## 复核入口

将 `[WindowPark]` 的停靠、落位与首帧日志对齐内容到达和高度变化，分别判断未绘制帧、尺寸空条、额外顶层窗口与长期黑边。

[WindowParkingTests](../Tests/App/Helpers/Visuals/WindowParkingTests.cs)、[AppTypographyTests](../Tests/App/Helpers/Visuals/AppTypographyTests.cs) 和 [DwmWindowCornersTests](../Tests/App/Helpers/Visuals/DwmWindowCornersTests.cs) 验证机制；混合 DPI、混合显卡、焦点与拖拽使用真机复核。
