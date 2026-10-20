# AutoCAD 图形另存为：当前目录仍拿不到

## 适用范围与核对基线

本文的来源是报告用户在 `acad.exe` 的 `图形另存为`（`#32770`，dialog `148,51..974,528`，pid 1708）上用一次性对话框探针跑出的两份完整控件输出，加上其手工做的 E1（2026-10-17/18）。那份探针脚本没有入库。日志摘录里的用户目录一律转写成 `%USERPROFILE%`。本机没有 AutoCAD，本文只写这些实测能证明的东西。

已修复的两条不在这里重复：放置 `52cdf3c4`、文件名残留 `d16b4bcc`。

代码侧的说法按 `main` = `9a6b9769` 重新核对过，文中的 `文件.cs:行号` 都是这个提交上的行号。J2 与 J4 到这一版仍未动手，`Plugins/AutoCAD/` 下没有任何读 `13001` 的代码。

## 跨进程读对话框的机制规则

`WM_GETTEXT` / `WM_SETTEXT` / `CB_GETLBTEXT` / `LB_GETTEXT` 这几条是 user32 自己跨进程搬运字符串的消息：缓冲区必须留在**调用方进程**，传 `VirtualAllocEx` 的远端地址会读到全空。曾用一次性探针在一个内容已知的 combo 上验过，`CB_GETLBTEXTLEN` 返回 18 和 10，正是 `AutoCAD 图形 (*.dwg)` 与 `所有文件 (*.*)`，远端写法的四种变体全部没有写入任何字节，本地缓冲区一次就读对了。

反过来，`CDM_GETFOLDERPATH` 这类 `WM_USER+N` 的应用私有消息**不会**被搬运，必须用远端缓冲区，这正是 `Plugins/CoreExtensions/FileDialog/ClassicFileDialogAdapter.cs:34` 注释里写的原因，也是 `GetCurrentPath` 里 `VirtualAllocEx` 的用法是对的（`Plugins/AutoCAD/AutoCADFileDialogAdapter.cs:34`）。两套写法不能混。

树里的两处实现各自站在正确的一侧：`ReadTextBox` 用本地 `StringBuilder` 走 `WM_GETTEXT`（`Plugins/AutoCAD/AutoCADFileDialogAdapter.cs:219`），`GetCurrentPath` 用远端缓冲区走 `CDM_GETFOLDERPATH`。读 combo 条目属于前一批，用本地缓冲区。

### 提权令牌下的存在性判断

`IFileDialogAdapter` 的方法都在**提权 hook 进程**里执行（调用点：`Core/Hook/Commands/FileDialogCommandHandler.cs:58`、`Core/Hook/ExplorerIntegration/FileDialogNavigationTracker.cs:46`），而用户未提权映射的网络盘在那个 token 下**根本不存在**：`Directory.Exists` / `File.Exists` 会返回 false，尽管对话框自己能把那条路径解析并跳转得好好的。`IFileDialogAdapter.TargetIsFolderOnly` 的注释写的就是这件事（所以 file→folder 的解析留在交互进程里做）。任何适配器里新写的存在性判断都会继承这个偏差，判断"这是不是目录"要留一条不依赖 I/O 的退路。

AutoCAD 这份的退路已经落地：`IsFolderNavigation` 用"扩展名有无"补足存在性判断（`Plugins/AutoCAD/AutoCADFileDialogAdapter.cs:211`）。一个这个进程看不见的真目录读出来是两个 false（`File.Exists` 与 `Directory.Exists`），一个没有扩展名的文件读出来也是同样的两个 false，所以没扩展名就按目录处理。

## AutoCAD 另存为的实测结构与控件 id

- **AutoCAD 自绘的对话框模板**：控件 id 是 13000 / 13001 / 13006 / 13008… 与 1008 / 1009 / 1010 混着，comdlg32 自己的 1090 / 1120 / 1121 / 1148 / 1152 一个都没有。任何"这是通用保存对话框"的推断都不成立。
- `CanHandle` 三个门槛全中：`Edit 1001`（文件名框）、`SysListView32 id=1` "FolderView"（`285,115..695,433`，真 shell 视图）、`ComboBox 1008`（空文本，挨着 `Static 1009 包含:`）。三个判定在 `Plugins/AutoCAD/AutoCADFileDialogAdapter.cs:445` 起。
- 整个对话框**只有一个 Edit**，就是文件名框。`Plugins/AutoCAD/AutoCADFileDialogAdapter.cs:311` 的 1152→1148→任意 Edit 链里前两级是死代码，但结果是对的。
- **跳转是工作的**：E1 里手动输入 `D:\a` 按回车，对话框真的跳进去了。所以 `NavigateTo` 的提交（`EN_CHANGE` + 延时 `PostMessage` 回车）不是问题，别去动它。
- `CDM_GETFOLDERPATH`：应答，但拷贝 0 字符。

## 仍未修复

### J2（P1）卡片拿不到对话框当前目录

`GetCurrentPath` 只有 `CDM_GETFOLDERPATH` 一条来源，两次实测都是拷贝 0 字符 → 恒为 null。**这条在上报的 `app.log`（2026-10-18 00:38）里已经 live 确认**，而且能看到后果：

```
[ExplorerTracker] Adapters for 0xa03dc: dialog=AutoCADFileDialogAdapter, inline=none (class=#32770, process=acad)
[ExplorerTracker] UpdatePath captured path: %USERPROFILE%\Desktop (isDesktop=False)   ← 先报了一个错的目录
[ExplorerTracker] UpdatePath captured path:  (isDesktop=False)                          ← 然后空
[Diagnosis] SearchScope='', isDialog=True, lastPath='%USERPROFILE%\Desktop', dirExists=True
[SearchExecutionEngine] Performing search: 'win', scope: ''                             ← 无域搜索
```

所以在这类对话框里卡片不但没有作用域，还会在头 ~300ms 里把上一次抓到的**桌面**当成这个对话框的目录显示。`Adapters ... dialog=AutoCADFileDialogAdapter` 那行也顺带证明了 `CanHandle` 在真机上是命中的，放置修改走的就是这个适配器（同一份日志里卡片随后报出 `anchor=316,459,802,473 list=646,108`、`at=350,502`，即锚点与列表都有了）。

这四行在当前树里都还在，前两条出自 `Core/Hook/ExplorerIntegration/ExplorerTracker.cs`（`:194` 和 `:417`），另外两条是 `App/ViewModels/Search/ExplorerJumpSuggestionHelper.cs:27` 与 `App/ViewModels/Search/SearchExecutionEngine.cs:127`。只有 `[Diagnosis]` 那行末尾多了一个 `isSamePath=` 字段。

有一条有希望的新线索。`保存于` ComboBox 13001 实测 **5 个条目、选中第 4 个**，窗口文本是当前目录的显示名（`文档` / `GuoheView`）。如果那 5 个条目像 comdlg32 的 Look-in 一样带真路径，当前目录就能从选中项读出来。需要报告用户用**更新过的**探针再跑一次（现在它会打印 combo 条目文本了），看 13001 的条目是显示名还是路径。在此之前不动代码。

### J4（P3，新观察，未动手）卡片每次呼出的头一帧仍按"量不到几何"摆放

上报的 `app.log` 里每次新建卡片都有这样一行，约 80ms 后才变成带 `anchor`/`list` 的那行：

```
00:38:28.263 [InlineCard] ... anchor=none list=none window=325x550 ... at=350,259   ← 压在对话框下半部
00:38:28.343 [InlineCard] ... anchor=316,459,802,473 list=646,108 window=325x217 ... at=350,502
```

这是设计使然：几何读取被 `InlineDialogGeometryProbe` 挡在 UI 线程之外（理由见 `App/Views/InlineSearchWindow/Helpers/InlineSearchWindowPositioner.cs:45`），所以第一遍必然没有答案。这行日志现在还在 `InlineSearchWindowPositioner.cs:332`，`list=` 打的是 `Right,Top`。

要消掉这一帧，得按对话框 hwnd 记住上一次量到的 `anchor`/`list` 并在卡片重建时复用。卡片每次呼出都是销毁重建的，而对话框还在，所以这份缓存必须放在窗口之外；`InlineDialogGeometryProbe` 的缓存键是对话框 hwnd 加它当时的矩形（`InlineDialogGeometryProbe.cs:32`），复用时照样得在窗口移动后重量一次，另有每次布局 3 次的提问预算（`:28`）。这条改的是 App 侧、所有对话框宿主共用的路径，本轮范围限定在 AutoCAD，所以没动，是否放宽由维护者定。

### 还缺的那份日志与取证方法

`[AutoCAD] navigate to ... file= directory= folderNavigation=` 和 `[AutoCAD] folder jump: wrote ..., box now ..., name to restore ...` 这两行是**提权 hook 进程**写的，文件名是 `hook.log`（`Service/Program.cs:22`，和 `app.log` 同一个 logs 目录），不在这份上报的 `app.log` 里。而且 hook 的级别取自它自己读到的 `settings.LogLevel`（`Service/HookModeLauncher.cs:33`）—— 若 `hook.log` 里没有这两行，先确认日志级别是 Debug。

按 `9a6b9769` 核对，要抓的行名已经变了。第一条还在，末尾多了 `edit=0x...`（`Plugins/AutoCAD/AutoCADFileDialogAdapter.cs:86`）；第二条在 `7c32a562` 之后换成了两行：`[AutoCAD] folder jump undo: {reading} on box 0x...`（`:133`）和 `[AutoCAD] folder jump undo pass {pass}: name written into box 0x...`（`:141`）。`{reading}` 取 `KeepWaiting` / `PutNameBack` / `LeaveAlone` 三个值之一（`:163`）。

## 本机验不了的部分

- 放置修复和文件名回填都只测了纯逻辑（positioner 的算术用上报的实测矩形；回填的判定条件 4 个用例，在 `Tests/Plugins/AutoCAD/AutoCADFileDialogAdapterTests.cs`）。真机效果必须 AutoCAD 在场。
- 回填不再用固定等待（旧的 1200ms 常数在报告用户那台机器上是看得见的延迟，在慢机器上又可能等不够）：轮询字段，连续 4 次读到「仍然是我们写进去的那串路径」才写回名字；第一遍在提交之后（>400ms）开始，第二遍在第一遍写完后 700ms 再看一次，专治慢机器上 AutoCAD 把路径又写回来的情形；每遍最多等 2500ms。快机器大约 550ms 就把名字放回去了。这几个数仍是经验值，慢机器上的实际表现要在报告用户那台机器上跑；日志会说明走了哪一路（undo pass 1/2、LeaveAlone、超时）。
- 精确等值比较是有意保留的：两份上报的 `hook.log` 里 wrote 与 box now 都逐字符相等（一例带尾分隔符的桌面路径、一例不带尾分隔符的 Z 盘映射路径）。判定在 `ClassifyReading`（`Plugins/AutoCAD/AutoCADFileDialogAdapter.cs:170`）里读的就是 `now != pathAskedFor`。若某个 AutoCAD 版本自己规范化了路径，判定只会得到 LeaveAlone，退化成残留，不会误写。
- 空框不再回填（前一版会回填）：上报的证据是 AutoCAD 跳转后把路径留在框里，所以框一旦为空，就说明里面的东西不是这次跳转留下的，不该由我们填。
- 同一份提交代码也用在 `Plugins/CoreExtensions/FileDialog/ClassicFileDialogAdapter.cs:82`（AutoCAD 那份是它的拷贝），残留问题在那些对话框上是否也存在没有实测过；本轮范围限定在 AutoCAD，其他适配器没有动。
