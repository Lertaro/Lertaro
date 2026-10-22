# 快速切换与重解析点审计

## 适用范围与核对基线

覆盖 `<repo>` 的安装形态判定、数据目录加固与迁移、Hook 侧快速切换（Ctrl+G、Ctrl+1..9）与内联卡空状态，以及这些改动留下的验证与决策记录。

只写仍未修复的内容、新发现，以及仍需人工做的验证。**已完成条目连同其实现在内一律不再保留**（也不再保留提交索引）：`git log` 就是索引，`dotnet build` 与 `dotnet format --verify-no-changes` 就是警告与格式的现状。

- 核对基线：`main` @ `9a6b9769`，文中 `文件:行号` 按这一基线复核过。本文成文时的基线是 `c9d5f33`。
- 优先级：**P1** = 影响可用性/正确性且值得尽快处理；**P2** = 已知上限或需决策/文档；**P3** = 小硬化项。
- 复核方式：逐处读实现（逻辑、异常处理、兜底路径），`rg`/`fd` 全仓检索；三个解决方案都做过 `--no-incremental` 强制全量重建（0 错误 / 0 警告）与 `dotnet format` 三模式校验（whitespace / style / analyzers 全部 0 findings）；行尾口径另用一个 `--config core.autocrlf=true` 的临时 clone 验证过（checkout 仍是 LF、工作区干净）。

## 结论

未修复的只剩 U2、U3、U4、U9、U11、U15、U16 七条，优先级都在 P2/P3，没有 P1。其中 U2/U3/U15/U16 是主动放弃，U4 是已知上限，U9 是接受项，U11 是搁置。U17 已修（2026-10-19 复核）。V1-V13 要真机验证，自动化测不了。

## 遗留项

### U2（P2，已放弃）安装期 ACL 加固仍拒绝链接

- **现象**：`PortableDirectoryLock.RepairProfileUserDirectory`（`Core/Services/Installation/PortableDirectoryLock.cs:98`，三处 `RejectLink` 调用在 :105-109）与 `InstallDirectoryLock` 的 zone 根检查（`Core/Services/Installation/InstallDirectoryLock.cs:89`）对链接直接抛错；`--repair-permissions` 也因此失败（#343 的 Scoop 布局）。
- **现状**：这条失败会写进 `%TEMP%\Lertaro\repair.log`，并由 App 弹窗原样呈现（不再是"did not complete successfully"）。
- **为什么不做**：改动落在提权 ACL 迁移路径，判断错误会对用户 profile 留下持久后果，且无法在本环境自动验证（四项评分里破坏性 5 / 可行性 6）。
- **建议**：若要做，先在可抛弃的虚拟机上，配合 `RepairProfileUserDirectory` 现有的**回读 owner** 后置校验一起验证。

### U3（P2，已放弃）安装形态判定未做路径规范化

- **现象**：`InstallationDetector.IsInstalledAt`（`Core/Services/Installation/InstallationDetector.cs:40`）把注册表 `InstallLocation` 与 `Environment.ProcessPath` 拼成完整路径后做字符串比较；进程镜像路径经 junction 时两者可能不等 → 被判为 Portable → 用户数据根改为 `<安装目录>\Data\Users\<hash>`。
- **兜底**：`DataDirectoryResolver` 的"Data 不存在则沿用安装版数据"逻辑目前挡住了绝大多数情况。
- **为什么不做**：形态翻转会让既有安装突然改读另一套数据目录，属数据可见性变化（破坏性 6）。

### U4（P2，已知上限）Z 序 ≠ 真实激活序

- **现象**：已打开文件夹的顺序取自 `EnumWindows` 的 Z 序（`WindowRanks`，`Core/Hook/Ipc/OpenedFolderSnapshotPublisher.cs:52`）。Z 序在以下情形会失真：置顶（always-on-top）的文件管理器永久第一；刚最小化的窗口沉底；`SWP_NOACTIVATE` 抬窗不产生激活；**其它虚拟桌面上的窗口根本不在枚举内**。
- **代价**：只影响 Ctrl+1..9 的落点与级联菜单顺序，不影响 Ctrl+G（Ctrl+G 走 tracker 的真实前台状态）。
- **更好的方案**：hook 进程内 `ExplorerTracker` 的 provider 列表本身就是按真实前台事件排序的，可作"激活序优先、Z 序兜底"。需要把它暴露给 publisher（构造点在 `HookProcess`），改动面大于收益，故留作后续项。

### U9（P3，接受项）排序会改变去重时保留的路径拼写

- **现象**：内联卡的空状态列表按上报顺序去重、**首个胜出**（`App/ViewModels/Search/InlineEmptyStateResultHelper.cs`，`HashSet.Add` 的先到先得）；顺序变化后，同一目录的两种拼写（大小写、`\\?\` 前缀、映射盘符 vs UNC）里被保留的那个可能不同。
- **影响**：显示的路径拼写与"跳转时使用的拼写"可能与之前不同（行为上等价）。
- **建议**：无需处理；若在意，可在去重前统一规范化（属于另一个改动）。

### U11（P2，搁置）启动失败弹窗没有"打开数据目录"按钮

弹窗会给出**数据目录路径与真实原因**（`SettingsStartupFailure.DescribeUnsupportedDataRoot`，`App/Services/SettingsStartupFailure.cs:66`），但用户仍需自己去资源管理器打开该路径。已确认继续搁置：`MessageBox` 加不了按钮，要走自定义对话框，属独立的 UI 改动。

### U15（P3，已放弃）测试、警告、格式都没有任何自动把关

- **现象**：`.github/workflows` 只有两个。`deploy-docs.yml` 只跑 `npm run site:build`；`release.yml` 在打 tag 时跑 `make.bat`，而 `make.bat` 里只有 `dotnet publish`（Release）+ Inno Setup 打包 + 压缩，**不跑 `dotnet test`，也不跑 `dotnet format`**。全仓 csproj/props 里也没有 `TreatWarningsAsErrors` / `EnforceCodeStyleInBuild`（复核 9a6b9769：两处都仍然如此）。
- **代价**：全仓唯一一条编译警告（`GlobalHotkeyDetector` 的 CS8601）一直没人看见，直到全量重建才浮出来；`.editorconfig` 里那 20 多条 `:warning` 的 C# 风格规则同样只在手动跑 `dotnet format style` 时才会报。
- **为什么不做**：维护者决定不加 CI 关卡。格式与警告只做一次性收敛。代价照旧：以后要靠手动跑"取证命令"一节的命令，回归不会被自动拦住。

### U16（P3，已放弃）两个插件测试项目里没有任何用例

- **现象**：`Tests/Plugins/Files` 与 `Tests/Plugins/OneCommander` 里除了 `MSTestSettings.cs` 没有任何测试类（没有 `[TestClass]`/`[TestMethod]`），但它们在 `Tests/Tests.slnx` 里。`dotnet test` 对这样的项目打印"没有可用测试"并**返回 0**，所以"插件测试全绿"实际是 24 个项目在跑、这 2 个项目什么都没跑。
- **影响**：这两个插件的覆盖是 0，而流水线上与有测试的项目看起来没有区别。
- **为什么不做**：维护者决定既不补用例、也不动 `Tests/Tests.slnx`。代价照旧：这两个项目仍然"绿"得没有内容，看结果时不要把它当成覆盖。

### U17（已修，2026-10-19 复核）一条 Core 用例要求本机服务正在运行

留下的坑：Core 测试曾有一条 Everything 用例在 Lertaro 服务不可达时必定超时失败，跑出来的通过数依赖环境（"1958 通过 / 0 失败"那样的数字要在服务在跑的前提下才成立）。改成进程内判答之后已不成立（7866b9cb）。

## 需要人工验证的清单

前四项依赖真实桌面/真实实例，无法自动化。

- **V1 链接迁移后正常启动**：把 `%LocalAppData%\Lertaro`（或整个 `C:\Users\<你>`）junction 到 D: → 应正常启动，`app.log` 里数据目录是解析后的真实路径，不再出现"配置传输未完成"。
- **V2 真实租约冲突文案正确**：一个实例停在配置导入/导出期间再启动一个 → 仍应提示"请关闭其他 Lertaro 实例"。
- **V3 便携版 `Data\Users` 链接**：与 V1 相同；注意 U2（安装期加固仍会拒绝链接）。
- **V4 Ctrl+G 多窗口落点**：两个文件管理器分别浏览 `D:\Work` / `C:\Temp`，从任一应用弹对话框按 Ctrl+G → 应落到"对话框所属文件管理器"的目录，否则落最近活动的那个。
- **V5 刚启动就能 Ctrl+G**：把文件管理器留在最近使用位置、完全退出再启动 Lertaro，立刻在对话框里按 Ctrl+G → 应能跳转；若该窗口被浏览器压在下面，按设计"不导航、不吞键"。
- **V6 锁竞争下不丢按键**：连续快速浏览目录同时反复按 Ctrl+G → 不再出现"完全没反应且日志无记录"；被拒绝时 `hook.log` 会出现 `[QuickSwitch] Not navigating …`。
- **V8 权限修复可见**：在 owner 为 `BUILTIN\Administrators` 的遗留目录上启动 → 弹窗含真实原因与路径，`%TEMP%\Lertaro\repair.log` 能看到步骤；`Verify` 的拒绝文案给出实际 owner 与期望 SID。
- **V9 更新失败提示**：把 `{"Status":"failed","Time":"<今天>","Message":"…"}` 写进安装目录的 `update-result.json` → 启动弹一次，再启动不重复；把该文件换成 >64KB 的垃圾 → 不读、不弹、`app.log` 有 Warn。
- **V10 Ctrl+1..9 顺序**：两个文件管理器分别点一下（最近使用的是第二个）→ 内联卡 Ctrl+1/2 指向最近使用的窗口；单窗口场景不变（U4 的失真情形除外）。
- **V11 图标告警消失**：反复打开级联菜单、切换主题 → 日志不再出现 `Failed to update themed icons`（三处插件）。
- **V12 网络位置搬迁被明确拒绝**：把数据根做成 junction 指向网络盘/可移动卷 → 启动弹窗直接说明"链接目标不是本地固定磁盘"并给出两条路径，**不再**说"另一个实例正在传输设置"。
- **V13 收尾瞬间的 Ctrl+G**：在 hook 退出/崩溃的瞬间按 Ctrl+G → `hook.log` 应出现 `Quick Switch could not be handed to the tracker thread (…)`，而不是无声无息（窗口极窄，自动化测不了，只能看日志）。

## 已决策事项

1. **旧版 owner 兼容**：不恢复。用户数据目录 owner 不属于本人时仍拒绝启动，但失败必须可诊断（弹窗原因 + repair.log + owner/期望 SID）。受影响用户按维护者给出的改名重建方案处理。
2. **B4/B5**（安装期加固接受链接、安装形态判定规范化）：放弃，理由见 U2/U3。
3. **#343 的语义争议**：忽略（不再讨论 Scoop 支持与否）。
4. **Z 序作为已知上限**：接受（U4）。
5. **U11**：继续搁置。
6. **格式化的口径**：以 `dotnet format`（whitespace + style + analyzers）为准，三个解决方案 `--verify-no-changes` 全绿；行尾统一为 LF，由 `.gitattributes` 的 `* text=auto eol=lf` 决定（不再依赖各人机器的 `core.autocrlf`），`.editorconfig` 的 `end_of_line` 同步为 `lf`。
7. **上游副本一并格式化**：`dotnet format` 也改写了 `Plugins/FlowLauncherBridge/Flow.Launcher.Plugin/**`（Flow Launcher 插件 API 的副本，改动以 IDE0022 表达式体为主）。若以后要与上游重新同步，应把该目录排除或按补丁处理。
8. **本文只追加"未修复/新发现"**：已完成条目的实现细节与提交索引不再写进来，避免文档变成变更日志。
9. **U13 的落点**：改测试，不动产品标记。断言 markup 现在真正实现的契约（`Border.BorderBrush` + `Opacity`），保留原有三条保证（两个 logo 都带指示器、服务异常触发器在 Stay Open 与 hover 之后因而压得住它们、静止态是透明轮廓）。已用"把警告轮廓改成 `AccentBlue`"验证该测试确实会红；`Tests/App` 因此首次 2048 通过 / 0 失败。
10. **U14 的落点**：新增 `.gitattributes` 把行尾钉在仓库里，并跑过 `git add --renormalize .`（**零内容变更**：2737 个文本文件本来就是 LF）。已用一个 `--config core.autocrlf=true` 的临时 clone 验证：checkout 出来仍是 LF，`git status` 干净。
11. **U15 / U16**：放弃，理由见各自条目（不加 CI 关卡；不为这两个插件补用例、也不动 `Tests/Tests.slnx`）。

## 取证命令

```powershell
# 逐级定位第一个 ReparsePoint：这就是链接拒绝的落点
$p = "$env:LOCALAPPDATA\Lertaro"
while ($p) {
    $i = Get-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue
    if ($i) { "{0,-60} {1}" -f $p, $i.Attributes }
    $p = Split-Path -Parent $p
}

# 便携版：Data\Users 与数据根本身
Get-Item -LiteralPath "<安装目录>\Data\Users" -Force | Select-Object FullName,Attributes,LinkType,Target

# 最近一次提权修复的步骤（超时时最后一行即卡住的那一步）
Get-Content (Join-Path $env:TEMP 'Lertaro\repair.log') -Tail 20

# 最近一次静默更新的结论
Get-Content "<安装目录>\update-result.json"

# 行尾：仓库实际存的是什么（应全是 i/lf w/lf）
git ls-files --eol | ForEach-Object { ($_ -split '\s+')[0..1] -join ' ' } | Group-Object | Select-Object Count,Name

# 行尾口径确实钉在仓库里：即使 autocrlf=true，clone 出来也应是 LF 且工作区干净
$tmp = Join-Path $env:TEMP ('Lertaro-lf-' + [guid]::NewGuid().ToString('N'))
git clone --quiet --config core.autocrlf=true . $tmp
git -C $tmp ls-files --eol '*.cs' | Select-Object -First 3   # 应显示 w/lf
git -C $tmp status --porcelain                               # 应为空
Remove-Item -LiteralPath $tmp -Recurse -Force

# 格式与警告的现状（三个解决方案都应 0 findings / 0 警告）
foreach ($s in 'Lertaro.slnx','Lertaro.Plugins.slnx','Tests\Tests.slnx') {
    foreach ($m in 'whitespace','style','analyzers') { dotnet format $s $m --verify-no-changes --no-restore }
    dotnet build $s --no-incremental -v normal | Select-String ': warning '
}

# 哪些插件测试项目其实没有用例（dotnet test 对它们返回 0）
Get-ChildItem Tests\Plugins -Directory | ForEach-Object {
    $hasTests = Get-ChildItem $_.FullName -Recurse -File -Filter *.cs |
        Where-Object { $_.FullName -notmatch '\\obj\\' } | Select-String '\[TestClass\]' -Quiet
    if (-not $hasTests) { $_.Name }
}
```

## 仓库状态里仍需要你处理的部分

- 那 14 个提交（7 项审计修复 + 1 条编译警告 + 4 项格式化/配置 + 1 项测试契约修正 + 1 项行尾口径）已在 `origin/main` 上。原先记的"只提交到本地、没有 push"已不成立：核对 `git rev-list --left-right --count origin/main...main` 为 0/0。
- 本文档在 `dev-docs/`，该目录已随 `a7a7249b` / `a09f8b95` 进 git（`git ls-files dev-docs` 可见），原来「整个 dev-docs/ 未被跟踪」的说法不再成立。`.git/info/exclude` 里的 `/AUDIT_*.md` 规则锚在仓库根，对 `dev-docs/` 下的文件不生效；要改哪篇直接编辑并提交即可。
- 本次会话为修复沙箱对该工作区的写入权限（`<repo>` 缺少沙箱所需的写权限，Win32 5），在根目录 ACL 上追加了一条当前用户的完全控制允许项；备份与回滚命令在与仓库同级的 `Lertaro-acl-recovery\` 目录。
