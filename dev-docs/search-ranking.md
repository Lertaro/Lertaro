# 搜索排序：两条管线与一把梯子

## 适用范围与核对基线

本篇由搜索排序审查（基点 `99a5f854`，PR #406）与两窗一致性调查合并而成，是搜索排序与两窗差异主题的唯一权威文档。代码位置按 `main` = `1986b835` 复核（排序相关代码自 `9a6b9769` 未改）。审查基点之后已落地的排序相关提交：`79de9469`（left-contiguous / contiguity / history / coverage / tier / name order 收口）、`99a5f854`（PR #406 合并）、`b57a57f4`（结果类型次序作为最外层键）、`ee75c500`（应用行独占快捷方式路径）、`ba6fea2c` / `714300ed` / `8c754217`（索引规则统一与按档位排序，见 `Core/ExclusionRuleSet.cs`、`Core/Settings/IndexRuleSetting.cs`）。审查方法为源码逐段核对、用真实 `PinyinAliasProvider` 和真实 Core 匹配 API 构造正反例、直接调用现有测试方法。审查中标注"修复前/待修/建议"的说法已逐条按 `9a6b9769` 的代码重新核对。审查没有跑真实大索引（200k/1M）的性能测量，只审逻辑与边界，不审延迟；审查时点没有在用户机器上复现，App 工程未在本机重建，App 侧结论来自 HEAD 源码。

## 结论速览

- 快速窗口与完整窗口是两条管线，结果成员不同：历史、Windows Recent、收藏、可搜索项、插件即时结果只在快速窗口加行；2026-10-19 收口后两窗共用同一组匹配质量键（快速窗口经 `MatchRank.CompareQuality`，完整窗口打包进 `SearchResultRelevance.HighKey`），但整个梯子不完全相同——快速窗口在质量键之前多一层结果类型顺序，完整窗口没有类型层。
- 现行梯子（`1986b835`）：进入排序的候选按 索引规则优先档 → 结果类型顺序（仅快速窗口；内联窗口传空顺序，等于没有）→ 六个匹配质量键（contiguity → shape → 左端连续 tie-break → start → coverage → tier）→ 收藏 → history band → history priority → 文件/文件夹 → 自然名序 → 名字长度 → 路径长度 → 盘 → 路径 排序。完整窗口没有结果类型顺序这一层，质量键打包进 `RankSortKey` 高 32 位，末尾多一个引擎低 32 位 tie-breaker。
- 快速窗口的截断分两层：引擎候选预算（默认 App 51 → 管道 204；token/文件过滤范围/内联文件夹专用范围/filename-like/有高档规则 1000 → 4000）和显示上限（默认 50 条候选行；token 50 条总计；文件过滤范围与 filename-like 不截）。详见「候选预算与显示截断」。
- “屏幕截图”命中 `D:\tools\smalltools\FastStone Capture\FSCapture.exe` 的来源是快速窗口独有的学习型搜索历史行：历史行用历史 keyword 匹配查询，不要求文件路径或文件名包含查询文字。别名层与拼音层不可能产生这个结果。
- 审查发现的 8 个排序/解析缺陷（F1-F8）全部已落地修复；审查时点搜索相关 192 个用例中 12 条红，收口后全部转绿或改写为新梯子的断言。

## 两条管线：快速窗口与完整窗口

加行来源差异：

| 来源 | 快速窗口 | 完整窗口 |
| --- | --- | --- |
| 索引命中 | 有（候选预算见下文，显示约 50 条） | 有（全量） |
| 搜索历史 | 加行 | 只排序 |
| Windows Recent | 加行 | 只排序 |
| 收藏夹 | 加行 | 只对索引行排序（favorite band；不新增行） |
| 可搜索项（开始菜单/设置） | 加行 | 无 |
| 插件即时结果 | 加行 | 无 |
| 内容 provider | 无 | 有 |

完整窗口的**排序域**只有索引返回的行：`SearchQueryDispatchController` 用 `HistorySearchCandidateMapper.RankingPriorities(...)` 把历史降级成排序输入，`RankingPriorities` 的注释原文为 "The full window ranks only rows the index returned… No existence probes"；行为由 `Tests/App/ViewModels/Search/Mapping/HistorySearchCandidateMapperTests.cs` 钉住。内容 provider（`IFullSearchFileResultProvider`）的行仍然会显示，但它们由 `StreamingResultAccumulator.QueueContentPrefix` 放在列表前缀，不进入排序（`ContentPrefixCount`）。

候选预算与上限（`SearchDispatchController.cs:188-204`、`SearchViewModel.cs:22,28`）：快速窗口普通查询给 `SearchService` 的 `fileLimit` 是 51，`SearchService` 对本地/网络源乘 4 得到 204 条候选（给排除过滤留余量，`SearchService.cs:48-51`），App 排序后显示约 50 条。带 token、文件过滤范围、内联文件夹专用范围、filename-like 查询（`LooksLikeFileNameQuery`：至少 3 字符、首字符不是 `! ^ ' - : .`、且含点），或存在任意高档规则时，App 的请求上限提到 `TokenQuickSearchFileLimit` = 1000（管道侧 4000）。完整窗口是 `int.MaxValue`（`SearchQueryDispatchController.cs:204`）。`skipDisplayCap` 的确切条件和各路径的最终显示上限见下文「候选预算与显示截断」。

显示侧过滤：完整窗口有侧栏（类型/大小/修改时间，选中项跨查询保留）和列排序（本次运行内记忆），`ViewMode="Grid"` 有列头；快速窗口是 `ViewMode="List"`，无侧栏、无列排序。

类型触发字符：两窗现在都在搜索前剥掉“按类型触发字符”（`UserSettings.ResultTypeTriggers`）。完整窗口原先不剥，已修（`SearchQueryDispatchController.cs:120`）；只剥字符，字符所选的类型不套用（那是侧栏的职责），剥的位置在文件作用域关键字之前，与快捷窗口一致。规则本身是 `ResultTypeTriggerHandler.Match`（纯函数，已有逐分支测试），接线由 `SearchQueryDispatchControllerTests` 钉住。

“能搜到 / 搜不到”的原因清单：

1. 只有快捷窗口能造出这一行（设计如此）。文件不在索引里时，完整窗口没有任何东西能在索引之外加行：盘未启用本地索引、网络/WSL/文件夹源未配置或没建完（`SearchServiceHelper.CheckNeedsLiveSearch` 的三档判定）、路径落在排除或忽略规则里、文件是隐藏/系统属性（索引结果被 `FileSystemItemFilter` 丢掉）、路径被 service 侧调用者可见性拒绝。
2. 完整窗口把它藏了（行在、看不到）：侧栏类型/大小/修改时间有选中项，或点过一次列头排序，排序状态本次运行内一直沿用。
3. 搜的不是同一串文字：原先快捷窗口剥触发字符而完整窗口不剥，同一串可见文字在两个窗口搜的是 `报告` 和 `.报告`；已修。
4. 交接后被替换：从快捷窗口“显示更多”进完整窗口，先把快捷窗口的行贴上去（只保留 `ResultKind=="File"`），随后被“只含索引”的真实结果整体替换，不在索引的那一行先出现再消失。
5. 作用域不是差异源：文件夹作用域只属于内联窗口（`SearchScope` 仅由内联窗口设置），快速与完整窗口都是全局搜索。内联窗口的“当前文件夹实时扫描 + 直系子项”是另一条非索引来源。

快捷窗口自建的历史、Recent、收藏行与索引行过同一口径（`HistorySearchCandidateMapper.IsExcludedFromSearch`）：同样要过“源在本会话不可达”和“隐藏/系统”两道门；属性探测只对本地路径做（`File.GetAttributes` 没有超时，网络路径会挂住，`SafeFileExists` 就是为这类调用加的护栏）。收藏行虽是用户自选，它同样是完整窗口永远列不出来的行，所以一并过这道门。

## 现行排序梯子

两窗共用同一组键。六个匹配质量键由 `MatchRank.CompareQuality`（`Core/SearchIndex/MatchRank.cs:75`）定义，这是唯一的质量顺序；完整窗口用 `SearchResultRelevance.HighKey` 把同一顺序打包成键：

```text
contiguity（匹配字符形成连续段的比例）
→ shape（whole word > word start > inside word > scattered）
→ 左端连续（Start == 0，只在 shape 相同时做 tie-break）
→ start（最左匹配位置；7 位，127 饱和，63 之后不再并列）
→ coverage（匹配覆盖名字的比例）
→ tier（字面名 > 简拼别名 > 全拼 > 生僻读音）
```

`Shape` 在 2026-10-19 第二轮按 owner 要求重新实现（`MatchShape`），位置在 contiguity 之后、start/coverage 之前；whole word 可以压过更靠左的 word start（`code-atlas` 是用户输入的整个词，`atlasing0` 只是以它开头）。左端连续只在 shape 相同时生效。`RankSignals`（recency band / path depth）、`MatchedLength`、`IsExactName` 保持删除。`MatchRank` 有 Tier / Start / Coverage / Contiguity / Shape 五个字段。

快速窗口的列表顶部还有一块不参与排序的前缀：`BuildQuickResults` 先把插件即时结果与插件搜索动作行放进 `uiResults`（`SearchResultMapper.cs:95-98`），进入排序的候选排在它们之后。下面的梯子只描述进入 `RankedCandidateOrdering` 的候选。

快速窗口梯子（`App/ViewModels/Search/Mapping/RankedCandidateOrdering.cs`）：

```text
索引规则优先档（High=0 / Normal=1 / Low=2，ExclusionRuleSet.GetPriority 按路径与祖先目录解析）
→ TypeRank（结果类型顺序）
→ 六个质量键
→ 收藏
→ history band（打开 ≥5 次为 0，1-4 次为 1，无历史为 2）
→ history priority（本次查询学到的 keyword 顺序）
→ 文件先于文件夹
→ 自然名序 → 名字长度 → 路径长度 → 盘 → 路径
```

排序前先由 `ApplicationOwned` 让应用行独占它的路径：任何被 provider 标成 Application 的路径，其 File 行先出局，不再参与类型比较（见坑与教训）。排完序后按 `NormalizedPath` 去重，首个（排名最高的）胜出。历史新增行带 `IsHistoryOnly`，由 `EffectiveMatch` 换成 `NoMatch` 参与排序。

注意这个梯子只在“进入排序的候选”内部成立：即时行前缀、以及内联窗口随后的邻近度分组都在它之外（见下文）。同一优先档内部，快速窗口先比类型再比质量；收藏与 history 只在同一档位内起作用，不能跨档位把低档行拉回来。

完整窗口梯子（`Core/SearchResult.cs` 的 `SearchResultRankComparer` + `Core/IndexV2/Search/SearchResultRelevance.cs`）：六个质量键打包进 `RankSortKey` 高 32 位，布局为 `contiguity(8) | shape(3) | not-left-contiguous(1) | start(7) | coverage(9) | tier(4)`，比较顺序是优先档 → 高 32 位 → 收藏 → history band → history priority → 文件/文件夹 → 自然名序 → 名字长度 → 路径长度 → 盘 → 路径 → 低 32 位（引擎自身的 fzf tie-breaker，永远排在窗口级键之下）。完整窗口**没有结果类型顺序这一层，也没有按类型分组**：`StreamingResultAccumulator` 用同一个比较器增量合并索引行，内容 provider 的行作为前缀不参与排序。`Core/SearchResult.cs:102` 的注释说“结果类型分组发生在该比较器之上”，那是改造前的说法，与当前实现不符；`RankedCandidateOrdering` 的注释“类型顺序只在快捷窗口生效”才对。`HighKey` 同时被 `NameSearch` 的候选截断和最终键使用，防止两阶段各自发展出不同的键语义。

内联窗口的全局段走同一个 `RankedCandidateOrdering`，但 `SearchResultMapper` 对 `isInlineWindow` 传入空的结果类型顺序，所有 `TypeRank` 同值，等于没有类型层；渲染前 `InlineListSearchHelper.MergeLocalMatches` 再按目录邻近度把当前文件夹段提到全局段之前。当前文件夹段（直达子项与文件夹内更深的行）不单独查档位：`ExplorerSearchHelper.RankMatches` 调用 `RankCandidates` 时不传 priority resolver，直达子项在 `CreateLocalSnapshot` 里按邻近度/历史/匹配排序后取前 50（`limit = 50`）。所以内联窗口的最终列表里，档位不是全局第一键，目录邻近度在它之上。

优先档与类型为什么放最外层（这一节讲快速窗口；完整窗口没有类型层，优先档之后直接是质量键）。结果类型优先级设置曾经“失效”：`UserSettings.ResultTypeOrder` 存得住，`SearchResultTypePriority.Rank` 也算出了 `TypeRank` 送进比较器，但梯子把 `TypeRank` 放在六项质量键之后，任何两个类型不同的结果几乎不可能在这六项上全部相等，类型顺序永远轮不到出手。用户选择的修法是类型顺序作为快速窗口的硬分组，组内再跑完整引擎梯子（`b57a57f4`）。后果（有意）：类型排在前面的行可以压过匹配更精确的行，收藏不再能跨类型越级。`8c754217` 又把索引规则优先档提到类型分组之前，作为硬分组：high 规则压过任何类型的 normal 行，low 规则输给所有 normal 行；若优先档放在类型组内部，高优先级的文件仍然爬不到应用组上面。这两个键先后各被放在内侧过一次，每次都让一个用户设置不可达。

历史只做 tie-breaker：band 在 tier 之后、收藏在 band 之前，历史永远排在匹配质量之后，不再压过更好的左端连续命中。打开次数由 `SearchHistoryStore.UsageCounts()` 提供，`SearchResultMapper`、完整窗口、`AppSearchPipeService` 都传入；旧调用方只给 priority map 时，路径在其中即按弱历史（1-4）处理。索引行本身是收藏时，完整窗口给同样的 favorite band。

名字不匹配的行使用共享兜底秩：`MatchRank.NoNameFallback(pathCoverage)` 给祖先目录补全出来的行（`ShapeNoName`）按“查询覆盖整条路径的比例”排序，引擎的 `SearchResultRelevance.Apply` 与快捷窗口梯子从此用同一个秩，`StartCap` 也随之提升到 `MatchRank` 上共用。纯排除查询产生的行用显式的 `MatchRank.NoPositiveTerms`（见下文 IsMatch/Rank 分裂）。

## 候选预算与显示截断

快速窗口的截断分两层：引擎先按候选预算截，App 排序后再按显示上限截。两层的数字不同，不能混。

引擎侧（`SearchService` → 本地管道/网络源 → `NameSearch`）：

- App 给 `SearchService` 的 `fileLimit`：默认 51；带 token、文件过滤范围、内联文件夹专用范围、filename-like 查询，或存在高档规则时是 1000（`SearchDispatchController.cs:188-197`）。完整窗口是 `int.MaxValue`（`SearchQueryDispatchController.cs:204`）。
- `SearchService` 对小于 `int.MaxValue/4` 的有界限额乘 4，给排除过滤留余量（`SearchService.cs:48-51`）：51 → 204，1000 → 4000。本地管道的多盘协调器再按同一个上限做一次全局 `Take`（`SearchCoordinator.cs:68`），网络源也共享同一上限（`NetworkIndexerSearchExtensions.cs:45-66`）。需要实时扫描的目录另按 10000 条枚举（`SearchService.cs:194`），不在这条链内。
- 每块本地盘的 `NameSearch` 用管道限额 `L`：保留 `min(max(L,8)*8, 全部行)` 条候选，模式查询再按 `keep*5`（即 `L*40`）扫描（`NameSearch.cs:42-46`），最后发出上限仍是 `L` 条（`:80`）。默认查询的 51 经这条链变成每盘扫描上限 8160；1000 变成 160000。
- 多盘协调器的全局 `Take` 用不带优先级/历史的 `SearchResultRankComparer.Instance`（`SearchCoordinator.cs:68`），所以优先级始终在引擎截断之后才参与。

App 侧（`SearchResultMapper.BuildQuickResults`，`SearchResultMapper.cs:298-339`）：

- 快速窗口的插件即时结果与搜索动作行先作为固定前缀放进列表，不占下面的候选名额（`SearchResultMapper.cs:95-98`）。
- 候选池不只是索引行：历史、Windows Recent、收藏、可搜项也进同一个 `RankedCandidateOrdering` 列表（`SearchResultMapper.cs:180-290`），按同一把梯子竞争下面的 50 个名额，来源本身不保留名额。
- `skipDisplayCap` 只在 token、文件过滤范围、filename-like 查询时为真（`SearchDispatchController.cs:204`）。为真时 mapper 不截断；否则，若「即时行 + 候选行」少于 10，全部显示；否则候选行最多显示 50 条，即时行不占这 50 个名额。候选超过 50 条时追加一行「N more」动作行（`SearchResultHelper.AddShowMoreResult`），点击进完整窗口。
- token 模式虽然跳过 mapper 的截断，文件行在截断前还会经过 `QueryTokenDispatcher`（可过滤/重排），随后 `QueryTokenResultComposer.Compose` 按 `DisplayLimit = 50` 再截一次，即时行与文件行共用这 50 个名额（`QueryTokenResultComposer.cs:6,21-32`；超过 50 时同样追加 N more 行）。文件过滤范围与 filename-like 没有这道后置截断：文件过滤范围对每个配置文件夹各发一次带 4000 上限的搜索，filename-like 显示引擎与历史/收藏/可搜项合并后的全部候选。
- 内联窗口的文件夹专用范围（`folderScope`）只加宽预算；没有 token/文件过滤范围/filename-like 时仍按 50 条截。当前文件夹段另有 50 条上限（`ExplorerSearchHelper.CreateLocalSnapshot` 的 `limit = 50`），全局段来自同一个快速窗口结果。
- 完整窗口对索引行没有 App 侧显示上限；索引侧也不截断（`int.MaxValue`）。内容 provider 的 `FullSearchFileRowLimit = 2000` 与首批 60 条是另一套预算（`SearchQueryDispatchController.cs:69-70`），不属于索引排序。

尺寸速记：默认 App 51 → 管道 204 → 每盘扫描上限 8160 → 显示 50；token/文件过滤范围/filename-like 1000 → 4000 → 显示 50（token）或不截（文件过滤范围/filename-like）；内联文件夹范围与「有高档规则」的普通查询也走 1000 → 4000，但显示仍按 50 条截。

## 学习型历史别名（“屏幕截图”→ FSCapture.exe 案）

机制。`SearchHistoryStore` 把“搜索框文字 → 打开过的路径”存成键值对；一个路径同一时刻只属于一个 keyword，最近一次从哪个查询打开它就归到哪个 keyword（`SearchResultHelper.HistoryKeywordOf` 记录 `result.SearchQuery`，插件行是剥掉触发词后的文字）。`HistorySearchCandidateMapper` 用 `fuzzy.BestMatch(entry.Keyword)` 拿历史 keyword 匹配查询，不要求文件路径或文件名包含查询文字，命中后用 `entry.Path` 造行，造行前只检查路径是否存在。用户在“屏幕截图”这次查询里打开过 FSCapture，历史就永久建立了“屏幕截图 → FSCapture.exe”的关联；之后每次输入“屏幕截图”，这一行都被加回快速窗口，再打开它又以同一 keyword 写回历史，形成自我维持。完整窗口只把历史当排序优先级，不会加行。

这解释三个现象：只有快速窗口能搜到（完整窗口的结果域是索引返回的行）；路径/文件名都不含“屏幕截图”（匹配的是历史 keyword）；文件夹改名后快速窗口也搜不到（路径失效，存在性检查丢弃）。最小复现（`HistorySearchCandidateMapper.Collect`）：entry = `("屏幕截图", "D:\tools\smalltools\FastStone Capture\FSCapture.exe")`，fileExists=true 时 rows=1、name=FSCapture.exe、keywordMatchStart=0，fileExists=false 时 rows=0。与“改名后搜不到”完全一致。

修复前的排序问题在于这行的 `Match` 是 keyword 的匹配秩而非文件名的匹配秩：keyword 与查询完全相同，得到 `Start=0, Shape=WholeWord, Contiguity=1, Weight=1, IsCurated=true, Priority=-50`，同时满足左端连续、最高 contiguity 和 curated，排在最前面，看起来就是“文件名完全不相关，却排在很前”。修复后历史新增行标记 `IsHistoryOnly`、按 `NoMatch` 参与排序，不再因为 keyword 完美匹配冒充文件名匹配。

同现象的其他来源，按可能性排序：Windows Recent（`WindowsRecentCandidateMapper` 匹配 Recent 快捷方式的文件名 `TargetName`，命中后用快捷方式的目标路径造行；若 `%APPDATA%\Microsoft\Windows\Recent` 里存在名为 `屏幕截图.lnk`、目标为 FSCapture.exe 的快捷方式，会出现同样的现象，但优先怀疑历史）。可搜索项的可能性较低：开始菜单项的 `Title` 是快捷方式名，但 `FullPath` 会是 `.lnk` 路径而不是用户看到的 exe 路径（`SearchableItemMapper.BuildCandidate` 用 `ActionArgument`）。插件即时结果（尤其 FlowLauncherBridge）可以返回任意路径；当前 Flow 的普通查询只分发给全局插件，且 manifest 里没有截图类插件，不能完全排除，需看结果行的来源标识或禁用插件验证。

别名与收藏夹与此无关，依据以下机制事实：`PinyinAliasProvider.GetAliases` 当前只输出“繁体名的简体写法”一种别名，没有按文件/按路径的用户别名存储；拼音层的 `CanHandle` 要求候选文本里至少有一个汉字，`FSCapture.exe` 是纯 ASCII，不进入拼音层，所以“屏幕截图”与 FSCapture 之间不存在文本/拼音匹配。真实匹配 API 实测：query=屏幕截图 vs `FSCapture.exe` 与 `FastStone Capture` 均 IsMatch=False、Rank=False；vs `屏幕截图.png`、`屏幕截图滚动...txt` 均 Rank=True、tier=0。文件索引按名字及名字的别名匹配，没有把 PE `FileDescription` 作为可搜索字段（PE 预览只读版本信息用于显示）。精确模式（`EnableFuzzyMatch=false`）下二者没有任何字面关系。收藏夹加行只存在于快速窗口；收藏夹若有同名条目也会造成类似现象。

用户侧定位步骤：

1. 关闭 Lertaro，检查用户数据目录的 `search-history.json`：安装版在 `%LOCALAPPDATA%\Lertaro\search-history.json`，便携版在 `<程序目录>\Data\Users\<用户哈希>\search-history.json`。搜索 `"屏幕截图"`，看对应 `path` 是否为 FSCapture.exe。若是，即为根因；在“设置 → 历史 → 搜索历史”删除该条，或关程序后编辑 JSON。
2. 历史里没有的话，检查 `%APPDATA%\Microsoft\Windows\Recent` 下是否有名为 `屏幕截图.lnk`、目标为 FSCapture.exe 的快捷方式。
3. 两者都没有，再排查插件即时结果：临时禁用 FlowLauncherBridge/第三方插件，或看结果行的来源标识。

用户感觉到的“别名”是真实存在的，藏在 `search-history.json`，位于设置页的别名配置之外。修复后它仍是历史行：如果它是查询的唯一候选，仍会出现在列表里。

## IsMatch 与 Rank 的分裂（已修）

这是一个通用接缝问题，审查时点的反例：

```text
query="!zzz"   name="abc.txt"    IsMatch=True   Rank.IsMatch=False
query="!zzz"   name="防弹笔记法"  IsMatch=True   Rank.IsMatch=False
query="atlas !code" name="code-atlas.txt"
                               IsMatch=False  Rank.IsMatch=True
```

原因：`HighlightMask.ComputeRank` 只能给“被画出来的正词”算秩，纯排除查询没有正词、mask 为空，返回 `NoMatch`；反过来它只画正词、不做排除裁决，所以“正词命中但排除词也命中”的名字照样算得出秩。当时的影响：`SearchResultMapper` 对索引行用 `fuzzy.Rank(result.Name)`，`!` 查询走 `FallbackRank(path)`，把本可按名字排的行降成 `ShapeNoName`；`SearchableItemMapper.BestMatch` 先 `IsMatch` 再 `Hit`，纯排除查询的 app/settings 项被丢弃；`WindowsRecentCandidateMapper` 只用 `Rank`，`atlas !code` 会把被排除的 Recent 项加回来。

修法是把“是否命中”与“如何高亮/打分”彻底分开。已按 `9a6b9769` 核对落地：`FuzzyMatcher.ComputeMatchRank` 先做整查询 `IsMatch` 门（多词/排除/OR 时），纯 inverse 查询返回显式的 `MatchRank.NoPositiveTerms`（`Core/SearchIndex/FuzzyMatcher.cs:202`；该秩留在质量梯子底部，不能冒充名字匹配），两个窗口对它比较一致；`WindowsRecentCandidateMapper` 改用 `BestMatch`（`WindowsRecentCandidateMapper.cs:60`），`HistorySearchCandidateMapper` 同样用 `BestMatch`（`HistorySearchCandidateMapper.cs:47`）。

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
          └─ 排序：上面那把梯子
```

拼音层是逐查询、逐候选的实时 NFA（`PinyinTermMatcher`），不烘焙读音；`HighlightMask` 负责最终高亮和 `MatchRank`。这部分没有“索引/查询规则两套实现”级别的分裂。

审查时点的解析缺陷（只认 ASCII 空格：`fast\tstone`、U+3000 不切分；全角字母/数字不折叠；组合字符不做 NFC，来自 macOS/压缩包的 NFD 文件名“看起来一样但搜不到”）均已修复：分词改为 `Split((char[]?)null)` 覆盖所有 Unicode 空白，转义空格用 `\u0001` 占位，避免全空白切分把转义空格拆开；查询与候选名字统一做全角 ASCII→半角、U+3000→空格并做 NFC（`SearchTextNormalizer`，通用 NFKC 不做），显示保留原文，`NormalizeInto` 返回位置映射，`HighlightMask` 用它把 mask/Start 映射回原文；查询解析、索引候选、LiveDirectory、PathTermFallback、PathGate、App 匹配边界全部接入。`SnapshotFormat.Version` 从 13 升到 14：`UniqueMasks` 增加折叠拼写，否则半角查询会被旧 prefilter 拒掉，旧索引按既有 stale-snapshot 路径重建。

拼音正例（HEAD + 真实 provider 实测，均精确模式除注明外）：

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

拼音的“单词内必须连续”语义被现有测试钉住：`fdbjf` 不会跨 `防-弹` 或 `防 弹` 里的分隔符；`fdbjfgxiao`（跨词）不命中。这是 WinShun 的既定语义，用户可能把“简拼跨空格”当成 bug，建议在设置/文档里写清。审查时点真实 provider 的拼音测试（`Tests/Plugins/PinyinAlias`，15 个测试类，含 DataRow 共 241 个用例）241/241 通过，覆盖全拼/简拼/混合块、大小写、数字、英文、符号、繁查简、排除词、高亮 mask、63 字符边界与真实索引烘焙；本次审查发现的问题集中在排序层和解析/边界。

## 边界与反例

- 长名：225 字符 ASCII 名中的 `atlas` 可以命中（Start=120，Shape=InsideWord，Weight≈0.02）；224 字符 CJK 名中的 `pingmujietu`/`pmjt` 可以命中。`PinyinTermMatcher.MaxText = 512` 是码点上限，匹配起点在 512 之后就不再看：实测 prefix=500 拼音 True，prefix=512 与 600 拼音 False（字面 屏幕截图 True）。文件名最多 255 字符，文件搜索不受影响；浏览器标签/插件标题这类可搜索项标题超过 512 码点时，拼音会静默失效。
- 拼音词长上限 63（64 位状态集）：62/63 字符可命中，64 不行；超长全拼查询只能走字面匹配。
- 单字母拼音词（`f`）被 `PinyinTermMatcher.IsEligible` 拒绝（要求 2..63），“输入一个首字母找中文名”不可用；这是 WinShun 的既定设计，对“简拼”用户是明显的手感差异。2-3 字母拼音词召回很大（如 `baj`），排序靠 tier/位置/覆盖率压低；现有测试把 `baj` 作为“半音节中名内命中”接受。
- 空格/英文分词：`fast stone`、`fast  stone`、`stone fast`、`f s c` 都按“空格分词 + 词序无关”工作。`fast stone` 能命中 `FastStone`（两个词分别落在 0 和 4），这是多词独立子串语义，连续短语必须加引号（`'fast stone'`）；引号把它变成一个含空格的词，只匹配真正含空格的文本。只有空格/空查询返回空结果。
- 引号与操作符：`fast\ stone` 把两个词合并成一个含空格的词（实测对 `fast stone notes` 为 true），这是有意的转义。未闭合的 `'` 被当作“精确性开关”，不是字面引号；`!`/`^`/`$` 开头没有通用转义；`$` 结尾永远是后缀锚点，文件真名以 `$` 结尾时只能用单独的 `$` 查。
- 排除：`!term` 只按名字字面裁决，不走拼音/别名/祖先目录（“名字级否决”的既定规则），所以 `!简单` 不能排除一个只有拼音别名含“简单”的名字。繁体查询的排除词不会转换（`GetQueryForms` 只用于正向词），`!創意` 不会排除简体名里的“创意”。
- 名字序比较：`SearchNameOrderComparer` 按数字段显著长度 + Ordinal 比较，不调用 `long.Parse`（修复前 20 位以上数字段会抛 `OverflowException`，实测 `"a12345678901234567890"` vs `"a2"` 抛出；`"a2"` vs `"a10"` 返回 -1）。规则为数字先于字母（大写先于小写）、符号最后，注释与实现已统一。
- 虚拟路径：`SearchResultHelper.NormalizePath` 对 `shell:` 与 `__SEARCHABLE_ITEM__:` 前缀有显式分支，不再走 `Path.GetFullPath`（修复前会把 app 的 `FullPath` 拼成当前工作目录下的相对路径；实测不崩，但去重键/路径比较变得不直观）。
- 完整窗口候选截断与最终键共用 `SearchResultRelevance.HighKey`，只改其一会让两个搜索阶段发展出不同的排序键语义（代码注释中的约束）。

## 有意保留的差异

- 非索引来源（历史、Recent、收藏、可搜索项、插件即时结果）只在快捷窗口加行；内容 provider 行只在完整窗口（provider 集合不同）。
- 快捷窗口的预算与显示上限：普通宽查询 App 要 51、管道 204、每盘扫描上限 8160，排序后显示 50 条候选行（即时行额外）；token/文件过滤范围/filename-like 要 1000、管道 4000；token 最终仍截 50 条，文件过滤范围与 filename-like 不截。更大的集合只有完整窗口能列全：它的索引侧没有截断，App 也不对索引行设显示上限（内容 provider 的 2000 条上限另算）。推导见上文「候选预算与显示截断」。
- 快捷窗口独有的收藏新增行与结果类型顺序是用户自己的动作/设置；列排序只属于完整窗口（快速窗口是列表视图，没有列头）。完整窗口没有收藏新增行，但索引行若本身就是收藏会得到同样的 favorite band。
- 只剥一次触发字符：连打两个触发字符时，“显示更多”交接的文本会被完整窗口再剥一次（边缘情形）。
- recency band 与 path depth 不参与排序；shape 位于 contiguity 之后、whole word 可压过更靠左的 word start，左端连续只在 shape 相同时生效（owner 选择的 A 方案）。
- 通用 NFKC 不做，只做全角 ASCII→半角 + U+3000→空格 + NFC；单字母拼音、64 字母拼音、512 码点上限是既有限制。

## 坑与教训

以下每条是审查时点（修复前）缺陷的结论，编号 F1-F8 保留用于对应回归测试。

- F1 位布局与契约不符：`HighKey` 的 start 只占 6 位（`start << 16`，bit16..21），注释写 `start(7)`、`MatchRank.StartCap=127`，实际位置 63 之后的所有命中在完整窗口完全并列（实测 start=63/64/100/127 得到同一个 0x003F0000）。现有测试 `KeyLayout_EveryOtherFieldSaturates` 只比较 127 与 400（都夹成 63）所以通过，掩盖了 bug；改布局必须补 63 vs 64 的对比用例。
- F2 默认值吞掉分数：`NoNameFallback` 的 `Contiguity=0` 使 coverage 点永远按最差算（`coveragePoint = WeightMax`），`pathWeight` 完全不参与键值（实测 pathWeight=0.200 与 0.238 得到同一个 key 0xFFFF7FE4），祖先目录补全的行只能按路径长度/路径排。现在 `MatchRank` 有显式 `Coverage` 字段。
- F4/F6 手工构造的 `new MatchRank(tier, start, weight)` 同样因默认 Contiguity=0 拿不到 coverage 分，受影响的不止测试：`SearchableItemMapper.MatchCatalogEntry` 的 keyword-only fallback 也曾如此。
- F3 两窗键序相反：完整窗口把 history priority 放在 `RankSortKey` 之前，快速窗口放在质量键之后；同一批索引行同一查询下，历史里名字匹配很差的行为排在完美 `atlas` 前面（完整窗口）或后面（快速窗口）。`RankedCandidateOrdering` 的注释当时写 "history and favorites sit AFTER the match rule"，完整窗口的实际行为相反。一次误开让不相关文件永远排在精确命中前面，是 FSCapture 工单的放大机制。
- F4 start 权重两窗不同：完整窗口在 contiguity 后比较 start，快速窗口只给 `Start==0 && Contiguity>=1` 一个布尔优先级。同长度同覆盖同 contiguity 时（`zatlaszzzzz` vs `axxxxxatlas`），两窗给出相反顺序，且与历史、拼音无关。
- F5 排序键“删而不净”：shape/recency/depth 不再参与排序后，字段生产者、注释、测试、提交信息还在承诺它们（`HighKey` 里 shape/recency/folders/weightPoint/lengthPoint 全部未使用；`MatchedLength` 实测恒为 0；`IsExactName` 的参数从未被用），后续任何人无法从代码判断到底按什么排。`Shape` 随后按 owner 要求重新实现，recency/depth 保持删除。
- 12 条红是证据不是噪声：修复前 192 个搜索相关用例中 12 条失败，全部集中在 `SearchResultRelevanceTests` 与 `NameSearchTests`；其中 `KeyLayout_WeightOutranksTier` 是 F2/F6 的直接证据，其余多数是“行为改了但测试没改”。在决定保留哪套梯子之前，这些用例不能简单删掉或改断言。
- 键位置决定设置可达性：类型顺序放在六项质量键之后，不同类型的行几乎不可能在质量键上打平，设置页的“结果类型优先级”实际失效；优先档若放在类型分组之内，高优先级文件仍爬不到应用组上面。给用户可见设置排键位时，用“这个键什么时候轮得到出手”验证。
- 行来源混进打分：历史行拿 keyword 的匹配秩当文件名的匹配秩参与排序；造行的行必须携带来源标记（`IsHistoryOnly`），梯子按 `NoMatch` 消费它。
- 同一路径的两种表示不该由偏好键裁决：`.lnk` 的索引 File 行与 provider 的 Application 行文本键全等，去重后谁活下来由类型顺序决定；Files 配在 Applications 前面时，开始菜单/桌面应用全部显示成“带 `.lnk` 后缀 + 文件图标”（实测 `Order([file(rank 0), app(rank 1)])` 只返回 File 行）。只有应用行能启动程序，所以应用行独占其路径（`ApplicationOwned`），类型偏好决定“先看哪一类”，不决定“同一个路径被表示成什么”。历史/收藏不受影响：可搜索条目自己会按 lookupPath 拿 history band/priority，被丢掉的文件历史行本来就是同一路径的重复。
- 统一排序不解决“候选有没有被取到”：默认 51 的 App 限额（管道 204）让排在后面的同名/近名文件根本到不了统一排序器，工单里那种带点名的查询（如 `plan.md`）表现为“只有一条”。修复：filename-like 查询用 1000 预算（管道 4000）并跳过显示上限；token 查询也加宽到 1000，但显示仍由 composer 截到 50。
- `IsMatch` 与 `Rank` 是两个判断：只用 `Rank` 的候选源会绕过匹配门（`atlas !code` 把被排除的 Recent 项加回来），只用 `IsMatch` 又打不出秩。加行来源必须走 `BestMatch`；“没有正词”要有一个显式的秩，借用 `NoMatch` 会把本可展示的行整个丢掉。
- 全角残留（U+3000、全角字母数字、NFD 组合形式）是中文输入法下“明明打了空格/看起来一样却搜不到”的主因；折叠必须同时接入查询解析、索引候选与高亮边界（位置映射回原文），并升快照版本号让旧 prefilter 重建。
- 排序比较器里做整数解析：`long.Parse` 在 `OrderBy` 内被直接调用且没有 try/catch，20 位数字段（时间戳/哈希/序号）抛 `OverflowException` 会把整次快速窗口排序打断；按数字段长度加 Ordinal 比较即可，还顺带避开 `char.IsDigit` 接受非 ASCII 数字时的 culture 差异。
- 注释自相矛盾会指导错误维护：`NameOrderComparer` 一处注释写“letters before digits”、另一处写“digits first”，实现是数字在前；此类注释必须与实现统一。
- 测试用例走真服务会变成环境依赖：`EverythingSearchDataProviderTests.QueryFolderSubtree_UnindexedFolder_ReturnsEmptyResult` 本机服务没在跑时抛 `TimeoutException`（消息 "The operation has timed out."），被层层包成 `IOException`，“服务没启动”和“文件夹没索引”对调用者同形。改法按 `DEVELOPMENT_GUIDE.md`（不往生产类塞测试专用构造）把子树应答抽成纯函数 `EverythingSearchDataProvider.AnswerFolderSubtreeSize`，查询串路由仍由 `EverythingQueryParserTests` 覆盖。
- 单测通过数要注明口径：`ExplorerStaInvokerTests` 单独仍出现 4 条与真实 Explorer/桌面 STA 环境相关的红，不是排序回归。

## 仍然待办

- 历史行如果它是查询的唯一候选，仍会出现在列表里；要彻底消除某条误关联，需要删除该条历史或给“弱历史不造行”另定规则（2026-10-19 修复时未定）。
- 拼音上限（单字母、64 字母、512 码点）与“简拼不跨空格”的语义：写进用户文档/设置提示，或做多词拆分。是否已写入用户文档未核对（截至 9a6b9769）。
- 通用 NFKC 不做，只做全角 ASCII→半角 + U+3000→空格 + NFC，其余宽度形式仍是字面差异（有意保留）。
- FSCapture 类现象的最终归属（历史 vs Recent vs 插件）仍需在用户环境按定位步骤确认；审查没有读取用户机器上的 `search-history.json` 或 Recent 目录。

## 怎么验证

构建与测试（仓库根 `<repo>`）：

```text
dotnet build Tests/Core/Core.csproj -m:1 -nodeReuse:false
dotnet build Tests/Plugins/PinyinAlias/PinyinAlias.csproj -m:1 -nodeReuse:false
```

审查沙箱对 `Core/obj` 等子目录写入被拒、`dotnet test` 的 testhost 在 `Process.EnsureWatchingForExit` 处报 `Win32Exception (5)`，所以当时把 Core、PluginSdk、Tests/Core、Plugins/PinyinAlias、Tests/Plugins/PinyinAlias 复制到可写临时目录构建，并加载 `Lertaro.Core.Tests.dll` / `Lertaro.Plugins.PinyinAlias.Tests.dll` 用反射直接调用 `[TestMethod]`/`[DataRow]`，手动执行 `[ClassInitialize]`/`[TestInitialize]`/`[TestCleanup]`/`[ClassCleanup]`（否则 `PluginSettingsService.GetSettingFunc` 等静态状态会串味，传统字测试假红）；`Assert` 仍是 MSTest 的真实断言。两窗排序反例、`HighKey` 位布局、`NoNameFallback`、`IsMatch` vs `Rank`、长数字比较器均由 HEAD 同源构建直接验证。

留档数字：

- 审查时点：搜索相关 15 个类、含 DataRow 共 192 个用例，pass=180 fail=12；拼音插件 15 个类 241 个用例 241/241。
- 2026-10-19 排序收口后：Core 2075/2075，App 2165/2165，PinyinAlias 插件 238/238；产品解决方案与插件解决方案构建通过。
- 结果类型最外层分组后：App 2168/2168，构建 0 警告 0 错误。
- 应用行独占路径后：App 2170/2170。
- 其后索引规则等提交的测试总数未复测（截至 9a6b9769）；审查只选取了与搜索/拼音/排序直接相关的类，其余类的红/绿未证明。

排序相关的测试类：`MatchRankTests`（五字段质量梯子、shape 层级、NoNameFallback 的 coverage）、`MatchShapeTests`（英文/中文 whole/word-start/inside）、`SearchResultRelevanceTests`（7 位 start、contiguity/shape/start/coverage/tier 顺序）、`SearchResultRankComparerTests`（5 次 history band、history 不压匹配键）、`RankAndDedupeTests`（history-only 不伪装成名字命中）、`SearchTextNormalizerTests`（全角/空白/NFC/高亮位置）、`SearchNameOrderComparerTests`（20 位数字不溢出）、`NameSearchTests`（全角名、whole word/word start、覆盖率）、`HistorySearchCandidateMapperTests` / `StreamingResultAccumulatorTests`（history band 与 seed）。类型分组与应用排他测试在 `Tests/App/ViewModels/Search/Mapping/SearchResultMapperTests.cs`：`RankAndDedupe_TypeGroupBeatsABetterMatch`、`RankAndDedupe_InsideOneTypeTheEngineQualityDecides`、`RankAndDedupe_TypeGroupBeatsAFavoriteOfALaterType`、`RankAndDedupe_ApplicationRowOwnsItsShortcutPath_EvenWhenFilesRankFirst`、`RankAndDedupe_FileRowSurvivesWhenNoApplicationRowClaimsThePath`，另有 `Collect_CarriesTheCallersFilesTypeRank`（`HistorySearchCandidateMapperTests`）。原 `RankAndDedupe_TypeRankAfterHistory` 断言的是“类型在 history 之后”，与新语义冲突，已删除。

## 关键源码位置

| 主题 | 位置 |
| --- | --- |
| 查询分词/操作符 | `Core/SearchIndex/Fzf/FzfPatternParser.cs` |
| 路径模式/盘符 | `Core/SearchIndex/Query/SearchQueryParser.cs` |
| 文本折叠（全角/空白/NFC） | `Core/SearchIndex/SearchTextNormalizer.cs` |
| MatchRank 契约与质量比较 | `Core/SearchIndex/MatchRank.cs`、`Core/SearchIndex/MatchRankQualityComparer.cs` |
| 完整窗口排序键打包 | `Core/IndexV2/Search/SearchResultRelevance.cs` |
| 完整窗口比较器 | `Core/SearchResult.cs` |
| 快速窗口梯子 | `App/ViewModels/Search/Mapping/RankedCandidateOrdering.cs` |
| 优先档/排除规则裁决 | `Core/ExclusionRuleSet.cs`、`Core/Settings/IndexRuleSetting.cs` |
| 历史加行 | `App/ViewModels/Search/Mapping/HistorySearchCandidateMapper.cs` |
| 历史持久化与打开次数 | `Core/SearchHistoryStore.cs` |
| Recent 加行 | `App/ViewModels/Search/Mapping/WindowsRecentCandidateMapper.cs` |
| 候选预算与显示上限 | `App/ViewModels/Search/Dispatch/SearchDispatchController.cs`、`App/ViewModels/Search/SearchViewModel.cs` |
| 名字序比较 | `Core/SearchIndex/SearchNameOrderComparer.cs` |
| 拼音 NFA | `Plugins/PinyinAlias/PinyinTermMatcher.cs` |
| 拼音评分/高亮 | `Core/IndexV2/Search/PinyinNameEvaluator.cs`、`Core/SearchIndex/HighlightMask.cs` |
