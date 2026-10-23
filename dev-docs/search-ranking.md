# 搜索排序（现行 V3）

当前生产策略：`RankPolicy.Version = 3`（单调证据分级模型，ranking-event schema 4）。历史 V2 基线与回放评估对照保留在 `Tests/Core/SearchRanking/ReplayEvaluator.cs`，演进记录见 [search-ranking-intuition-plan.md](search-ranking-intuition-plan.md)。

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
   - 快速/内联无范围全局查询并行执行 `ScopedSearchRunner.RunShortcutSupplementAsync`（`fileNameFilter: "*.lnk;*.url"`），确保跨盘快捷方式不会被大量普通文件挤出管道预算，再在 `SearchStreamRenderer` 按规范化路径去重。
2. **单一路径实体合并**（`RankedCandidateOrdering.MergeByPath`）：
   - 同一规范化路径只保留一行，可执行的 `Application` 表示优先持有 UI 行与执行委托。
   - 合并后保留更强的直接文本 `Match`/`MatchEvidence`、路径规则输入、收藏并集、最大打开次数、更优类型秩与全部诊断 `RankingCandidateSource` 位；`LearnedAlias` 永不借给直接命中行。

## V3 单调排序内核

快速窗口（`RankPolicy.BuildKey` + `RankKeyComparer`）与完整窗口（`SearchResultRanking` + `RankedSearchResultComparer`）严格按同一字典序比较，不使用手工加减的跨档总分：

```text
Section ↑ → PinOrder ↑ → EvidenceGrade ↑ → HistoryBand ↑ → HistoryTie ↓ → PriorityOrder ↑ → TypeOrder ↑ → Quality ↑ → Tail ↑
```

### 四段硬边界（`RankSection`）

| 段 | 编号 | 进入条件 | 语义 |
| --- | ---: | --- | --- |
| `Pinned` | 0 | `High && AlwaysTop`，或 `IsFavorite && Direct && Match.IsStrong && Priority != Low` | 显式置顶：`AlwaysTop` 排在强匹配收藏之前 |
| `Boosted` | 1 | 非 `Low` 的直接命中与次要别名命中（`Grade 0..3`） | 主结果区 |
| `Low` | 2 | `Priority == Low` 的直接/次要命中 | 系统缓存、`AppData\Local/Roaming`（Start Menu 显式豁免回 `Normal`）与用户降权目录；高频打开不跨出 `Low` |
| `HistoryTail` | 3 | `IsHistoryOnly` 学习关联行（`Grade 4`） | 仅在所有直接与次要文本命中之后补充召回 |

### 五级单调证据分级（`MatchEvidenceGrade`）

普通偏好（结果类型顺序、`High/Normal` 目录优先级、历史打开次数）只能在**同一 `EvidenceGrade`** 内微调，绝不跨级反超：

| 等级 | 名称 | 判定条件 | 普通类型顺序参与度 |
| ---: | --- | --- | --- |
| `0` | `ExactIdentity` | 直接名称/显示标题完整命中（去掉 `.lnk/.url/.appref-ms` 容器后缀或普通文件扩展名 stem，不剥 `.exe`），且 `IsStrong` | 参与（序数比较，无加分） |
| `1` | `DirectStrong` | 直接名称/显示标题命中且 `Match.IsStrong`（词首、词中连续、整词、普通全拼/简拼） | 参与（序数比较，无加分） |
| `2` | `DirectApproximate` | 跳跃子序列、生僻多音读音、仅父路径兜底等弱直接命中 | 置中性（不借类型顺序反超） |
| `3` | `SecondaryField` | 干净（`IsStrong`）的非标题表示：`ShortcutTarget`、`ProductMetadata`、`CuratedAlias`、`UserAlias`、`ProviderAlias`、`Keyword` | 置中性；副标题显示本地化来源（如 `别名：…`、`目标：…`） |
| `4` | `LearnedAlias` | `IsHistoryOnly` 查询→路径关联召回 | 固定落入 `HistoryTail` |

次要别名与本地单结果用户别名（`UserSettings.ResultAliases`，可在动作菜单 `Ctrl+O` 绑定/清除）均要求 `MatchRank.IsStrong`，拒绝零散子序列（防止 `#440` 类误召回）。不支持全局同义词表。

公共尾（`RankTail`）依次比较：文件优先于目录、自然名序、名称长度、路径长度、盘符、忽略大小写路径、Ordinal 路径、引擎 tie，保证严格反对称与传递全序。

## 历史频率与查询关联分离

`SearchHistoryStore` 将两种历史信号物理拆分为独立存储：

1. **路径使用频率**（`search-history.json`）：按 90 天半衰期计算有效分（`H5 >= 5.0`，`H3 >= 3.0`；快速窗口配额 3/10，内联窗口 1/3），仅用于同 `Section`、同 `EvidenceGrade` 内的 `HistoryBand` / `HistoryTie` 微调。
2. **规范化查询关联**（`search-query-associations.json`）：按大小写折叠与空白归一化的查询词记录自然打开次数 `OrganicOpens`：
   - 至少 **3 次自然打开**（`MinAssociationOrganicOpens = 3`）才允许跨名召回；
   - 通过 `LearnedAlias` 召回后再次点击（`fromLearnedRecall: true`）**不累加 `OrganicOpens`**，阻断自强化回路；
   - `Low` 优先级目标不进入关联召回，单次查询最多补充 `1` 条 `LearnedAlias` 候选；
   - 旧版历史迁移时将初始 `OrganicOpens` 钳制到 `<= 1`，并在“设置 → 历史记录 → 查询关联”中支持独立查看、单条删除与清空。

## 拼音空格近不变性与诊断隐私

- 至少两个无操作符 ASCII 拼音正词同时评估多词分支与同 provider 拼接分支（`PinyinNameEvaluator`），返回真实胜出 witness（provider、tier、span），排序证据、UI 高亮与诊断解释共用同一 witness，消除 `cao zuo xi tong` 与 `caozuoxitong` 的跨档反转。
- `ranking-events.jsonl`（schema 4）默认仅记录查询与路径的 12 位 SHA-256 截断哈希，绝不写入查询、标题或路径明文；仅用户主动触发“复制排序解释”时剪贴板文本才包含原文；离线回放夹具严禁包含真实用户路径（`RankingCaseFixtureLoader` 校验）。

## 验证命令

```powershell
dotnet test Tests/Core/Core.csproj --filter "FullyQualifiedName!~ExplorerStaInvokerTests&FullyQualifiedName!~UpdaterRollbackTests"
dotnet test Tests/App/App.csproj
dotnet test Tests/Plugins/CoreExtensions/CoreExtensions.csproj
dotnet test Tests/Plugins/PinyinAlias/PinyinAlias.csproj
npm run site:build
```
