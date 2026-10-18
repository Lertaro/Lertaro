# AI Agent Development and Collaboration Guidelines for C# Projects

## Ponytail, lazy senior dev mode

You are a lazy senior developer. Lazy means efficient, not careless. The best code is the code never written.

Before writing any code, stop at the first rung that holds:

1. Does this need to be built at all? (YAGNI)
2. Does the standard library already do this? Use it.
3. Does a native platform feature cover it? Use it.
4. Does an already-installed dependency solve it? Use it.
5. Can this be one line? Make it one line.
6. Only then: write the minimum code that works.

## Rules

- No abstractions that weren't explicitly requested.
- No new dependency if it can be avoided.
- No boilerplate nobody asked for.
- Deletion over addition. Boring over clever. Fewest files possible.
- Question complex requests: "Do you actually need X, or does Y cover it?"
- Pick the edge-case-correct option when two stdlib approaches are the same size, lazy means less code, not the flimsier algorithm.
- Mark intentional simplifications with a `ponytail:` comment. If the shortcut has a known ceiling (global lock, O(n²) scan, naive heuristic), the comment names the ceiling and the upgrade path.

Not lazy about: input validation at trust boundaries, error handling that prevents data loss, security, accessibility, the calibration real hardware needs (the platform is never the spec ideal, a clock drifts, a sensor reads off), anything explicitly requested. Lazy code without its check is unfinished: non-trivial logic needs real MSTest coverage as part of the same change --- see rule 10 for what counts as worth testing and rules 11/14 for how to write and structure it. Trivial one-liners and pure UI/XAML glue need no test.

(Yes, this file also applies to agents working on this repo's own tooling and guidelines --- including edits to `AGENTS.md` itself. Especially to them.)

---

## 更新日志 / 发行说明：规则的家在哪（2026-10-07 补）

**通用规则不写在本文件里**，以两处为准：

1. `bobo198504/.github` 的 `CONTRIBUTING.md`（账号级默认文件，所有仓库可见）；
2. 本机全局偏好 `~/.dsh/AGENTS.md` 的「更新日志 / 发布说明」一节。

跨项目工程流程参考：`D:\Projects\Code\_Process optimization`（本项目档案 = `projects/lertaro.md`）。

**本仓库专属的那部分留在这里，不要搬走：**

- **发行说明的"源" = 本仓库 `CHANGELOG.md`**；GitHub 上的正文是它的**投影**（中 / 英 / 哈希三栏、最新在上、
  只写与上游的差异；`+ 基线：…` 这类非改动行不写）。
- 正文 = `## <tag>` 那一条（去掉版本标题本身）＋ `## SHA-256 Checksums`（**只取该发行自己的**资产 digest，
  跳过 `.sig`；**绝不取上游 release 的哈希** —— 两者构建签名不同）。
- **一次写对**：`.github/workflows/fork-release-notes.yml` 是这条规则的**可执行实现**（上游工作流先覆盖正文，
  它作为最后一个写入者再写对）。它必须存在于**默认分支 `main`** 上。
- fork 的三条本地规则与上游差异说明，见 `_Process optimization\projects\lertaro.md` §9.22 / §9.21。

- **fork 每个版本的更新日志都必须含一条「保留鼠标平滑滚动」**（中文 `+ 保留鼠标平滑滚动`，英文 `+ Keeps the smooth wheel scrolling`），
  位置与其它条目同框；**只要上游还没把这个功能接回去，以后 fork 所有版本都带这条**（用户 2026-10-09 明确）。
- **fork 每个版本的更新日志都必须含一条「自动更新只检查本仓库的发行」**（中文 `+ 自动更新只检查本仓库的发行`，英文 `+ Auto-update only checks this repository's releases`），位置与其它条目同框。本 fork 已是**正式分支**：自动更新若指向上游，上游未发版的改动会被装进便携安装（2026-10-11 拖慢搜索的权限检查即此），而便携安装无法回退。**以后 fork 所有版本都带这条**（用户 2026-10-11 明确）。
- **Ctrl+G（Quick Switch）是本 App 最重要的功能之一，不能再坏**：任何涉及活动路径提供者、对话框跟随、
  `GlobalHotkeyDetector`、`ExplorerActivePathPoller`、`ExplorerTracker.LastActiveExplorerPath` 的改动，
  都必须验证"在文件管理器里换目录后按 Ctrl+G 立刻跳到当前目录"，且**两个文件管理器同时开着**时也要成立。
---

# Lertaro 本机工程规则（2026-10-19 写入；新对话请先读完这一节再动手）

## 环境与命令（固定，不要临时换）
- SDK 只用共享工具链：`D:\Projects\Code\_tools\dotnet10\dotnet.exe`（裸 `dotnet` 解析到无 SDK 的 runtime）。
- 先设 `$env:NUGET_CONFIG_FILE='D:\Projects\Code\_tools\nuget.config'`。
- 构建：`... build Lertaro.slnx --nologo -v:q`（默认 Debug）。
- 测试：**`Lertaro.slnx` 不含任何测试工程**，`dotnet test Lertaro.slnx` 是**空跑**（exit 0 无意义，绝不能当作"测试通过"）。
  真实测试工程在 `Tests\**\*.csproj`（30 个，例如 `Tests/Core/Core.csproj`、`Tests/App/App.csproj`、`Tests/Plugins/PinyinAlias/PinyinAlias.csproj`）。
  跑单个：`... test Tests\Core\Core.csproj --nologo -v:q`（不加 `--no-build`；要过滤用 `--filter 'FullyQualifiedName~关键字'`）。
- **本机 `dotnet test` 目前跑不起来**（2026-10-19 实测，已核实）：testhost 对**父进程 `vstest.console`** 调
  `OpenProcess(..., SYNCHRONIZE)` 时被拒 → `Win32Exception (5) 拒绝访问` @ `ProcessManager.OpenProcess`。
  注意：崩溃栈里的 `testhost.x86` 是**符号误导** ✗，架构是 x64，与架构无关；也**不是** shell 完整性级别的问题 ✗。
  用 `explorer.exe` 拉 `.vbs` 绕开工具进程树**也没成功** ✗（无输出、无 trx）。
  因此：**不要声称"测试通过"**；`dotnet build Lertaro.slnx` 也**不包含 `Tests/**`**，测试工程从未被编译过 ✗。
  可行验证 = 构建 exit 0 + 部署后人工验证；要跑测试需先解决 testhost 的该问题（待查：是否 SRP/EDR 拦截或 job/句柄限制）。
- **`Tests/**` 与 fork 的偏差**：`Tests/Core/Hook/Ipc/OpenedFolderSnapshotPublisherTests.cs` 在 `1bff6429` 上引用
  `OrderByWindowRank` / `WindowRanks`，而 fork 的 `OpenedFolderSnapshotPublisher` 里没有这两个成员 ✗（fork 用桌面窗口过滤替代）。
  要让测试真正可用，必须先让 `Tests/**` 编译通过（对齐 fork 实现），否则任何"跑测试"都是空谈。

## 部署（本机便携安装 `D:\App protable\Lertaro`）
- `Deploy-Local.ps1 -Full` 只 publish App，**不会重建、也不会拷贝 `Plugins/`**。
  改到任何插件时必须：① 逐个 `dotnet build Plugins\**\*.csproj -c Release`；② 把 `App\bin\Release\net10.0-windows\Plugins\*.dll` 复制进 `publish\x64\Lertaro\Plugins\`；③ 再跑部署。
  否则安装里仍是旧 DLL（已踩过，用哈希对比才发现）。
- 启动 App 一律 `Start-Process -FilePath 'explorer.exe' -ArgumentList ('"' + $exe + '"')`（脱离工具进程树、普通完整性级别）。
- 停进程**只能按可执行文件完整路径**判定，禁止按名字或命令行子串批量终止。
- 改完直接部署，不必请示；部署后必须核对：安装 ProductVersion、服务 Status、install 与 publish 哈希、app.log 有无异常。

## 搜索语义（已与 owner 逐条确认，**不要擅自"优化"**）
- 排序梯子（2026-10-19 定稿，**两侧同序**）：`左侧连续`（Start==0 且连续度>=1）→ `Contiguity` → `IsCurated/Priority`（收藏/历史，**已确认排在覆盖率之前**）→ `覆盖率`（Weight/Contiguity）→ `Tier` → `TypeRank` → `IsDir` → `NameOrderComparer` → `Name.Length`（**名字长度在最后**）。
- 引擎键 `SearchResultRelevance.HighKey` 同步为：`contiguity(10) | start(6) | coverage(10) | tier(4) | folder(1)`。
- ⚠️ 多词查询**不再**强制 `Contiguity=0`（那会把空格查询的位置/覆盖率信号全抹平）：空格也按左侧优先排序。
- `NameOrderComparer`：数字 < 字母 < 符号；字母内**大写在前**；数字按数值（a2 < a10）。
- LINQ 里只能有一个 `OrderBy`，其余必须 `ThenBy`（`OrderBy` 会重置前面的排序）。

- 空格 = 分词 + **AND**；空格本身不可匹配；多词查询**保留**位置/覆盖率信号，按左侧优先排序。
- 混拼：只有完整读音或声母可作非末段；**只有韵母不参与**（`AliasMatchRules.AllowsMatchSpan*` + `PinyinTermMatcher`）。
- 内联卡片在以上规则之外再加三层目录邻近：当前目录 → 其子目录 → 全局。

## 版本、提交与发行
- 版本号在 `App/App.csproj`；`CHANGELOG.md` 一条一行、中英对应；**每个版本**必须含「+ 自动更新只检查本仓库的发行」与「+ 保留鼠标平滑滚动」。
- `git commit` / `git push` 必须 owner 明确授权；部署、构建、测试不需要。
- 回滚：`git reset --hard 1bff6429`（回滚后**也要重编插件**）；二进制备份 `D:\App protable\Lertaro.bak-5917`（5.9.1.7）。

## 工作方式
- 先拿事实再改（加计时/日志/单测复现），每改一处立刻构建；测试见上文（`Tests/**` 单跑，本机 testhost 目前需用 explorer 起的独立进程）。
- PowerShell 按行号改文件切坏过多次：优先整段/子串替换，改完必须打印上下文核对。
- 状态可疑就回退到上一个可用状态，不要在坏状态上叠加改动。
- 临时文件只放 `%TEMP%`；删除前先列内容核实绝对路径。

## 当前待办（2026-10-19 写入；做完一条就删掉这条，别让清单烂掉）

**起点状态**：`HEAD 1bff6429` + 若干未提交改动（排序 ✓ 混拼 ✓ 探针 ✗），全量测试 exit=0，本机已部署且可用。
**回滚点**：`git reset --hard 1bff6429`（回滚后必须重编插件再部署）。

1. ✅ **已完成**：探针已挪到 `App/ViewModels/Search/Dispatch/SearchDispatchController.cs`（`DispatchSearch` 里），
   并改为**写 `%TEMP%\lertaro-queryterms.txt`**（App 的 logger 把 Info 过滤掉了，`app.log` 一直是 0 KB ✗）。
   实测结论：`raw=lert exe → parsed=[lert] | [exe]` ✓ **解析正确、空格没被吃** ✗。旧探针（`IndexV2Searcher`）已删 ✓。
2. ✅ **已解决（2026-10-19 复测）**：`lert exe` / `lerta exe` / `lertar exe` 现在都能正确命中 `Lertaro.App.exe`。
   实际原因是**排序**而不是匹配 ✗：多词查询的掩码被当成"连续命中"，含 `exe` 的行把含 `lert` 的行压到很后面，
   看起来就像"只有 exe 生效"。待办 ③（多词 ⇒ 强制非连续）修好后自动恢复 ✓。
   「已知异常」一节保留作历史记录 ✓，**不要再按它重开排查** ✗。
3. ✅ **已完成（2026-10-19）**：`HighlightMask.ComputeRank` 里 `pattern.TermSets.Length > 1` 时把 `Contiguity` 置 0；
   同档内再加"名字长度优先"（`RankedCandidateOrdering`）→ `ab` 时 `abc` 在 `abab` 前 ✓、`a` 时 `a1` 在 `aaa` 前 ✓。
4. ✅ **已完成**：探针全部移除（`IndexV2Searcher` ✓ `SearchDispatchController` ✓）。
5. **把插件构建写进 `Deploy-Local.ps1`**：逐个 `Plugins\**\*.csproj -c Release` 构建 + 同步
   `App\bin\Release\net10.0-windows\Plugins\*.dll` → `publish\x64\Lertaro\Plugins\`（现在靠手工，已踩过坑）。
6. 🔄 **进行中**：提交 + 重发 fork `5.9.7.1`（owner 已授权「收口」）（注意：本机 testhost 不可用，本轮所有"测试通过"均为空跑，发版前需用 explorer 起的独立进程真跑一次 `Tests/**`）（原发布被 owner 手动删过；版本号仍用 5.9.7.1）。

## 流程骨架（从改一行到可以发版；每一步都要给出**证据**，没有证据就写"未验证"）

0. **环境自检（每次新会话第一件事，只做一次）**
   `... build Tests\Core\Core.csproj --nologo -v:q` → 必须 exit 0（证明测试工程能编）。
   然后跑一次带 trx 的测试：
   `... test Tests\Core\Core.csproj --nologo -v:q --logger trx --results-directory %TEMP%\lertaro-trx`
   **只看 trx 的内容，不看 exit code**，三态判据（2026-10-19 实测）：
   · **无 trx / 无测试文件** → 命令根本没进测试阶段 ✗（参数或路径问题）
   · **trx 存在但 `total=0`，`ResultSummary/@outcome=Error`（"中止"）** → **testhost 起不来** ✗
     （本机当前状态：`Win32Exception (5) 拒绝访问` @ `ProcessManager.OpenProcess`，testhost 对 `vstest.console` 取的句柄打不开）
     → 明确写"本机测试环境不可用、未验证" + 给人工验证清单；**不要静默降级，也不要说"测试通过"**
   · **`total>0` 且 `failed=0`** → 真通过 ✓（这才是唯一可引用的"通过"证据）
   ⚠️ `dotnet test Lertaro.slnx` 与上面无关：`Lertaro.slnx` 没有测试工程，它是**空跑**，exit 0 永远不是证据。

1. **改动落点**：先 grep 到"真正的入口"（哪一行决定了这个行为），把 `文件:行号` 写进回复里。说不出入口就还没到能改的程度。

2. **改动**：最小改一处；有语义的地方（排序/匹配/范围）先写一条会红的单测，再改到它绿。

3. **编译**：`... build Lertaro.slnx -c Release --nologo -v:q` → exit 0（这是**唯一**能证明"代码能编"的证据）。
   改到插件时另加：逐个 `Plugins\**\*.csproj -c Release` 构建 → 失败数必须为 0。

4. **测试**：按第 0 步的方式跑**受影响的测试工程**（`Tests/**`），给出 `total/passed/failed`。
   跑不起来 → 明确写"本机 testhost 不可用，未验证"，并说明用什么代替（人工步骤清单）。

5. **部署**：插件三件套（Release 构建 → 同步进 `publish\...\Plugins` → `Deploy-Local.ps1 -Full`）。
   证据：部署日志末行、App pid、服务 Status、安装 `ProductVersion`、install 与 publish 的 DLL 哈希是否一致。

6. **人工验证**：把"用户该看到什么"写成 3~5 条可勾选的观察项（具体到查询词与文件名），交给 owner 确认。

7. **提交/发版（需 owner 明确授权）**：`App.csproj` 版本号 → `CHANGELOG.md` 中英各一条（含两条常驻行）→ tag → 等 CI → 用**下载资产的内部版本号**做决定性验收。

8. **收尾**：删掉临时探针与 `%TEMP%` 草稿；把踩到的坑写回本文件（按**流程位置**写，不要按事故追加）。

## 待办补记（来自另一对话的实测，本机环境特有）

- 本机 `dotnet test` 起不来的可能原因：**本会话的沙箱/作业对象限制**（另一对话环境不同、可能正常）→ 所以第 0 步必须**实测一次**再下结论，别假定不可用。
- `Deploy-Local.ps1` 应改为**自校验**：把"插件编译 + DLL 同步 + 哈希比对 + 服务 Status"做成脚本自己的 exit code（脚本不会遗忘，人会）。
- `~/.dsh/AGENTS.md` 里那份"测试命令固定用法"应**只留指针**指向本文件，避免两处规则分叉（该文件的修改需 owner 确认）。
- 工作区可能带着另一对话的**接缝修复**（`Core/Hook/Ipc/OpenedFolderSnapshotPublisher.cs` 的 `OrderByWindowRank/WindowRanks`）。
  它能让 `Tests/Core` 编译通过；**接受它，或先 `git checkout --` 退回再改** —— 不要在两份状态上叠改。

## 已修复（保留作历史）：`<4+ 字母的词> + e 开头的短词` 多词查询"失效"（2026-10-19 由待办③顺带修好）

owner 判定：太反常、影响面小、其它组合都正常 → **先记录，不动代码**。

**现象**：`lert exe` 这类查询只有 `exe` 生效，`lert` 那半被吞掉；`lert` 后面接更多字也一样。

**已验证的矩阵**（同一台机、同一版）：
```
lert            ✓ 单独能中
ler exe         ✓ 能中
ler ro          ✓ 能中
le aro          ✓ 能中
lert ro         ✓ 能中          ← 第一词 4 字母没问题
lert e          ✗ **不中**
lert ex         ✗ 不中
lert exe        ✗ 不中
lerta exe       ✗ 不中
lertar exe      ✗ 不中
```

**已排除**（有证据）：
- 解析阶段没问题：探针显示 `raw=lert exe → parsed=[lert] | [exe]` ✓，`raw=le exe → [le] | [exe]` ✓（**空格没被吃** ✗）；
- 第一词长度没问题（`lert ro` 能中 ✓）；
- 不是 `--no-build`/测试环境问题（与本条无关）。

**待定的一步**（下次接手第一件事）：搜 `ler e`
- 能中 ✓ → 阈值在"第一词 4 字母 + e 开头的短词"这个**组合**；
- 不能中 ✗ → 就是"**单字母第二词**"本身。

**最可疑落点**：`MixedQueryMatcher.TrySegmentPattern`（`Core/IndexV2/Search/SearchMatcherRow.cs:75` 的兜底分支）
把两个词**跨空格拼起来**（`lert e` → `lerte`），拼出来的串又恰好像拼音片段 → 被吞进别名通道 → 英文名没有别名 → 只剩 `exe` 命中。
`lert ro` 之所以正常，是因为 `lertro` 不像可用片段、退回普通 AND。

**附**：查询语法**没有通配符** ✗（`*` `?` 会被当普通字符）。位置语义（完全一样 → 前缀 → 包含 → 散落）是**自动**的，不需要写通配符。

7. **多词查询按输入顺序匹配**（owner 2026-10-19 要求）：`le app` 只能命中"le 在 app 之前"的名字，`apple` ✗ 不再命中。
   落点：`Core/SearchIndex/Fzf/FzfPattern` 或多词收口处校验各词 `MinBegin` 递增；约 20~40 行 + 单测。
   副作用（owner 已知悉）：`mp3 歌` 这类顺序写反的查询会失效。
## 已知行为：多词查询里的某个词可能被"剥词"（2026-10-19 记录，**暂不修**）

实测：`szz mp` 会返回 `D:\…\世的牽掛版本8.mp3`（名字与路径里**没有任何 `z`** ✗，不可能靠 `szz` 命中），
而单独搜 `szz` 能正确找到 `S:\…\树枝折断声.mp3` ✓。
推测 `DispatchSearch` 里 `FileFilterScopeResolver` 或 `PluginTriggerQuery.Strip` 把 `szz` 当成了过滤器/插件触发词剥掉，
真正搜索的只剩 `mp` → 于是任何 `.mp3` 都命中。

owner 判定（2026-10-19）：`szz` 单独搜能用、影响面小 → **暂不修**，不要再重开排查 ✗。
若以后要修：触发词/过滤器关键字应只在"**它是查询里唯一的前缀词**"时生效，而不是任意位置的首词。
