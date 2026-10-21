# 搜索排序：两窗共用的四段模型

## 适用范围与核对基线

本篇是搜索排序与两窗差异的唯一权威文档，描述的是**已实施**的行为。设计依据、参数理由、阶段进度与性能数字在 [search-ranking-redesign.md](search-ranking-redesign.md)；未被采纳的独立分值方案在 [search-ranking-score-model.md](search-ranking-score-model.md)。

基线：`main` = `fd490885`，排序改进（阶段 1–3）已在该基线上落地。旧文描述的“优先档 → 类型 → 六质量键 → 收藏 → history band → …”梯子已整体删除：`RankedCandidateOrdering` 的 11 层 LINQ 换成了“合并路径 → 装饰 → 历史预算 → `Array.Sort`”，完整窗口换成一次性装饰 + 只读字段的比较器。本文按该状态重写，与旧文冲突时以本文为准。

范围是两个 GUI 窗口（快速、完整）与内联窗口的全局段。CLI（`Cli/Search/…`）与 Everything 提供者的名称排序仍借用旧比较器 `SearchResultRankComparer`，不在本文范围内。

## 结论速览

- 两个窗口共用一个内核 `Core/SearchIndex/RankPolicy.cs`：强/弱判定、分值、四段、段内键序与公共尾。快速窗口把候选装饰成 `RankKey[]` 后 `Array.Sort`；完整窗口把同一答案写在 `SearchResult` 的三个字段上，比较器只读字段。
- 先分段，再在段内排序。段序固定：Pinned（始终置顶）→ Boosted（收藏强匹配、H5/H3、正常或高优先的未提升行）→ Low（低优先且未被提升）→ HistoryTail（纯历史、有效分 < 3）。分段是必需的：没有任何一组权重能同时让低优先整段高于历史尾部、又让六档分值保持单调。
- 分值 `S = 10*C + 8*P`：强匹配 C=1；高优先 P=2、正常 P=1、低 P=0。五档 26/18/16/8，收藏强匹配 44，历史 H5 34、H3 22，纯历史尾部 6。0.8 的含义是“一个优先档抵 0.8 个匹配档”：高优先弱匹配（16）排在正常优先强匹配（18）之后。
- 历史用衰减分（半衰期 90 天，封顶 10），不是打开次数：有效分 ≥ 5 进 H5、≥ 3 进 H3。快速窗口 H5 最多 3 条、H3 最多 10 条；内联窗口降到 1/3；完整窗口只按阈值判定、不设名额，也不加历史行。
- 收藏只在强匹配时置顶；弱匹配不加分。Windows Recent 的历史分恒为 0，只按匹配质量竞争，不会因为“最近用过”被抬。
- 索引规则的“始终置顶”只对高优先生效，且不是加行：命中的行仍须匹配查询，只是进 Pinned 段。
- 引擎截断（每盘缓冲 → 排序 → `Take`）用 `SearchCandidateComparer`，只读匹配质量与公共尾，不读优先级/历史/收藏。“改截断不改终局”是刻意解耦，免得规则一改，被截掉的候选也跟着变。

## 两条管线与两窗差异

加行来源：

| 来源 | 快速窗口 | 完整窗口 |
| --- | --- | --- |
| 索引命中 | 有（候选预算见下，显示约 50 条） | 有（全量，不截断） |
| 搜索历史 | 加行 | 不加行，只提升索引行的历史档 |
| Windows Recent | 加行 | 不加行（历史分为 0） |
| 收藏夹 | 加行 | 不加行；索引行命中收藏时参与 44 分 |
| 可搜索项（开始菜单/设置） | 加行 | 无 |
| 插件即时结果 | 加行 | 无 |
| 内容 provider | 无 | 有，作为列表前缀，不参与排序 |

完整窗口的**排序域**只有索引返回的行。每行到达时 `SearchResultRanking.Decorate(raw, context)` 写一次分值与段，`StreamingResultAccumulator` 用 `RankedSearchResultComparer` 做增量合并（二分插入 + 反向合并，只重写受影响的后缀）。因为行到达时全集未知，历史档只能按阈值即时判定，不能像快速窗口那样在整页上分配 3/10 个名额——否则每来一批就要重算，已显示的行会跳动，十万行也扛不住。这是两窗唯一一处“同规则、不同分配方式”的地方。

完整窗口没有结果类型层：`RankedSearchResultComparer` 的 `TypeRank` 恒为 0。内联窗口的全局段同样传空类型顺序；当前文件夹段由 `InlineListSearchHelper.MergeLocalMatches` 按目录邻近度提到全局段之前，不参与四段竞争。

快速窗口的列表顶部还有一块不参与排序的前缀：插件即时结果与插件搜索动作行先放进 `uiResults`（`SearchResultMapper.BuildQuickResults` 的开头），进入排序的候选排在它们之后。下面所有梯子都只描述进入 `RankedCandidateOrdering` 的候选。

类型触发字符、触发词剥除、scope（文件夹作用域只属于内联窗口）、侧栏筛选与列排序这些“能搜到/搜不到”的差异，与排序模型无关；旧文对应段落的结论仍然成立，只是不再引用旧梯子。

## 排序模型

### 强匹配

`MatchRank.IsStrong`（`Core/SearchIndex/MatchRank.cs`）：`IsMatch && Tier <= TierFull && Shape <= ShapeInsideWord`。whole word、word start、inside word 都算强；scattered、`ShapeNoName`、`TierFullRare`、`NoPositiveTerms` 都算弱。

为什么用 Shape 而不是 `Contiguity >= 1`：多词查询 `code atlas` 命中 `code-atlas` 时两个词各占一段，Contiguity 只有 0.5，但它在用户眼里是正常命中；Scattered 才是“看着不像”的那种。

完整窗口从 `RankSortKey` 的高 32 位反解强弱（`SearchResultRelevance.IsStrongMatch`），不重建 `MatchRank`。`RankSortKey == 0` 的行按弱匹配处理：旧语义里键 0 是最差质量，若把它当成“shape=0/tier=0 的完美匹配”会白白抬到 18 分。

### 分值

```text
C = 强匹配 ? 1 : 0
P = High ? 2 : Normal ? 1 : 0        // Low 与其它按 0
S = 10*C + 8*P
```

| 情形 | 分 |
| --- | ---: |
| 高优先 + 强 | 26 |
| 正常优先 + 强 | 18 |
| 高优先 + 弱 | 16 |
| 正常优先 + 弱 | 8 |
| 低优先 + 强 | 10 |
| 低优先 + 弱 | 0 |
| 收藏 + 强 | 44 |
| 历史 H5 | 34 |
| 历史 H3 | 22 |
| 纯历史（有效分 < 3） | 6 |

取值优先级：收藏强 > H5 > H3 > 纯历史尾部 > 基础分。所以收藏强匹配即使同时有 H5 也是 44，不会取 34。收藏弱匹配不加分，按基础分走；H5/H3 只由预算发给合格行（纯历史行或强匹配的索引行），弱匹配的索引行拿不到历史提升。

### 四段与段内键序

| 段 | 谁进 | 段内顺序 |
| --- | --- | --- |
| Pinned | `AlwaysTop && Priority == High` | Score↓ → TypeRank↑ → 质量 → 公共尾 |
| Boosted | 收藏强匹配、H5/H3、正常或高优先且未被上面两条提升 | Score↓ → HistoryTie↓ → TypeRank↑ → 质量 → 公共尾 |
| Low | 低优先且未被提升 | Score↓ → 质量 → TypeRank↑ → 公共尾 |
| HistoryTail | 纯历史行、历史档 Tail | HistoryTie↓ → TypeRank↑ → 质量 → 公共尾 |

公共尾（`RankPolicy.CompareCommonTail`）：文件先于文件夹 → 自然名序（`SearchNameOrderComparer`）→ 名字长度 → 路径长度 → 盘（字母序数）→ 路径（忽略大小写）→ 路径（Ordinal，保证全序）→ `EngineTie`（引擎低 32 位，永远最后）。

三个刻意的选择：

- Boosted 里 TypeRank 在质量之前：沿用“结果类型优先级”这个显式设置的语义，代价是同档内更好的匹配可能排在后面（日志里能看到，见 redesign §5.5）。Low 段反过来，质量在 TypeRank 之前，给被降级的行一个按相关性翻身的机会。
- HistoryTail 整段在 Low 之后：只打开过一两次的纯历史行，只有在没有更好的东西可展示时才出现。
- `HistoryTie` 只给 H5/H3 与纯历史 Tail 行非 0；索引行即使落在 Tail（分值是 18 或 8）也不参与历史 tie-break，否则它会排在同类索引行前面。

`RankKeyComparer` 是快速窗口的唯一比较器；完整窗口的 `RankedSearchResultComparer` 调同一个 `CompareSectionRank` 与 `CompareCommonTail`，因此同一批 signals 下两边的两两顺序一致（有一条一致性用例钉住，防止两个比较器各自漂移）。

### 历史

存储：`SearchHistoryStore` 的 `StoredEntry` 带 `Score`（0–10），`Count` 保留但不再决定排序。

衰减：`Effective = clamp(Score * 0.5^(days/90), 0, 10)`（`SearchHistoryBucketSupport.EffectiveScore`）；未来时间不放大。

打开一次：`Score = min(10, Effective + 1)`，`Time` 更新，`Count += 1`。加的是“当前有效分 + 1”，不是“打开次数 + 1”——放了一个月再打开，从衰减后的分往上走，而不是从 10 往上撞顶。

迁移：旧 JSON 没有 `score` 字段时，`Score = min(Count, 10)`，再按 `Time` 自然衰减；幂等（重读重写不改结果）。`SaveEntries` 在锁内保留旧记录的分数，缺分时才用 `Count` 兜底。

读取：`ScoreSnapshot(now)` 在锁内算一次 `path → round(effective*100)`，每次查询取一次（流式重绘不得二次衰减）。`RecordCore` 只由“打开”触发；`GetEntries`/`Flatten` 读原始分，不衰减。

档位：`Effective >= 5` → H5，`>= 3` → H3；资格是“纯历史行”或“强匹配的索引行”。快速窗口按有效分降序（同分按路径 Ordinal）取前 3 条进 H5、接下来 10 条进 H3；第 4 条 5+ 掉进 H3 而不是掉出提升；3–5 分的行不会补 H5 的空位；没进 H5/H3 的纯历史行落 Tail。内联窗口名额 1/3；完整窗口只看阈值，没有 Tail。

scope：`HistorySearchCandidateMapper` 只收集当前 scope 内的历史行；scope 外的路径不参与预算，不会被提升。

Recent：`WindowsRecentCandidateMapper` 的行 `HistoryCount = 0`，历史分为 0，因此永不提升。

历史加行本身仍会经过 `IsHistoryOnly` 标记，排序时按 `NoMatch` 消费——它不冒充文件名匹配（见下文“学习型历史别名”）。

UI：进入 H5/H3 的行在快速窗口名称右侧显示「常用」徽标（`AppSearchResult.IsFrequent`，由 `RankedCandidateOrdering` 按预算结果回填）；Tail 与未入档的行不显示。徽标不改变排序，只是把“为什么这行在前面”显示出来。

### 收藏

`IsFavorite` 由候选自带的标记（快速窗口）或查询级 `RankContext.FavoritePaths`（完整窗口的索引行）提供。只有强匹配给 44 并进 Boosted；弱匹配不加分、也不因收藏进 Boosted。完整窗口的收藏只抬索引行，不加行。

### 始终置顶（AlwaysTop）

`IndexRuleSetting.AlwaysTop` 只在高优先规则上生效（`IndexRuleSyntax.TryParse` 里 `alwaysTop && effect == High` 才保留）。命中的行进 Pinned 段，但**不会凭空出现**：它必须是这次查询的候选，且仍然要匹配查询。

继承规则（`ExclusionRuleSet.GetPriorityVerdict`）：

- 路径规则：最近的一层显式路径规则整体替换祖先，包括“没勾置顶”。子级显式 High 未勾选、或子级显式 Normal，都会清掉祖先的 pin。
- 模式规则（glob/regex）：沿目录链累积取最强；同档时“任一 pinned”胜出。
- 路径规则与模式规则同档平局时路径规则优先——路径规则是文件夹级的显式决策，否则用户关不掉一条全局 pinned 的 pattern。
- `GetPriority(path, isDirectory)` 是 `GetPriorityVerdict(...).Effect` 的薄包装，旧调用点语义不变。

`AlwaysTop` 不产生补搜根：`HighPriorityRoots` 只从路径规则取。所以一条 pinned 的 glob 规则若命中的文件被引擎截断挡在候选池外，这一行不会出现（best-effort）。

### 引擎截断与优先档补搜

`SearchCoordinator` 在 `limit < int.MaxValue` 时按盘缓冲，然后 `OrderBy(SearchCandidateComparer.Instance).Take(limit)`。`SearchCandidateComparer` 只比 `RankSortKey` 高 32 位的匹配质量与公共尾，与“空历史、无收藏、无优先档解析器”的旧比较器逐位等价；它看不到规则与历史，所以截断结果不会因优先级而变。

高优先路径规则另走补搜：`HighPriorityRoots` 给出的每个根单独搜一次（上限 50 条），保证档位高的目录不会因为全局预算不够而整段消失。这是“路径型置顶可靠、模式型置顶 best-effort”的原因。

## 候选预算与显示截断

快速窗口的截断分两层：引擎先按候选预算截，App 排序后再按显示上限截。两层的数字不同，不能混。

引擎侧（`SearchService` → 本地管道/网络源 → `NameSearch`）：

- App 给 `SearchService` 的 `fileLimit`：默认 51；带 token、文件过滤范围、内联文件夹专用范围、filename-like 查询，或存在任意高档规则时是 1000（`SearchDispatchController.cs`）。完整窗口是 `int.MaxValue`（`SearchQueryDispatchController.cs`）。
- `SearchService` 对小于 `int.MaxValue/4` 的有界限额乘 4，给排除过滤留余量：51 → 204，1000 → 4000。本地管道的多盘协调器再按同一上限做一次全局 `Take`，网络源也共享同一上限。需要实时扫描的目录另按 10000 条枚举，不在这条链内。
- 每块本地盘的 `NameSearch` 用管道限额 `L`：保留 `keep = min(max(L,8)*8, 全部行)` 条候选，模式查询再按 `keep*5`（即 `L*40`）放大扫描余量，最后仍只发 `L` 条。默认查询的 204 因此变成每盘扫描上限 8160；4000 变成 160000。
- 截断比较器是 `SearchCandidateComparer`，只看质量与公共尾，所以优先级始终在引擎截断之后才参与。

App 侧（`SearchResultMapper.BuildQuickResults`）：

- 插件即时结果与搜索动作行先作为固定前缀放进列表，不占下面的候选名额。
- 候选池不只是索引行：历史、Windows Recent、收藏、可搜项也进同一个 `RankedCandidateOrdering` 列表，按同一套段与分值竞争下面的 50 个名额，来源本身不保留名额。
- 排序前按规范化路径合并（`PathKey = NormalizedPath.TrimEnd('\\')`）：同一路径的索引行、历史行、收藏行只留一行，信号取并集（收藏取或、历史次数取最大、`TypeRank` 取最小、`IsHistoryOnly` 取与），代表行按 Application → 收藏 → 非历史 → 质量 → `TypeRank` → 名字的顺序挑。合并发生在历史预算之前，所以索引行能吃到自己那条历史；同一路径不会出现“文件一行、历史又一行”。
- `skipDisplayCap` 只在 token、文件过滤范围、filename-like 查询时为真。为真时不截断；否则，若「即时行 + 候选行」少于 10，全部显示；否则候选行先补足到 8 行，再最多显示 50 条（即时行不占这 50 个名额）。候选超过 50 条时追加一行「N more」动作行（`SearchResultHelper.AddShowMoreResult`），点击进完整窗口。
- token 模式虽然跳过 mapper 的截断，文件行在截断前还会经过 `QueryTokenDispatcher`（可过滤/重排），随后 `QueryTokenResultComposer.Compose` 按 `DisplayLimit = 50` 再截一次，即时行与文件行共用这 50 个名额。文件过滤范围与 filename-like 没有这道后置截断。
- 内联窗口的文件夹专用范围（`folderScope`）只加宽预算；没有 token/文件过滤范围/filename-like 时仍按 50 条截。当前文件夹段另有 50 条上限（`ExplorerSearchHelper.CreateLocalSnapshot` 的 `limit = 50`），全局段来自同一个快速窗口结果。
- 完整窗口对索引行没有 App 侧显示上限；索引侧也不截断（`int.MaxValue`）。内容 provider 的 `FullSearchFileRowLimit = 2000` 与首批 60 条是另一套预算，不属于索引排序。

尺寸速记：默认 App 51 → 管道 204 → 每盘扫描上限 8160 → 显示 50；token/文件过滤范围/filename-like 1000 → 4000 → 显示 50（token）或不截（文件过滤范围/filename-like）；内联文件夹范围与「有高档规则」的普通查询也走 1000 → 4000，但显示仍按 50 条截。

## 学习型历史别名（“屏幕截图” → FSCapture.exe 案）

机制。`SearchHistoryStore` 把“搜索框文字 → 打开过的路径”存成键值对；一个路径同一时刻只属于一个 keyword，最近一次从哪个查询打开它就归到哪个 keyword（`SearchResultHelper.HistoryKeywordOf` 记录 `result.SearchQuery`，插件行是剥掉触发词后的文字）。`HistorySearchCandidateMapper` 用 `fuzzy.BestMatch(entry.Keyword)` 拿历史 keyword 匹配查询，不要求文件路径或文件名包含查询文字，命中后用 `entry.Path` 造行，造行前只检查路径是否存在。用户在“屏幕截图”这次查询里打开过 FSCapture，历史就永久建立了“屏幕截图 → FSCapture.exe”的关联；之后每次输入“屏幕截图”，这一行都被加回快速窗口，再打开它又以同一 keyword 写回历史，形成自我维持。完整窗口只按阈值把历史分注入索引行，不会加行。

这解释三个现象：只有快速窗口能搜到（完整窗口的结果域是索引返回的行）；路径/文件名都不含“屏幕截图”（匹配的是历史 keyword）；文件夹改名后快速窗口也搜不到（路径失效，存在性检查丢弃）。最小复现（`HistorySearchCandidateMapper.Collect`）：entry = `("屏幕截图", "D:\tools\smalltools\FastStone Capture\FSCapture.exe")`，fileExists=true 时 rows=1、name=FSCapture.exe、keywordMatchStart=0，fileExists=false 时 rows=0。

历史行标记 `IsHistoryOnly`，排序时按 `NoMatch` 消费，所以 keyword 与查询完全匹配也不会让它冒充文件名匹配；它能不能排前，取决于历史档与段（H5/H3 → Boosted，< 3 分 → HistoryTail）。

同现象的其他来源，按可能性排序：Windows Recent（`WindowsRecentCandidateMapper` 匹配 Recent 快捷方式的文件名 `TargetName`，命中后用快捷方式的目标路径造行；若 `%APPDATA%\Microsoft\Windows\Recent` 里存在名为 `屏幕截图.lnk`、目标为 FSCapture.exe 的快捷方式，会出现同样的现象，但优先怀疑历史）。可搜索项的可能性较低：开始菜单项的 `Title` 是快捷方式名，但 `FullPath` 会是 `.lnk` 路径而不是用户看到的 exe 路径。插件即时结果（尤其 FlowLauncherBridge）可以返回任意路径；当前 Flow 的普通查询只分发给全局插件，且 manifest 里没有截图类插件，不能完全排除，需看结果行的来源标识或禁用插件验证。

别名与收藏夹与此无关，依据以下机制事实：`PinyinAliasProvider.GetAliases` 当前只输出“繁体名的简体写法”一种别名，没有按文件/按路径的用户别名存储；拼音层的 `CanHandle` 要求候选文本里至少有一个汉字，`FSCapture.exe` 是纯 ASCII，不进入拼音层。精确模式（`EnableFuzzyMatch=false`）下“屏幕截图”与 FSCapture 之间不存在文本/拼音匹配。

用户侧定位步骤：

1. 关闭 Lertaro，检查用户数据目录的 `search-history.json`：安装版在 `%LOCALAPPDATA%\Lertaro\search-history.json`，便携版在 `<程序目录>\Data\Users\<用户哈希>\search-history.json`。搜索 `"屏幕截图"`，看对应 `path` 是否为 FSCapture.exe。若是，即为根因；在“设置 → 历史 → 搜索历史”删除该条，或关程序后编辑 JSON。
2. 历史里没有的话，检查 `%APPDATA%\Microsoft\Windows\Recent` 下是否有名为 `屏幕截图.lnk`、目标为 FSCapture.exe 的快捷方式。
3. 两者都没有，再排查插件即时结果：临时禁用 FlowLauncherBridge/第三方插件，或看结果行的来源标识。

## IsMatch 与 Rank 的分裂（已修）

这是一个通用接缝问题，审查时点的反例：

```text
query="!zzz"   name="abc.txt"    IsMatch=True   Rank.IsMatch=False
query="!zzz"   name="防弹笔记法"  IsMatch=True   Rank.IsMatch=False
query="atlas !code" name="code-atlas.txt"
                               IsMatch=False  Rank.IsMatch=True
```

原因：`HighlightMask.ComputeRank` 只能给“被画出来的正词”算秩，纯排除查询没有正词、mask 为空，返回 `NoMatch`；反过来它只画正词、不做排除裁决，所以“正词命中但排除词也命中”的名字照样算得出秩。

修法是把“是否命中”与“如何高亮/打分”彻底分开。`FuzzyMatcher.ComputeMatchRank` 先做整查询 `IsMatch` 门（多词/排除/OR 时），纯 inverse 查询返回显式的 `MatchRank.NoPositiveTerms`（该秩留在质量梯子底部，不能冒充名字匹配），两个窗口对它比较一致；`WindowsRecentCandidateMapper` 与 `HistorySearchCandidateMapper` 都改用 `BestMatch`。

## 拼音与空格混杂的解析行为

当前管线：

```text
输入
  └─ SearchQueryParser：路径模式判定、盘符、Unicode 空白切分
      └─ FzfPatternParser：操作符（! ^ $ ' |）、引号合并、别名 query forms
          └─ 匹配
              ├─ 名字：SearchMatcher / SearchMatcherRow（字面 + 拼音 NFA + 别名 + 混合字母）
              ├─ 路径兜底：PathTermFallback（多词中至少一词命中名字，其余由祖先目录补）
              └─ 路径模式：PathGate / PathSearchFuzzy
          └─ 排序：上面的四段模型
```

拼音层是逐查询、逐候选的实时 NFA（`PinyinTermMatcher`），不烘焙读音；`HighlightMask` 负责最终高亮和 `MatchRank`。这部分没有“索引/查询规则两套实现”级别的分裂。

分词与折叠：只认 Unicode 空白（`Split((char[]?)null)`），转义空格用 `\u0001` 占位；查询与候选名字统一做全角 ASCII→半角、U+3000→空格并做 NFC（`SearchTextNormalizer`，通用 NFKC 不做），显示保留原文，`NormalizeInto` 返回位置映射，`HighlightMask` 用它把 mask/Start 映射回原文；查询解析、索引候选、LiveDirectory、PathTermFallback、PathGate、App 匹配边界全部接入。`SnapshotFormat.Version` 因此升到 14（`UniqueMasks` 增加折叠拼写，旧索引按既有 stale-snapshot 路径重建）。

拼音正例（真实 provider 实测，均精确模式除注明外）：

| 场景 | 查询 | 候选 | 结果 |
| --- | --- | --- | --- |
| 全拼 | `fangdanbijifa` | 防弹笔记法… | TierFull，Start 0 |
| 简拼 | `fdbjf` | 防弹笔记法… | TierInitials，Start 0 |
| 半音节+简拼 | `fdan` | 防弹笔记法… | TierFull |
| 多词拼音 | `fangdan bijifa` | 防弹笔记法… | 各词独立 |
| 汉字+简拼 | `防弹 fdbjf` | 防弹笔记法… | TierName |
| 拼音+英文 | `fdbjf Anna` | … Anna Esor | 各词独立 |
| 繁转简 | `創意市集` | 创意市集 | 字面别名 |
| 简转繁 | `创意市集` | 創意市集 | TierInitials（扁平别名） |
| 混合字母 | `电nao` | 电脑玩物 | 精确 false / fuzzy true（设计如此，仅模糊模式） |
| 单汉字 | `防` | 防弹笔记法 | 字面 |
| 数字 | `fangdan2024` | 防弹2024 | 拼音+字面 |
| 63 字母拼音词 | `a`×63 | 阿×63 | NFA 上限内 |
| 64 字母拼音词 | `a`×64 | 阿×64 | 超过 64 位状态集，false |

拼音的“单词内必须连续”语义被现有测试钉住：`fdbjf` 不会跨 `防-弹` 或 `防 弹` 里的分隔符；`fdbjfgxiao`（跨词）不命中。这是 WinShun 的既定语义，用户可能把“简拼跨空格”当成 bug，建议在设置/文档里写清。

## 边界与反例

- 长名：225 字符 ASCII 名中的 `atlas` 可以命中（Start=120，Shape=InsideWord，Weight≈0.02）；224 字符 CJK 名中的 `pingmujietu`/`pmjt` 可以命中。`PinyinTermMatcher.MaxText = 512` 是码点上限，匹配起点在 512 之后就不再看：实测 prefix=500 拼音 True，prefix=512 与 600 拼音 False（字面 屏幕截图 True）。文件名最多 255 字符，文件搜索不受影响；浏览器标签/插件标题这类可搜索项标题超过 512 码点时，拼音会静默失效。
- 拼音词长上限 63（64 位状态集）：62/63 字符可命中，64 不行；超长全拼查询只能走字面匹配。
- 单字母拼音词（`f`）被 `PinyinTermMatcher.IsEligible` 拒绝（要求 2..63），“输入一个首字母找中文名”不可用；这是 WinShun 的既定设计，对“简拼”用户是明显的手感差异。2-3 字母拼音词召回很大（如 `baj`），排序靠 tier/位置/覆盖率压低；现有测试把 `baj` 作为“半音节中名内命中”接受。
- 空格/英文分词：`fast stone`、`fast  stone`、`stone fast`、`f s c` 都按“空格分词 + 词序无关”工作。`fast stone` 能命中 `FastStone`（两个词分别落在 0 和 4），多词独立子串语义；连续短语必须加引号（`'fast stone'`），引号把它变成一个含空格的词，只匹配真正含空格的文本。只有空格/空查询返回空结果。
- 引号与操作符：`fast\ stone` 把两个词合并成一个含空格的词（实测对 `fast stone notes` 为 true），这是有意的转义。未闭合的 `'` 被当作“精确性开关”，不是字面引号；`!`/`^`/`$` 开头没有通用转义；`$` 结尾永远是后缀锚点，文件真名以 `$` 结尾时只能用单独的 `$` 查。
- 排除：`!term` 只按名字字面裁决，不走拼音/别名/祖先目录（“名字级否决”的既定规则），所以 `!简单` 不能排除一个只有拼音别名含“简单”的名字。繁体查询的排除词不会转换（`GetQueryForms` 只用于正向词），`!創意` 不会排除简体名里的“创意”。
- 名字序比较：`SearchNameOrderComparer` 按数字段显著长度 + Ordinal 比较，不调用 `long.Parse`（20 位以上数字段不会抛 `OverflowException`）。规则为数字先于字母（大写先于小写）、符号最后。
- 虚拟路径：`SearchResultHelper.NormalizePath` 对 `shell:` 与 `__SEARCHABLE_ITEM__:` 前缀有显式分支，不走 `Path.GetFullPath`（否则 app 的 `FullPath` 会被拼成当前工作目录下的相对路径）。
- 引擎截断与终局排序都读 `RankSortKey` 的高 32 位质量键（`SearchResultRelevance.HighKey`），只改其一会让两个阶段发展出不同的排序键语义。该键的每一位都是“越小越好”，比较必须升序；反过来会把最差的行排到最前。

## 有意保留的差异

- 非索引来源（历史、Recent、收藏、可搜索项、插件即时结果）只在快速窗口加行；内容 provider 行只在完整窗口（provider 集合不同）。
- 快速窗口的预算与显示上限：普通宽查询 App 要 51、管道 204、每盘扫描上限 8160，排序后显示 50 条候选行（即时行额外）；token/文件过滤范围/filename-like 要 1000、管道 4000；token 最终仍截 50 条，文件过滤范围与 filename-like 不截。完整窗口索引侧无截断，App 也不对索引行设显示上限（内容 provider 的 2000 条上限另算）。
- 历史名额：快速 3/10、内联 1/3、完整只判阈值。快速窗口的名额是“整页最优”的分配，完整窗口必须让每行到达时就有最终段位，所以放弃了名额。
- 只有快速窗口有收藏新增行与结果类型顺序；完整窗口没有类型层，收藏只作用于已有索引行。列排序只属于完整窗口。
- `RankSortKey == 0` 的完整窗口行按弱匹配处理（保守）；索引行正常情况下低 32 位由引擎填充，不会为 0。
- 只剥一次触发字符：连打两个触发字符时，“显示更多”交接的文本会被完整窗口再剥一次（边缘情形）。
- recency band 与 path depth 不参与排序；shape 位于 contiguity 之后、whole word 可压过更靠左的 word start，左端连续只在 shape 相同时生效（owner 选择的方案）。
- 通用 NFKC 不做，只做全角 ASCII→半角 + U+3000→空格 + NFC；单字母拼音、64 字母拼音、512 码点上限是既有限制。
- CLI 的交互式与非交互式搜索、Everything 提供者的名称排序仍用旧 `SearchResultRankComparer`；管道无 token 路径按设计不排序，排序留给客户端。它们不在本次迁移范围。

## 坑与教训

以下每条是审查时点（修复前）缺陷的结论，编号 F1-F8 保留用于对应回归测试。

- F1 位布局与契约不符：`HighKey` 的 start 只占 6 位（`start << 16`，bit16..21），注释写 `start(7)`、`MatchRank.StartCap=127`，实际位置 63 之后的所有命中在完整窗口完全并列。已修；改布局必须补 63 vs 64 的对比用例。
- F2 默认值吞掉分数：`NoNameFallback` 的 `Contiguity=0` 使 coverage 点永远按最差算，`pathWeight` 完全不参与键值；`MatchRank` 现在有显式 `Coverage` 字段。
- F4/F6 手工构造的 `new MatchRank(tier, start, weight)` 同样因默认 Contiguity=0 拿不到 coverage 分，受影响的不止测试。
- F3 两窗键序相反：完整窗口曾把 history priority 放在 `RankSortKey` 之前，快速窗口放在质量键之后，同一批行同一查询在两窗顺序不同。1c/1d 之后两窗共用 `RankPolicy`，历史只通过 H5/H3 档与 `HistoryTie` 参与，不再各自维护一段键序。
- F5 排序键“删而不净”：shape/recency/depth 不再参与排序后，字段生产者、注释、测试、提交信息还在承诺它们；后续任何人无法从代码判断到底按什么排。`Shape` 随后按 owner 要求重新实现，recency/depth 保持删除。
- 键位置决定设置可达性：类型顺序曾放在六项质量键之后，不同类型的行几乎不可能在质量键上打平，设置页的“结果类型优先级”实际失效；优先档若放在类型分组之内，高优先级文件仍爬不到应用组上面。现在的做法是类型顺序进段内键（Pinned/Boosted/HistoryTail 在质量前，Low 在质量后），并且只用分值而不是硬分组。给用户可见设置排键位时，用“这个键什么时候轮得到出手”验证。
- 行来源混进打分：历史行曾拿 keyword 的匹配秩当文件名的匹配秩参与排序；造行的行必须携带来源标记（`IsHistoryOnly`），梯子按 `NoMatch` 消费它。
- 同一路径的两种表示不该由偏好键裁决：`.lnk` 的索引 File 行与 provider 的 Application 行文本键全等，只有应用行能启动程序。现在由 `MergeByPath` 在排序前合并，Application 行优先，类型偏好决定“先看哪一类”，不决定“同一个路径被表示成什么”。
- 统一排序不解决“候选有没有被取到”：默认 51 的 App 限额（管道 204）让排在后面的同名/近名文件根本到不了统一排序器。filename-like 查询用 1000 预算（管道 4000）并跳过显示上限；token 查询也加宽到 1000，但显示仍由 composer 截到 50。
- `IsMatch` 与 `Rank` 是两个判断：只用 `Rank` 的候选源会绕过匹配门，只用 `IsMatch` 又打不出秩。加行来源必须走 `BestMatch`；“没有正词”要有显式的秩，借用 `NoMatch` 会把本可展示的行整个丢掉。
- 全角残留（U+3000、全角字母数字、NFD 组合形式）是中文输入法下“明明打了空格/看起来一样却搜不到”的主因；折叠必须同时接入查询解析、索引候选与高亮边界（位置映射回原文），并升快照版本号让旧 prefilter 重建。
- 排序比较器里做整数解析：`long.Parse` 在 `OrderBy` 内被直接调用且没有 try/catch，20 位数字段会抛 `OverflowException`；按数字段长度加 Ordinal 比较即可。
- 注释自相矛盾会指导错误维护：`NameOrderComparer` 一处注释写“letters before digits”、另一处写“digits first”，实现是数字在前；此类注释必须与实现统一。
- 测试用例走真服务会变成环境依赖：`EverythingSearchDataProviderTests` 曾因本机服务没在跑抛 `TimeoutException`，被层层包成 `IOException`；改法是把子树应答抽成纯函数 `AnswerFolderSubtreeSize`，查询串路由仍由 `EverythingQueryParserTests` 覆盖。
- 单测通过数要注明口径：`ExplorerStaInvokerTests` 与 `UpdaterRollbackTests` 在方法级并行下会偶发环境性红，不是排序回归；Core 的日常基线要带过滤，见下节。

## 仍然待办

- 应用内 p50/p95 未测：基准只覆盖 `RankPolicy.BuildKey`、`SearchCandidateComparer` 与 `RankedSearchResultComparer`，窗口重绘与数据到达的真实延迟要跑起应用才能取。
- 真实日志的校准未做：0.8 交换率、90 天半衰期、3/10 名额都是先验值，需要用 `ranking-events.jsonl` 的样本回放（`RankingEventLogTests` 只保证采样正确，不评价好坏）。
- 模式型 `AlwaysTop` 在引擎截断下是 best-effort：它不产生补搜根，命中的文件若没进候选池就不会出现；路径型规则由 `HighPriorityRoots` 分根补搜保证。
- “常用”徽标只做了快速窗口（H5/H3 行在名称右侧显示），内联窗口的窄行与完整窗口（纯阈值制、不加历史行）尚未显示。
- CLI 与 Everything 的旧比较器未迁移（有意保留，见上文）。
- FSCapture 类现象的最终归属（历史 vs Recent vs 插件）仍需在用户环境按定位步骤确认。
- 拼音上限（单字母、64 字母、512 码点）与“简拼不跨空格”的语义：写进用户文档/设置提示，或做多词拆分。

## 怎么验证

构建与测试（仓库根）：

```powershell
dotnet test Tests/Core/Core.csproj --filter "FullyQualifiedName!~ExplorerStaInvokerTests&FullyQualifiedName!~UpdaterRollbackTests"
dotnet test Tests/App/App.csproj
dotnet test Tests/Plugins/PinyinAlias/PinyinAlias.csproj
```

2026-10 核对的基线：Core 2222（带上面两个过滤）、App 2240、PinyinAlias 238；不带过滤的 Core 会多出那两类共 18 条，已知偶发环境性红。

排序基准（不进 CI）：

```powershell
dotnet run -c Release --project benchmarks/Lertaro.Benchmarks -- --filter '*'
```

数字与机器信息只放在 redesign §7.3；本目录不复制基准结果。日志验证：关掉“设置 → 通用 → 系统 → 排序事件日志”后，`%LOCALAPPDATA%\Lertaro\ranking-events.jsonl` 不再增长；测试程序集在 `Tests/App/AppTestAssembly.cs` 里关掉了 `RankingEventLog.EnabledOverride`，跑测试不会写用户目录。

排序相关的测试类：

- `RankPolicyTests`：分值 26/18/16/8/44/34/22/6、四段归属、段内键序、`HistoryTie` 的 0 值、设计文档 3.7 的十项梯子。
- `HistoryBudgetTests`：阈值优先于名额、第 4 条 5+ 落 H3、空位不补、纯历史落 Tail、重复路径取强档、名额可配 1/3 与 0。
- `SearchResultRankingTests` / `RankedSearchResultComparerTests`：完整窗口的装饰、阈值档、无类型层、`RankSortKey == 0` 按弱、与快速窗口比较器的一致性。
- `RankedCandidateOrderingTests` / `SearchResultMapperTests`：快速窗口的合并、收藏强匹配、H5 压普通强、低优先顺序、AlwaysTop、内联名额。
- `SearchHistoryStoreTests`：衰减、封顶、迁移幂等、旧 JSON 缺字段、`SaveEntries` 保留分数。
- `AlwaysTopTests` / `IndexRuleConflictTests`：置顶继承与清除、覆盖提示（含“只排除了正则的一部分就不提示”）。
- `MatchRankTests` / `SearchResultRelevanceTests`：质量键与 `HighKey` 位布局。

## 关键源码位置

| 主题 | 位置 |
| --- | --- |
| 排序内核（分值/分段/键序/历史预算/公共尾） | `Core/SearchIndex/RankPolicy.cs` |
| 查询级上下文（规则裁决、历史分、收藏、名额） | `Core/SearchIndex/RankContext.cs` |
| 强匹配与质量键 | `Core/SearchIndex/MatchRank.cs`、`Core/IndexV2/Search/SearchResultRelevance.cs` |
| 完整窗口装饰与比较器 | `Core/SearchIndex/SearchResultRanking.cs`、`Core/SearchIndex/RankedSearchResultComparer.cs` |
| 引擎截断比较器 | `Core/SearchIndex/SearchCandidateComparer.cs`、`Core/Indexer/Usn/SearchCoordinator.cs` |
| 快速窗口合并与排序 | `App/ViewModels/Search/Mapping/RankedCandidateOrdering.cs` |
| 快速窗口候选/截断 | `App/ViewModels/Search/Mapping/SearchResultMapper.cs`、`App/ViewModels/Search/Dispatch/SearchDispatchController.cs` |
| 完整窗口流式合并 | `App/ViewModels/Search/Mapping/StreamingResultAccumulator.cs`、`App/ViewModels/Search/Dispatch/SearchQueryDispatchController.cs` |
| 历史持久化与衰减 | `Core/SearchHistoryStore.cs`、`Core/SearchHistoryBucketSupport.cs` |
| 历史加行 | `App/ViewModels/Search/Mapping/HistorySearchCandidateMapper.cs` |
| Recent 加行 | `App/ViewModels/Search/Mapping/WindowsRecentCandidateMapper.cs` |
| 索引规则与置顶裁决 | `Core/ExclusionRuleSet.cs`、`Core/Settings/IndexRuleSetting.cs` |
| 排序事件日志 | `Core/SearchIndex/RankingEventLog.cs`、`App/Views/Settings/General/GeneralSettingsSystemTab.xaml` |
| 候选预算常量 | `App/ViewModels/Search/SearchViewModel.cs`、`Core/Services/Search/SearchService.cs`、`Core/IndexV2/Search/NameSearch.cs` |
| 名字序比较 | `Core/SearchIndex/SearchNameOrderComparer.cs` |
| 拼音 NFA 与评分 | `Plugins/PinyinAlias/PinyinTermMatcher.cs`、`Core/SearchIndex/HighlightMask.cs` |
