# 不透明窗口的未绘制帧：根源、量测与已交付修复

## 适用范围与核对基线

这条规则适用于任何**不透明 + 首帧未提交**的窗口。卡片只是最先撞上它的一类，其余窗口同样成立。

核对基线：`main` = `9a6b9769`。相关已落地提交：`681d353c`、`183a3982`、`b3a28d67`、`036ad31d`、`06fb5e84`、
`4028dfa8`、`af8f944e`、`6e4b6f59`、`4d1abc17`、`71821a33`、`b59beb23`、`d5090aea`、`20692d47`。

对着代码核对后，与最初记录有出入的地方：

- `WindowParking` 确实在 `App/Helpers/Visuals/WindowParking.cs`：`OffScreen = -32000`（:35）、`Park`（:41）、
  `ShowThenPlace`（:63）、`AfterFrame`（:101）、`ParkUntilPainted(window, place)`（:125）。
- `App/Helpers/Visuals/UnpaintedBrush.cs` 确认不在工作区，全仓没有任何 `WM_PAINT` / `WM_ERASEBKGND` 钩子。别再去找它。
- `CardRenderMode` 明确不读 `EnableHardwareAcceleration`（`App/Helpers/Visuals/CardRenderMode.cs:23-26`）；那个开关
  改成在任何窗口创建前设进程级 `RenderOptions.ProcessRenderMode`（`App/App.xaml.cs:116-117`），默认值仍是 `true`
  （`Core/Settings/UserSettings.cs:58`）。软件合成的调用点：快速窗口 `QuickSearchWindowLifecycle.cs:49`、内联卡片
  `InlineSearchWindow.xaml.cs:160`、通知的两张面经 `WindowSurface.cs:37`。
- 内联卡片 dismiss 现在是 `Hide()` + `WindowParking.Park()`（`App/Services/InlineSearch/InlineSearchManager.cs:262-263`），
  只有 `Dispose()` 才 `Close()`（:301）。
- 触发点 3 里写的 `EnsureWindowCreated()` 这个名字当前代码已不存在；对话框激活路径现在是
  `CloseInlineSearch("ExplorerActivated (Dialog)")` 紧接 `SummonWindow()`（:79-80），而 `Summon()` 复用同一个窗口，
  只在 `_manager.Window ??= Create()` 处建一次（`InlineSearchWindowSummonSupport.cs:39`）。
- dismiss 用停靠（park）处理，理由带实测背书，写在 `WindowParking.cs:14-17`：隐藏会让下一次呼出重画整棵可视树，
  227–258ms；移动一个已可见的窗口是 23–26ms。
- `ResizeBorderThickness` 现在在七个带 chrome 的窗口一律为 0（`9a6b9769`：6px 的 resize band 让
  `WM_NCHITTEST` 把指针全收走，滚动条 thumb 拖不动）。下面黑边那张变体表里 `ResizeBorderThickness=0`
  那一行已是出厂状态，不再是需要复刻的变体；"改 resize 边框救不了黑边"这条结论照样成立。
- 边缘命中那一类量测已从一次性探针变成回归测试：`Tests/App/Views/WindowEdgeResizeBandTests.cs`。
- 测试计数核对：`Tests/App/Helpers/Visuals/WindowParkingTests.cs` 5 条（`Park` 两个坐标、`ShowThenPlace`
  保留目标 x、`AfterFrame` 落在渲染队列后第一拍、`ParkUntilPainted` 抬 y 不抬 x、已有句柄时不动作）。
- `d5090aea` 之后，`[WindowPark]` 的两行 Info（park 是否守住、未绘制帧持续多久）就是
  `WindowParking.cs:72-82` 与 `:124-136`，是这类问题的现行取证入口。

交叉引用：`font-clarity.md` 关键经验 1/2/4。

## 根源：一条规则，三个触发点

`font-clarity.md` 的关键经验 1/2 已经写下过：**不透明窗口的内容未绘制 = 黑块**。
本项目为了 ClearType 主动放弃层叠窗口（`WindowSurface` 的 layered 分支已删，见 `WindowSurface.cs:11-16`），
代价是卡片的像素只存在于渲染线程为"当前尺寸 + 当前 DPI"提交的那一帧里。HWND 已经可见、而那一帧还不存在的
这段时间，客户区显示的是窗口类背景刷 —— 黑色。

症状是画得晚；内容本身画出来是对的。三个触发点：

1. **呼出路径没有首帧屏障**：`Show()` 只置 `WS_VISIBLE`，`UpdateLayout()` 只排布局。（`b3a28d67` + `4028dfa8` 已关）
2. **可见状态下改尺寸/改 DPI**：内联卡片每次搜索落定都改 `Height`（`InlineCardSizingSupport.cs:214`），再由
   positioner 落位时 `SetWindowPos`（`InlineSearchWindowPositioner.cs:153`）；跨 DPI 副屏呼出时表面按新缩放重建。
   （屏上改高度这一路本机量到 9–12ms、重内容无黑帧；跨 DPI 那一半仍未量到）
3. **每次新建 HWND**：`InlineSearchWindow` 每呼出新建、dismiss 时 `Hide()+Close()`；对话框激活路径甚至
   `CloseInlineSearch()` 紧接重建窗口。（`183a3982` 已关）

放大器（当时的状态）：`EnableHardwareAcceleration` 默认 true，而软件渲染闸门只装在快速窗口上，内联卡片与通知卡
完全没有渲染模式处理。

探针顺带证实了停靠时只推 y、保留目标 x 是必要的：零相交窗口取最近显示器的缩放，保留 x 那一列就让停靠帧按目标
显示器的缩放合成，落位时既不改尺寸也不改 DPI，不会在用户眼前重建表面（这条规则也写在 `WindowParking.cs:48-51`）。
`InlineCardSpace` 用 `Screen.FromHandle` 的分支也一并落在对的屏上（`InlineCardSpace.cs:61,85`）。

还有一条同样能咬人的规则，与首帧无关，见下面"常驻黑边"。

## 已交付修复

- `681d353c` refactor：停靠规则（-32000 + 两个坐标一起设）收进 `WindowParking`，挡住"只设 `Top`、`Left` 仍是
  `CW_USEDEFAULT` 导致 y 被忽略、未绘制窗口出现在屏幕顶部的级联位置"。
- `183a3982` fix(inline)：卡片只建一次，dismiss = 隐藏 + 停靠，`Dispose()` 才真销毁；owner HWND、键盘布局、
  宿主看门狗计时、actions 菜单状态改为每次呼出重做（`HostOnto` / `BeginSession` / `RestoreKeyboardLayout`，
  `InlineSearchWindow.xaml.cs:240-256`）。挡住"每次呼出都把新建窗口 + 首帧合成摆在用户眼前"。
- `b3a28d67` fix(visuals)：`WindowParking.ShowThenPlace/AfterFrame` 首帧屏障，接在快速窗口（复用
  `_suppressPositioning`，`QuickSearchWindow.xaml.cs:45`、`QuickSearchWindowShowSupport.cs:83`）、内联卡片
  （positioner 记录目标、落位时应用，`InlineSearchWindowPositioner.cs:114-118`）、通知卡（首次 `Restack` 推迟一帧，
  `NotificationWindowManager.cs:89`）三处。挡住"Show 与落位之间那段可见空窗"。
- `036ad31d` fix(inline)：先定停靠宽度再量卡片高度 —— 复用后 `ActualWidth` 是上一个宿主的，旧顺序会让卡片以错高度
  落屏再被改一次。挡住"落屏后第二次改尺寸"。
- `06fb5e84` fix(visuals)：`CardRenderMode` 让三张呼出类卡片恒用软件合成；`EnableHardwareAcceleration` 改为在任何
  窗口创建前设 `RenderOptions.ProcessRenderMode`，那个"需重启"的开关第一次真的管全程序（原先只有快速窗口读它，
  改完它就没人读了）。挡住"第一次显示要建 Direct3D 设备与表面、尺寸或 DPI 变了还要重建表面"。
- `4028dfa8` fix(visuals)：全仓窗口统一屏障（覆盖清单见下节）。挡住"闪黑框只在卡片上成立"这个假设。
- `af8f944e` fix(inline)：`ApplyCardHeight(forSummon: true)` 跳过 `if (IsSearching) return;` 这条 veto
  （`InlineCardSizingSupport.cs:165-176`），挡住"唯一一次上屏前的尺寸计算被搜索中守卫否掉，卡片沿用上一会话高度、
  进程第一次呼出用 XAML 的 `Height="550"`"。这条改动的安全性在于：清列表之后 `CurrentLayout()` 的答案本来就是
  "空状态"高度，搜索落定后照常纠正，与改动前（新建窗口 = 全新 VM = `IsSearching=false`）行为一致。
- `4d1abc17` fix(inline)：`CardHeight` 只在有行可画时才预留满行预算（`InlineCardSizingSupport.cs:248`），挡住
  "面板还 Visible 就按 8 个空行开尺寸"；那条注释里留着日志读数 550->324->36->324（results=0,0,58）。
- `b59beb23` fix(inline)：`FreezeCurrentResultsArea()` 非零区域照旧冻结、零区域不冻结
  （`InlineCardSizingSupport.cs:130-143`），挡住"召唤时把 0 行区域冻住、空条挂满整场搜索"。中间态从此不存在：
  36 → 首批到达即长成有内容的尺寸。
- `6e4b6f59` / `71821a33` chore(inline)：每一趟尺寸决策都写日志、两处呼出诊断抬到 Info，挡住"下一轮只能靠猜"。
- `d5090aea` chore(visuals)：记录 park 是否守住与未绘制帧持续多久，挡住"这台机器上停靠与空窗看起来一模一样"。
- `20692d47` docs(visuals)：把两处过度声称的注释改回事实（门把可见空窗从约 10 帧压到约 2 帧，它不会把空窗挪走）。
- 黑边修复：`PluginFieldPromptWindow.Window_Loaded` 的 `ContextIdle` 那一拍里，`UpdateLayout()` 之后锁定尺寸
  （`SizeToContent=Manual; Width=ActualWidth; Height=ActualHeight;`，`PluginFieldPromptWindow.xaml.cs:109-131`）
  再切 `ContentRow` 到 `Star`、按 `Width/Height` 重新居中，挡住"`SizeToContent` 夹窗口不夹内容留下的 14 DIP 黑带"；
  顺带让 `ResizeToFit()` 的 `SizeToContent != Manual` 早退守卫恢复为可达路径（:62-64）。
  `CustomMessageBoxWindow`（`SizeToContent=Height`）经变体表实测没有这条黑带，未改。

测试与文档：`Tests/App/Helpers/Visuals/WindowParkingTests.cs` 5 条；`ParkUntilPainted` 两条（抬 y 不抬 x、渲染
过完回落；已有句柄时不动作），连跑三次稳定。当时全量 App 套件 2159 passed / 0 failed，`Lertaro.slnx` 全解决方案
0 警告 0 错误。`Site/<7 locales>/user-guide/settings/general.md` 的硬件加速条目语义已同步。

## 全仓窗口屏障覆盖

14 个 Window 子类全部不透明（无任何 XAML 设 `AllowsTransparency=true`），核对到 `9a6b9769` 仍是这个覆盖面。

| 窗口 | 每次新建？ | 屏障 |
|---|---|---|
| QuickSearchWindow / InlineSearchWindow / NotificationCard / NotificationNotice | 复用 / 复用 / 每条新建 / 每条新建 | ✅ 早先四个提交 |
| CustomMessageBoxWindow、PluginFieldPromptWindow | 每次新建 + ShowModal | ✅ `OwnedDialog.ShowModal` 一处覆盖（`OwnedDialog.cs:88`，导出/导入/恢复 + 全应用消息框） |
| SettingsWindow | 缓存，但关闭后重开＝新建 + Show | ✅ `ParkUntilPainted` + 落位回调（`AppWindowManager.cs:51,86`） |
| SearchWindow | 缓存，但关闭后重开＝新建 + Show | ✅ `ParkUntilPainted` + 落位回调（`AppWindowManager.cs:286`） |
| QuickPanelWindow | 每次打开新建（注释明确说复用曾带来 5 个缺陷） | ✅ `ShowThenPlace`（`QuickPanelManager.cs:234`） |
| LocalSendSendWindow / LocalSendReceiveWindow | 每次新建；接收窗是网络事件自己弹出 | ✅ `ParkUntilPainted` + 落位回调（`LocalSendAppEventHandler.cs:110,139`，接收窗是 ShowDialog 模态） |
| QuickLookWindow、PreviewOverlay 匿名 overlay | 缓存 / 每次新建 | ❌ 两者都在 Show **之后**按 owner / 内嵌原生预览再定位，要屏障得搬定位逻辑，不是包一层 |
| MenuHelperWindow | 每次新建 | ❌ 1×1 锚点，机制同但不可见 |
| 原生 SaveFileDialog / OpenFileDialog / FolderBrowser | — | 不适用（系统窗口，不画我们的类背景） |
| 托盘菜单与 Shell 动作 Popup | — | 不适用：`font-clarity.md` 关键经验 4 已记（不透明 Popup 会把未覆盖客户区涂黑 → 弹出层已放弃圆角） |

`ParkUntilPainted` 与 `ShowThenPlace` 的分工：卡片是**我们自己定位**的，所以先摆好 x、把 y 抬进停靠行、Show、
两趟后落位；对话框/大窗是 **WPF 定位**的（CenterOwner / CenterScreen），模态窗口更不能由调用方摆位置（ShowDialog
不返回），所以在 `SourceInitialized` 里抬 y —— 实测那一刻 centered rect 已经算出来（542x353，Show 之前是 NaN）。
有句柄的窗口不再触发该事件，所以它是"只在首开"的闸门，也顺手避免了重复注册处理器。
`ParkUntilPainted(window, place)` 的第二参数是给"调用方其实有位置想摆、但那一刻还没有句柄"的窗口：
`SourceInitialized` 正是句柄第一次存在、而一个字都还没画出来的时刻，落位先跑、随后捕获的矩形就是落好位的矩形，
恢复用 `SetWindowPos` 写**物理像素** —— 原来写回捕获时的 DIP Top 在跨 DPI 屏上必然落偏，因为那个数字是按窗口创建时
的缩放读的，而窗口此刻已被重设为目标屏的缩放。
为什么这四个窗口不再交给 `CenterScreen`，是实测的：它确实按鼠标所在屏居中，但用**系统缩放**换算窗口尺寸，
建好之后再按该屏缩放重设尺寸，于是它对准的中心是"另一个更小的窗口"的中心。100% 主屏 + 250% 副屏、854x480 DIP 实测
rect=(3093,768) 尺寸 2135x1200，而该屏 work area 真正的中心是 (2453,408)，偏 +641,+360 = 恰好 (新尺寸−旧尺寸)/2；
两屏同缩放时这个偏差恒为 0，所以一直没人看见。落位由 `WindowCentering.CenterOnCursorScreen` 做（Show 之前只写 DIP
会把窗口建在**错误的屏**上，实测 (981,163) 落在主屏，所以物理像素那一趟才是算数的）。
仪器：`scratch/centerprobe`（带宿主同一份 PerMonitorV2 manifest，`EnsureHandle` 就能读，不用真开窗口）。

`CardRenderMode`（软件合成）仍只给三张"召唤即用"的卡片：对话框靠屏障已经把黑帧挪到屏幕外，不需要改它们的合成模式。

## 量测基线

数字来自一次性探针（已删，当时 gitignore 内、不入库）：跑约 3 分钟，会在屏幕左上角闪绿色卡片；机器为单屏
1920×1200 @100%、render tier 2、12 核；采样点每秒数百次读屏幕像素，每条场景 5~8 次；指标 = "卡片落到目标位置"
到"该点出现内容"的毫秒数。

| 呼出形状 | 轻内容 | 重内容（400 行 + Effect） |
|---|---|---|
| place→Show（改前） | 53–203 ms | 146–258 ms |
| 停靠行 Show→让过一帧→落位（改后） | 24–28 ms | 30–32 ms |
| 常驻可见停靠→移动 | 22–30 ms | 22–28 ms |
| 屏上改高度 | 9–12 ms | 无黑帧 |

首次呼出（重内容树 + 硬件合成）：

| 首次呼出形状 | 到出现内容 |
|---|---|
| cold，只有闸门 | 51–64 ms |
| cold，先停靠 Show 400 ms 预热再过闸门 | 21–28 ms |
| cold，先真实显示+隐藏再预热 | 28–46 ms |
| warm（第二次以后），只有闸门 | 20–24 ms |

停靠时长标定（同一冷对话框，CenterOwner + SizeToContent）：

| 停靠时长 | 落位到画好 |
|---|---|
| 0 / 30 / 60 / 100 / 150 / 200 / 260 ms | 28 / 24 / 27 / 22 / 29 / 31 / 23 ms |

这张表是平的：首次呈现被"必须在屏上"门控，停靠再久也不会变成 0。门的价值是把可见空窗从约 10 帧压到约 2 帧。

门的实效（`[WindowPark]` 两行 Info，某次现场日志）：

```
SettingsWindow           restored after 47ms  parkHeld=True   first frame 16ms after placed
PluginFieldPromptWindow  restored after 16ms  parkHeld=True   first frame 31ms after placed
InlineSearchWindow                            (gated)          first frame 63ms after placed
```

`parkHeld=True` 说明没有谁把窗口摆回去；设 `Owner` 不建句柄，`CenterOwner` 位置在 `SourceInitialized` 里已就绪。
肉眼还看得见的空窗是 1–4 帧。

下面这段是修尺寸回退之前某次呼出的现场日志。

```
38.670 召唤尺寸趟  forSummon=True searching=False results=0 visibleContent=True  550->36
38.699 卡片显示
38.717 搜索 's' 才开始
38.760 Results.CollectionChanged 已到（首批结果在集合里了）→ 被"搜索中不重排"否决
39.176 搜索落定 → 36->324
40.113 InlineSelectionChanged / InlineSearchFinished → 用户点了行，正常关闭
```

空条挂了 **477ms**，而内容在 **+60ms** 就已经到了。两个回退规则各自负责一种难看：`CurrentLayout()` 搜索中回退到
**满行预算**（= 之前那个 324 大空框），`FreezeCurrentResultsArea()` 在召唤时把旧区域冻结成 **0 行**（= 这条 477ms
空条）。

变体表来自一次性探针（已删、不入库），它复刻 `App/Views/Controls/Dialogs/PluginFieldPromptWindow.xaml` 逐项截图
读像素（同一台机器 100% DPI、浅色主题 `GlacierLight`），从窗口外沿向内走。该弹窗是级联文件夹的
"添加文件夹"确认框，右、下两侧各有一条 14 DIP 的纯黑带：

| 变体 | 窗口 | 内容 | 右/下 14 DIP | 窗内近黑像素 |
| --- | --- | --- | --- | --- |
| 原样（`SizeToContent=WidthAndHeight`，`MinWidth=480 MinHeight=160`） | 480×347.7 | 466×333.7 | `000000` | 10629 |
| `ResizeBorderThickness=0` / `ResizeMode=CanResize` / 去掉 `WindowChrome` | 同上 | 466×333.7 | `000000` | 同上 |
| `MinWidth=100` | 100×347.7 | 86×333.7 | `000000` | 同上 |
| Min 尺寸挪到内容 Border 上 | 494×347.7 | 480×333.7 | `000000` | 同上 |
| `Window_Loaded` 里锁定 `SizeToContent=Manual; Width=ActualWidth; Height=ActualHeight` | 480×372 | 480×372 | 内容色 | 127（抗锯齿） |
| `SizeToContent=Height` + `Width=480` | 480×357.7 | 480×357.7 | 内容色 | 127 |

规则（可复用，与上面的首帧规则相互独立）：**`SizeToContent` 非 `Manual` 时，WPF 只把窗口夹到
`MinWidth/MinHeight/MaxWidth/MaxHeight`，内容仍按其自身期望尺寸排在左上角，剩下的一条用窗口类背景刷填满 ——
不透明窗口上就是黑色。** 与 `WindowChrome` 的 resize 边框、`ResizeMode`、Min 尺寸放在哪一层都无关；本机实测
固定 14 DIP（右、下各一条），`MinWidth=MinHeight=100` 时同样是 14。

副产品：DWM 会给每个圆角窗口自己画 1 px 边框（本机浅色主题下 `#BABDC8`，压住应用自己的 `#DCE3EA` 1 DIP 描边），
`DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR=34, 0xFFFFFFFE)` 可以让出这一像素。这条与黑边无关，未改，
核对到 `9a6b9769` 全仓仍未设该属性。

## 被否证的理论

每条留一句结论，免得下次再走一遍。

- 曾判断"停在屏幕外等 `ContentRendered` 再上屏"可以当闸门：该事件在 -32000 停靠位 32/32 超时从不触发（窗口不与
  任何显示器相交 → 可见区域为空 → 不发 `WM_PAINT`），因此这个闸门设计不可用。
- 曾判断"常驻可见（永不 Hide）更保险"：实测与"停靠 Show + 一帧后落位"延迟等价（26 vs 30 ms）。选了后者，因为它
  不需要改 `IsVisible` 语义、不需要摘掉 owner HWND、也不让卡片长期挂在 UIA 树里。
- 曾判断软件渲染只是"更保险"、可能更慢：每一种呼出形状都更快或持平，因此"更慢"这一条不成立；它同时省下首次显示
  建 D3D 设备与尺寸/DPI 变化重建表面这两笔开销。
- 曾把"极其明显"的首次呼出归给冷启动时序：冷启动只比热启动慢 30–40 ms，解释不了；真正成因在代码里，是我在
  `183a3982` 引入的回归：`UpdateSearchDisplay(string.Empty)` 挪进 `Summon()`，而它排在
  `ApplyCardHeight()` 之前，清空文本框会立刻发起空查询搜索，`ApplyCardHeight` 开头那句
  `if (IsSearching) return;` 于是否掉了唯一一次上屏前的尺寸计算，卡片沿用上一会话高度，而进程第一次呼出用的是
  XAML 的 `Height="550"`，比真实卡片（对话框 4 行 ≈ 150–250 DIP）高两到三倍，一块深色板子看起来就是黑框；
  后续呼出继承了一个"差不多对"的高度，所以只有首次最明显。`af8f944e` 修掉。
  教训（下次直接照这条查）：**"首次/某次才明显"几乎都是状态继承差异**，延迟类假设要先按"只有第一次"
  这个限定词反推；另外 `deferred pass` 的 `if(...) return;` 守卫一旦被搬进同步路径，就会顺手否掉同步那一次，
  移动副作用调用时必须检查下游守卫。
- 曾判断剩下的"一闪而过"仍是绘制问题：上面那段日志显示内容 +60ms 已到、空条挂 477ms，成因是尺寸回退规则
  （`b59beb23` + `4d1abc17` 挡掉），这条与绘制无关。
- 曾判断"每次 dismiss 清缓存导致搜索变冷"：`UsnIndexer.ClearAllPathCaches()` 是空方法
  （`Core/Indexer/Usn/UsnIndexer.cs:159-161`，注释写明 Snapshot 没有逐行路径备忘可清），因此这条不成立，已排除。
- 曾判断"停靠更久能把未绘制帧挪到屏幕外"：停靠时长 0–260ms 的标定表是平的（22–31ms），首次呈现被"必须在屏上"
  门控，因此这条路不通；`20692d47` 把两处过度声称的注释改回事实。
- 曾判断还有一块约 200ms 的黑框：`[WindowPark]` 显示 `parkHeld=True`、首帧 16–63ms 后到，剩下的肉眼空窗是 1–4 帧，
  因此"还有几百毫秒黑框"这一判断不成立。
- 曾判断 `RenderTargetBitmap` 离屏预热能压掉这段时间：149ms 只降到 133ms，这条路不通。
- 曾判断那 1–2 帧若是类背景刷擦出来的，可以在 `WM_ERASEBKGND` 里用窗口自己的底色填掉，黑框变"卡片色空框"：
  一次性探针实测（`hook=erase`，两次 `[erase] 406x300 filled=1`，钩子确实执行）显示屏幕采样点在 152ms 之前仍是
  桌面色 → WPF 窗口的客户区由渲染线程表面合成，GDI 擦除结果根本不上屏。所以他机上的黑更可能是 DWM 未初始化表面，
  这条路救不了，代码从未入库（`UnpaintedBrush.cs` 已不在工作区，全仓无相关钩子）。
- 曾判断常驻黑边是首帧没画出来：`[WindowPark]` 显示该弹窗首帧门已生效（`restored after 46ms, parkHeld=True` /
  `first frame 16ms after it was placed`），黑带在之后一直在，因此首帧解释不成立；实际是 `SizeToContent` 那条规则。
- 曾判断黑边与 `WindowChrome` 的 resize 边框、`ResizeMode`、Min 尺寸放在哪一层有关：变体表这几行读数完全相同
  （右/下仍是 `000000`、近黑像素 10629），因此都与它们无关。

两个踩坑记录（错了会在 WPF 消息钩子里把进程直接带走，且**没有托管异常痕迹**）：

- `CreateSolidBrush` 在 **gdi32**，`FillRect` 在 **user32**（`winuser.h`）。写错 DLL → `EntryPointNotFoundException`
  在 `Show()` 里当场崩，探针 exit=127、一行输出都没有。
- 结论：先让探针跑绿，再往 app 里发。这一次又一次靠"先在探针里试"避免了一次线上崩溃提交。

## 还没验证的（需要在真机复测）

1. **跨 DPI 副屏**：本机只有一块屏，"停靠保留目标 x → 按目标缩放合成"这条只在原理上成立，没量到。复测：副屏
   （与主屏不同缩放）呼出快速窗口与内联卡片，看是否还有黑框。若仍在，剩下唯一嫌疑是 `WM_DPICHANGED` 在 `Show()`
   之后才送达；下一步就把落位改成"先按目标缩放做一次隐藏态移动、再 Show"，或让卡片在隐藏期就切到目标 DPI 上下文。
2. **混合显卡（Optimus）副屏**：软件合成应已消除跨适配器拷贝，需实测。
3. **复用后的行为回归**：`InlineOpenedFoldersRefreshHelper` 的"每窗口一次快照"（该文件 :20/:29 仍是按窗口实例记）
   现在等于"每进程一次"，Directory Opus / TC 的已打开文件夹列表会跨整个会话变陈旧；通知卡逐条新建未改（只改了
   落位时机）。还要过一遍的是卡片停留期间改宽度、actions 菜单进出、拖拽 Logo 定位，以及点击外部关闭和
   Esc/Enter 执行。
4. **最后 1–2 帧是什么颜色**：类背景刷（可用 `WM_ERASEBKGND` 改成卡片色，但这条已在本地否证）还是未初始化的 DWM
   表面（改不了）。这台机器上两种情况都显示桌面色，无法分辨，只能在真机上一次试验定论。
5. **单字符 's' 在本目录搜索落定要 459ms**（首批 60ms、终批 459ms）：这条与绘制无关，是搜索/服务往返的延迟，
   至今没有解释，值得单独一轮。
6. **预热方案（启动时停靠 Show 一次）**：经测只值 30–40 ms，且常驻停靠 Show 是否能合成帧在单屏机上不可证，暂不做。

## 若还要再快

`resident-parked→move`（常驻可见）仍是 22–30 ms 且不需要"等一帧"，是这条路的下一步；代价就在被否证那条
"常驻可见更保险"里列着（`IsVisible` 语义、UIA 常驻可见、owner 关系）。除非改后仍测到 >60 ms，不值得付。

## 取证命令

一次性探针，脚本已从本目录删除，命令形态留在这里备查：用 `Add-Type` 起 P/Invoke，`EnumWindows` +
`IsWindowVisible` + `GetWindowRect` + `GetClassName` + `GetWindowText` + `GetWindowThreadProcessId`，再对每个句柄取
`DwmGetWindowAttribute(hwnd, 14 /* DWMWA_CLOAKED */, out int, sizeof(int))`；按固定间隔拍快照、打印两次快照之间的
NEW/GONE 差集。hidden 窗口要留在结果里，被找的那个对用户本来就是不可见的。调用形态：
`pwsh -NoProfile -File <脚本> -Watch 45 -Launch <Lertaro.App.exe 路径>`（可加 `-Out <文件>` 同步落盘）。它当时用来
判定"屏幕上那个矩形背后到底有没有一个窗口"，也就是把合成残留与真实窗口分开。

现行可复用的取证手段是应用自己的 `[WindowPark]` Info 两行（park 是否守住 + 首帧在落位后多久到），以及
`WindowParkingTests`；边缘命中那一类改由 `Tests/App/Views/WindowEdgeResizeBandTests.cs` 覆盖。
