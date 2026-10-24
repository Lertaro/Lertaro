# 直觉排序实施计划

核对基线：`ff0608a`，2026-10-23。现行行为见 [search-ranking.md](search-ranking.md)。本文件只维护尚在执行的目标、阶段和验收门槛。

## 状态

| 阶段 | 状态 | 行为变化 |
| --- | --- | --- |
| P0 工单 fixture | 部分完成（5/6） | 无 |
| P1 端到端 ranking trace | 完成 | 无 |
| P2 离线回放器 | 完成 | 无 |
| P3 shadow evidence / identity | 完成 | 无 |
| P4 拼音空格等价 | 完成 | 受限 joined 拼音分支 |
| P5 应用与快捷方式身份 | 完成 | 同路径合并保留最强文本信号；新 alias 仅 shadow |
| P6 影子 V3 模型与回放门槛 | 完成 | `RankPolicyV3` 与 V2/V3 回放对照门槛 |
| P7 切换生产到单调 V3 | 完成 | `RankPolicy.Version = 3`；快速/完整/内联窗口统一单调分级；启用干净次要别名与来源副标题 |
| P8 历史频率与查询关联拆分 | 完成 | 拆分 `search-history.json` 与 `search-query-associations.json`；canonical 合并（ASCII 词无空格拼接）后 exact lookup；自然打开 ≥ 3、阻断自强化、旧计数钳制 ≤ 1、设置页独立管理 |
| P9 跨盘与快捷方式检索召回 | 完成 | 多网络索引有界全局排序截断；快捷方式补召回改为主查询之后、且仅在主结果没有 `*.lnk`/`*.url` 时才跑 |
| P10 文档同步与影子路径收口 | 进行中 | `RankPolicyV3` 委托至线上 `RankPolicy`；P7 之后恒等的影子排名通道已删除；dev-docs 与 7 语种用户文档同步 |

Windows/.NET 10 实测状态（2026-10-24）：移除排序日志、common-only 接受前两个读音并改为多词连续性后 Tests/Core 2265、Tests/App 2338、PinyinAlias 246、CoreExtensions 402、ContentSearch 173 通过；`dotnet build Lertaro.slnx` 0 警告 0 错误。P10 未做完的部分见文末“仍未闭合”。

P0 fixture：`Tests/Core/SearchRanking/Fixtures/reported-search-cases.v1.json`，P6 回放套件：`Tests/Core/SearchRanking/Fixtures/ranking-shadow-v3-cases.v1.json`（基线 `ranking-replay-cases.v1.json`，冻结的 V2 与离线策略在 `OfflineRankingPolicies.cs`）。当前 trace schema 4；导出文本只由本次搜索的内存 trace 生成，不读取旧文件。P7 后干净（`IsStrong`）的 provider 次要别名进入 `SecondaryField` 并展示来源副标题，零散子序列仍仅保留在 `ShadowAliasGate` 观测通道。P8 的 learned alias 只走 canonical exact lookup；完整窗口不设历史配额（产品决定，见文末）。

## 目标与约束

排序目标不是提高某个经验分，而是满足可测试的单调约束：

1. **文本证据优先**：完整直接命中不得被普通类型偏好、High/Normal 或打开次数越过。
2. **显式操作单独处理**：类型触发字符是过滤；AlwaysTop 是 pin；Low 是降级。三者不等价于普通偏好。
3. **行为不伪造证据**：历史只细排直接命中。只有 query-path 关系的行必须标为 learned alias。
4. **实体只保留一个可执行表示**：File/Application/History/Favorite 合并后保留正确执行身份及全部信号。
5. **拼音空格近似不变**：纯拼音词边界加空格或去空格时，目标都能召回、证据等级相同，受控集名次差不超过 1。
6. **召回与排序分开**：期望项不在排序前候选集时，修 provider/index/budget，不调终局权重。
7. **两窗共享证据**：移除快速窗口独有偏好后，同一候选的证据与两两顺序一致。
8. **证据可展示**：普通文本、拼音和 alias 召回使用同一个已验收 witness 完成判定、高亮和导出说明。

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
  Field          DisplayTitle | FileName | CuratedAlias |
                 ShortcutTarget | ProductMetadata | ProviderAlias | Keyword |
                 ParentPath | HistoryKeyword
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
| 0 ExactIdentity | 规范化查询等于文件/应用标题或文件茎；或常用 joined 拼音从 0 覆盖完整词干。目录不算身份，精确同名目录降为 Grade 1 | `sousuo jieguo` → `搜索结果.lnk` |
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
  → High before Normal
  → DirectHistoryTier / frecency ↓
  → MatchRank quality
  → TypeRank ↑（仅应用/系统设置/文件，且其余键全平）
  → stable common tail
```

约束：

- TypeRank 是 ordinal key，不再映射为 `16/8/4/1`。
- 类型偏好只是**最后平局键**（2026-10-24 用户裁决）：只在双方都属于“应用/系统设置/文件”三个内建桶、且 section/Grade/Priority/Band/Tie/Quality 全部相等时参与；不跨 Grade、不压质量。`DirectApproximate` 也不再按类型成组。
- 目录是容器而非身份：精确同名目录不占 `ExactIdentity`，降为 `DirectStrong`；类型只在质量完全平局后决胜，不能替目录跨级。
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

### P0：冻结工单证据 — 部分完成（5/6）

产物：`reported-search-cases.v1.json`。fixture 使用合成路径和 token，保留查询、候选关系、环境前提及断言。P2 才接 loader。

已知缺口：本文件“已知 case”表中的 `437-shortcut-low` 尚未转录（缺原始查询/候选语料），不计入已冻结证据；见“仍未闭合”第 8 条。

### P1：端到端 ranking trace — 完成

现行产物：

- 每个快速查询一个 `searchSessionId/querySeq`，每个组合页一个 `pageId`（仅内存）；
- UI 行携带 `pageId + finalDisplayedRank`，供整页导出时写进说明；
- 说明中带 engine/filter/merge/prefix/type-coverage/token/final-cap 计数；
- 来源 flags、drop stage、provider readiness 与原始 provider hit 数；
- invariant key/value formatter 和“导出本页排序详情”；
- 不写 JSONL、不保留历史日志；只有用户主动导出时才落盘明文。

P1 只增加观测，不改变候选、排序、截断或打开。导出开关默认开启，关闭后不构建解释。

### P2：候选录制与离线回放器 — 完成

`Tests/Core/SearchRanking/` 包含脱敏 `RankingCase`/`CandidateSnapshot`、P0 loader、V2 与后续策略共用的回放接口，以及 Recall@K、MRR、nDCG、pairwise inversion 和 target rank delta。受控 fixture 覆盖普通 `.lnk`、AppData 下的 PixPin 快捷方式、direct history 和 learned alias；路径均为 `X:\Fixture\...`。

V2 基线见 `Tests/Core/SearchRanking/Baselines/v2-baseline.md`。PixPin 当前排为 folder → exe → shortcut：AppData 的默认 Low 规则先把快捷方式放进 Low 段，类型 bonus 无法跨段。指标用手算单相关项验证，并固定枚举顺序无关、比较器反对称和传递测试。Windows/.NET 10 已运行 Core MSTest；无 SDK 的环境只能把该段数字当契约，不能当实测结果。

退出：后续阶段必须对同一候选集同时报告 V2 与候选策略，不能用权重修改掩盖召回缺失。

### P3：影子 MatchEvidence 与 CandidateIdentity — 完成

`SearchRankingEvidence.cs` 定义字段、模式、query variant、Grade、kind flags 和候选身份。快速窗口在实际命中的 title、filename、provider alias、keyword 或 history keyword 上记录 Field；无法区分 alias 与 parent-path 的引擎 fallback 保持 Unknown。Grade 只由已知表示及既有 `MatchRank` 推导，V2 比较器不读取影子字段。同路径合并保留执行行、来源并集和最强 evidence，诊断 fingerprint 覆盖新增字段。

P3 提交时 matcher 只返回 `MatchRank`，所以普通文本、拼音和 provider alias 的 accepted branch、provider id 与 spans 当时都保持 `LegacyUnknown/-1`；history-only 行因来源明确可记为 `LearnedAlias`。P4 只补上可证明的 joined 拼音 witness，其余分支仍不得从 tier 或高亮结果反推。

退出：schema 4、导出 formatter 能显示已知 Field/Grade/Kind；影子字段差异不改变 V2 顺序。

### P4：拼音空格等价 — 完成

`PinyinNameEvaluator` 只为至少两个普通正词建立 joined 分支：查询不得含 drive、OR、排除或显式匹配操作符；每词必须是 ASCII 字母；拼接长度为 2–63；同一个 live provider 必须能处理每个原词和拼接词。跨 provider 不合成。

original 多词与 joined 单词在同一 scorer 中竞争。有字面词时，joined 仍须原始分更高；全读音多词的逐词位置 bonus 会随空格数重复，不能与单词分直接比较，此时只在 joined 起点不更晚且源覆盖不更小时采用 canonical joined 分。否则保留 original。多 provider 同分再按 span/tier/provider 稳定决胜。胜出 witness 含 provider、tier 和源 span，`HighlightMask` 直接涂该 span，`FuzzyQuery` 同时写出 `PinyinJoined`、实际拼音 mode 和 span。比较器、候选集和非 joined evidence 不读这些字段。

common-only 只消费表内按频率排序的前两个读音：`重` 的 `zhong/chong`、`行` 的 `xing/hang`、`乐` 的 `le/yue` 都算常见，避免把 `chong` 误判为 `TierFullRare`。第三个及以后读音仍只属于 all-readings 分支。

Core fake-provider 测试覆盖资格、strict winner、跨 provider、枚举顺序、操作符、非 ASCII 与 64 字符；`PinyinTermMatcher` 测试覆盖 common-only 接受前两读音、排除第三读音；真实 provider 的 snapshot/delta 测试覆盖 `sousuojieguo`、`sousuo jieguo`、`sou suo jie guo` 的召回、Grade、`MatchRank`、高亮和受控 target rank delta，并在 fuzzy 开关两态核对反序、`visual studio`、`fast stone` 与含 `-` 的候选。普通多词仍走原分支，单 token 不跨候选分隔符。

Windows/.NET 10 已跑 Core/PinyinAlias/App MSTest；端到端 p95 按 2026-10-23 决策放弃测量，合入门槛仍未闭合（见文末“仍未闭合”第 1 条）。

### P5：应用与快捷方式身份

1. 用 `KindFlags` 表示 File/Shortcut/Application；`.exe` 不自动升级。
2. shortcut 的 `SearchTitle` 去已知扩展名，执行路径不变。
3. provider `Aliases` 成为带 provenance 的 Grade 3 evidence；`Keywords` 的干净前缀命中按 `ExactIdentity` 参与类型顺序（2026-10-24 决策，当前用于系统设置）。
4. 开始菜单 provider 在加载时缓存受控 alias：标题、target exe stem、package/product metadata；按键热路径不做 COM 或文件解析。
5. 新 provider alias 在 V3 切换前只进 shadow 候选；现有 provider keyword 保持 V2 行为。
6. 合并行保留 Application 执行委托、File 规则/质量、收藏和历史。
7. provider readiness 变化应重跑当前查询。
8. 只有 trace 证明 recall 失败时才把 source top-K 交给 P9。
9. 用户别名功能已彻底移除（2026-10-24 产品决定）：不再读取 `ResultAliases` 数据，也不提供绑定/清除入口；不引入全局同义词库。

退出：身份与标题 case 通过；可信 alias 的 shadow Recall 达标；无关 `.exe` 不升级。

现行实现把 `SearchableItemAlias` 限为 `Curated/ShortcutTarget/ProductMetadata/User` 四种来源。host 在 provider 后台加载时去空白、按文本去重，并限制为每项 32 条、每条 128 字符。Start Menu provider 在同一加载阶段读取 shortcut target stem、PE product/description/original filename 或 package name；查询线程只读快照。受控 shadow Recall 用 5 个冷启动 alias case 固定为 5/5，另有非 executable target、package path、`.exe` identity 和合并信号回归；Windows/.NET 10 已运行 Core MSTest；无 SDK 的环境只把数字当契约。

alias 仅在导出开关开启、快速窗口内存 trace 构建时、且标题/既有 provider alias/keyword 均未命中时计算。命中写 schema 4 `ShadowAliasGate` candidate 及 Grade 3（`SecondaryField`）provenance，不进入生产列表。readiness 沿用 `ProviderLoaded` 后 150 ms 合并重查。路径合并继续由 Application 行持有执行对象，同时合并 File 的更强直接文本 `Match`/evidence、路径规则、收藏、历史和来源 flags；learned-history alias 不借给直接行。`SearchTitle` 只去 `.lnk/.url/.appref-ms`；普通文件和 `.exe` 保留扩展名。

### P6：V3 影子排序

1. 固化 Grade 规则与属性测试。
2. 建立独立 V3 key，保留 V2 生产路径。
3. trace 开启时仅用 P3 预计算字段计算 rank delta；不做第二次匹配。
4. 比较 V2/V3：weak-over-strong inversion 为 0；P0 正例通过；#440 继续 absent；旧集 nDCG 不下降。
5. 验证全序、路径去重、稳定尾和两窗一致性。
6. 记录每个争议边界及人工决定。

退出：影子门槛与性能通过，UI 仍使用 V2。

### P7：切换到 V3

1. bump `RankPolicy.Version`。
2. 生产路径移除 `16/8/4/1`；TypeRank 只在所有其他键相等后、且双方都是三个内建类型时作为最后平局键。
3. High/Normal 不跨 Grade；Low、AlwaysTop 保持显式段。
4. direct H5/H3 只做同 Grade tie-break。
5. learned alias 进入 Grade 4；P8 前保守读取旧存储。
6. 启用 P5 的 secondary alias，并显示来源；先跑 alias false-positive 集。
7. 更新 7 种语言的类型顺序说明为“仅在应用/系统设置/文件之间、其余排序键全部相等时决胜”。
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
- 导出开关关闭时新增观测分配接近 0；
- 扩大候选预算必须附扫描量和内存。

隐私：

- 默认不写任何排序文件；
- 用户主动导出时写明其中含明文 query/title/path；
- fixture 无真实路径和用户名。

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

## 仍未闭合

按“先测量再动手”的顺序排列，未列出的阶段视为已闭合。

1. **性能门槛（已决定放弃端到端测量）**：P4/P5 的“同机端到端 p95 ≤ V2 的 110%”和 P9 的“扫描量 / 首行延迟 / 内存”不再作为闭合条件；V2 生产路径已删除，也没有同机 V2 基线可对比。保留已测的排序热路径绝对值作为历史参考（benchmark 工程已于 2026-10-24 删除；原数据来自 i5-10400F、.NET 10.0.13、InProcess、Release，两次运行）：`QuickRankBenchmarks.DecorateAndSort` 204 行 Mean 228–249 µs、分配 105.47 KB；4000 行 Mean 3.23–3.55 ms、分配 3887 KB。非 InProcess 的 Default job 两次都因 `debug/Data/Machine/indexes` 拒绝访问而生成失败，只有 InProcess 结果可用；不得把该数字表述为相对 V2 的 110% 结论。P9 的快捷方式补召回已从“每次按键无条件并行跑第二遍全索引”改为“主查询流完后、且主结果里没有 `*.lnk`/`*.url` 才跑”，普通按键回到一次查询；该档首行延迟的风险作为已接受的遗留风险记录。
2. **expected Recall@50 = 100% 离线测不了**：回放 run 的候选集就是 fixture 给的候选集，结构上不可能缺失，因此这条只能在真实索引上做端到端测量。当前只有 `RankingMetrics.RecallAt` 这个工具，没有调用它的门槛。
3. **两窗 Grade 仍有一档差**：拼接分支确实胜出的空格拼音行，快速窗口判 `ExactIdentity`、完整窗口判 `DirectStrong`。完整窗口只有 32 位打包质量键，键里没有“胜出分支”这一位；闭合它要把该位带过引擎/管道边界，也就是 P9 里排最后的第 4 项，须由 1、2 的测量决定是否需要。
4. **`Low` 与强收藏的优先级（已决定）**：产品决定“收藏优先”，段判定先于 `Low`，被规则降权的强收藏仍进 `Pinned`。实现与决定一致，不再作为待决项。
5. **P8 canonical query（已实施）**：`NormalizeAssociationQuery` 在折叠大小写/空白后，仅对全部由 ASCII 字母组成的词做无空格拼接，`sousuojieguo` 与 `sousuo jieguo` 归并到同一键；引号、操作符、非 ASCII 查询不拼接。加载、旧版迁移与新增关联都走同一规范化并合并计数，已有键在加载时幂等迁移。
6. **P0 工单断言（部分可执行）**：`#440` 已补候选生成层断言——跨 15 字符的松散布被 `AliasMatchRules.IsTightOccurrence` 拒绝，11 字符紧凑正例通过；其余 `v3Expected.assertions` 仍是散文，`LoadReportedCases` 现在读 id/window/查询/路径及候选的 token/displayName/role/tightness 期望，但仍不执行散文契约。要执行剩余断言必须补成完整回放 fixture 或接真实索引。
7. **两窗质量精度不同**：完整窗口用量化键（连续性 1/255、覆盖率 1/511），快速窗口用原始 double。差异小于一格的成对比较两窗可能给不同名次。
8. **P0 仍缺一条 case**：`reported-search-cases.v1.json` 只有 5 条；`437-shortcut-low` 没有查询与候选数据，无法回放。P0 仍只能算 5/6；没有原始工单时无法凭空补录，需继续向工单/用户取数，或明确接受该条不进入门槛。
9. **learned alias（已实施 exact）**：`HistorySearchCandidateMapper.CollectCore` 现在只按规范化查询键精确查 `search-query-associations.json`，不再对所有关联调用 `BestMatchWithEvidence(entry.Keyword)`。换来的边界是：只有规范化后完全相同的查询才召回学习项，旧版模糊能命中的前缀/变体不再命中；这是 P8.4 的本意，不是召回退化。
10. **完整窗口没有历史配额（已决定）**：快速窗口 H5/H3 限 3/10、内联 1/3，完整窗口不设配额。产品决定优先保证“展开全部”时相关行不被配额藏掉；同分相邻行两窗差一档名次作为已知差异接受，已写入 `search-ranking.md`。
11. **absence/recall 门槛**：`#440` 已补真实索引/候选生成层 harness（`Tests/Plugins/PinyinAlias/PinyinExactModeTests.cs`，真实 `Snapshot → LiveIndex → IndexV2Searcher`）：精确 `zbl` 只召回 11 字符紧凑行、不召回 15 字符松散布。这不是通过终局排序过滤得到的，而是候选层本身的行为。其余 case 的 `mustBeAbsent` 仍只检查回放 run 的已排序候选 token，`MissingExpectedTokens` 因 loader 要求 expected token 必须是候选而恒为 0；Recall@50=100% 仍不能由离线回放单独背书。
12. **`#440` 紧凑正例（已闭合，未放宽）**：`zbl` 对 11 字符紧凑名在 `AliasMatchRules.IsTightOccurrence(3, 11)` 为真；精确模式下字面 `FzfPattern.TryMatch` 因连字符不连续而失败，但共享 alias 门接受该 11 字符 span（实测 score 72，门槛 `queryLen * 5 = 15`），`FuzzyMatcher.IsMatch` 在真实 pinyin provider 下为真。原“被分数门槛拒掉”的判断不成立，因此没有改阈值；真实索引测试 `Reported440_RealIndexCandidateLayerKeepsOnlyTheTightAlias` 已钉住该行为。
13. **系统设置 `Keyword` 前缀（已决定 2026-10-24）**：`SearchableItemMapper` 的 Control Panel 关键词兜底原先产出 `Keyword / SecondaryField` 和 `ShapeScattered / coverage=0`，导致 `env` 只命中 `envvar` 前缀时被 `env.h` 等文件压到 40 名以后。现改为：每个查询词都干净前缀命中声明词时，按最短匹配词计算 coverage，判 `ExactIdentity` 并参与类型顺序。字段仍是 `Keyword` 以保留 provenance；当前只有 `SystemSettingsItemProvider` 提供 `Keywords`，其他 provider 若启用需先确认同义词质量。同日后续裁决作废了「设置项与精确文件同级」这半句：默认规则 `__SEARCHABLE_ITEM__:SystemSettingsItemProvider:*` 把整组设置行降进 Low 段，`Keyword` 只保留跨语言召回，不再越过任何直接命中（见 `dev-docs/index-rules.md`、`dev-docs/search-ranking.md`）。
14. **多词连续性（试验 2026-10-24）**：`HighlightMask.RankFromMarks` 对多词查询改为按正词计数 run（`positiveTermCount / runCount`），使 `cf wj` 对 `cf 桌面文件` 的 `Contiguity` 从 0.5 升到 1，再以覆盖率 0.571 胜过 `牧场粪污技术交流.doc` 的 0.333；单词查询仍用原来的 run 平方和。已知边界：OR 分支的两个 alternative 同时出现会给一个词贡献多个 run；当前只加注释，未做逐词 mask，避免热路径重复匹配。

## 关键位置

| 主题 | 文件 |
| --- | --- |
| V3 内核与回放门面 | `Core/SearchIndex/RankPolicy.cs`、`Core/SearchIndex/RankPolicyV3.cs`、`Core/SearchIndex/SearchResultRanking.cs`、`Core/SearchIndex/RankedSearchResultComparer.cs` |
| 快速窗口 | `App/ViewModels/Search/Mapping/SearchResultMapper.cs`、`RankedCandidateOrdering.cs` |
| 类型与 shortcut | `App/ViewModels/Search/SearchResultTypePriority.cs` |
| 可搜索项 | `App/ViewModels/Search/Mapping/SearchableItemMapper.cs`、`App/ViewModels/Search/SearchableItemCache.cs` |
| 历史 | `Core/SearchHistoryStore.cs`、`App/ViewModels/Search/Mapping/HistorySearchCandidateMapper.cs` |
| alias gate | `Core/SearchIndex/Fzf/FzfPatternAliasMatchExtensions.cs`、`Core/SearchIndex/AliasMatchRules.cs` |
| 拼音 | `Core/IndexV2/Search/PinyinNameEvaluator.cs`、`Plugins/PinyinAlias/PinyinTermMatcher.cs` |
| 高亮 | `Core/SearchIndex/HighlightMask.cs` |
| 引擎截断 | `Core/SearchIndex/SearchCandidateComparer.cs` |
| schema 4 trace | `Core/SearchIndex/RankingTrace.cs`、`RankingTraceModels.cs`、`App/ViewModels/Search/Mapping/QuickSearchRankingTrace.cs` |
