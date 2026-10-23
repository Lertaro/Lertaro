# 直觉排序实施计划

核对基线：`ff0608a`，2026-10-23。现行行为见 [search-ranking.md](search-ranking.md)。本文件只维护尚在执行的目标、阶段和验收门槛。

## 状态

| 阶段 | 状态 | 行为变化 |
| --- | --- | --- |
| P0 工单 fixture | 完成 | 无 |
| P1 端到端 ranking trace | 完成；待 Windows/.NET 环境跑 MSTest | 无 |
| P2 离线回放器 | 完成；待 Windows/.NET 环境跑 MSTest | 无 |
| P3 shadow evidence / identity | 完成；待 Windows/.NET 环境跑 MSTest | 无 |
| P4 拼音空格等价 | 完成；待 Windows/.NET 环境跑 MSTest 与同机性能测试 | 受限 joined 拼音分支 |
| P5–P10 | 未开始 | 逐阶段说明 |

P0 fixture：`Tests/Core/SearchRanking/Fixtures/reported-search-cases.v1.json`。P1 使用 ranking-event schema 3；旧 schema 行缺失的字段读作 Unknown/null。P4 后只有胜出的 joined 拼音分支具有结构化 mode/provider/span；其余无法证明的 matcher 分支仍保持 `LegacyUnknown`，不得反推或伪造。

## 目标与约束

排序目标不是提高某个经验分，而是满足可测试的单调约束：

1. **文本证据优先**：完整直接命中不得被普通类型偏好、High/Normal 或打开次数越过。
2. **显式操作单独处理**：类型触发字符是过滤；AlwaysTop 是 pin；Low 是降级。三者不等价于普通偏好。
3. **行为不伪造证据**：历史只细排直接命中。只有 query-path 关系的行必须标为 learned alias。
4. **实体只保留一个可执行表示**：File/Application/History/Favorite 合并后保留正确执行身份及全部信号。
5. **拼音空格近似不变**：纯拼音词边界加空格或去空格时，目标都能召回、证据等级相同，受控集名次差不超过 1。
6. **召回与排序分开**：期望项不在排序前候选集时，修 provider/index/budget，不调终局权重。
7. **两窗共享证据**：移除快速窗口独有偏好后，同一候选的证据与两两顺序一致。
8. **证据可展示**：普通文本、拼音和 alias 召回使用同一个已验收 witness 完成判定、高亮和日志。

当前 `S = 10C + 8P` 本身是一张可解释的离散查表：它明确让 Normal+强（18）高于 High+弱（16）。问题不在“线性”二字，而在把类型序数、二值文本等级、规则档和有偏点击继续放入同一可加尺度。下一版先建立证据等级，再在等级内校准偏好。

## 已知案例

| Case | 查询/条件 | 契约 |
| --- | --- | --- |
| `437-cfwj-order` | `cfwj`；“重复文件”快捷方式与偶合项 | 目标 evidence 更强，必须在偶合项前；记录 App Recall@10 |
| `437-db-intent` | `db`；目标为 Telegram/电报/TG | 先判断目标是否进入候选；可信 alias 可提高 Recall，不能扩散全局 fuzzy |
| `437-fscapture-source` | 原始查询未转录；历史已清 | 未有 trace 前不归因；无 witness 时必须 absent |
| `437-pinyin-space` | `sousuojieguo` / `sousuo jieguo` | 都召回、同 Grade、rank delta ≤ 1、高亮一致 |
| `437-shortcut-low` | shortcut 位于工具目录 | stock 规则不得误降级；用户显式 Low 继续生效 |
| `440-zbl-alias-scatter` | 精确 `zbl` 与长 EPUB 名 | 必须 absent；紧凑 alias 正例保留；matcher 与 painter 共用 witness |

## 目标模型

### 数据

```text
MatchEvidence
  Field          DisplayTitle | FileName | CuratedAlias | UserAlias |
                 ShortcutTarget | Keyword | ParentPath | HistoryKeyword
  Mode           Literal | PinyinInitials | PinyinFull | PinyinRare |
                 Fuzzy | LearnedAlias
  QueryVariant   Original | PinyinJoined | ProviderAlias | HistoryExact
  AllPositiveTerms
  DirectText
  Witness        provider id + accepted branch + spans/positions
  MatchRank
  Grade          0..4

CandidateIdentity
  Source         FileIndex | SearchableProvider | Favorite | History | Recent | Plugin
  KindFlags      File | Directory | Application | Shortcut
  SearchTitle
  PathKey
```

`ResultKind` 继续服务 UI，但不再同时表示执行方式、类型、来源和命中字段。

### EvidenceGrade

数字越小越好：

| Grade | 定义 | 例子 |
| --- | --- | --- |
| 0 ExactIdentity | 规范化查询等于标题/文件茎；或常用 joined 拼音从 0 覆盖完整词干 | `sousuo jieguo` → `搜索结果.lnk` |
| 1 DirectStrong | 全部正词在显示标题/文件名形成连续的 whole/word-start/inside 命中 | `jieguo` → `搜索结果` |
| 2 DirectApproximate | 名字上全部词成立，但为散点、低覆盖、模糊或生僻读音 | 长名弱散点 |
| 3 SecondaryField | 可信 alias、target stem、provider keyword 或父路径解释全部词 | `电报` → Telegram |
| 4 LearnedAlias | 只有规范化 query-path 历史关联 | 用户学出的工具别名 |

无法解释全部正词的行不进入普通竞争。纯排除查询沿用专门规则，不借 Grade 0。

### 目标键

```text
Section
  0 ExplicitPinnedText
  1 TextualHighNormal      // Grade 0..3
  2 TextualLow             // Grade 0..3
  3 LearnedAliasOnly      // Grade 4；首屏默认最多一条

段内：
  EvidenceGrade ↑
  → PreferenceEligible ? TypeRank ↑ : neutral
  → High before Normal
  → DirectHistoryTier / frecency ↓
  → MatchRank quality
  → stable common tail
```

约束：

- TypeRank 是 ordinal key，不再映射为 `16/8/4/1`。
- 初版只允许 Grade 0–1 使用类型偏好；Grade 2 是否允许由黄金集决定。
- H5/H3 只在同 Grade 内细排，不生成跨 Grade 的 34/22 分。
- learned alias 独立在最后；没有直接结果时仍可成为首项。
- AlwaysTop 保留显式越级；Low 保留显式降级。
- 强收藏可 pin，弱收藏按自己的 Grade 排序。

## 观测与评测

### 指标

| 层 | 指标 |
| --- | --- |
| 召回 | expected Recall@20/@50、各 source recall、零结果率 |
| 排序 | MRR、Success@1/@3、nDCG@10、pairwise inversion |
| 稳定性 | 拼音变体 rank delta、Jaccard@20、Kendall tau |
| 专项 | app Recall@10、cold-start Alias Recall@10、alias false-positive、weak-over-strong inversion、history-only top-3 |
| 性能 | 按键到首屏 p50/p95、候选扫描 p95、排序分配 |

点击受既有曝光位置影响，只做线上护栏。主判断来自脱敏黄金集与成对偏好；样本不足时报告“无结论”，不比较两个时期的裸点击率。

每个 fixture 至少包含：

```text
caseId, window, rawQuery, canonicalExpectation
fuzzyEnabled, typeOrder, indexRules, historySnapshot, providerReadiness
candidate source/path token/title token/expected grade
mustAppearBefore / mustBeTopK / mustBeAbsent
```

禁止提交用户真实路径、用户名或原始日志。

## 分阶段实施

每阶段单独提交、测试和评审，不提前接下一阶段。

### P0：冻结工单证据 — 完成

产物：`reported-search-cases.v1.json`。fixture 使用合成路径和 token，保留查询、候选关系、环境前提及断言。P2 才接 loader。

### P1：端到端 ranking trace — 完成

现行产物：

- 每个快速查询一个 `searchSessionId/querySeq`，每个组合页一个 `pageId`；
- UI 行携带 `pageId + finalDisplayedRank`，open 直接使用该 impression；
- 分开记录 engine/filter/merge/prefix/type-coverage/token/final-cap；
- 来源 flags、drop stage、provider readiness 与原始 provider hit 数；
- invariant key/value formatter 和“复制排序解释”；
- schema 3 writer、兼容 reader、10 MiB × 3、30 天保留、5 秒邻接页折叠、fail-open。

P1 只增加观测，不改变候选、排序、截断或打开。已完成 C# 语法树、翻译 JSON、隐私字段和 diff 检查；当前环境无 .NET SDK，MSTest 尚未运行。

### P2：候选录制与离线回放器 — 完成

`Tests/Core/SearchRanking/` 包含脱敏 `RankingCase`/`CandidateSnapshot`、P0 loader、V2 与后续策略共用的回放接口，以及 Recall@K、MRR、nDCG、pairwise inversion 和 target rank delta。受控 fixture 覆盖普通 `.lnk`、AppData 下的 PixPin 快捷方式、direct history 和 learned alias；路径均为 `X:\Fixture\...`。

V2 基线见 `Tests/Core/SearchRanking/Baselines/v2-baseline.md`。PixPin 当前排为 folder → exe → shortcut：AppData 的默认 Low 规则先把快捷方式放进 Low 段，类型 bonus 无法跨段。指标用手算单相关项验证，并固定枚举顺序无关、比较器反对称和传递测试。当前环境无 .NET SDK，MSTest 尚未运行。

退出：后续阶段必须对同一候选集同时报告 V2 与候选策略，不能用权重修改掩盖召回缺失。

### P3：影子 MatchEvidence 与 CandidateIdentity — 完成

`SearchRankingEvidence.cs` 定义字段、模式、query variant、Grade、kind flags 和候选身份。快速窗口在实际命中的 title、filename、provider alias、keyword 或 history keyword 上记录 Field；无法区分 alias 与 parent-path 的引擎 fallback 保持 Unknown。Grade 只由已知表示及既有 `MatchRank` 推导，V2 比较器不读取影子字段。同路径合并保留执行行、来源并集和最强 evidence，诊断 fingerprint 覆盖新增字段。

P3 提交时 matcher 只返回 `MatchRank`，所以普通文本、拼音和 provider alias 的 accepted branch、provider id 与 spans 当时都保持 `LegacyUnknown/-1`；history-only 行因来源明确可记为 `LearnedAlias`。P4 只补上可证明的 joined 拼音 witness，其余分支仍不得从 tier 或高亮结果反推。

退出：schema 3、reader 和复制解释能显示已知 Field/Grade/Kind，旧行读作 Unknown；影子字段差异不改变 V2 顺序。

### P4：拼音空格等价 — 完成

`PinyinNameEvaluator` 只为至少两个普通正词建立 joined 分支：查询不得含 drive、OR、排除或显式匹配操作符；每词必须是 ASCII 字母；拼接长度为 2–63；同一个 live provider 必须能处理每个原词和拼接词。跨 provider 不合成。

original 多词与 joined 单词在同一 scorer 中竞争。有字面词时，joined 仍须原始分更高；全读音多词的逐词位置 bonus 会随空格数重复，不能与单词分直接比较，此时只在 joined 起点不更晚且源覆盖不更小时采用 canonical joined 分。否则保留 original。多 provider 同分再按 span/tier/provider 稳定决胜。胜出 witness 含 provider、tier 和源 span，`HighlightMask` 直接涂该 span，`FuzzyQuery` 同时写出 `PinyinJoined`、实际拼音 mode 和 span。比较器、候选集和非 joined evidence 不读这些字段。

Core fake-provider 测试覆盖资格、strict winner、跨 provider、枚举顺序、操作符、非 ASCII 与 64 字符；真实 provider 的 snapshot/delta 测试覆盖 `sousuojieguo`、`sousuo jieguo`、`sou suo jie guo` 的召回、Grade、`MatchRank`、高亮和受控 target rank delta，并在 fuzzy 开关两态核对反序、`visual studio`、`fast stone` 与含 `-` 的候选。普通多词仍走原分支，单 token 不跨候选分隔符。

当前环境无 .NET SDK，尚未运行 Core/PinyinAlias/App MSTest，也无法记录 Windows 同机 p95；合入门槛仍是 p95 不超过基线 110%。

### P5：应用与快捷方式身份

1. 用 `KindFlags` 表示 File/Shortcut/Application；`.exe` 不自动升级。
2. shortcut 的 `SearchTitle` 去已知扩展名，执行路径不变。
3. provider `Aliases/Keywords` 成为带 provenance 的 Grade 3 evidence。
4. 开始菜单 provider 在加载时缓存受控 alias：标题、target exe stem、package/product metadata；按键热路径不做 COM 或文件解析。
5. 新 target/user alias 在 V3 切换前只进 shadow 候选；现有 provider keyword 保持 V2 行为。
6. 合并行保留 Application 执行委托、File 规则/质量、收藏和历史。
7. provider readiness 变化应重跑当前查询。
8. 只有 trace 证明 recall 失败时才把 source top-K 交给 P9。
9. 若可信 metadata 仍不能召回 `db` 目标，再提供单结果本地用户别名；不引入全局同义词库。

退出：身份与标题 case 通过；可信 alias 的 shadow Recall 达标；无关 `.exe` 不升级。

### P6：V3 影子排序

1. 固化 Grade 规则与属性测试。
2. 建立独立 V3 key，保留 V2 生产路径。
3. 日志开启时仅用 P3 预计算字段计算 rank delta；不做第二次匹配。
4. 比较 V2/V3：weak-over-strong inversion 为 0；P0 正例通过；#440 继续 absent；旧集 nDCG 不下降。
5. 验证全序、路径去重、稳定尾和两窗一致性。
6. 记录每个争议边界及人工决定。

退出：影子门槛与性能通过，UI 仍使用 V2。

### P7：切换到 V3

1. bump `RankPolicy.Version`。
2. 生产路径移除 `16/8/4/1`，TypeRank 只在可偏好 Grade 内比较。
3. High/Normal 不跨 Grade；Low、AlwaysTop 保持显式段。
4. direct H5/H3 只做同 Grade tie-break。
5. learned alias 进入 Grade 4；P8 前保守读取旧存储。
6. 启用 P5 的 secondary alias，并显示来源；先跑 alias false-positive 集。
7. 更新 7 种语言的类型顺序说明为“在相关性相近的结果中优先”。
8. 用 I1/I2 属性测试替换允许弱类型越过强命中的旧断言。
9. 逐入口接快速、完整和内联窗口。

退出：fixture、单元测试和人工 diff 审阅通过；普通偏好不跨 Grade。

### P8：历史去反馈回路

1. 将 direct frecency 与 query-path association 分开存储。
2. canonical query 复用 P4 的拼音等价资格，不无条件删除空格。
3. 旧 entry 幂等迁为低置信 pair；保留回读兼容。
4. learned alias 只做 canonical exact lookup，不做一般 fuzzy。
5. 区分 organic/direct 与 learned-injected 打开，采用不同增量并封顶。
6. 同一路径允许多个 query pair；learned alias 首屏默认最多一条。
7. 提供删除单条关联的 UI/命令。
8. 测试衰减、迁移、多对多、scope、Recent、拼音 canonical 和反馈循环。

退出：纯历史行不越过直接文本行；稳定的精确查询仍能召回用户学习项。

### P9：按测量修候选截断

仅当 P2/P5 证明目标不在 C0/C1 时执行，按成本排序：

1. 修 provider 根目录、readiness 或缓存失效；
2. 合并前为缺失 source 保留小额 top-K；
3. 对明确应用偏好增加 shortcut 专用二次检索；
4. 最后才考虑将 EvidenceGrade 带入引擎 top-N 或协议。

每项必须报告 source 扫描量、首行延迟和内存；不把历史/用户设置整体传入提权服务，不因一条 High 规则无条件扩大所有查询预算，并覆盖路径补搜、网络源和跨盘 Take。

退出：expected Recall@50 = 100%；p95 不超过基线 110%，否则需明确批准性能换正确性。

### P10：校准与收尾

1. 用黄金集确定 Grade 2 偏好边界和 learned alias 名额。
2. 样本足够后报告 Success@1/3、MRR、history-only top-3、app/Alias Recall、alias false-positive 与 p95；不足则写无结论。
3. V2/V3 用影子回放或 interleaving 比较，不比裸点击率。
4. 删除 V2 死代码、兼容 wrapper 和过时测试；生产只留一套策略。
5. 将 `search-ranking.md` 更新为 V3，并同步用户指南和 7 种语言文案。

退出：代码、测试、设置文案、站点与 dev-docs 描述一致。

## 依赖

```text
P0 → P1 → P2 → P3
                 ├→ P4 ─┐
                 └→ P5 ─┴→ P6 → P7 → P8
                                      └→ P9（仅由数据触发）
P7/P8/P9 → P10
```

P4 与 P5 可并行，但都依赖 P3，且不能提前切换排序。P9 可跳过。

## 发布门槛

功能：

- P0 的 `mustBeTopK/mustAppearBefore/mustBeAbsent` 全通过；
- 拼音空格 case 同 Grade，target rank delta ≤ 1；
- ordinary weak-over-strong inversion = 0；
- expected Recall@50 = 100%；
- 旧黄金集 nDCG@10 不下降，或逐 case 有人工决定。

性能：

- 快速窗口端到端 p95 ≤ V2 的 110%；
- 比较期无字符串解析、COM 或文件 I/O；
- provider alias 在加载期缓存；
- 日志关闭时新增观测分配接近 0；
- 扩大候选预算必须附扫描量和内存。

隐私：

- 默认日志无原始 query/title/path；
- fixture 无真实路径和用户名；
- 复制解释只由用户主动触发，并提示其中包含明文。

## 阶段任务模板

```text
只实施 Px，不提前做下一阶段。
开始前列出调用链、改动文件和保持不变的行为。
实现后：
1. 增加本阶段 MSTest；
2. 运行受影响的 Core/App/PinyinAlias 项目；
3. 报告命令、结果、性能或无法运行的原因；
4. 更新 Px 状态；
5. 检查 diff，不夹带无关重构。
若目标在更早候选层已丢失，停止调排序并标记 retrieval 问题。
```

评审只检查：问题层级、fixture 复现、Grade 单调性、热路径成本、诊断解释。

## 关键位置

| 主题 | 文件 |
| --- | --- |
| V2 内核 | `Core/SearchIndex/RankPolicy.cs` |
| 快速窗口 | `App/ViewModels/Search/Mapping/SearchResultMapper.cs`、`RankedCandidateOrdering.cs` |
| 类型与 shortcut | `App/ViewModels/Search/SearchResultTypePriority.cs` |
| 可搜索项 | `App/ViewModels/Search/Mapping/SearchableItemMapper.cs`、`App/ViewModels/Search/SearchableItemCache.cs` |
| 历史 | `Core/SearchHistoryStore.cs`、`App/ViewModels/Search/Mapping/HistorySearchCandidateMapper.cs` |
| alias gate | `Core/SearchIndex/Fzf/FzfPatternAliasMatchExtensions.cs`、`Core/SearchIndex/AliasMatchRules.cs` |
| 拼音 | `Core/IndexV2/Search/PinyinNameEvaluator.cs`、`Plugins/PinyinAlias/PinyinTermMatcher.cs` |
| 高亮 | `Core/SearchIndex/HighlightMask.cs` |
| 引擎截断 | `Core/SearchIndex/SearchCandidateComparer.cs` |
| schema 3 trace | `Core/SearchIndex/RankingEventLog.cs`、`RankingTraceModels.cs`、`App/ViewModels/Search/Mapping/QuickSearchRankingTrace.cs` |
