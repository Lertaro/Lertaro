# 窗口渲染与清晰度

现行入口：[WindowParking](../App/Helpers/Visuals/WindowParking.cs)、[AppTypography](../App/Services/AppTypography.cs)、[DwmWindowCorners](../App/Helpers/Visuals/DwmWindowCorners.cs)。本页保留实现约束；硬件相关结论来自 2026 年 10 月的历史探针，本次未重新做屏幕测量。

## 文字与轮廓

- 有内容的窗口采用不透明表面，避免分层透明窗口带来的文字渲染代价；不透明只是提供渲染条件，不能承诺所有硬件都使用 ClearType。
- 不再全局强制 `TextFormattingMode=Display`。文本测量与实际排版必须一致；只改渲染模式、不改 `FormattedText` 的测量方式，会造成宽度偏差。
- 根节点通过 `AppTypography` 开启 `UseLayoutRounding / SnapsToDevicePixels`。计算字号使用 `SnapFontSize` 向上取档或取整；这首先是尺寸约定，不应再解释成 Ideal 模式下清晰度的充分条件。
- 顶层轮廓用 `CornerRadiusWindow=8`、内层带区 7 的资源约定。最大化后还原也读资源，不写第二份常量。DWM 请求需要 HWND，构造期调用应延后到 `SourceInitialized`。
- 不透明 Popup 必须覆盖整个客户区；圆角留下的空角可能变黑。弹出层使用方角，不把顶层窗口的 DWM 圆角方案照搬过去。
- 整窗或内容根淡出不能用于掩盖首帧；内部高亮动画与整窗透明度是不同层。文字模糊还要检查容器缩放、位图缓存和分数滚动偏移。

## 首帧与位置

`Show()` 返回只说明窗口已显示，`UpdateLayout()` 只完成布局，都不表示像素已提交。新增不透明窗口必须检查第一次 Show、重建 HWND、改变尺寸和 DPI 这几条路径。

| 场景 | 现行做法与前提 |
| --- | --- |
| 尚无目标位置的窗口 | `Park` 同时设置 Left/Top；只写 Top 而 Left 为 NaN，会落入系统默认级联位置 |
| 调用方定位的卡片 | 先定目标横坐标，再 `ShowThenPlace`；保留目标列，只抬起纵坐标 |
| WPF 定位的大窗或模态窗 | Show/ShowDialog 前安装 `ParkUntilPainted`，在 `SourceInitialized` 捕获位置，再延后恢复 |
| 跨 DPI 落位 | 同步 WPF 的 DIP 状态，并用 HWND 的物理坐标落位；不能把旧缩放下捕获的 DIP 直接写回 |
| 隐藏的菜单锚点 | 无内容、全透明并复用；“尺寸设为零”不等于没有屏幕像素 |

`AfterFrame` 经过 Render 和 Loaded 两次 dispatcher 调度。历史探针中，停在 -32000 等 `ContentRendered` 的 32 次尝试全部超时，因此它不能作为离屏放行条件。停靠可减少冷布局暴露，但多等 0–260 ms 并未消除落位后的约 22–31 ms 残差；不能写成“保证零黑帧”。

现有快速、内联和通知卡片经 `CardRenderMode` 使用软件合成；该选择有本机测量依据，不等于所有 WPF 窗口都应改软件渲染。QuickLook 与 PreviewOverlay 已有先停靠再显示路径，不能再保留“尚未门控”的旧待办。

## 复用和尺寸的教训

- 内联窗口 dismiss 是 Hide + Park，Dispose 才 Close。复用时重置 owner、会话、键盘布局、菜单和宿主相关状态；不能只保存 HWND 而保留上一宿主的尺寸和缓存。
- 停靠和全透明窗口仍可能报告 `IsVisible=true`，不能据此选为对话框 owner。托盘与菜单锚点都要声明 `ITransientHostWindow`；延迟居中前也要重新检查 owner，否则弹窗会被搬到屏幕外，看起来像刚出现就关闭。
- 上屏前先定宽度再量高度。`IsSearching` 等异步早退条件不能否掉同步召唤阶段唯一一次尺寸计算；空结果区域不能被冻结为整个查询的高度。
- 首次特别明显的问题先比较状态初始化，持续黑边先检查布局。`SizeToContent` 被窗口最大尺寸夹住时，内容可能没有同步夹住；应量完后固定实际尺寸，再让内容行分配剩余空间。
- 预热、DPIChanged、位置更新不能把仍在停靠的窗口提前搬上屏。守卫放在实际定位入口，不能只靠某一个调用方自觉。

## 如何复核

先读 `[WindowPark]` 的停靠是否守住、落位与首帧间隔，再与内容到达和高度变化日志对齐。区分未绘制帧、尺寸空条、第二个顶层窗口与长期黑边；不要统一归因于“渲染慢”。

现有 [WindowParkingTests](../Tests/App/Helpers/Visuals/WindowParkingTests.cs)、[AppTypographyTests](../Tests/App/Helpers/Visuals/AppTypographyTests.cs) 和 [DwmWindowCornersTests](../Tests/App/Helpers/Visuals/DwmWindowCornersTests.cs) 验证机制。混合 DPI、混合显卡、真实焦点和拖拽仍需真机验证；不要用单元测试或离屏截图声称已经排除这些问题。
