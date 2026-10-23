# 搜索排序（现行 V3）

当前生产策略：`RankPolicy.Version = 3`（单调证据分级模型，ranking-event schema 4）。V2 基线冻结在 `Tests/Core/SearchRanking/OfflineRankingPolicies.cs`，V2/V3 对照门槛在 `Tests/Core/SearchRanking/ShadowV3EvaluationTests.cs`，演进记录见 [search-ranking-intuition-plan.md](search-ranking-intuition-plan.md)。

## 范围

GUI 有三条搜索路径共用同一 V3 排序原则：

| 路径 | 候选来源 | 排序与上限 |
| --- | --- | --- |
| 快速窗口 | 索引（含跨盘与快捷方式补召回）、已确认历史关联、Windows Recent、收藏、可搜索项；插件即时结果/动作作为前缀 | `RankedCandidateOrdering`（预计算 `RankKey[]`）；默认最多 50 个排序候选 |
| 内联窗口 | 当前文件夹近邻段 + 快速窗口的全局段 | 同层近邻内直接命中优先于学习关联；全局段沿用同一内核（H5=1, H3=3） |
| 完整窗口 | 索引全量；内容 provider 作为前缀 | 到达时 `SearchResultRanking.Decorate`，`RankedSearchResultComparer` 增量合并 |

CLI 与 Everything IPC 的名称排序仍使用 `SearchResultRankComparer`，不在个性化模型内。

## 召回与合并边界

1. **召回与排序解耦**：缺失候选只能在 provider、索引或检索预算层修复，绝不用放大终局排序权重补召回。
   - 本地多盘有界查询按 `SearchCandidateComparer` 全局合并后再取 `limit`（`SearchCoordinator`）；网络盘/目录索引多源有界查询同样全局排序截断（`NetworkIndexerSearchExtensions.SearchSnapshots`）。
   - 快速/内联无范围全局查询在主查询流完之后，仅当主结果里一条 `*.lnk`/`*.url` 都没有时才补跑 `ScopedSearchRunner.RunShortcutSupplementAsync`（`fileNameFilter: "*.lnk;*.url"`），避免每次按键都付第二遍索引扫描、第二次设置读取与第二份排除规则；补召回行仍按规范化路径在 `SearchStreamRenderer` 去重。
2. **单一路径实体合并**（`RankedCandidateOrdering.MergeByPath`）：
   - 同一规范化路径只保留一行，可执行的 `Application` 表示优先持有 UI 行与执行委托。
   - 合并后保留更强的直接文本 `Match`/`MatchEvidence`、路径规则输入、收藏并集、最大打开次数、更优类型秩与全部诊断 `RankingCandidateSource` 位；`LearnedAlias` 永不借给直接命中行。

## V3 单调排序内核

快速窗口（`RankPolicy.BuildKey` + `RankKeyComparer`）与完整窗口（`SearchResultRanking` + `RankedSearchResultComparer`）比较同一组结构化键，不使用手工加减的跨档总分。段内键序并非一条通用链，而是每段各一条，两窗只在“完整窗口没有类型秩”这一点上不同（完整窗口的 `TypeOrder` 比较恒等，也不按类型分组）：

```text
Pinned       AlwaysTop ↑ → FavoritePin ↑ → Grade ↑ → TypeOrder ↑ → Priority ↑ → Band ↓ → Tie ↓ → Quality ↑ → Tail ↑
Boosted      Grade ↑ → TypeOrder ↑ → IsFavorite ↑ → Priority ↑ → Band ↓ → Tie ↓ → Quality ↑ → Tail ↑
Low          Grade ↑ → TypeOrder ↑ → Band ↓ → Tie ↓ → Quality ↑ → Tail ↑
HistoryTail  Band ↓ → TypeOrder ↑ → Tie ↓ → Tail ↑
```

完整窗口对同一段使用同一顺序，只是把 `TypeOrder ↑` 位置留空（恒等），并把 `Quality ↑` 换成打包后的 32 位质量键——两者键序一致，仅在量化精度上不同（连续性 1/255、覆盖率 1/511），因此两窗对“差异小于一个量化格”的两行可能给出不同名次。

### 四段硬边界（`RankSection`）

| 段 | 编号 | 进入条件 | 语义 |
| --- | ---: | --- | --- |
| `Pinned` | 0 | `High && AlwaysTop`，或 `IsFavorite && Direct && Match.IsStrong` | 显式置顶：`AlwaysTop` 排在强匹配收藏之前；段判定先于 `Low`，因此被规则降权的强收藏仍然置顶 |
| `Boosted` | 1 | 非 `Low` 的直接命中与次要别名命中（`Grade 0..3`） | 主结果区 |
| `Low` | 2 | `Priority == Low` 的直接/次要命中 | 系统缓存、`AppData\Local/Roaming`（Start Menu 显式豁免回 `Normal`）与用户降权目录；高频打开不跨出 `Low` |
| `HistoryTail` | 3 | `IsHistoryOnly` 学习关联行（`Grade 4`） | 仅在所有直接与次要文本命中之后补充召回 |

### 五级单调证据分级（`MatchEvidenceGrade`）

普通偏好（结果类型顺序、`High/Normal` 目录优先级、历史打开次数）只能在**同一 `EvidenceGrade`** 内微调，绝不跨级反超：

| 等级 | 名称 | 判定条件 | 普通类型顺序参与度 |
| ---: | --- | --- | --- |
| `0` | `ExactIdentity` | 直接名称/显示标题完整命中（`.lnk/.url/.appref-ms` 容器后缀或普通文件扩展名按 stem 归一），且 `IsStrong` | 参与（序数比较，无加分） |
| `1` | `DirectStrong` | 直接名称/显示标题命中且 `Match.IsStrong`（词首、词中连续、整词、普通全拼/简拼） | 参与（序数比较，无加分） |
| `2` | `DirectApproximate` | 跳跃子序列、生僻多音读音等弱直接命中 | 参与（序数比较，无加分；同等级内先按类型成组，再比质量） |
| `3` | `SecondaryField` | 干净（`IsStrong`）的非标题表示：`ShortcutTarget`、`ProductMetadata`、`CuratedAlias`、`UserAlias`、`ProviderAlias`、`Keyword`；以及引擎无名回退（如仅父路径解释）的命中 | 参与（序数比较，无加分）；副标题显示本地化来源（如 `别名：…`、`目标：…`） |
| `4` | `LearnedAlias` | `IsHistoryOnly` 查询→路径关联召回 | 固定落入 `HistoryTail` |

次要别名与本地单结果用户别名（`UserSettings.ResultAliases`，可在动作菜单 `Ctrl+O` 绑定/清除）均要求 `MatchRank.IsStrong`，拒绝零散子序列（防止 `#440` 类误召回）。不支持全局同义词表。

上表的 0..4 是文档语义号；枚举 `MatchEvidenceGrade` 另用 `LegacyUnknown = 0` 表示“未知”，真实等级从 1 开始，未知值永不参与排序。回放夹具因此用名字声明等级（`"expectedV3Grade": "ExactIdentity"`），避免编号口径分歧。

公共尾（`RankTail`）依次比较：文件优先于目录、自然名序、名称长度、路径长度、盘符、忽略大小写路径、Ordinal 路径、引擎 tie，保证严格反对称与传递全序。

## 历史频率与查询关联分离

`SearchHistoryStore` 将两种历史信号物理拆分为独立存储：

1. **路径使用频率**（`search-history.json`）：按 90 天半衰期计算有效分（`H5 >= 5.0`，`H3 >= 3.0`；快速窗口配额 3/10，内联窗口 1/3，完整窗口不设配额），仅用于同 `Section`、同 `EvidenceGrade` 内的 `HistoryBand` / `HistoryTie` 微调。
2. **规范化查询关联**（`search-query-associations.json`）：先按大小写折叠、空白归一化，并在只由 ASCII 字母组成的词之间做无空格拼接（`sousuojieguo` 与 `sousuo jieguo` 因此是同一个键；含引号、操作符或非 ASCII 的查询不拼接），再对规范化键做**精确查找**；不做一般 fuzzy，避免 `b` 之类的短查询把无关关联行吸进来：
   - 至少 **3 次自然打开**（`MinAssociationOrganicOpens = 3`）才允许跨名召回；
   - 通过 `LearnedAlias` 召回后再次点击（`fromLearnedRecall: true`）**不累加 `OrganicOpens`**，阻断自强化回路；
   - `Low` 优先级目标不进入关联召回，单次查询最多补充 `1` 条 `LearnedAlias` 候选；
   - 旧版历史迁移时将初始 `OrganicOpens` 钳制到 `<= 1`，并在“设置 → 历史记录 → 查询关联”中支持独立查看、单条删除与清空。

完整窗口不设 H5/H3 配额是有意的产品选择：完整列表本来就是“展开全部”，用配额截断会把相关行直接藏掉。代价是同分相邻行在快速与完整窗口可能差一档名次；该差异已作为已知项记录，不用配额去换两窗完全一致。

## 拼音空格近不变性与诊断隐私

- 至少两个无操作符 ASCII 拼音正词同时评估多词分支与同 provider 拼接分支（`PinyinNameEvaluator`），返回真实胜出 witness（provider、tier、span）。排序路径把它直接交给画刷，画刷不再重复打分；只有手上没有 witness 的显示路径（`HighlightMask.Compute`）自己求一次。两者对同一 pattern 与同一文本调用同一确定性函数，因此判定、高亮与解释始终涂同一段。
- 完整窗口只有打包后的质量键，键里没有“胜出分支”这一位，所以 `SearchResultRanking` 一律按 `Original` 变体解等级：带空格的拼音查询在完整窗口最高只到 `DirectStrong`，即使拼接分支真的胜出。单词整词干读音与规范化同名字符串仍判 `ExactIdentity`。残留缺口是已知的：拼接胜出的那一行在快速窗口为 0、在完整窗口为 1，两窗名次可能不同；闭合它需要把该位带过引擎/管道边界（见实施计划的未做项）。
- `ranking-events.jsonl`（schema 4）默认仅记录查询与路径的 12 位 SHA-256 截断哈希，绝不写入查询、标题或路径明文；“复制排序解释/导出本页排序详情”由用户主动触发，文本首行自带 `# plaintext:` 声明其中的查询、显示名与路径未做哈希；离线回放夹具严禁包含真实用户路径（`RankingCaseFixtureLoader` 校验）。
- 排序反馈只需要“预览、不打开”：开启设置中的排序事件记录后，每次预览都会写 `kind:"page"` 与 `kind:"row"` 事件（`opened:false`），无需实际点击。提交 `%LOCALAPPDATA%\Lertaro\ranking-events.jsonl`（便携版为 `Data\Users\<hash>\ranking-events.jsonl`）；因为文件名/查询只有哈希，再附上原始查询、错误行的显示名/路径与期望顺序。若只针对某一行，右键该行“复制排序解释”，把剪贴板中的明文另存为 `.txt` 一并提交即可，仍然不需要打开它。若要提交整个快速窗口列表，右键任一行选“导出本页排序详情”，会一次写出所有当前显示行的明文 `.txt`。

## 验证命令

```powershell
dotnet test Tests/Core/Core.csproj --filter "FullyQualifiedName!~ExplorerStaInvokerTests&FullyQualifiedName!~UpdaterRollbackTests"
dotnet test Tests/App/App.csproj
dotnet test Tests/Plugins/CoreExtensions/CoreExtensions.csproj
dotnet test Tests/Plugins/PinyinAlias/PinyinAlias.csproj
npm run site:build
```
