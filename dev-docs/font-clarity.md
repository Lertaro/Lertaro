# 字体清晰度：不透明化、字号与圆角规则

## 适用范围与核对基线

覆盖 `<repo>` 桌面 App（`App/`、`PluginSdk/`、`Plugins/`）的文字渲染与窗口外观规则。

- 核对基线：`main` @ `9a6b9769`。文中 `文件:行号` 按这一基线复核过；commit hash 是各轮的落地提交。
- 闪黑框（不透明窗口在第一次呈现之前的黑矩形）的量测与根因在 `opaque-window-flash.md`，那才是那件事的现行权威。
- 原来的一~十分步 A/B 试点计划已于 2026-10-19 删除，被下文的执行结果取代。当时决定不试点，一次性执行 Phase 1~5 并完全放弃半透明。那份计划留下的只有判断依据，也就是下文的背景与目标，其余内容是执行记录、关键经验和仍未收口的项。

## 背景与目标

调研已经确认，当前字体边缘模糊、发灰、发虚的主要根因是核心窗口大量采用 WPF 分层透明窗口（`AllowsTransparency=True`）。字体选择、字号设置、DPI 配置、缺哪个 TextOptions 配置都不是根因。

该架构下无法稳定获得完整 ClearType 子像素渲染能力，文字普遍退化为灰度抗锯齿。

本计划的目标有三条。

1. 通过严格 A/B 实验验证渲染架构调整收益。
2. 用实际截图和数据判断是否推广。
3. 避免继续投入时间在收益极低的字体参数微调上。

## 执行结果（2026-10-14，已完成并合入工作区）

决定：**不满足于试点，一次性执行 Phase 1~5，并激进地完全放弃半透明支持。**

| 计划项 | 结果 |
|---|---|
| Phase 1 QuickSearchWindow | A/B 试点验证通过后，B（不透明）成为唯一形态；A 的开关与探针已删除 |
| Phase 2 InlineSearchWindow | 不透明；`CardMargin` 12 → 0（`App/Views/InlineSearchWindow/Helpers/InlineCardSizingSupport.cs:24`），`xamlMargin` 12 → 0，SizingSupport/Positioner/创建流程同步；呼出前先定尺寸 |
| Phase 3 SearchWindow | 不透明 + WindowChrome；`VisibleContentInset` 8 → 0；最大化/还原不再改 Margin |
| Phase 4 LayoutRounding | 由 `AppTypography.OnRootLoaded` 在**所有**窗口/Popup/ContextMenu/ToolTip 根上开启（`App/Services/AppTypography.cs:40,121`，两属性都继承） |
| Phase 4 字号量化 | 新增 `AppTypography.FontSizeLadder` + `SnapFontSize`（`App/Services/AppTypography.cs:98,110`），接入 `UiMetrics`（斜体行/结果名/路径）、`ActionMenuItem`、`SearchBoxControl`；XAML 里 23 处半点字号改为档位值：属性写法 `FontSize="12.5"` 扫出 19 处，Setter 写法 `Property="FontSize" Value="12.5"` 整批漏网的 4 处（`Menu.xaml` 菜单行、`ActionMenuItem.xaml` 紧凑行各 12.5，`LocalSendWindow.xaml` 两个输入框各 11.5）在 2026-10-18 补上；`BaseResultPathFontSize` 12.8 → 13（`App/Services/UiMetrics.cs:56`）。取整方向后来由"字号一律整数"一节定为向上 |
| Phase 5 ClearType | **（2026-10-18 已回退，见"回退强制渲染模式"一节，83f21823）** 当时认为不透明即已获得子像素抗锯齿，且 `Display` 是全应用默认（`App.xaml.cs` 覆盖元数据），搜索框上唯一的 `Ideal` 覆盖（d7f6cd49）因此删除 |
| 半透明 | 完全移除：`WindowEffectHelper`/`LayeredSurface` 删除（只剩 `WindowSurface`，`App/Helpers/Visuals/WindowSurface.cs:11` 记着旧名），`ThemeManager` 的整窗淡入淡出删除，`ITheme.WindowOpacity` 标记 `[Obsolete]` 且无任何读取（`PluginSdk/Abstractions/ITheme.cs:18`）；其余窗口（QuickPanel、Settings、LocalSend ×2、两个对话框、QuickLook、PluginWindow、四个 Popup）全部迁移 |

## 必须遵守的关键经验（后续改动都要守）

1. **不透明窗口 + 内容透明度 = 黑块。** 层叠窗口在 opacity<1 时是隐形，不透明窗口是纯黑。因此任何整窗淡入/淡出都必须取消（通知窗早已如此），只保留卡片内部元素的动画。
2. **不透明窗口未绘制时也是黑块。** 启动预热 `Show()` 必须先停放到屏幕外**并且两个坐标都设**（只设 `Top` 时 `Left` 仍是 NaN → `CW_USEDEFAULT` → y 被忽略，窗口出现在级联位置）。这条现在是 `WindowParking.Park` 的实现（`App/Helpers/Visuals/WindowParking.cs:29`，两个坐标一起设成 `-32000`）；4028dfa8 把这道闸门从卡片扩到 app 每次新建的所有表面。
3. **预热窗口一律不许被定位。** 实测元凶是构造函数里的 `DpiChanged` 处理器：窗口停在屏幕外 → Windows 归到主显示器 → WPF 抛 DPI 变化 → `if (IsVisible)` 成立（此时 `Hide()` 尚未落到 HWND）→ 把未绘制的窗口摆到卡片位置，黑块持续到第一次呼出。闸门因此放在 `PositionWindow` 本身（`App/Views/InlineSearchWindow/Helpers/InlineSearchWindowPositioner.cs:99`，调用点同样守着：`App/Views/InlineSearchWindow/InlineSearchWindow.xaml.cs:178,183`）。
4. **不透明 Popup 会把自己未覆盖的客户区涂黑。** 圆角会留下四个黑色楔形，因此弹出层放弃圆角（Popup 也无法借 DWM 圆角）；`PopupAnimation` 在非透明时被 WPF 强制为 `None`。托盘菜单的 chrome 样式 `MenuPopupChrome` 就是按这条画成方角的（`App/Resources/Styles/Controls/Menu.xaml:209`）。
5. **XML 注释里不能出现 `--`**（`--`/`-->` 之外），本次有 9 个文件因此编译失败。

呼出的卡片窗另有一条（2026-10-18，06fb5e84）：它们强制软件合成（`App/Helpers/Visuals/CardRenderMode.cs`），代码注释里写着这条路同时保住 ClearType，而本 app 放弃的分层窗口做不到。

## 字号与渲染模式规则

### 回退强制渲染模式（2026-10-18，83f21823）

前提修正：本 app **从未强制 ClearType**。`TextRenderingMode` 全程 `Auto`，而 Auto 的语义就是"按计算机与硬件设置选最优"，即跟随系统；被强制的是 `TextFormattingMode`。因此"统一使用系统默认"落到实际改动上只有一件事：撤掉对 `TextFormattingMode` 的强制。

已撤（核对：仓库里已无强制 `Display` 的写法，守卫是 `AppTextRenderingTests.NoFormattingModeIsForcedOntoAnySurface`，`Tests/App/Helpers/Visuals/AppTextRenderingTests.cs:21`）：

1. `App/App.xaml.cs` 静态构造里对 `FrameworkElement` 与 `FrameworkContentElement` 的 `OverrideMetadata(... Display)`，整个静态构造随之删除。
2. 五处手工重复的 `TextOptions.TextFormattingMode="Display"`：通知卡片窗、通知提示窗、`PluginSdk/Windows/PluginWindow.xaml`、日历插件 `CalendarView` 与 `AlmanacPanel`。这些当年是为了"插件/通知自己的树够不到 App 的元数据覆写"而各自补的，现在没有覆写要补了。
3. 结果：全局回到 WPF 自己的默认 `Ideal`。

撤掉它同时修正了一处既有的错位，这是实测出来的（11 DIP，Consolas/Global User Interface）：

| 文本 | 默认构造 FormattedText | Display | Ideal |
|---|---|---|---|
| `iiii` | 24.187 | 24.000 | 24.187 |
| `WWWW` | 24.187 | 24.000 | 24.187 |
| `INFO 中文日志` | 74.233 | 74.000 | 74.233 |
| `MyApplication.exe` | 102.793 | 102.000 | 102.793 |
| `快捷导航` | 44.000 | 44.000 | 44.000 |

不带 mode 参数的 `FormattedText` 构造与 `Ideal` **逐位相同**，而 app 里 7 处测量文本宽度的代码用的正是这个构造（核对：产品代码里 `new FormattedText(` 共 7 处）。强制 Display 之后，渲染按 Display 排字、测量按 Ideal 算宽，两边一直在错开；现在统一了。宽度差异本身小于 0.8%，且 Display 更窄，所以回到 Ideal 只会让文字稍微变宽，不会新增裁切。

日志字体的等步进不变量（`iiii` 与 `WWWW` 等宽）在 Ideal 下同样成立，`AppTypographyTests` 已改按 Ideal 断言（`LogFontHasEqualLatinAdvancesAndSupportsMixedText`，`Tests/App/Helpers/Visuals/AppTypographyTests.cs:201`）。

**没有动的东西**，以及为什么：

- 不透明窗口架构。它是本计划的主线，且与 ClearType 只有一层间接关系：不透明只是让子像素抗锯齿"可以"出现，强制与否在 `TextRenderingMode`，而那是 `Auto`。
- `UseLayoutRounding` / `SnapsToDevicePixels`（`AppTypography.OnRootLoaded` 给所有窗口/Popup/ContextMenu/ToolTip 根开启）。它们属于布局取整，不是文字渲染模式；关掉会让文字落在半像素上，与本条回退不同源，要单独 A/B 再决定。
- 字号档位 `SnapFontSize`。**它的理由已经变弱**：原先"整数 DIP 才有清晰字距"的论证依赖 Display 把字距对齐到整像素，Display 撤掉后这条论证不再成立，现在只剩"尺寸词汇表统一"这个较弱的理由。已在注释里如实改写（`App/Services/AppTypography.cs:90-98`），是否保留待看完成效再定。

一个有意思的回环：`d7f6cd49 fix(typography): smooth quick search input with ideal text formatting` 当年特意给搜索框单独设 `Ideal`，理由是输入更顺；`23413ad7` 又把这条覆盖删掉，理由是全局已是 Display、不该再有例外。删除全局强制之后，默认值回到 Ideal，等于那条被删的覆盖想要的结果由默认值本身提供了，两次改动互相抵消而不是互相打脸。

### 字号一律整数，非整数向上取整（2026-10-18，74fc6060）

规则确定：应用内**不存在非整数字号**，计算得到的小数值一律**向上**取整。向上取整是唯一符合原意的方向。计算值是"这段文字还能看清的最小尺寸"，向下会跌破那个地板。

已改（行号按 9a6b9769 复核）：

1. `AppTypography.SnapFontSize` 的两个兜底分支由 `Math.Round(..., AwayFromZero)` 改为 `Math.Ceiling`（`App/Services/AppTypography.cs:113,118`）。之前"低于首档只做取整、不放大"的说法在方向上是错的：取整本身就必然放大，只是不该放大到 12 那一档。现在 `9.4 → 10`、`11.2 → 12`、`37.4 → 38`（原先是 `37`，那是唯一一个"向下"的出口）。档位表是 `[12, 14, 16, 18, 20, 24, 28, 32, 36]`。
2. `Plugins/CoreExtensions/Preview/Providers/DefaultPreviewProviders.cs:120` 里文本预览的 `FontSize = 12.5` → 13。这是 Phase 4 那句"半点字号全部改完"最大的漏网：它不在 XAML 里，而在 C# 的对象初始化器里，之前所有扫描都只看标记文件。
3. 补那 4 处 Setter 写法时（2026-10-18）向下取整的三处，现在按向上重排：`Menu.xaml` 菜单行 12 → **13**（`App/Resources/Styles/Controls/Menu.xaml:55`，正好与动作列表普通行同档，字号词汇表因此更统一）；`LocalSendWindow.xaml` 两个输入框 11 → **12**（`App/Resources/Styles/Windows/LocalSendWindow.xaml:12,42`，与 `TextBox` 基础样式的 12 一致）。
4. **`ActionMenuItem.xaml` 紧凑行保持 12**（`App/Resources/DataTemplates/ActionMenuItem.xaml:102`），不跟随第 3 条上到 13：它的正常档就是 13，紧凑行的存在意义就是比正常小半档，12.5 向上取整会得到 13 并使这个触发器变成空操作。这是"向上取整"唯一需要让路的情形，已在注释里写明。

守卫（`Tests/App/Helpers/Visuals/AppTypographyTests.cs`）：

- `SnapFontSizeNeverReturnsAFraction`（:262）：按 0.1 步长扫 6.0～48.0 全部输入，断言输出恒为整数。这条是对兜底分支的直接约束，不依赖源码写法。
- `FontSizesInTheSourceAreWholeNumbers`（:286，由 2026-10-18 那轮的 `XamlFontSizesAreWholeNumbers` 扩写）：同时扫 `.xaml` 与 `.cs`，覆盖四种写法：元素属性、Setter 的 Value、对象初始化器 `FontSize = x`、`SetValue(...FontSizeProperty, x)`。实测命中 362 / 19 / 1 处，非整数 0 处；三种模式都验证过确实匹配到真实文件，避免又是空转。

`MaxItemTextWidth = 220` 与两处注释里的字号数字随之更新为 13（`App/Services/ShellMenu/QuickNav/QuickNavigationMenuContentExtensions.cs:124`）。字号变大半档后 220 DIP 能容纳的字符数略降（约 4%），没有改这个值：它本来就是"典型文件夹名"的经验值，不是按字号算出来的。

### Phase 4 文本容器专项审查（2026-10-18 执行，此前未记录）

范围 `App/ PluginSdk/ Plugins/`，四类：`ScaleTransform`/`LayoutTransform`、`BitmapCache`/`CacheMode`、`Opacity`、`OpacityMask`。

- `OpacityMask`：全项目 0 处。
- `Opacity` 动画：只落在卡片内部的 overlay/accent bar（Button、ListBox、Menu 的 `HighlightOverlay`/`AccentBar`）和滚动条整体上，没有整窗/文字根，符合关键经验 1。`BitmapScalingMode="HighQuality"` 的若干处都在图标位图上，与文字无关。
- **命中 1：起始页磁贴**。`App/Views/QuickSearchWindow/QuickSearchLaunchPanel.xaml:56` 用 `LayoutTransform` 缩放整个 `ListViewItem`，而 `LaunchPanelItemScale` 来自 `CalculateItemScale` 的连续算值（几乎不等于 1）。磁贴里的名称 11、快捷提示 9 因此**绕过 `SnapFontSize`**，实际字号 = 值 × scale，不在档位上。机制要说准：WPF 的 transform 作用于矢量层，字形按缩放后尺寸重新光栅化，**不是**位图拉伸；代价是 Display 模式的整像素 advance 在变换后又被缩放，正是已删除的分步计划里布局取整那一节要消掉的字距抖动。修法是把 scale 乘进模板里的字号与图标尺寸而不是变换容器，会牵动 `CalculateItemScale` 的高度拟合。这属重构，且必须真机目视，不盲改。
- **命中 2：主题卡片**。`App/Views/Settings/AppearanceSettingsPage.xaml:54` 给整块卡片 `ListBox` 加了 `CacheMode=BitmapCache`，卡片含主题名文字。缓存表面贴回设备坐标时子像素相位要重采样，外层 ScrollViewer 的分数滚动偏移尤其容易让 ClearType 发糊；而这条缓存的注释写明是为消除实测滚动卡顿加的。两个目标对立，需真机 A/B 后再定（候选：只缓存卡片的背景与阴影层，文字留在缓存外）。

## 圆角

### 主窗口方角的真正原因：圆角请求发在了 handle 存在之前（2026-10-18，6b03e319）

调用**是**有的，XAML 也没问题，问题在时机。`DwmWindowCorners.ApplyRound` 需要 HWND：

```csharp
var hwnd = new WindowInteropHelper(window).Handle;
if (hwnd == IntPtr.Zero) return false;   // 构造函数里就是 Zero
```

WPF 的窗口句柄在首次显示时才创建，构造函数阶段 `Handle` 是 `IntPtr.Zero`，于是这句直接静默返回。透明窗口时代圆角是 XAML 画出来的，不依赖这个调用；不透明化迁移删掉了自绘圆角、改用"让窗口管理器把窗口本身削圆"，而调用点留在了构造函数里。迁移后这些窗口的圆角**从来没有生效过**，只是到这次才被发现。

实测（本机 build 26300，`DwmGetWindowAttribute(33)` 可读写）：

| 时序 | `ApplyRound` 返回 | 窗口读回的轮廓 |
|---|---|---|
| 完全不请求 | — | `0` DEFAULT（无框窗口即方角） |
| 构造期请求（修复前） | `False` | `0` |
| `Show()` 之后请求 | `True` | `2` ROUND |

受影响的是 9 个在构造函数里直接调用的窗口：主搜索窗（`App/Views/SearchWindow/SearchWindow.xaml.cs:51`）、设置窗、两个 LocalSend 窗口、QuickLook、内联卡片、两个对话框、QuickPanel。

**为什么只有快速搜索窗是圆的**：`WindowSurface.Apply`（通知卡片、插件窗）和 `QuickSearchWindowLifecycle.HandleSourceInitialized()` 都在 `SourceInitialized` 里调，`WindowSurface` 的注释甚至写明"需要 handle，所以走事件而不是在这里调"。同一个 API 两种用法，一种对一种错，错的还写了"Windows 11 会把窗口本身削圆"的注释。

**修法**：在 `ApplyRound` 内部延后，而不是改 9 个调用点。没有 handle 时订阅一次 `SourceInitialized` 再落属性（该事件在首帧之前，轮廓变化不会被看到），已落地在 `App/Helpers/Visuals/DwmWindowCorners.cs:31-40`。这样将来任何窗口在构造期调用都会自动正确，两个依赖返回值的调用方（`WindowSurface`、`PluginWindow`）本来就在 `SourceInitialized` 内，走的是立即路径，返回值仍然可信。

回归测试 `Tests/App/Helpers/Visuals/DwmWindowCornersTests.cs:26`：比较"构造期请求"与"handle 存在后请求"两种时序下窗口实际轮廓，要求一致；先写测试、确认它在修复前失败（2 对 0），修复后通过。OS 不支持该属性时跳过比较，不在 Win10 上误红。

### 圆角轮廓统一为 8 DIP（2026-10-18，按"统一为 8"执行，94055c83）

`DWMWCP_ROUND` 的轮廓固定是 8 DIP，属性值改不了它。自绘只要比 8 大，就等于在窗口自己的裁切里面再画一条更圆的曲线，1 DIP 的 `WindowBorderBrush` 描边跟不上，四角的弧线会断开。

改动前同一个角色（顶层卡片窗户外轮廓）存在**三种数字**，而且只有 3 个窗口走 token：

| 来源 | 原值 | 现在 |
|---|---|---|
| `CornerRadiusWindow` token（搜索窗、设置窗、QuickLook） | 10 | **8**（`App/Resources/Styles/Tokens/Spacing.xaml:20`） |
| `CornerRadiusWindowTop` / `Bottom`（带区，规则＝外半径减一个描边宽） | 9 | **7**（同文件 :23-24） |
| 消息框、插件输入框、QuickPanel 的 XAML 字面量 | 10 | 改用 token |
| 两个 LocalSend 窗口的 XAML 字面量 | 12 | 改用 token |
| 内联卡片的 XAML 字面量 | 8 / 带区 7 | 改用 token（值不变） |
| 快速搜索窗 `CardBorder` | 8 | 改用 token（内层裁剪仍是 7，那是刻意的描边内缩） |
| `PluginWindow.xaml` 外层与内层裁剪 | 10 | 8（PluginSdk 看不到 App 的 token，只能留字面量；外层本来就由代码在 `SourceInitialized` 里按 DWM 结果重设，`PluginSdk/Windows/PluginWindow.xaml.cs:51`） |

**一处会悄悄抵消本次改动的地方**：主窗口和设置窗在取消最大化时用代码把半径还原成写死的 `new CornerRadius(10)`，只改 token 的话，用户最大化一次之后圆角就又变回 10。现在这两处从窗口自己的资源作用域读 `FindResource("CornerRadiusWindow")`，代码里不再有第二份数字（`App/Views/SearchWindow/SearchWindowChromeHandler.cs:57`、`App/Views/Settings/SettingsWindow.xaml.cs:248`）。

**仍存的 Win10 分歧（未改）**：`WindowSurface` 与 `PluginWindow` 在 DWM 不支持时把表面设成 0（方角，与方形窗口一致，`App/Helpers/Visuals/WindowSurface.cs:40`、`PluginSdk/Windows/PluginWindow.xaml.cs:51`），而 token 窗口在 Win10 上仍画 8（方形窗口里一个圆角卡片，角上的楔形因为窗口背景与卡片同色而看不出来）。两种哲学都对，但确实不一致；要统一的话是另一个决定。

测试：

- `DwmWindowCornersTests.PaintedWindowRadiusSitsOnTheWindowManagerContour`（`Tests/App/Helpers/Visuals/DwmWindowCornersTests.cs:51`）直接读 `Spacing.xaml`，钉住 8 与带区的 7（外半径减描边）关系。用 `XNamespace` 解析 `x:Key`，并且用 `Single` 而不是 `FirstOrDefault`：取不到就抛，避免又写出一条静默通过的断言。
- 时序那条测试改成用 `WindowInteropHelper.EnsureHandle()` 制造句柄（实测它会触发 `SourceInitialized`，属性读回 2），因此**不再显示任何窗口**，也不会再影响别人屏幕上的前台。另外加了一个"完全没请求"的对照窗口：如果平台默认就是 ROUND，这条测试会明确报 Inconclusive，而不是靠平台默认蒙混通过。

顺带记下这次踩到的教训：第一版时序测试用 `Show()` 起了两个真窗口，而那之后 `Tests/App` 全量里出现了两次"1 个失败、名字没抓到、复现不了"。去掉 `Show()` 之后连续 7 轮 2154 全绿。这不足以证明因果，但在这个测试套件里**不要创建可见窗口**是这次该记住的结论。

## 托盘菜单

### 试点（2026-10-18，106fa93a）

`TrayMenuController` 的宿主从 `ContextMenu` 换成不透明 `Popup` 承载竖排 `Menu`。行的样式仍走 `Menu.xaml` 的隐式 `MenuItem` 模板，因此字号、图标列、悬停高亮全部沿用原样；`Grid.IsSharedSizeScope` 手工补上（原来由 ContextMenu 样式给）。`Menu.xaml` 新增两个资源 `VerticalMenuItemsPanel` 与 `MenuPopupChrome`（`App/Resources/Styles/Controls/Menu.xaml:205,209`），因为 `Menu` 没有 `Orientation`，横向 items panel 必须替换；`ActionFlyout` 和 `PluginContextMenuHelper` 目前仍是各自在代码里用 `FrameworkElementFactory` 搭一遍（`App/Services/ShellMenu/ActionFlyout/ActionFlyout.cs:113`、`App/Services/ShellMenu/QuickNav/RightClickActions/PluginContextMenuHelper.cs:112`），收口仍未做。

试点要盯的四件事，全部无法在这里判定：

1. 是否真的拿到 ClearType。这是整份计划的前提，但只有眼睛能判。
2. **关闭语义是否还完整**。`ContextMenu` 原本由 WPF 内部的 popup 负责"点外面就关"；这里换成 `StaysOpen=false` 顶上，另外那个 1×1 锚点窗口是 `Topmost`，关掉它会不会连带把菜单提前关掉，需要实测。
3. **键盘导航是新写的**（上下环绕、回车/空格触发、Esc 关闭），高亮仍靠反射拿 `MenuItem` 私有的 `IsHighlighted` 属性键，拿不到时整套导航静默失效，与 `ActionFlyout` 同样的既有取舍。这条键现在由 `ActionFlyoutItems.IsHighlightedKey` 一处提供，flyout、quick-nav 右键菜单和托盘菜单共用（`App/Services/ShellMenu/ActionFlyout/ActionFlyoutItems.cs:24`）。焦点必须在打开后落到 `_menu` 上，否则方向键进不去。
4. 点条目现在由 `Item()` 里显式 `CloseMenu()` 再执行动作。原来 `ContextMenu` 会自动关，`Popup` 不会；漏掉这一步会留下一个挂在窗口上的僵尸菜单。

已避开的两个坑：菜单和 chrome 必须建一次反复打开，因为 `Border.Child` 已持有 `Menu`，每次重建会因"元素已属于另一父节点"抛异常；键盘导航不能复用 `PluginContextMenuBuilder.GetActiveMenuState`，它只看 `IsEnabled`，而 LocalSend 关闭时那一行是 `Visibility.Collapsed` 并非禁用，跟着走会让回车从一个不可见的行打开已被用户关掉的窗口。

`ShowAt(target, ...)`（非托盘入口）同样改走这个宿主，两处共用一条路径。

### 托盘菜单里那圈虚线（2026-10-18，试点的副作用，已修 81c94817）

现象：重启程序后第一次右键托盘图标，菜单内部有一圈虚线；点任意一项后消失；每次重启都能复现。

来源是 WPF 的默认 `FocusVisualStyle`（一个围绕**获焦点元素**画的虚线矩形），与选中样式无关。托盘试点为了让方向键能走到菜单行上，打开时显式 `_menu.Focus()`，于是这个矩形画在了 `Menu` 宿主身上，也就是"卡片内部整列行的外围"。旧的 `ContextMenu` 路径从来不出现这个，因为它根本不请求焦点（只有 `IsOpen = true`）。

为什么只有重启后第一次有：焦点装饰按"这次焦点是不是键盘拿的"决定画不画。托盘右键是 WinForms `NotifyIcon` 的消息，不经过 WPF 的输入管理器，所以新进程里 WPF 仍认为当前是键盘驱动，程序化 Focus 就带装饰；一旦点过任意一项，真实鼠标交互已经到达 WPF，之后的打开不再显示，直到进程重启。

修法：`Menu.xaml` 给 `Menu`、`ContextMenu`、`MenuItem` 三个宿主压掉 `FocusVisualStyle`，与本 app 已有约定一致（列表行、TextBox、搜索窗按钮、侧栏都是这么处理的）。可见反馈没有损失：行上的 accent bar 与 hover 填充由 `IsHighlighted` 驱动。测试 `MenuSurfacesAreDeclaredWithoutTheDefaultFocusRectangle` 读样式本身断言（`Tests/App/Helpers/Visuals/AppTextRenderingTests.cs:53`；`Single` 取不到即抛，不是一条静默通过的断言）。

设置界面按 Alt 出现的同类印记是**另一件事，且早于本分支**：`Button.xaml` 本分支未改，`SettingsButton` 与页面上的 CheckBox 都可聚焦且没有压掉装饰，按 Alt 会让 WPF 重新进入"显示键盘线索"状态，于是当前获得焦点的那个控件（点提示文字不会改变焦点，所以是之前留下的那个）画出虚线框。当时没动它：对输入控件而言那圈虚线**就是**键盘焦点指示器，删掉是可用性取舍，不是清理。三选一摆在面前：保持现状 / 换成主题描边（保留可见指示）/ 全局压掉（键盘用户失去焦点位置提示）。

按选项 3 执行（`b38fe369`）：在 `AppTypography.Initialize` 里给 `Control` 注册一次类处理器，元素 Loaded 时把该属性置为**本地值** null（已有本地值的元素不动），不给每个样式单独补 `FocusVisualStyle`，已落地在 `App/Services/AppTypography.cs:52-64`。必须这么做的原因：显式 keyed 样式会**完全忽略**同类型的隐式样式，而该 keyed 样式没写的属性仍由主题样式提供。这正是 `SettingsButton`、驱动器行的 CheckBox 在 app 已经压掉二十处之后依旧留框的机制，而"给每个类型加隐式样式"这条路根本走不到它们。

已接受并写明的代价：键盘用户不再能看到焦点落在哪个普通控件上；回退只是删掉一个 `RegisterClassHandler`。app 自有样式里那些 `{x:Null}` 保留，不让任何表面只依赖这个处理器。

仍然存在的另一件事（与焦点装饰无关，没有改动）：按 Alt 会给标签和菜单表头里的访问键（`RecognizesAccessKey`）加下划线，那是 Windows 的常规行为，不是这圈虚线。

测试 `AppTypographyTests.PlainControlsAreGivenNoFocusRectangle`（`Tests/App/Helpers/Visuals/AppTypographyTests.cs:153`）断言的是**本地值**而不是解析后的属性值：没被测量过的窗口不会应用主题样式，若断言解析值，处理器什么都不做也能通过。

## 剩余项与未核对

2026-10-18 对着代码逐条核实，行号按 9a6b9769 复核。

| 项 | 状态 |
|---|---|
| DPI 矩阵（125/150/175/200%，已删除的分步计划里的一节） | 未做，需真机。**先记下一条推论**：整数字号只在 100% 下等于整设备像素，150% 时 13 DIP = 19.5 px，档位规则本身不再成立。目视时请判断"整数 DIP 在缩放屏上是否仍优于连续值"，别假定它一定更好 |
| Windows 10 直角 + DWM 阴影 | 未做，需 Win10 机器。核对：`DwmWindowCorners` 里没有版本闸门，该属性在不支持的 OS 上直接失败（`App/Helpers/Visuals/DwmWindowCorners.cs:47-50`） |
| 根 `ContextMenu` 仍是分层窗口，文字灰度抗锯齿 | **托盘菜单已改造完成（2026-10-18，106fa93a，待目视）**，其余仍未做。WPF 侧机制已核实：dotnet/wpf `ContextMenu` 每次 Open 都执行 `_parentPopup = new Popup { AllowsTransparency = true }`，字段是 private 且没有可覆写的工厂 → 覆写 ControlTemplate 无效，只能换成不透明 `Popup` 承载 `Menu`（上游源码的 `ContextMenu.cs:482`/`:696` 行号本机未复核；仓库侧记录见 `App/Services/Tray/TrayMenuController.cs:18-21` 的注释）。剩余面按频次排：快速导航根菜单（最高频，但那个 `ContextMenu` 实例被传进内容扩展、子菜单加载器和右键 flyout 的协调逻辑，改造牵连最广，`App/Services/ShellMenu/QuickNav/QuickNavigationMenu.cs:112`）＞搜索框 TextBox 菜单（`App/Resources/Styles/Controls/TextBox.xaml:55`）＞起始页磁贴 / 收藏 / 服务设置三处（`App/Views/QuickSearchWindow/QuickSearchLaunchPanel.xaml:144`、`App/Views/Settings/FavoritesSettingsPage.xaml:155`、`App/Views/Settings/ServiceSettingsPage.xaml:265`）。统一代价：根菜单失去圆角与 `HasDropShadow=True`，转而与已经方角化的子菜单、flyout 一致，脱离当前父圆角子方角的漂移状态 |
| MenuItem 子菜单 Popup、动作 flyout、Site 文档（7 语言） | **已完成**（`feat(menus)` 73972a10、`docs(site)`），原先记为未做，已更正 |
| QuickLook 预览卡片按全尺寸显示（宽 24 DIP） | 未做目视；外置预览提供者拿到的矩形同步变大。核对：本机未在 `App/Views/QuickLook/` 下找到这个 24 DIP，该值未核对 |
| 起始页磁贴的 `LayoutTransform`、主题卡片的 `BitmapCache` | 两处命中都未改，见上文专项审查；必须真机目视/A-B |
| 4 处 Setter 写法字号（`Menu.xaml`、`ActionMenuItem.xaml`、`LocalSendWindow.xaml`） | 代码与测试背书：`dotnet build` 全绿、`Tests/App` 2149 个测试通过（那 4 处改的是"只有编译与测试背书"的部分）；菜单行与 LocalSend 输入框的实际观感未经目视 |
| 回退 `Display` 后的观感 | 待真机比较：高分屏上文字彩边是否消失、`Display` 的字距抖动是否本就是觉得"发虚"的那一项、日志/表格这类等宽场排在 Ideal 下是否仍然对齐 |
| 字号上到整数后的行高与宽度 | 待真机确认：菜单行与 LocalSend 输入框各大约半档后，行高（`UiMetrics.MenuItemHeight`）与 26 DIP 高的输入框是否仍然合适；文本预览从 12.5 到 13 后行宽是否需要更多横向空间 |
| `SnapFontSize` 是否保留 | 理由已变弱（见"回退强制渲染模式"），待看完成效再定 |

测试背书：handle 时序那次（6b03e319）`Tests/App` 4 次全量 2153 通过、新类单跑 6 次稳定通过、0 警告 0 错误；8 DIP 统一那次（94055c83）把 `Show()` 从测试里去掉之后连续 7 次全量 2154 全绿。handle 时序那次期间有 1 次全量出现 1 红，未复现且当时没抓到测试名，因此不能断言它是本机已知的 flaky，也不能排除与那次改动的关系。
