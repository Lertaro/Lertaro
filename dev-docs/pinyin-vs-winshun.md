# 拼音搜索：Lertaro 现状与 Win顺 对照

拼音主题此前散在三篇调查记录里（十条坑对照、四问审查、多音字阶段记录），现合并为本文，以本文为准。

## 适用范围与核对基线

核对基线是 `main` = 9a6b9769。标记含义：`已核对` 指本次逐行读过 Lertaro 代码并当场确认行号；`推演` 指由源码推导、没有跑 harness 复核；`未核对（截至 9a6b9769）` 指既没复核过也不删的说法。Win顺 一侧的数字全部是他们文档自述的实测（i9-14900HX，316–335 万项，4 块本地盘），本机未复现；文中"坑号"即他们 `docs\pitfalls.md` 的行号。仓库路径统一写作 `<repo>`，Win顺 仓库与它同级。

一次性探针（读音网格的量测、token 形状的量测）用过就删了，本文只留输出数字，不留脚本路径；短且可复用的命令原样写在文中。

## 结论速览

现行形态：拼音匹配整个离开索引，每个查询词构造一个位并行读音 NFA、在候选名上现走一遍（`Plugins/PinyinAlias/PinyinTermMatcher.cs`、`Core/IndexV2/Search/PinyinNameEvaluator.cs`，经 `PluginSdk/Abstractions/Plugins/IAliasLiveMatcherProvider.cs` 接入）。这套判定是从 Win顺 的 `ws::pinyin::Matcher` 逐字移植的（`IAliasLiveMatcherProvider` 的 remarks、commit `154e947c`/`e652974e`/`d40e8f9c`），所以两家的拼音判定规则现在是同一套；读音网格那一层在 `b8e1061c` 整删，`AliasGridMatcher`、`AliasGridJointMatcher`、`AliasGridFormat`、`SearchMatcherGridAliases`、`IAliasGridProvider`、`AliasReadingTable` 在代码里 0 命中（已核对）。`SnapshotFormat.Version` 现为 14（`Core/IndexV2/Persistence/SnapshotFormat.cs:45`），插件 `Version` 为 7（`Plugins/PinyinAlias/PinyinAliasProvider.cs:31`）。

十条坑的对照：

| # | 主题 | Win顺坑号 | Lertaro 现状 | 标记 |
| --- | --- | --- | --- | --- |
| 1 | 快照保存挡住搜索 | 1401 | 同一形态，锁粒度更粗 | 已核对 |
| 2 | 慢管道客户端拖住索引更新 | 1401/611 | 同因反向，`ponytail` 已记 | 已核对 |
| 3 | 两个全局搜索互相静默作废 | 1407 | 同型竞态，症状更静 | 已核对 |
| 4 | 多盘等最慢的盘 | 1395 | 同型，上限已命名 | 已核对 |
| 5 | 卡片跟随对话框的挪窗成本 | 506/514 | 每条事件挪一次；抑制器已造好，这条路径仍没经过它 | 已核对 |
| 6 | 文件夹路径灌进"文件名"框 | 418/332/412/424 | 多条同域，风险最高 | 已核对 |
| 7 | 双击修饰键：窗口硬编码 / 丢 key-up / Alt 按住仍算 / 不看鼠标 | 116/126/130/134 | 四条同型 | 已核对 |
| 8 | "最早在前"把无时间排最前 | 551 | 同型 | 已核对 |
| 9 | 按路径排序的文化感知比较 | 563 | 同域，未量 | 已核对（形态） |
| 10 | MFT extent 未按记录长度对齐 | 533/539 同域 | 代码推得，失败模式是静默少收还声称完整 | 未实测 |
| — | 同音错字反查 | 310 | Lertaro 缺这个能力 | 已核对 |
| — | 常用读音优先 | 打分表 | 已补（表按频率序 + −12 档 + 生僻 tier） | 已核对 |

Qt 专属那一大类在这里不适用，不再逐条列：`startSystemMove`、`sourceSize`、`QVariantAnimation` 这类移动与动画 API，lupdate/moc/qmllint 工具链，Inno Setup 与 Mica 材质，还有 QML 列表属性。

## Lertaro 现在的拼音匹配

### 数据流

```
查询串
 └─ FzfPatternParser（先折叠全角与 NFD，再解析空格=AND 集合、|、引号、! ' ^ $、drive 前缀；统一小写）
     └─ 每个正词追加 provider 的 GetQueryForms（拼音 provider 现在只追加繁转简写法）
唯一名扫描（字符掩码预筛；掩码烘的是名字各位置全部读音的字母，读音本身不落盘）
 └─ 名字层 PinyinNameEvaluator.TryScore：逐词先按字面判，字面不中再走该词的读音 NFA
     ├─ 合格的多拼音词另算同 provider 的 joined 分支，按分数或源 span 质量选 canonical winner
     ├─ 一个词的可读形态在模糊模式下也按连续子串判（两模式同答的根据）
     ├─ 字面命中的词用 fzf 自己的分数，只有需要读出来的词用参考实现的加分表
     └─ 排除词只按字面否决，别名层不得复活被 ! 排除的候选
名字层不中 → 字符串别名层（只剩繁转简写法）→ 混合字母 lane（电nao 类，现拼一份单读音拼写）
名字层没填满一页时 → PathTermFallback（逐词兜底，≥2 词、≤16 词）
即时接缝 FuzzyMatcher.IsMatch 与高亮走同一套判定
```

关键代码位置（已核对）：`Core/IndexV2/Search/PinyinNameEvaluator.cs:255`（可读词的 Exact 化处理）、`:313-329`（覆盖加分 25、整茎拼满 +40）、`:341-342`（`PinyinBonus`）、`:346`（`StemLength`）；掩码预筛与 OR-first 关预筛在 `Core/IndexV2/Search/SearchMatcher.cs:66-115`；混合 lane 在 `Core/IndexV2/Search/SearchMatcherAliasExtensions.cs:36-52`，它要的拼写由 `IAliasLiveMatcherProvider.AppendSpellings` 现拼；兜底闸门在 `Core/IndexV2/Search/NameSearch.cs:101`（`emitted < limit`）与 `Core/IndexV2/Search/PathTermFallback.cs:71,85`（`MaxTerms = 16`、少于 2 词直接返回）。

排序键只有一条顺序：contiguity → shape → left-contiguous → start → coverage → tier（`Core/SearchIndex/MatchRank.cs:4-17`，完整窗打包成 `contiguity(8)|shape(3)|not-left-contiguous(1)|start(7)|coverage(9)|tier(4)`，`Core/IndexV2/Search/SearchResultRelevance.cs:18`）。多词查询的 contiguity 按“每个正词各一个 run”计，单词查询仍是整段 run 的平方和（`Core/SearchIndex/HighlightMask.cs:329-390`）。tier 的四档是 字面名(0) > 首字母(1) > 整读音(2) > 生僻读音(3)（`MatchRank.cs:29-35`），所以拼音命中打不过同形状的字面命中，但 shape 与 coverage 仍能把它往上顶。生僻档由 `PinyinNameEvaluator.TryPaintTerm`（`:198-219`）在高亮 pass 里定，同一个信号在加分表里是 −12。

### 表、读音序与体积

表是自产的 `Plugins/PinyinAlias/Resources/pinyin.bin`，由 `Plugins/PinyinAlias/tools/make_pinyin_bin.py` 从 vendored 的 pinyin-data 与 kTGHZ2013 生成（commit `c5156fd7`、`6fae29ce`）。规则：去声调、ü 写作 v、在 2013 规范字表里的字取规范读音且常用在前、每字最多 3 条，表外的生僻字只留最常用一条。当前文件 60,916 字节，416 个音节，字符表 28,647 项覆盖 U+3007–U+9FFD；生成范围从 U+3400 起，因为字符表本来就索引到 U+9FFD，停在 U+4E00 会丢掉先前覆盖的 5,787 个字，其中 77 个在 2013 规范字表里且用于人名（脚本 docstring）。

形状（本次直接从 `pinyin.bin` 数出）：有读音 26,696 字，1 读 26,365、2 读 317、3 读 14，范围内无读音 1,951。Win顺 自述的形状是 20,924 字有读音、1 读 20,593、2 读 317、3 读 14，两侧的多读数量一致，覆盖字数差来自表范围。

体积：读音不占快照字节。索引侧每唯一名只多一个 64 位字符掩码（含它各位置读音的字母，`Core/IndexV2/Alias/AliasGenerationUtf8.cs:99-110`），别名段现在只剩繁转简写法一条字符串别名（`PinyinAliasUtf8Encoder.cs:5-16`）。对比：Win顺 每名一个"含汉字"位（`FileIndex.h` 的 `EntryFlag::Han`，他们自述）。原来网格那套的 0.49×、18,344 B、约 60 B/名都是已删架构的数字。

成本：一条拼音查询在本机量到约 30 ms@20 万行、约 120 ms@100 万行，纯字面查询不受影响（早前的一次性量测，本次未复测，仍列在未收口清单里）。

### 单字与词形规则

判定权威是 NFA 的走位规则（`PinyinTermMatcher.cs:62-146` 的 `TryFind` / `Step` / `CanStart`）：一个词必须是名字里一段连续的位置，每个位置至少吃掉一个字母；ASCII 位置按字面自匹配（大小写折叠），汉字位置可以吃它任一读音的任意非空前缀（首字母、整音节、或半截），前缀落在词的任意位置都可以；词的中间不能有跳过的名字字符；`v` 兼收 `u`；词长 2–63 个 ASCII 字母或数字且至少含一个字母，单字母不作拼音词（`IsEligible`，`:46-60`）。首字母位图 `CanStart` 先把不可能起步的位置否掉。上限是词 ≤63 位（正好一个 64 位状态字）、文本 ≤512 码点。

下面这张表除标注"推演"的两行外都钉在测试里（`Tests/Plugins/PinyinAlias/PinyinSyllableRuleTests.cs`，每条都在精确与模糊两种模式下断言同一答案）：

| 查询 | 名字 | 结果 | 原因 |
| --- | --- | --- | --- |
| `pj`、`pjia`、`pij`、`piji`、`pinj`、`pingji` | 评价 | 命中 | 相邻位置各自给出读音的前缀 |
| `ing`、`ia`、`ja`、`zhengp` | 评价 | 拒绝 | 前缀必须从读音第一个字符开始，`zhengp` 点不到这个名字的任何字 |
| `p`、`j` | 评价 | 拒绝 | 单字母不作拼音词 |
| `gl`、`zhengsh`、`glyhzshu`、`guanliyonghu` | 管理用户证书 | 命中 | 连续段 |
| `gsh`、`glzs`、`guanlishu` | 管理用户证书 | 拒绝 | 中间字符给不出下一个字母，要跳字 |
| `bi`、`fbi`、`fei` | 4分贝 | 拒绝 | `ei` 不是 bei 的前缀；`bi`、`fbei` 在 4分笔/4分贝 上照常命中 |
| `su`、`suan` | 算 | 命中（推演，无测试钉住） | `su` 是 suan 的真前缀 |
| `uan` | 算 | 拒绝（推演） | `u` 不是任何读音的开头 |

`su` 单独查"算"这条是原文档留到最后的规格分歧（用户侧的期望是 `su` 不命中算、`zgsu` 命中）。它现在仍是命中，而且两家已经是同一份实现，所以这条分歧的性质变成"用户期望严于现行规则"。要拒绝它、同时保留 `zgsu`，得再加一条规则（例如收尾的半截前缀要求前面已有至少一个完整片段）；现行规则里前缀可以在词的任意位置结束，没有这个区分。

### 精确模式下的混合查询

解析规则（`Core/SearchIndex/Fzf/FzfPatternParser.cs`，已核对）：

| 输入 | 含义 |
| --- | --- |
| 空格 | 分集合，集合之间 AND；集合内 `|` 连接的是 OR 备选 |
| `|` | OR，AND-first 是默认，空格绑定更紧 |
| `'x'`、`'x`、`!x`、`^x`、`x$` | 整词/边界、翻转精确性、排除、前缀、后缀 |
| 引号 | `'...'` 或 `"..."` 合并含空格的短语 |
| 大小写 | 一律不敏感；全角与 NFD 在解析前折叠（`:10-14`，正是 `Version` 14 那一 bump 的内容） |
| `!x` | 完全不展开别名或拼音形态（`AddAliasQueryForms` 对 inverse 直接 return，`:200-205`） |

出厂默认是模糊匹配开启（`Core/Settings/UserSettings.cs:62`，`EnableFuzzyMatch = true`），精确模式指把它关掉。设置只决定候选自己文本怎么读：精确要连续子串，模糊要子序列。拼音层在两种模式下给同一个答案，机制是 `PinyinNameEvaluator.cs:255`：一个读音能判定的词，它的字面一侧即使在模糊模式下也按连续判；让这样的词散到名字的字母上是两模式答案不同的唯一来源。断言在 `PinyinExactModeTests.BothModes_ReachTheSameNamesForPinyinQueries` 与 `PinyinMultiBlockQueryTests.SameQuery_ReachesTheSameFileWithFuzzyMatchingOnAndOff`。

无空格的拼音串是一个词，必须在名字里找一段连续位置；空格分隔的词原本彼此独立、顺序自由（`MultiTermTokens_AreIndependent`）。对至少两个普通 ASCII 字母正词，系统还会让同一个 live provider 试一次去空格拼接；drive、OR、排除、显式操作符、长度超过 63 或需要跨 provider 的查询不试。逐词分支照常计算。含字面命中时 joined 仍须分数更高；全读音分支的逐词位置 bonus 会随 token 数重复，改用源起点与覆盖选 canonical winner。这样反序和普通多词保留原语义，拼音加空格不因 bonus 重复而抬分。joined 胜出时，判定、高亮和诊断共用它返回的 provider/tier/span witness。

普通内容词与拼音词之间必须用空格隔开；同一个 token 里混排汉字与拼音字母走混合 lane，而这条 lane 只在模糊模式下认领多词查询里的混合词（`PinyinRobustnessTests.MixedAlphabetTerm_IsAFuzzyModeRescue`，`chuangysji Anna 电nao` 在精确模式不中）。

原来说过"整个查询必须落在同一份拼写上才算名字层命中"，多词需要联合取证才能满足。这个约束随网格一起没了：现在每个词独立在同一条名字上判定，不存在"各词只能落在不同拼写上"的形态，`xinl gxiao jt fd 简单` 这类查询直接由名字层给出（`PinyinMultiBlockQueryTests` 的 DataRow）。名字层真的没接住的行才由 `PathTermFallback` 逐词补齐，rank 记 `ShapeNoName`/`NoNameFallback`（`MatchRank.cs:43,68`），永远排在任何真名字命中之后；它只在名字层没填满引擎这次 `limit` 时跑（`NameSearch.cs:101`）。默认快速查询经 `SearchService` 的 4 倍余量后，每块本地盘的这个 `limit` 是 204 条，与 App 显示的约 50 条不是一回事（见 search-ranking.md 的「候选预算与显示截断」）。

### 超长文件名

覆盖整条名字的每个位置，ASCII 位置（字母、数字、标点）的"读音"就是它自己，所以一段连续判定里可以同时吃拼音和字面。词内不能跳字，这条决定了长名字的用法：不同块的缩写必须各自成词，用空格分开。

工单名（188 字符，含 `笔abc记`、`高 2AB964能`、`Chu效 ban` 这些中断）已经钉成 fixture：`fangdanbijifa`、`fdbjf`、`fdan`、`gxiao`、`chuangysji`、`dnwwzz sj`、`fangdan baohu nx dnwwzz sj`、`diannao chu ban chuban df7686`、`Esor xinliu cbcb archive`、`9786267`、`Anna Archive`、`创意`（繁转简）全部命中且两模式一致。反过来，`fdbjfgxiao`、`dnwwzzfdbjf`、`fangdanbijifajiandan`、`duc`、`fdanbjfa cy Anna test duc hu` 在两种模式下都拒绝（`PinyinRobustnessTests.RejectedShapes_ReachNothing`），报的那个例子里去掉 `duc` 这一词后整句命中（`ReportedExample_MinusTheGappedToken_ReachesBothNames`）。

`duc` 的拒绝理由写在测试注释里：它的 d/u/c 只能带空隙才凑得出，单 token 跳字是旧语义。原来说"无距离限制对拼出了读音的词完全成立、纯缩写词仍受质量门约束"，两道门（包络 ≤ max(3q,20)、逐词最紧窗口和 ≤ max(3q,12)）随网格一起删除，没有重新引入。`PinyinNameEvaluator.cs:19-22` 给了不引入的理由：跨度那一半现在空洞（连续段里每个位置都至少吃掉一个字母，段宽不会超过词长），分数那一半在这张加分表里表达不出来（名字中段的缩写不管是不是巧合都值 4 分，任何高于 4 的下限都会删掉真命中）。巧合的读法改由 tier 与位置键降档处理，`baj`（版给 ba、集给 j）就是这种形状：命中，排在任何字面或边界对齐的结果之后（`HalfTypedReadingMidName_IsReadAndRankedLow`）。

## 与 Win顺 的十条坑对照

### 存索引把搜索挡在门外（1401，已核对）

他们的现象：半小时一次的快照保存期间正好有文件变动，搜索停到保存结束（325 万项存盘约 0.27 秒）。原因写得具体：Windows 上的 `std::shared_mutex` 是 SRW 锁，写者排队时新读者也排在写者后面；他们用小程序量到读者持锁 3 秒、写者 200ms 排队、400ms 才来的读者等到 3015 毫秒。做法是把长读（存快照、内容索引全表扫）改走 `FileIndex::longReadLock()`：先取"写者门"的共享锁再取读锁，长读期间写者停在门外、不占读写锁的队，搜索照常进，并有 `longReadLetsReadersPastWriters` 钉住。

我们的现状：`Core/IndexV2/LiveIndex.cs:12-14` 的注释自己写着 Compact() 整段持写锁（merge + 文件写 + 重开），这会挡掉期间的所有搜索。`LiveIndex.Compact` 在 `:97`，写锁在 `:99`，merge 在 `:113`、`SnapshotWriter.Write` 在 `:119`，全在排他锁内（行号本次确认）。

这个缺口有两点限制条件。

- 触发面比注释说的窄。调用在 `Core/Indexer/Usn/UsnIndexerDurabilityExtensions.cs:102`，由 3 秒空闲计时器（`Core/SearchEngine.cs:26,54`）调起，门槛是 `IdleCompactPendingThreshold = 4096`（`:20`），所以不是每次停顿都整盘重写。注释里 "bounded、idle-triggered" 站得住。
- 合并加整份快照重写仍然在排他锁里跑，一次就是一整份；`UsnIndexerDurabilityExtensions.cs:100` 那句 "which no search should queue behind" 正是想避免而没做到的事。我们没有"写者门"这一层。

### 同一把锁的反方向：慢客户端拖住索引更新（已核对）

`Core/IndexV2/Search/IndexV2Searcher.cs:20-25` 的 `ponytail:` 注释把整条链写对了：整个搜索连每一次 onResult 回调都在读锁内跑，生产回调是对 `SearchStreamPump` 有界 channel 的阻塞写，所以一个慢管道客户端会把搜索线程连锁一起停住；而 `ReaderWriterLockSlim` 在有写者等待时会挡住新读者，`LiveIndex.Mutate`（USN/watcher 应用）与 `Compact` 排在它后面，一个卡住的客户端能让这块盘的索引更新一起卡住。这条是 2026-09-22 明确接受的延后，注释要求观察到实卡再重开。

上一条与这一条是同一个缺口的两面。可以借的检查表是他们注释里那句原话：保存处原来的注释"搜索照样跑，它们也只要读锁"只在没有写者时成立。

### 两个全局搜索互相作废，且失败不可见（1407，已核对）

他们的现象是读代码读出来的（特意标了没在用户那里见过）：对话框旁的框一搜，启动器正在等的结果就不来，旧结果一直灰着，直到再打一个字。做法是给对话框一个独立引擎。

我们的形态：`Core/SearchEngine.cs:166` 按 `directoryFilter` 作废上一个搜索，`Core/SearchCancellationRegistry.cs:29` 把 `null` 折成 `string.Empty`，`Core/Services/Search/SearchStreamPump.cs:93-95` 对全局搜索一律传 `directory = null`。任何两个全局搜索者（快速窗、内联卡片、完整窗、CLI、插件）共享同一个槽，后到的静默作废先到的；被作废一方的 `OperationCanceledException` 在 `:114-115` 被吃掉，客户端只看到 header 加结束帧，零行、无错误、无"我被打断了"的信号。`SearchCancellationRegistry.cs:8-14` 的注释说明当时只把"多文件夹扇出互相取消"那半个 case 修掉了。

他们的症状是旧结果灰着（用户看得见），我们的症状是空列表（用户只能以为没结果），所以这条更值得修。待确认的仍是哪些窗口会真并发发全局搜索：每窗口各自一个 `QuickSearchViewModel`，槽在进程侧共享。

### 多盘有界搜索等最慢的盘（1395，已核对）

`Core/Indexer/Usn/SearchCoordinator.cs:43` 的 `ponytail:` 注释写明上限：有界限制时缓冲所有盘（drives × limit 行），放弃流式，第一行要等最慢的盘返回才发。这条是已知的自觉简化，不是盲区。

值得借的是触发条件：睡着的移动硬盘、出毛病的 U 盘、连不上的网络共享（`\\10.255.255.1\share\…` 第一次 15 秒、之后 10 秒）。他们的做法是搜索线程不碰磁盘，`RecentOnDisk` 每盘一条线程问存在性，搜索最多等 100 毫秒，没回答就用上次的答案、没有就不列，慢盘在日志里记一行 `drive … took … ms`（一直慢只记一次），并给了复现步骤（往 `history.txt` 首行塞一个死共享路径再启动）。

叠加关系：一块盘正在做第一条的 Compact 时，其余盘已经排好序的结果也发不出去。

### 卡片跟随对话框，成本全在挪窗上（506/514，已核对）

他们的结论是这批坑里最反直觉的一条：搜索框跟着别人的对话框跑，那个对话框自己的拖动变慢。先怀疑线程、再怀疑合并，实测证明换线程与合并都没用，是挪窗口这个动作本身在给宿主窗口加活；最后用 `EVENT_SYSTEM_MOVESIZESTART/END` 在拖动期间不跟随（247 → 161 次/秒）。

我们的链路（当场逐跳读过）：`Core/Hook/ExplorerIntegration/ExplorerTracker.cs:606-609` 每个 `EVENT_OBJECT_LOCATIONCHANGE` 发一次 `OnActiveWindowMoved` → `App/Views/InlineSearchWindow/InlineSearchWindow.xaml.cs:283-292` → `App/Views/InlineSearchWindow/Helpers/InlineSearchWindowPositioner.cs:160+` 重算并落位。这一条比原文记的形态多了一层自我抑制：位置器现在有输入未变的早退缓存（`:231-244`）、0.5px 容差和"尺寸与位置合成一次原生 `SetWindowPos`"（`:145-157`）。拖动期间矩形每事件都在变，所以每事件仍是一次完整重算加一次挪窗。

抑制器已经造好了，只是这条路径没经过它：`Core/Hook/ExplorerIntegration/ExplorerActivePathPoller.cs:10-14` 的 `LocationSettleMs = 200` 注释里就写着拖动或缩放时约 200 个 LOCATIONCHANGE/秒（调大小 Total Commander 时量的）。全仓 grep `MOVESIZE`、`ENTERSIZEMOVE`、`LEAVESIZEMOVE` 本次仍 0 命中，拖动期间不松手这条完全没有。

### 文件夹路径灌进"文件名"框再按回车（418/332/412/424，已核对）

这一条一次撞上 Win顺 四条不同的坑，是十条里最像真 bug 的。

`Plugins/CoreExtensions/FileDialog/StandardFileDialogAdapter.cs:113-168`：

- `FindSubEditBox`（`:311`）取第一个类名为 `Edit` 的子窗，通用对话框里那是"文件名"框，不是地址栏（对应坑 332：地址栏平时根本没有输入框）。
- 新增的早退只看路径是否已等于目标（`:123-125`），相等就返回。
- `WM_SETTEXT` 写路径、补 `\`、给父窗发 `EN_CHANGE`（`:127-134`），然后 `Task.Run` + `Task.Delay(300)` + `GetForegroundWindow() == hwnd` 判断，再 `AttachThreadInput` + `SetForegroundWindow` + `SetFocus` + 投 `VK_RETURN`（`:138-155`）。对应坑 412：刚交还前台就操作、焦点还不回去，他们是等真拿到前台和焦点控件，我们是猜 300 毫秒。
- 还投了一条 `WM_LBUTTONDOWN/UP`（`:156-157`），对应坑 424：单击坐标要用对话框自己的坐标系，0,0 很可能落在下拉箭头或滚动条区。
- 全程不保存也不还原用户原本打在文件名框里的文字，也不区分这个对话框是选文件夹还是选文件。

对照坑 418：在 `FOS_PICKFOLDERS` 对话框上这条路径会选中该文件夹并关闭对话框；在保存对话框上会覆盖用户半打的文件名。

原来 `未复核` 的那半条本次核了：`App/Views/InlineSearchWindow/Helpers/InlineSearchNavigator.cs:230` 会用 `TargetIsFolderOnly` 把选中的文件解析成所在文件夹再发 `NavigateDialog`，导航本身不拦；同一个标志也用于收窄结果范围（`App/ViewModels/Search/SearchExecutionEngine.cs:169`、`App/ViewModels/Search/Dispatch/SearchDispatchController.cs:184`）。标准对话框与 WPS 都把该标志记为 `CanHandle` 时匹配到的结论（`StandardFileDialogAdapter.cs:14,40`、`Plugins/WPS/WPSFileDialogAdapter.cs:40`），所以"只有 WPS 那条链走对了字段判定"这句不再成立，folder-only 对话框照样走导航这条已由代码确认。

### 双击修饰键的四条（116/126/130/134，已核对）

`Core/Hook/InlineSearch/ModifierDoubleTapDetector.cs` 一共 56 行，四条全在里面。

| 坑 | Win顺 的结论 | Lertaro |
| --- | --- | --- |
| 130 窗口太窄 | 用 `GetDoubleClickTime()` 再夹到 400–900ms | `:29` 硬编码 `elapsed > 100 && elapsed < 300`；`GetDoubleClickTime()` 在 `MouseHookService.cs:48` 导了但只用于鼠标双击（`:102`）。鼠标宏、快速连按 <100ms 直接不认 |
| 126 丢 key-up | 用 `GetAsyncKeyState` 复核真按着还是真松开，不让状态机卡在 `_wasReleased=false` | `:26` 之后，丢掉 Ctrl+Alt+Del 或被别的钩子吞掉的 key-up 要第三次按才触发。`GlobalHotkeyDetector.cs:33` 的 `SynchronizeModifierState()` 只刷新 `ModifierKeyState`（唯一调用点在 `KeyboardHookService.cs:138`），从不碰检测器 |
| 134 按住 Alt 也算 | 有别的修饰键按着时不算一次 tap | `GlobalHotkeyDetector.cs:66-71` 裸修饰键分支喂检测器时不看 `_modifierKeyState`；`HasControlAltOrWindowsDown`（`:36`）只用在内联按键路径（`KeyboardHookServiceInlineSearchExtensions.cs:127`）。按住 Alt 连按两下别的程序的快捷键会弹框 |
| 116 鼠标动作 | Ctrl+点击多选、Ctrl+滚轮、拖动期间要能识别 | 检测器里没有鼠标概念；`HookProcess.cs:422-437` 的鼠标钩子只服务右键菜单宽限与快捷导航 |

`已核对的例外`：`Core/Hook/InlineSearch/KeyboardHookServiceInlineSearchExtensions.cs:19` 用 `LLKHF_INJECTED` 放行注入键，方向与 Win顺 坑 855（别按"是不是注入的"放行按键）相反。我们的注释给了具体动因（Quicker 的右 Ctrl+数字合成组合键被当内联输入吞掉），属于有意识的反向选择，而 Win顺 那条坑长在"开始菜单转交按键"这条我们不具备的路径上。记着两边结论相反即可。

Raw Input 那两条（坑 1866/1874：进程登记了 Raw Input 时自己排第一的低级钩子不会被调用，排在后面的别人的钩子也收不到键）已核对为不适用：全仓 `RegisterRawInputDevices`、`WM_INPUT` 0 命中（本次复查），双击判定纯走 `WH_KEYBOARD_LL`。这两条是定时炸弹：哪天为鼠标精度引入 Raw Input 就会立刻撞上。

### "最早在前"把无时间排最前（551，已核对）

他们的现象：按最早在前排，前面一整屏都是 1970-01-01。结论是要区分 1980 DOS 纪元（老 Mac 拷贝、`robocopy /XJ`、MFT 时间字段为 0）与真正的 0 值，用 `isKnownTime`（1987–2107 之外当未知）在比较里排最后，1970 的行照常显示、只是不参与最早档。

我们的现状：`App/ViewModels/Search/SearchResultSorter.cs:29-32` 直接 `OrderBy(r => r.DateModified)`，升序时无时间行在最前。原来 `未复核` 的那半条本次核了：`FileTimeHelper` 在 `Core/Indexer/FileRecordStore.cs:70-81`，`ToUnixSeconds` 把 pre-1970 夹到 0，`FromUnixSeconds` 把 0 映射成 `DateTime.MinValue`，注释写着"0 means not recorded，不是真 1970 时间戳"；没有 1987–2107 那类已知时间判定。坑照旧成立。

### 按路径排序的文化感知比较（563，已核对形态）

`SearchResultSorter.cs:17-27` 对 Name 与 Path 都用 `StringComparer.CurrentCultureIgnoreCase`。Win顺 坑 563 是搜索面板按路径排序一次要 5 秒。CNC64 搜索面板能装到 5000 行，值得量一次 Path 那列。Name 列的文化排序是有意为之（按界面语言取 collation），要动只动 Path。

### MFT extent 未按记录长度对齐（533/539 同域，未实测）

`Core/Indexer/Mft/MftIndexScanner.cs:95-110` 的分块循环：`off = lcn * bytesPerCluster`，`chunk -= chunk % recordSize`（`:101`），内层只走 `r + recordSize <= chunk`（`:110`），`off` 按记录对齐的 chunk 前进（`:180`），所以 chunk 之间不丢记录。留下的三点：

1. extent 起点只做 cluster 对齐，不做 record 对齐。`bytesPerCluster < recordSize` 时（512E、4Kn、`convert /fs:ntfs` 老盘）extent 可能错位半个记录，整个 extent 的记录全都过不了 `FILE` 签名检查。
2. extent 尾部不足一条记录的字节没人读，跨 extent 边界的那条两半都失败，每 extent 至多丢 1 条。
3. 失败模式最差：`:193` 无条件 `store.IsComplete = true`，静默少收还声称完整。

已核对为不适用：Win顺 坑 539（占用位图不一定在 0 号记录）我们不依赖位图，逐条查 `headerFlags & 0x01`。这一条是从代码推的，没找到这种卷验证过，列在这里是为了知道该去测什么，不是缺陷定论。

### 子代理报的、未复核的线索

只当搜索起点，不作为结论：`Enter` 路径在 Dispatcher 里做 `Directory.Exists`/`File.Exists`（对应坑 1369"UI 线程问网络文件，启动器卡半分钟"）；`IndexBatchProcessor.cs:129` 在超时闸门之前 `File.Exists`；WPS 收到不存在路径缺前置校验（坑 501）；`UsnServicePipeServer.cs:46` 只预建 2 个监听循环而扇出按文件夹数、每次连接硬等 2000ms（坑 1966 `ERROR_PIPE_BUSY`）；`TrayMenuController.cs:81` 每次打开菜单都同步 `UserSettings.Load()`（坑 91 托盘首次打开慢的同型嫌疑）。这些位置未核对（截至 9a6b9769）。

### Lertaro 已经领先或等价的几处

- 坑 2392（自己缩短的名字 `Text.truncated` 是假的）：`App/Helpers/Visuals/TrimmedTextToolTip.cs:53-55` 用 `FormattedText` 加 `VisualTreeHelper.GetDpi` 实测溢出，与截断方式无关。
- 坑 187（一条慢缩略图挡住整列图标）：`App/Services/ShellIcons/ShellIconHelper.cs:62` 的 `GetIconFromCacheOnly` 先同步给占位、再后台取真图，按 `IconLoadingStarted` 去重。
- 坑 148（开始菜单开着时双击 Ctrl，焦点回不去）：`App/Views/QuickSearchWindow/Helpers/ShellOverlayDismissHelper.cs:32-61` 覆盖 SearchHost、StartMenuExperienceHost、ShellExperienceHost、ShellHost 与裸 `CoreWindow`，先释放按着的 Ctrl/Alt 再注入 Esc，等过渡完成并重试 3 次，比他们的版本更全。
- 坑 434（Win11 资源管理器所有标签页都算 visible）：`App/Services/ExplorerShellWindowsHelper.cs:36,247` 取第一个 `EnumChildWindows` 命中并用 `IShellBrowser::GetWindow` 校验，不按"可见"筛。
- 坑 483（WPS 对话框普通权限 UIA 什么也看不到）：适配器在提权 Hook 进程里执行（`Core/Hook/Commands/FileDialogCommandHandler.cs`），App 自身 `asInvoker`。
- 上级文件夹名兜底两边独立得出同一设计（他们 `NameMatcher::Folders` +3 分、1024 格 memo；我们 `PathTermFallback.MaxTerms = 16`）。两个实现撞车通常是规则本身对。
- 排除词不展开拼音，两边同一决定（他们 architecture.md:632，我们 `FzfPatternParser.cs:200-205`）。

## 四维对照（拼音架构）

判定层已经同形，四维的差别落在表、打分归属和有没有测量数字上。

| 维度 | Win顺 | Lertaro（9a6b9769） |
| --- | --- | --- |
| 架构 | 零预存（每名 1 bit）加每查询现算；逐字读音并集，不做跨字一致性约束 | 同一形态，`PinyinTermMatcher` 是参考实现的移植；每名多一个烘进掩码的读音字母集；宿主另有一层字符串别名（繁转简）与混合字母 lane |
| 效率 | 329 万项：一般 2–3ms、单字母 11ms、多词 4–7ms；拼音增量几乎免费；贵的地方是零结果时 ≤8 次全量扫描。数字量在他们的大盘上 | 一条拼音查询约 30 ms@20 万行、约 120 ms@100 万行，纯字面查询不受影响；数字未复测（截至 9a6b9769） |
| 占用 | 表约 175 KB 常驻加每名 1 bit；有完整实测表（索引 34 B/项、约 110 MB 常驻、27 MB 磁盘，进程工作集 120–140 MB，均为他们自述） | 表 60,916 B 常驻加每名字一组掩码位，读音零字节；总工作集没有实测数字 |
| 正确率 | 有频率信号（常用 +26、生僻 −12），拼法宽松（`bga` 认），不做纠错；没有召回率数字，只有断言用例 | 频率信号已引入（表按频率序、−12 加分、生僻 tier）；拼法与参考实现相同；网格时代的召回与误收数字作废，现行形态没有新的召回数字 |

## 双方互有所长

对方仍有、我们没有的：同音错字反查（`Query.cpp` 的 `homophoneQueries`，输入含汉字且名字搜索零结果时逐字展开成拼音查询，只枚举多音字位的里程表、常用组合在前、≤8 个，同项取最高分，行标 `approximate`，列表上方提示"没有叫『年度报高』的，按读音找到："，高亮按拼音回填，有结果时一步不多走）；真机大盘上的实测数字。

我们已经补上的：频率优先级（表常用在前、`PinyinBonus` 的 −12、`MatchRank.TierFullRare`）。原来记为"缺口 A：Lertaro 没有频率优先级"，判断依据是读音按字母序排列、单读音兜底会挑字母序第一个（`市` → fu 先于 shi）。那次表重建把它反转了。

我们仍有、对方没有的：跨字面与拼音混排的混合字母 lane（`电nao`，词内跳字形态，参考实现做不了）；繁转简双向（`創意` 查到简体名，`PinyinRobustnessTests.TraditionalQuery_ReachesTheSimplifiedName`）；可运行真实快照与真实 provider 的集成 fixture。

两边的坑沉淀方式不同：他们靠断言测试加文档，我们靠工单史与插件测试。`pitfalls.md` 里没有拼音匹配类条目，用 `拼音|多音|同音|读音` 通查只命中 IME 输入法与设置搜索 3 处，两侧的规律一样：拼音搜索的坑通常以"某个形状搜不到"的工单形式出现，靠回归测试钉住，而不是通用坑条目。

索引版本兼容的取向差属于口径差异：Win顺 旧格式快照照样能读（architecture.md:285，第 7 版加时间列，读到旧版照常可搜、后台补时间），Lertaro 每次 bump 都是打开即拒、整盘重建（`SnapshotFormat.cs:26-44`）。这个代价已记录并接受；要借他们的做法得在读取侧做多版本兼容，那是复杂度的另一头。

短拼音查询的噪音治理是"同题不同闸"：他们学会了两条长度门槛（设置搜索的分类关键词 ≥3 字母才按拼音，pitfalls.md:1133；系统入口里只靠拼音首字母对上的降档，architecture.md:327），我们过去用跨度与分数门事后拒绝，现在两条都没有，巧合读法靠 tier 与位置键降档。网格时代的单音节形状 per-attempt 误收 8.47% 是旧形态的数字，现行形态的同类数字没量过。

## 多音字：两条相反的路线与现行形态

### 两条路线的原样记录

Win顺 的路线：表从 pinyin-data 加 kTGHZ2013 生成（去声调、ü→v、每字最多 3 个读音、常用在前，表外生僻字留 1 条），索引里一个拼音字节都不存，所有拼音形态每次查询现算，核心是位并行 NFA（不做组合枚举）。他们自述的表形状是 20,924 字有读音，其中 1 读 20,593、2 读 317、3 读 14。多音字组合数因此根本不进复杂度，首字母与全拼是同一机制的两种长度；每个词两个 matcher，全读音加一个只看常用读音的。与精确搜索的混合靠打分不靠分层：字面 词首/词边界/其他 +30/+18/+4，常用读音拼音同一位置 +26/+16/+4，需要生僻读音先 −12 再走同一档，整名相等/词干相等/整名拼满拼音 +60/+45/+40（仅单词），词只出现在上级文件夹名 +3（刻意低于名字里任何位置）。同一位置上"字面 > 常用读音拼音 > 生僻读音拼音"是构造出来的。自述边界（architecture.md:632）：拼音只认读音的开头、不做模糊纠错、排除词只按字面；`bga` 命中"报告"是被当作正确行为写进测试的。

Lertaro 曾经走过相反的路线（`af5de5b5` 引入，`b8e1061c` 删除）：每个唯一名烘一条"读音网格"payload（每位置一组读音加每位置一个音节边界位），线性于名字长度、与多音字个数无关（44 字全多音名网格约 0.9 KB，同期枚举字符串 7,807 B）；查询侧在"位置 × 词索引"上做可达性 DP，选出一套跨词一致的读音，物化成一条具体拼写再交给真 fzf 打分，另有 `AliasGridJointMatcher` 回答"存不存在一套读音选择同时满足所有词集"。当时的质量门是 span ≤ max(3q,20) 且 score ≥ 5q，某个词自己把读音拼出来时豁免 span（`f96ea6f4`，为 184 字符名的 span 82 vs 预算 30）。混合同样靠分层加 rank 打包，字面名先跑，跑不上才进别名层。

两条路线的取舍在 2026-10-17 那次重写里合流到第一条：判定逐查询现算，索引不烘读音，打分表与语义照搬参考实现。

### 仍成立的量测

阶段 3 记过一次语料形状。语料是 1200 个名字，其中 308 个由拼音 provider 处理（6685 个位置：1948 个被转写的字、5000 个原样穿过的字符）。下面是当时的数字，字节数那两行属于已删的网格编码，只作过程记录；剪枝那几张表是语料的性质，与编码形态无关。

| 问题 | 实测 |
| --- | --- |
| 逐位置网格字节（当时） | 36,769 B / 308 名 = 119 B/名 |
| 读音改用 id（表内 282 个 token，1–2 字节 varint） | 15,676 B = 51 B/名，0.43x；落地后实测 0.49x |
| 每位置只有一条读数的占比 | 96.2% |
| 名字各位置读音去重后之和 / 位置数 | 0.698 |
| 音节 token 的文档频率 | 中位 0.97%、p90 5.19%、最大 62.0%（`xue`），>10% 的只有 9 个 |
| 首字母 token 的文档频率 | 26 个 token，`d` 覆盖 110%（一名多现）、`x` 84%、`y` 82%、`j` 62% |

剪枝力对照（同一批 308 个名字，必要条件下的幸存数；当时的前置门是"别名里含查询的全部字母"，现行前置门是"烘进掩码的读音字母含查询全部字母"，两者对拼音查询等价）：

| 查询 | 字母掩码幸存 | token 交集幸存 |
| --- | --- | --- |
| `chuangyishiji` | 16 | 0 |
| `fangdanbijifa` | 44 | 0 |
| `pingjia` | 79 | 0 |
| `shi` | 141 | 24 |
| `xue` | 199 | 179 |
| `gxiao` | 83 | 2 |
| `fdan` | 86 | 3 |
| `fdbjf`（纯简拼） | 48 | 48（一点也没多剪） |

由此得出的三条结论保留：想要大小，改编码格式就够，不必换子系统；倒排的速度收益只在"词选择性高"的形状上，纯简拼查询一点额外剪枝也没有，因为 26 个首字母本身覆盖 30–110% 的名字，posting list 从一开始就是长的，这正是工单那条 `fdbjf` 的形状；幸存者仍要走判定与打分，上限是"扫一遍"那一半，不是数量级。

网格时代另有一批插桩数字：同一 harness、同一 1201 名语料、2301 条查询，只计搜索循环，无拼音 769–1027 ms → 字符串别名 1930/2434 ms → 网格全开 3113–3541 ms，两处剪枝（走完就停加首字母位图）后 −11%（2831–2896 exact / 3163–3169 fuzzy）且召回逐位不变（92.829% / 92.873%）；成本中心是 witness DP（约占网格 tier 的七成），物化加 fzf 加门只占约一成。这些是已删架构的测量，不再适用。留在经验里的两条是方法：一是"大头在 DP，不在第二遍 fzf"这类归因只能靠临时插桩量，二是想跳过"物化加打分加门"来量这条 pass 的上限反而更慢（4007 vs 3247 ms），因为去掉门之后返回的行变多，下游排序与高亮的活计涨回来，wall clock 这时量的不是那条 pass；要量这一层得数行数，不能数时间。

### 决策门槛的三个数

阶段 2 动工前要求先取的三个数：真实快照里别名段每行字节 p50/p95/p99；其中由变体叉乘（而非名字长度）贡献的比例；含汉字但别名为 0 的唯一名占比。前两项随网格作废，现在别名段只剩繁转简写法。第三项当年是"阶段 1 之前的真实影响面"，那次修法（全拼放不下时退化为每字取第一个读音，`2737030a`）后来连同整个变体枚举一起删掉，256 字符上限（`MaxComboFullPinyinLength`）在代码里 0 命中，现在没有这条悬崖。

真实大盘上仍缺的数换成了一组新的：现行 live 匹配在真实规模下的首行延迟与总耗时分布、巧合读法在用户侧样本上的误收、短 CJK 查询在掩码预筛失效时的全扫代价。本机开发盘（`%PROGRAMDATA%\Lertaro\indexes\*.idx`）的 CJK 名稀疏，只能给形状，给不了分布，取数要从用户侧样本或造出来的大盘上取。

### 阶段 3：倒排/音节 token 索引，记录，不排期

把拼音做成可检索的 token 集合（每个音节或首字母一条 posting list）能把 per-row 存储换成全局索引，长名字彻底无关。这是换子系统：要处理 delta 覆盖层与倒排的增量一致性和墓碑、跨盘索引，以及"同一行压缩前后答案一致"这条不变量的重建。只有在现行 live 匹配的真实规模量测显示"每查询现算仍然太贵"时才值得重开，否则视为过度设计。

## 动手前的测量清单（门槛，不是待办）

1. 死盘或慢盘注入：按 Win顺 坑 1395 的复现法造一个不连通的共享放进最近打开或索引范围，量有界搜索的首行延迟，用来定第 4 条要不要按盘加预算。
2. Compact 时长分布：给 `LiveIndex.Compact` 加耗时与"期间排队的搜索数"日志，跑日常使用一周。没有这个数，第 1、2 条的"写者门"改造就是猜。
3. 全局搜索并发截图：在快速窗、内联卡片、CLI、插件四种搜索者里挑两两并发，看第 3 条能不能在真实操作里复现，还是纯潜伏竞态。
4. 跟随成本：按 Win顺 的方法量"开卡片 vs 关卡片"时宿主页面的移动事件数/秒（他们测到 247 → 161），先证明第 5 条值得动，再决定走 MOVESIZE 抑制还是别停这条路径。
5. 对话框矩阵：文件夹选择器、保存对话框、已存在与不存在的路径、用户已半打文件名，这四种情形的实际后果表（第 6 条）。
6. Path 列排序：5000 行面板下按路径排序一次多少毫秒（第 9 条）。
7. 拼音在真实规模：现行 live 匹配在几万到几百万项的本机索引上跑一轮 harness，定首行延迟与总耗时分布；1201 名语料的外推在这个问题上是无效的。
8. `bytesPerCluster < recordSize` 的卷是否存在于本机；若存在，第 10 条从推断升级为复现。

网格时代用来复核判定的那个探针 harness 随网格一起删了，仓库里已无对应项目。现在验一个形状命中与否走插件测试本身（真实 provider、真实快照、真实 searcher）：把名字与查询加进 `Tests/Plugins/PinyinAlias/PinyinRobustnessTests.cs` 的 DataRow 跑 `dotnet test`。

## 不做什么

- 不照抄 Qt 那批坑的修法（材质、DPI、QML 列表、lupdate），那些是他们的框架债，不是产品债。
- 在有测量数字前不动第 1/2 条的锁结构、第 3 条的取消槽语义、第 5 条的跟随策略。
- 不把跨度门或分数门重新引入拼音层。`PinyinNameEvaluator.cs:19-22` 记着原因：名字中段的缩写不管是不是巧合都值 4 分，任何高于 4 的下限都会删掉真命中；降档现在只靠 tier 与位置键。
- 原有一条"不为对照而重写匹配层（两边架构是相反的取舍，迁移等价于从零做拼音）"已经不成立：移植做完了，代价与收益由 154e947c 到 b8e1061c 那一串提交付掉了。

## 坑与被推翻的结论

一行一条，记的是"别再走这条路"和"这个结论已经作废"。

- 一个预算同时决定两件事：全拼串限 256 字符，本是限制多音变体叉乘占多少字节的闸门，但简拼只在递归走到叶子时才写出，全拼放不下就一条分支都走不完，简拼一并消失，该行没有任何别名，任何长度的拼音查询都到不了它，而字面命中照旧。凡是一个上限能顺带消灭另一路输出的设计，后续都会以"功能整体静默失效"的形式回报。重建索引必然无效，因为每次重烘都算出同样的 0 条。
- 体积压力来自变体枚举，不来自名字长度。实测 44 字符的名字（`重`×40）产出 7,807 字节的别名串，64 字符的单音名只产出 183 字节。任何"削名字长度"的做法（截断、滑动窗口）都打偏了。
- "取更小的那个编码"这个判据本身是错的：串形态是同一个读音空间被枚举后截断的样子，与网格不等价，一旦截断某些字的读音就不在任何一条别名里（`市` 的 shi 被字典序在前的 fu 挤掉），而"按字节数取小"恰好会挑中这份残缺的表示。
- 读音网格整层作废（`b8e1061c`）。它带来两次 `SnapshotFormat.Version` bump（10→11、11→12，每次都是旧 `.idx` 打开即拒、每盘整盘重建）、每命中候选一次 payload bind 加规则 DP 加物化加一遍 fzf，成本中心是 witness DP；跨词联合取证与"一套一致读音"这两个约束在现行形态里没有对应物。别再拿"预存网格换来读音一致性"当论据。
- 网格时代的所有数字作废，包括 0.49×、18,344 B、约 60 B/名、`Version` 11→12、恒用网格多花 25–30% 时间、召回 92.83%→94.35%、混合形状 75.49%→80.75%、单次误接受 1.731%、单音节形状 8.47%。它们是网格形态的测量。
- "词内可跳"曾经是 Lertaro 独有、参考实现做不了的能力（`duc`、`gsh`、`guanlishu` 这类跨字面与拼音的混排词），移植后被拒（`RunsThatWouldNeedSkipping_ReachNothing`）。想要这类形状命中只能靠空格把块分开。
- `piji` 曾被当作"我们比他们严"的证据（`pi` 是 ping 的真前缀、`ji` 是 jia 的真前缀，只查边界子序列的实现必收），现在在 评价 上是正常命中；同一个词在工单名上仍拒，因为那里凑不出连续段。严格性卖点随移植一起没了。
- "截断只在词尾允许"这条规则（当年的 `MatchSyllableFull` 三分支）已作废：半截前缀可以落在词的任意位置。`zhengsh` 命中、`huchu` 仍拒的理由换成"名字里没有任何读音以 h 开头"。
- 质量门（span ≤ max(3q,20) 且 score ≥ 5q，加 `f96ea6f4` 的拼出读音豁免）没有随重写重新引入，理由与代价见"超长文件名"一节。当年"免包络把精确召回从 92.83% 提到 94.35%、他名误收从 +23.9% 压回 +3.4%"这组数字属于旧门，别再拿它论证新门。
- 变体枚举与 256 字符上限随网格一起删除，阶段 1 的"count == 0 时退化为单读音展开"没有对应物了；`GenerateSingleReading` 现在是唯一生成路径（`PinyinAliasCombinationGenerator.cs:46`），只服务混合 lane 与 `MapAliasToSourceIndices`。
- 阶段 1 遗留第 1、2 条里"阶段 2 已解决"的说法作废：超长名的其它读音现在由逐查询 NFA 现场判定，不再有烘出来的串别名。
- 阶段 1 遗留第 3 条（字符串路径限 256 字符、字节路径限 256 字节这两套单位不一致，只有携带多字节字面标点的名字会让字节路径提前触顶）随枚举删除一起消失，当初它"未引入也未消除"的判断不必再管。
- 阶段 1 遗留第 4 条（插件 csproj 的 `<Version>1.2.1</Version>` 未 bump，插件版本与服务端清单的耦合方式未核查）未核对（截至 9a6b9769）。
- 生效方式这条仍成立：provider `Version` 进入 `ComputeProvidersFingerprint`，快照打开时指纹不符即触发后台强制重压缩并重新生成全部别名（`Core/IndexV2/Alias/AliasProvidersReconciler.cs`），不需要用户手动重建；代价是该盘在这次重压缩期间搜索阻塞，每块盘更新后各发生一次。
- "读音排列是字母序、不是频率序"（commit `36192b57` 的说明）被 `c5156fd7` 反转，现行表按常用在前，那条 commit 的说法不再适用。
- `IAliasGridProvider` 契约那一层（可选能力接口、Core 侧探测到才走网格）删干净了；现行可选能力接口是 `IAliasLiveMatcherProvider`，`IAliasProvider` 的字符串契约（`GetAliases`、`GetAliasesUtf8`、`MapAliasToSourceIndices`、`GetQueryForms`）原样保留。第三方 provider 不受拼音形态影响这条仍然成立。
- 把网格拼回文本的唯一一处消费者是混合字母 lane；现在它换成 `AppendSpellings` 现拼单读音拼写（`SearchMatcherAliasExtensions.cs:36-52`）。当初"不补这一处，`电nao` 这类词会整条失效"的判断仍然有效，形态换了。
- 重烘窗口那条经验保留：格式版本不一致的快照根本打不开，就不存在"新二进制读到旧 payload"的中间态，payload 形状靠读取侧当场判定这条复杂度不必付。
- 混合查询里"取分高者"的两个 tier（Initials 与 Full）不再并存：tier 由命中的连续段是不是恰好等于词长来分（`PinyinNameEvaluator.cs:214-218`）。
- `DeltaRow_AnswersTheSameQueriesBeforeAndAfterCompaction` 与 `PinyinGridSnapshotTests` 这两个测试名已不在源码里；压缩前后同答这条不变量现在由 `CompactionTests.cs` 一类承担，两模式同答由 `PinyinExactModeTests` 与 `PinyinMultiBlockQueryTests` 钉住，"快照里不烘读音"由 `PinyinLiveSnapshotTests.LongPolyphonicName_IsFoundWithNoAliasesBaked`（文件名仍是 `PinyinGridSnapshotTests.cs`）钉住。
- 覆盖加分要按茎（去扩展名）量，不按整名量：把扩展算进分母时 `报告.txt` 与文件夹 `报告` 的解释度一样，文件会以 6 比 25 输给自己的文件夹（参考实现的 pitfall 2360，`PinyinNameEvaluator.cs:313-329`、`dd4a2029`）。
- 拼音 provider 的 `GetReadingSets` 返回的是可复用的线程 scratch，可能比名字长、条目只在下次同线程调用前有效，用之前要拷（`IAliasLiveMatcherProvider` remarks）。
- 名字里带真实 `|` 的候选（浏览器标签标题那类）必须逐段判定，不能跨边界匹配；别名生成里 `|` 用 U+0001 占位，否则会被当成读音分隔符切错（`PinyinAliasCombinationGenerator.cs:19-32`、`PinyinNameEvaluator.cs:148-180`）。
