# 搜索排序（现行）

核对基线：`ff0608a`，`RankPolicy.Version = 2`，ranking-event schema 3（2026-10-23）。本文只描述当前实现；后续改动按 [search-ranking-intuition-plan.md](search-ranking-intuition-plan.md) 分阶段执行。

## 范围

GUI 有三条相关路径：

| 路径 | 候选 | 排序与上限 |
| --- | --- | --- |
| 快速窗口 | 索引、历史、Windows Recent、收藏、可搜索项；插件即时结果/动作作为前缀 | `RankedCandidateOrdering`；通常最多 50 个排序候选 |
| 内联窗口 | 当前文件夹段 + 快速窗口的全局段 | 全局段沿用同一内核，历史名额较小 |
| 完整窗口 | 索引全量；内容 provider 作为前缀 | 到达时装饰，`RankedSearchResultComparer` 增量合并；索引行无 App 侧上限 |

CLI 与 Everything 的名称排序仍使用 `SearchResultRankComparer`，不在本模型内。

## 快速窗口管线

```text
引擎候选
  → 查询目录/scope/类型/folder 过滤
  → 加入历史、Recent、收藏、可搜索项
  → 按规范化路径合并
  → RankPolicy 排序
  → 类型覆盖选择
  → 与插件前缀组合
  → token 过滤/重排（若有）
  → 最终 50 条上限
```

同一路径只保留一行。合并时收藏取并集、历史次数取最大、类型秩取更靠前者；可执行的 Application 表示优先于普通 File 表示。诊断来源只做 flags 合并，不进入比较器。

普通查询向 App 请求 51 个文件候选，服务端为过滤余量放大到 204；token、文件范围、filename-like 查询以及高优先规则使用 1000/4000。每盘搜索还会扩大扫描余量，但返回量仍受管道预算约束。引擎截断只看文本质量和公共尾，不看历史、收藏或索引优先级。

App 通常保留 50 个排序候选。`EnsureTypeCoverage` 先为每个类型保留最多 3 行，再按全局顺序补满；它不改变存活行之间的顺序。token 模式在 provider 处理后由 `QueryTokenResultComposer` 对前缀和文件行合计截到 50。文件范围和 filename-like 查询不做这道显示截断。

## 排序内核

快速窗口与完整窗口共用 `Core/SearchIndex/RankPolicy.cs`。快速窗口预计算 `RankKey[]` 后排序；完整窗口把同一组字段写入 `SearchResult`，比较器只读已装饰字段。

### 匹配强弱

`MatchRank.IsStrong` 要求：

```text
IsMatch
&& Tier <= TierFull
&& Shape <= ShapeInsideWord
```

whole-word、word-start、inside-word 和普通全拼属于强匹配；scattered、no-name、rare-reading 以及只有排除词的查询属于弱匹配。判据用 shape，而不是要求总 contiguity 为 1：干净的多词命中可能由多段组成。

### 分值

基础分：

```text
S = 10 × C + 8 × P
C = 强匹配 ? 1 : 0
P = High ? 2 : Normal ? 1 : 0
```

| 信号 | 分值 |
| --- | ---: |
| High + 强 | 26 |
| Normal + 强 | 18 |
| High + 弱 | 16 |
| Normal + 弱 | 8 |
| Low + 强 / 弱 | 10 / 0 |
| 收藏 + 强 | 44 |
| 历史 H5 / H3 / Tail | 34 / 22 / 6 |

`10:8` 表示一个优先档可交换 0.8 个强匹配档，因此 High+弱（16）仍低于 Normal+强（18）。这是一条显式策略，不是从点击数据拟合出的参数。

快速窗口还给用户设置的前四个结果类型加 `16/8/4/1`。弱匹配只获得 `bonus × Coverage × Contiguity` 的四舍五入值；收藏和历史固定分不叠加类型 bonus。完整窗口没有类型层。

### 四段

段序是硬约束，段内才比较分值：

| 段 | 进入条件 | 段内主键 |
| --- | --- | --- |
| Pinned | High 且 `AlwaysTop` | Score↓ → TypeRank↑ → 质量 |
| Boosted | 收藏强匹配、H5/H3、其余 Normal/High | Score↓ → HistoryTie↓ → TypeRank↑ → 质量 |
| Low | Low 且未被提升 | Score↓ → 质量 → TypeRank↑ |
| HistoryTail | 未达到 H3 的纯历史行 | HistoryTie↓ → TypeRank↑ → 质量 |

公共尾依次比较：文件/目录、自然名序、名字长度、路径长度、盘符、忽略大小写路径、Ordinal 路径、引擎 tie。路径最终全序保证 `Array.Sort` 的输出可重复。

分段不能等价地改成一个总分：Pinned、Low 和 HistoryTail 表达的是不可交换约束。类型位置则是序数设置，却被映射为固定基数 bonus；这是当前启发式的主要局限之一。

## 历史、收藏与索引规则

历史存储 0–10 的 score，按 90 天半衰期衰减：

```text
Effective = clamp(Score × 0.5^(days / 90), 0, 10)
```

有效分至少 5 为 H5，至少 3 为 H3。快速窗口最多分配 3 个 H5 和 10 个 H3；内联窗口是 1/3；完整窗口只按阈值提升已有索引行，不加历史行。超出 H5 名额的行可继续竞争 H3，不用空余名额跨档补位。

快速窗口还会按历史中的 query-path 关系加行。该行标为 `IsHistoryOnly`，文本质量按 `NoMatch` 消费，避免把历史 keyword 当成文件名证据。这个关联仍会被再次点击强化，因此点击日志不能直接当作无偏相关性标签。

收藏只有在强匹配时取 44 分；弱匹配不加分。Windows Recent 不获得历史分。

`AlwaysTop` 只对 High 生效，且不能创造候选。路径型 High 规则可通过 `HighPriorityRoots` 补搜；glob/regex 命中若在引擎截断前没有进入候选池，仍是 best-effort。索引规则的继承与冲突规则见 [index-rules.md](index-rules.md)。

`.lnk`、`.url`、`.appref-ms` 文件可借用 Application 与 Files 中更靠前的类型秩；`.exe` 不借用。按路径合并时，可执行的 Application 行代表同一实体。

## 文本匹配边界

查询解析、反向词、路径兜底和拼音都在排序之前决定候选及 `MatchRank`。排序不能补回没有召回的项目，也不能凭空生成匹配 witness。

当前拼音实现是逐查询、逐候选的 NFA。查询与候选统一做全角 ASCII→半角、U+3000→空格和 NFC；不做通用 NFKC。单个拼音词要求 2–63 个字符，候选文本最多检查 512 个码点，简拼不跨候选中的分隔符。完整规则与测试矩阵见 [pinyin-vs-winshun.md](pinyin-vs-winshun.md)。

## 排序诊断

“设置 → 通用 → 系统 → 排序事件日志”控制 `%LOCALAPPDATA%\Lertaro\ranking-events.jsonl`。schema 3 记录：

- `searchSessionId + pageId`：连接 page、row/candidate 与 open；
- `kernelRank` 与 `finalDisplayedRank`：区分内核顺序和最终 UI 顺序；
- 引擎、过滤、合并、插件前缀、类型覆盖、token、最终 cap 的计数；
- 候选来源、drop stage、provider readiness；
- 排序分解字段与策略版本。

默认日志只保存查询和路径的 12 位 SHA-256 截断值，不写查询、标题或路径原文。用户主动执行“复制排序解释”时，剪贴板文本会包含这些原文，分享前应检查。

文件达到 10 MiB 后轮转，最多保留 3 个；进程首次写入时清理 30 天前的文件。相邻 5 秒内完全相同的页复用 `pageId`。日志失败不会阻止搜索或打开结果。reader 对旧 schema 缺失的字段返回 Unknown/null，并忽略未来字段。

影子 `MatchEvidence` 记录实际参与匹配的 title、filename、provider alias、keyword 或 history keyword，并据此写 Field/Grade；无法区分 alias 与 parent-path 的引擎 fallback 保持 Unknown。`CandidateIdentity` 分开记录来源和 File/Directory/Application/Shortcut flags。matcher 尚未返回 accepted branch 和 spans，因此普通结果的 `Mode/Span` 仍是 `LegacyUnknown/-1`；不得从 `MatchRank` 反推 alias branch 或 span。

## 科学性与校准边界

当前模型的可取之处是：硬约束与可交换偏好分开、两窗共用内核、比较键全序、参数含义可解释。它仍不是经过充分标注数据训练的学习排序器：

- `10/8`、90 天、3/10 和类型 bonus 是先验值；
- 类型序数映射成基数 bonus，会放大相邻位置差异；
- Strong 二值门在边界处不连续；
- 历史同时承担 frecency 与 query-path alias，存在反馈回路；
- 引擎截断目标与终局个性化排序不同。

因此参数调整必须基于固定候选回放，分别报告 Recall、MRR/nDCG、pairwise inversion 与性能；不能只用点击率，也不能用排序权重修复召回。执行步骤和发布门槛见 [search-ranking-intuition-plan.md](search-ranking-intuition-plan.md)。

## 验证

```powershell
dotnet test Tests/Core/Core.csproj --filter "FullyQualifiedName!~ExplorerStaInvokerTests&FullyQualifiedName!~UpdaterRollbackTests"
dotnet test Tests/App/App.csproj
dotnet test Tests/Plugins/PinyinAlias/PinyinAlias.csproj
dotnet run -c Release --project benchmarks/Lertaro.Benchmarks -- --filter '*'
```

排序改动至少覆盖：比较器传递性与全序、两窗 pairwise 一致性、历史预算、路径合并、类型覆盖、最终 UI rank、page/open join、schema 兼容、无明文和 fail-open。

最近保存的 BenchmarkDotNet 样本（Windows 11、i5-10400F、.NET 10、InProcess、2 次暖机 + 5 次计测）仅供同机回归：

| 基准 | 规模 | Mean | Allocated |
| --- | ---: | ---: | ---: |
| Quick `DecorateAndSort` | 204 | 184.0 µs | 0 B |
| Quick `DecorateAndSort` | 4000 | 4.33 ms | 3.65 KB |
| Engine `CandidateComparer` | 100000 | 24.28 ms | 4.94 KB |
| Full `SortDecoratedRows` | 100000 | 30.83 ms | 4.94 KB |

迭代数较少，4000 行样本有明显噪声；这些数字不能代替应用内 p50/p95，也不能证明单次比较零分配。新的排序提交应保存同机前后结果，不用跨机器绝对值下结论。

## 关键位置

| 主题 | 文件 |
| --- | --- |
| 内核与比较器 | `Core/SearchIndex/RankPolicy.cs`、`RankKeyComparer.cs`、`RankedSearchResultComparer.cs` |
| 文本质量 | `Core/SearchIndex/MatchRank.cs`、`Core/IndexV2/Search/SearchResultRelevance.cs` |
| 快速窗口 | `App/ViewModels/Search/Mapping/SearchResultMapper.cs`、`RankedCandidateOrdering.cs` |
| 完整窗口 | `App/ViewModels/Search/Mapping/StreamingResultAccumulator.cs` |
| 历史 | `Core/SearchHistoryStore.cs`、`SearchHistoryBucketSupport.cs` |
| 引擎截断 | `Core/SearchIndex/SearchCandidateComparer.cs`、`Core/Indexer/Usn/SearchCoordinator.cs` |
| 诊断 | `Core/SearchIndex/RankingEventLog.cs`、`RankingTraceModels.cs`、`App/ViewModels/Search/Mapping/QuickSearchRankingTrace.cs` |
