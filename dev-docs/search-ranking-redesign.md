# 搜索排序改进计划：0.8 交换率、历史衰减与始终置顶

## 适用范围与核对基线

- 基线：`main` = `fd490885`（2026-10-21 核对）；排序相关代码最后改动 `8c754217`。现行行为以 `search-ranking.md` 为准。
- 实施进度：阶段 1（快速窗口与完整窗口）、内联窗口（含 1/3 历史名额）与本文 5.5 的排序事件日志（含设置页开关）都已落地；7.3 的性能基准已在本机跑过一轮并给出实测表，类型秩也改成预计算字典。引擎有界堆不再计划，推迟理由见 5.4。本文保留设计依据与尚未做的部分，现行行为的权威描述仍是 `search-ranking.md`（收尾步骤统一改写）。
- 目标：把用户给出的 6 档顺序落成可解释、可测试、可回放的排序策略，并让快速窗口与完整窗口共用同一套评分内核；快速窗口的排序耗时和分配不高于现行实现。
- 非目标：不做学习排序，不引入云端个性化，不改索引格式，不为了排序重建索引。

术语：

- **显示优先级**：设置 → 通用 → 快速搜索窗口 → 结果类型优先级（`UserSettings.ResultTypeOrder`）。只作用于快速窗口。
- **优先档**：索引规则里的高 / 正常 / 低（`IndexRuleEffect.High/Normal/Low`）。
- **强匹配**：查询在文件名里形成连续词、词首或词内连续段，且不是生僻读音；定义见 3.1。
- **历史分**：按打开次数和时间衰减算出的分数，不是裸次数。
- **置顶**：进入历史 5+ 档（H5）或 3–4 档（H3），不是无条件排第一。

## 1. 决策台账

| 事项 | 决定 | 说明 |
| --- | --- | --- |
| 高优先 + 弱匹配（档 3） | 让位于中优先 + 强匹配（档 2） | 用户明确 3 在 2 之后；用 0.8 交换率实现 |
| 高优先的“永远第一”出口 | 单条索引规则可勾选“始终置顶” | 只对高优先规则生效，不新增第五个档位枚举 |
| 历史机制 | 衰减分数 + 5/3 阈值 + 上限 | 半衰期 90 天；置顶最多 3 条、中间档最多 10 条 |
| 历史置顶资格 | 强匹配，或历史关键词关联 | 弱文件名匹配的索引行不因历史次数置顶 |
| 历史 1–2 次 | 档 6，排在低优先之后 | 5+/3–4 才提升；弱历史信号不压过低优先匹配 |
| 收藏 | 置顶，仅强匹配 | 分值 44，高于历史 5+ 的 34 |
| 优先级/匹配交换率 | 1 个优先档 = 0.8 个匹配等级 | 整数实现 `S = 10*C + 8*P`，精确表达 0.8 |
| 第一阶段范围 | 快速窗口 + 完整窗口 | 内联窗口第二阶段 |
| 低优先级可见性 | 不额外处理 | 接受低优先结果可能被快速窗口 50 条上限挡住 |
| 本地验证日志 | 允许，默认开、可关 | 只存哈希与排名，不上传 |
| 文档位置 | `dev-docs/` | 本文；不新建 `exam/` |

## 2. 现状问题

1. **优先档是硬隔离**。现行梯子先按优先档分组，高优先的弱匹配永远压过正常档的强匹配。用户要的档 3 在档 2 之后，硬隔离做不到。
2. **历史是裸次数**。`HistoryBand` 只看 `usageCount >= 5 / >= 1`，没有时间衰减；一个两年前打开过 5 次的文件会永久置顶，最近常用的文件反而上不来。Windows Recent 行还被当成 1 次历史，容易误置顶。
3. **收藏与历史的实际位置与设置文案相反**。设置页写“历史记录和收藏夹始终排最前”，实现里两者都在六键质量之后；文案需要同步修正。
4. **显示优先级的位置没有区分场景**。快速窗口现在把 `TypeRank` 放在质量之前，完整窗口没有类型层；低优先组又要求“先匹配、再显示优先级”，需要条件化。
5. **快速窗口排序常数偏大**。`RankedCandidateOrdering.Order` 是 11 层 LINQ，比较时逐层取键；`Priority` 每次比较做字典查找；路径 `TrimEnd` 每次比较重复。候选 51–4000 条时这是主要可控成本。
6. **引擎截断发生在 App 排序之前**。`SearchCoordinator` 用不带优先级/历史的 `SearchResultRankComparer.Instance` 做跨盘 `Take`，高档目录靠 `HighPriorityRoots` 补搜兜底；低优先可能根本进不了 App 排序。第一阶段保留这个结构，只需确认路径型 AlwaysTop 根已被 `HighPriorityRoots` 覆盖。

## 3. 目标排序模型

### 3.1 强匹配与弱匹配

```text
Strong = Match.IsMatch
      && Match.Shape <= MatchRank.ShapeInsideWord   // whole word / word start / inside word
      && Match.Tier  <= MatchRank.TierFull          // 字面名 / 简拼 / 全拼；生僻读音不算
```

- 用 `Shape` 而不是 `Contiguity >= 1`：双词字面命中（如 `code atlas` 命中 `code-atlas`）的 `Contiguity` 只有 0.5，用 1 会把正常的多词查询误判为弱匹配。`HighlightMask.RankFromMarks` 的 `Contiguity = Σ run² / matched²` 决定了这一点。
- 用 `Shape + Tier` 而不是给 `Coverage/Start` 设阈值：短查询命中长名字时 `Coverage` 天然低，但词首命中仍然强；`Start/Coverage` 继续留在同档内的六键质量里做细分。
- `ShapeScattered`、`ShapeNoName`、`TierFullRare` 一律弱匹配；`NoPositiveTerms` 也是弱匹配。
- 全拼算强匹配：中文输入法下 `xingjian` 命中“新建”是正常主路径，不能因为层级比简拼低就掉到弱档。

### 3.2 分值

```text
C = Strong ? 1 : 0
P = High ? 2 : Normal ? 1 : 0
Base = 10*C + 8*P
```

`10:8` 就是 0.8：同档内强/弱差 10，同匹配度下高/正常差 8；高优先弱匹配（16）比中优先强匹配（18）低 2，正好实现“档 3 在档 2 之后”。

| 场景 | C | P | 分值 | 备注 |
| --- | --- | --- | --- | --- |
| 高优先 + 强匹配 | 1 | 2 | 26 | 档 1 |
| 中优先 + 强匹配 | 1 | 1 | 18 | 档 2 |
| 高优先 + 弱匹配 | 0 | 2 | 16 | 档 3 |
| 中优先 + 弱匹配 | 0 | 1 | 8 | 档 4 |
| 低优先 + 强匹配 | 1 | 0 | 10 | 档 5；在历史尾部之前 |
| 低优先 + 弱匹配 | 0 | 0 | 0 | 档 5 |
| 收藏 + 强匹配 | — | — | 44 | 高于 H5 |
| 历史 5+ | — | — | 34 | 高于高优先强匹配 |
| 历史 3–4 | — | — | 22 | 在 26 与 18 之间 |
| 历史 <3 的纯历史行 | — | — | 6 | 档 6；在低优先之后 |

低优先不是靠分值自然沉底：低优先强匹配 10 会高于正常弱匹配 8，所以必须有独立的分段门。分段顺序同时表达用户列表里的“档 5 在档 6 前”：

```text
0 Pinned      AlwaysTop
1 Boosted     收藏强匹配 / 历史 H5+H3 / 正常或高优先
2 Low         低优先且未被提升
3 HistoryTail 历史 <3 的纯历史行（档 6）
```

分段赋值：

```text
AlwaysTop                                   → Pinned
否则 收藏强匹配 或 历史 H5/H3                → Boosted
否则 纯历史行 且 历史 <3                     → HistoryTail
否则 优先档 == Low                           → Low
否则                                        → Boosted
```

### 3.3 排序键

先按 `Section` 升序分段，再在段内比较：

| Section | 段内键 |
| --- | --- |
| 0 Pinned | `Score` 降序 → `TypeRank` 升序 → 六键质量 → 公共尾部 |
| 1 Boosted | `Score` 降序 → `HistoryTie` 降序 → `TypeRank` 升序 → 六键质量 → 公共尾部 |
| 2 Low | `Score` 降序（10/0）→ 六键质量 → `TypeRank` 升序 → 公共尾部 |
| 3 HistoryTail | `HistoryTie` 降序 → `TypeRank` 升序 → 六键质量 → 公共尾部 |

公共尾部（所有段相同）：

```text
文件先于文件夹
→ 自然名序
→ 名字长度
→ 路径长度
→ 盘
→ 路径
→ 引擎低 32 位 tie-breaker（仅完整窗口）
```

`HistoryTie` 只在历史桶内有值，精确到 0.01 分；非历史行为 0。`TypeRank` 只在快速窗口有值，完整窗口固定为 0。

三个关键选择：

- **Boosted 段里 `TypeRank` 在质量之前**。这是对现行设置语义的延续：显示优先级是用户显式声明的偏好，不只做“质量完全相等”时的 tie-break。代价是同一档内类型顺序可以压过质量差异；设置页文案必须写明这一点。
- **Low 段里质量在 `TypeRank` 之前**。这是用户计划第 5 条的原话：“先按照匹配度从高到低，再把结果按显示优先级从高到低二次排序”。这条只对低优先组生效，不影响前四档。
- **HistoryTail 在 Low 之后**。这是用户计划第 6 条“历史记录”的位置；只有 5+ / 3–4 的加权历史才会被提升到 Boosted，1–2 次的纯历史行仍留在档 6。索引行自身的历史只加分，不把整行降成 HistoryTail。

`AlwaysTop`、收藏强匹配、历史 3+ 三者都会把行提升到 Pinned/Boosted，因此它们不受 Low 段限制；低优先的弱收藏与 1–2 次历史仍留在 Low/HistoryTail。

### 3.4 历史分

数据模型（`Core/SearchHistoryStore.cs` 的 `StoredEntry`）：

```text
Path, Kind, Time, Count  // 现有字段
Score                    // 新增：上次更新后的衰减前分数，0..10
```

读取时按距上次打开的天数衰减：

```text
Effective(entry, now) = entry.Score * 0.5 ^ (days(now - entry.Time) / 90)
```

打开时：

```text
s = Effective(entry, now)
entry.Score = min(10, s + 1)
entry.Time  = now
entry.Count += 1
```

- 半衰期 90 天：90 天前的 5 次 ≈ 2.5 分，低于 H3 阈值，落到尾部；180 天前 ≈ 1.25 分，继续下沉。比“永久 5 次置顶”更接近当前习惯。
- 上限 10 分：防止一天内反复打开把分数推到无穷；也避免单个文件长期霸榜。
- 阈值：`Effective >= 5` 为 H5，`>= 3` 为 H3，其余为历史尾部。这里的 5/3 是衰减后的分数，不是裸 `Count`；旧数据先迁移再按同一公式计算。
- 旧数据迁移：`Score <= 0` 时令 `Score = min(Count, 10)`，再用 `Time` 做一次衰减。两年前打开 10 次的老记录会自然降到接近 0；昨天打开 10 次的记录仍然接近 10。迁移幂等，只写回一次。
- 上限：H5 最多 3 条，H3 最多 10 条。按 `Effective` 降序取：前 3 条 H5，接下来 10 条 H3，其余纯历史行落 HistoryTail（档 6）。第 4 条 5+ 历史不会掉到档 6，而是进 H3；这样“5 次置顶”不会因为第 4 名而突然消失。
- 资格：`Effective >= 3` 且（`IsHistoryOnly` 或 `IsStrong`）才可进 H5/H3。纯历史行由关键词关联天然满足；索引行必须强匹配，避免“打开过但这次只是模糊命中”的文件被抬上来。
- 作用域：快速窗口只在当前 scope 内允许置顶；scope 外的历史分不参与。
- Windows Recent：不写 `Score`，永远不置顶；它仍按自己的文件名匹配参与普通档。
- 内联窗口：名额定为 1 条 H5 + 3 条 H3，通过 `RankContext.HistoryFiveMax` / `HistoryThreeMax` 传给 `RankPolicy.BuildHistoryBudget`。内联的两条入口各自构造上下文：`App/ViewModels/Search/Dispatch/SearchDispatchController.cs` 在内联上下文取 1/3、否则用默认 3/10；内联“当前文件夹”那套在 `App/ViewModels/Search/SearchExecutionEngine.cs` 固定 1/3。快速窗口与完整窗口仍是 3/10，完整窗口本来就走阈值制，不读名额。超出的历史行不消失，只是回到 Boosted/HistoryTail 按分数排。`AlwaysTop` 在内联同样生效：置顶是用户显式勾选，不占历史名额。
- UI：H5/H3 行加“常用”徽标（已实现于快速窗口：`AppSearchResult.IsFrequent` + `SearchResultTemplate` 的名称右侧小胶囊；内联窗口的窄行与完整窗口暂不显示）；不新增“关闭历史置顶”的开关，阈值与上限就是调节手段。

历史桶内排序：`Score` 同档时先比 `Effective`（`HistoryTie`），再比 `TypeRank`/质量。这样 5.9 分排在 5.1 分前，而不是完全并列。

**完整窗口的例外**：完整窗口逐批流式合并，排序键必须在到达时就固定，因此不设 H5≤3 / H3≤10 的集合名额，也不产生 HistoryTail 行；历史档按阈值即时判定（有效分 ≥5 → H5，≥3 → H3，且必须是强匹配）。名额只对快速窗口的单页有意义。

### 3.5 始终置顶

数据模型：

```csharp
// Core/Settings/IndexRuleSetting.cs
public sealed class IndexRuleSetting
{
    public string Pattern { get; set; } = string.Empty;
    public IndexRuleEffect Effect { get; set; } = IndexRuleEffect.Normal;
    public bool AlwaysTop { get; set; }          // 新增，默认 false
}

public readonly record struct ParsedIndexRule(
    string Text, IndexRuleKind Kind, string Pattern,
    IndexRuleEffect Effect, bool AlwaysTop);     // 新增
```

- 只有 `Effect == High` 时 `AlwaysTop` 生效；UI 在高档被选中时才启用复选框，切到其他档时清空该标志。
- 不新增 `IndexRuleEffect` 枚举值：持久化格式、旧配置迁移、UI 下拉框都不用动；`AlwaysTop` 是高档上的一个修饰位。
- 继承：沿用现有优先档继承算法，但两条路径分开表述。路径规则由最近一层显式规则覆盖：子级显式 `High` 未勾选也会清掉祖先的 `AlwaysTop`，子级显式 `Normal` 同样清除，否则无法“只把这个子目录降回正常”。模式规则沿链取最强；只有当链上最强档位同为 `High` 时，`AlwaysTop = true` 才优先于同档的 `false`。
- `ExclusionRuleSet` 增加 `PriorityVerdict(IndexRuleEffect Effect, bool AlwaysTop)` 与 `GetPriorityVerdict(path, isDirectory)`；保留现有 `GetPriority` 作为薄包装，减少调用点改动。
- 语义：`AlwaysTop` 是 Pinned 段，压过收藏、历史、低优先段；但候选仍必须命中查询，不会把不相关文件硬塞进来。
- 补搜：`AlwaysTop` 只对 `High` 生效。路径型规则根目录已被现有 `HighPriorityRoots` 覆盖（`Core/ExclusionRuleSet.cs:131-137` 只从路径规则取根），不需要独立补搜；glob/regex 型本来就没有根，沿用现有高档补搜的同一限制。实施时加断言：路径型 AlwaysTop 存在时 `HighPriorityRoots` 必须包含其根。若以后允许 `AlwaysTop` 独立于 `High`，再单独补搜。完整窗口不截断，不需要补搜。

### 3.6 快速窗口与完整窗口

| 维度 | 快速窗口 | 完整窗口 |
| --- | --- | --- |
| 类型顺序 `TypeRank` | 有，非低优先在质量前，低优先在质量后 | 无 |
| 历史 | 加行；H5/H3 置顶 + 历史尾部 | 只作为索引行的排序输入，不加行 |
| 收藏 | 加行；强匹配 44 | 只对索引行生效；强匹配 44 |
| Windows Recent | 加行；不置顶 | 无 |
| 可搜索项/插件即时 | 加行，前缀不参与排序 | 无 |
| 低优先 | 可能在引擎截断时被丢掉（接受） | 全量，档 5；在历史尾部之前 |
| 引擎截断 | 51/1000 → 管道 204/4000 | `int.MaxValue`，不截 |

完整窗口的内容 provider 行走前缀（`ContentPrefixCount`），不进入排序；这条不变。

### 3.7 例子

查询 `report`，候选与分值：

| 候选 | 条件 | 分值 | 位置 |
| --- | --- | --- | --- |
| `D:\Pinned\r-e-p-o-r-t.txt` | 高优先 + 弱匹配（散点）+ AlwaysTop | 16 | 第 1（AlwaysTop） |
| `D:\Fav\report.docx` | 收藏 + 强匹配 | 44 | 第 2 |
| `D:\High\report.docx` | 高优先 + 强匹配，历史 6 次 | 34 | 第 3（H5） |
| `D:\High\report-old.docx` | 高优先 + 强匹配 | 26 | 第 4 |
| `D:\Normal\report.docx` | 中优先 + 强匹配，历史 3.2 次 | 22 | 第 5（H3） |
| `D:\Normal\report-final.docx` | 中优先 + 强匹配 | 18 | 第 6 |
| `D:\High\r-e-p-o-r-t.txt` | 高优先 + 弱匹配（散点） | 16 | 第 7 |
| `D:\Normal\reprot.txt` | 中优先 + 弱匹配（模糊） | 8 | 第 8 |
| `D:\Low\report.pdf` | 低优先 + 强匹配 | 10，`Section=Low` | 第 9（档 5） |
| `D:\Low\misc.txt` | 低优先 + 弱匹配 | 0，`Section=Low` | 第 10（档 5） |

同档内的 `TypeRank` 与六键质量继续细分；上表只展示桶边界。第 7 与第 6 的对比就是用户计划里的“档 3 在档 2 后”：高优先散点 16 < 中优先强匹配 18。

## 4. 为什么这套规则合理

1. **分段 + 可交换分数**。纯字典序分段（现行）无法表达“高优先弱匹配 vs 中优先强匹配”；纯加权和又会让低优先强匹配浮上来。这里用“四段 + 段内分值”的组合：前四档可交换，低优先在历史尾部之前仍是硬隔离，符合用户“档 5 在档 6 前”的列表顺序。
2. **0.8 是可解释的边际替代率**。它表示“用户愿意用 0.8 个匹配等级换 1 个优先档”。整数化后没有浮点误差；档 2 与档 3 的差是 2 分，边界清楚，测试可钉死。
3. **强匹配用结构信号，不用拍脑袋阈值**。`Shape` 和 `Tier` 是匹配器已经算出的离散信号；`Contiguity/Coverage/Start` 是连续信号，留给同档内六键排序。这样“强/弱”边界可解释，也不会因为查询长度变化而漂移。
4. **历史用 frecency，而不是裸次数**。Firefox Places 的 frecency = frequency × recency × visit-type，并对旧访问做衰减；历史建议（adaptive input history）用 `use_count = use_count * 0.9 + 1`、上限 10、约 90 天未用删除。本文的 90 天半衰期、10 分上限、5/3 阈值是同一思路的本地化版本：常用且近期使用才置顶，旧习惯自然退场。
5. **上限防反馈回路**。历史/收藏是用户行为信号，天然有“越点越前”的正反馈；3/10 条上限保证它们不会把整个结果页吃满，也给新文件留出曝光。
6. **显示优先级是显式偏好**。它不是模型猜的，而是用户在设置里排的；放在同档质量之前是尊重显式偏好，代价写进文案。低优先组按用户要求把质量放回前面，形成“前四档看偏好、第五档看匹配”的分层。
7. **可回放**。所有输入（匹配秩、优先档、历史分、收藏、类型顺序、策略版本）都能在本地日志里还原；不需要训练模型就能解释“为什么它排这里”。

局限：0.8、90 天、3/10 都是产品判断，不是从点击数据拟合出来的；第一阶段先用默认值上线并记录日志，第二阶段再用真实打开排名校准。没有做 learning-to-rank，是因为本地搜索的候选集小、查询短、冷启动没有标注，复杂模型的可解释性和延迟都不划算。

## 5. 架构

### 5.1 共用内核

把评分与比较下沉到 Core，快速/完整窗口只负责“收集信号”和“呈现”：

```text
RankSignals
  MatchRank Match
  bool IsHistoryOnly
  bool IsFavorite
  IndexRuleEffect Priority
  bool AlwaysTop
  int HistoryScoreX100
  int TypeRank
  bool IsDir
  string Name
  string NormalizedPath
  int Drive
  ulong EngineTie

RankKey
  byte AlwaysTop
  byte Section   // 0 Pinned / 1 Boosted / 2 Low / 3 HistoryTail
  int Score
  int HistoryTie
  int TypeRank
  MatchRank Match
  bool IsDir
  string Name
  string NormalizedPath
  int Drive
  ulong EngineTie
```

- `RankPolicy.Classify(MatchRank)`：强/弱。
- `RankPolicy.Section(signals, historyTier)`：按 3.2 分成 Pinned/Boosted/Low/HistoryTail。
- `RankPolicy.Score(signals, historyTier)`：3.2 的分值；收藏/历史/分段都在这里。
- `RankPolicy.BuildHistoryBudget(candidates, now)`：按 3.4 选出 H5/H3/尾部。
- `RankKeyComparer : IComparer<RankKey>`：3.3 的键序；快速窗口和完整窗口共用，完整窗口把 `TypeRank` 固定为 0。
- `RankContext`：优先档解析器、收藏集合、历史分快照、类型顺序表、`now`。每次查询构造一次，只读。

### 5.2 快速窗口

```text
收集（索引/历史/Recent/收藏/可搜索项/插件）
→ 按 NormalizedPath 合并（ApplicationOwned；历史/收藏/索引同一路径合并信号）
→ HistoryBudget（H5/H3/HistoryTail）
→ Decorate 成 RankKey[]
→ Array.Sort(keys, payloads, comparer)
→ 显示截断（8/50，N more）
```

- 合并先于排序：同一路径只留一行，历史分、收藏、优先档、类型取“最强/最合适”的组合；避免历史行和索引行互相挤占。
- `ApplicationOwned` 保留：同路径的 Application 行优先，合并时把历史/收藏信号带过去。
- `Array.Sort` 不稳定，但最后有 `NormalizedPath` 全序，结果确定；不需要 LINQ 的稳定排序。
- 显示截断沿用现行 8/50 规则；低优先不预留槽位。

### 5.3 完整窗口

```text
索引行
→ 一次性 Decorate（MatchClass、Score、HistoryTier、AlwaysTop）
→ 排序（无 TypeRank）
→ 内容 provider 前缀
```

- 完整窗口可能有十万级行，不做 `RankKey[]` 全量复制；在 `SearchResult` 上放 `internal int RankScore`、`internal byte RankFlags`、`internal int HistoryScoreX100`，装饰一遍后比较器只读字段。
- 档位在这里是阈值制（见 3.4 的例外），理由同上：排序键要在行到达时就定死，重算名额会让已经显示的行跳动。强/弱直接读 `RankSortKey` 高 32 位里打包好的 tier 与 shape，不再重建 `MatchRank`；`RankSortKey == 0`（从未打包过，旧梯子里排最差）按弱匹配处理，否则它会以 tier 0、shape 0 冒充完美命中抢到 18 分。
- 历史不加行，只把 `HistoryScoreX100` 注入索引行；收藏同理。
- 分段在完整窗口同样生效；因为不截断，低优先结果仍可见。这个窗口不产生 HistoryTail 行（没有历史行可归入），段号只用到 0/1/2。

另外两个消费方也走同一个 `RankContext`，别以为只有 GUI 的两个窗口接了新梯子：外部管道的 token 搜索（`App/Services/Pipe/AppSearchPipeService.cs` 的 `RunTokenizedSearchAsync`）本来就是「先拿全量再排」，用完整窗口那套 `SearchResultRanking.Decorate` + `RankedSearchResultComparer`；内联窗口「当前文件夹」段（`ExplorerSearchHelper.CreateLocalSnapshot` 的可选 `rankContext`，由 `SearchExecutionEngine.RenderInlineSearchAsync` 每次搜索构造一次）用快速窗口那套 `RankCandidates`，所以置顶与衰减历史分在这两条路径同样生效。无 token 的流式路径不变，仍把增量排序留给客户端。

### 5.4 引擎截断

- 第一阶段保留 `SearchCoordinator` 的“每盘缓冲 → 跨盘排序 → Take(limit)”；把参与截断的比较器改成只读质量键的 `SearchCandidateComparer`，不再复用最终排序器。这样引擎不需要历史/优先级上下文，也不会因为 `Instance` 语义变化而误伤。
- 快速窗口复用现有 `HighPriorityRoots` 补搜；路径型 AlwaysTop 天然在其中，glob/regex 型沿用同一限制。补搜上限仍 50，先观察是否够用。
- 有界堆与「堆内预留高档/置顶槽位」**明确推迟，不在第一阶段做**。三条依据：
  1. 本地盘的截断不发生在客户端进程：`SearchService.SearchStreamingAsync` 只把带 `Limit` 的 `SearchRequestMessage` 交给 `SearchPipeClient.SendSearchPipeCommandAsync`（`Core/Services/Search/SearchService.cs:105`），真正执行搜索与截断的是提权服务进程里的 `engine.SearchStreaming(...)`（`Core/Services/Search/SearchStreamPump.cs:95`）。要在截断时按规则预留槽位，规则信息必须先扩进管道消息协议。
  2. 路径型高优先（含置顶）规则的行本来就不靠主查询：`SearchStreamRenderer` 会对 `HighPriorityRoots` 二次分根搜索（`App/ViewModels/Search/SearchStreamRenderer.cs:225` 调 `ScopedSearchRunner.RunPriorityRootsAsync`，每根 `PriorityRootResultLimit = 50`，`:35`），不受主查询截断影响。
  3. 主查询自带余量：`fileCandidateLimit = maxResults * 4`（`Core/Services/Search/SearchService.cs:51`），存在高优先规则时快速窗口还会把 `fileLimit` 抬到 `TokenQuickSearchFileLimit`（`App/ViewModels/Search/Dispatch/SearchDispatchController.cs:200-201`）。模式型置顶规则没有根可枚举，只能 best-effort。
  所以在有基准证明跨盘 `OrderBy().Take()` 是瓶颈之前不引入有界堆；真要引入，得同时把 `HighPriorityRoots` 放进请求协议。

### 5.5 设置与日志

已实施：`Core/SearchIndex/RankingEventLog.cs`（`internal static`，测试接缝 `EnabledOverride` / `DirectoryOverride`，后者赋值时顺带清掉进程内的折叠签名与「每进程一次年龄清理」标记）。`Tests/App/AppTestAssembly.cs` 在 `[AssemblyInitialize]` 里把 `EnabledOverride` 设成 false——合成查询一旦哈希过就和真实查询分不出来，测试不许写用户的日志。

- `EnableRankingEventLog` 是 `UserSettings` 的属性（默认 true），设置页开关在「通用 → 系统」，位于「日志级别」与「界面语言」之间（`App/Views/Settings/General/GeneralSettingsSystemTab.xaml` 的 `RowEnableRankingEventLog`）；关掉后下一次写入尝试就不落盘。策略版本没有做成设置项，而是 `RankPolicy.Version = 2` 的代码常量：策略版本必须跟着代码走，让它能被用户配置改写就等于没有版本。
- `UseLegacyRanking` 不实现。旧梯子先按优先档硬分组、历史只做质量之后的 band，与新模型的「四段 + 可交换分数」不是同一套语义，留着开关等于让一次搜索可能跑出两种顺序；回滚手段是 revert 对应提交。`StoredEntry.Score` 是增量字段，旧版本读不到它也不会读坏，所以数据不需要回滚。
- 记录范围：只记快速窗口排序结果的前 10 行（`RankedCandidateOrdering.Order` 排完就记，发生在 8/50 显示截断之前；不含即时结果与插件前缀行，它们不参与排序）。用户打开某一行时另记一行 `opened:true`，**不带 rank/bucket/score**——点击发生时列表可能已被后续批次重排，名次早就不是当时那个名次了；`FullPath` 为空的行（计算器一类即时结果）不记，免得占一个哈希桶。
- 三条不采样的路径：内联「当前文件夹」走 `RankCandidates`，它本来就不传 query；内联的全局层确实走 `BuildQuickResults`，但它用的是 1/3 历史名额而快速窗口用 3/10，混进同一份没有窗口字段的日志会让 bucket 分布偏向 Boosted，所以 `isInlineWindow` 时传 `query: null`；完整窗口走流式累加器，根本不经过 `Order`。
- 文件：`%LOCALAPPDATA%\Lertaro\ranking-events.jsonl`，每行一个 JSON 对象，UTF-8 无 BOM；单文件满 10 MB 轮转为 `ranking-events.1.jsonl`、`.2.jsonl`，只留 3 个；每进程首次写入时删掉 `LastWriteTimeUtc` 早于 30 天的 `ranking-events*.jsonl`。字段 `ts / queryHash / pathHash / rank / bucket / score / policyVersion / opened`。
- 隐私：查询与路径都先 `Trim().ToUpperInvariant()` 再取 SHA-256 前 12 位小写十六进制（`HashKey`），所以只有大小写或首尾空格差异的查询/路径会归到同一个键；原始文本绝不落盘，也不上传。
- 同一条查询在流式重绘时会反复调用 `RecordQuery`：前 10 行的路径哈希与上次相同、且距上次写入不足 5 秒就跳过。这是把相邻的重复页折叠成一条，不是采样修正——top 10 真变了就照写。
- 任何写入失败都被静默吞掉（含目录不可用），校准日志没有资格让一次搜索失败。
- 用途：上线后统计“打开项落在第几档”、H5/H3 命中率、AlwaysTop 使用率，反推 0.8/90 天/3/10 是否需要调。

## 6. 文件级改动

| 文件 | 改动 | 风险 |
| --- | --- | --- |
| `Core/SearchIndex/MatchRank.cs` | 增加 `IsStrong` 计算或 `MatchClass` 静态方法 | 低；纯函数 |
| `Core/SearchIndex/RankPolicy.cs`（新） | `RankSignals/RankKey/RankKeyComparer/RankContext/HistoryBudget/SearchCandidateComparer` | 中；新内核 |
| `Core/SearchResult.cs` | 增加 `RankScore/RankFlags/HistoryScoreX100`；`SearchResultRankComparer` 改走 `RankPolicy` | 中；完整窗口热路径 |
| `Core/SearchHistoryStore.cs` | `StoredEntry.Score`、衰减、迁移、`ScoreSnapshot(now)` | 中；历史数据兼容 |
| `Core/Settings/IndexRuleSetting.cs` | `AlwaysTop` 字段与 `ParsedIndexRule` | 低；JSON 向后兼容 |
| `Core/ExclusionRuleSet.cs` | `PriorityVerdict`、`GetPriorityVerdict`、继承合并（含 `AlwaysTop`） | 中；规则语义 |
| `Core/Indexer/Usn/SearchCoordinator.cs` | 截断比较器换成 `SearchCandidateComparer` | 低；行为等价 |
| `App/ViewModels/Search/Mapping/RankedCandidateOrdering.cs` | 删除 11 层 LINQ，改 `RankPolicy` + `Array.Sort` | 高；核心行为 |
| `App/ViewModels/Search/Mapping/SearchResultMapper.cs` | 合并/去重、HistoryBudget、装饰、截断 | 高；候选构建 |
| `App/ViewModels/Search/Mapping/HistorySearchCandidateMapper.cs` | 输出 `HistoryScoreX100`、H5/H3、scope 过滤 | 中；历史行 |
| `App/ViewModels/Search/Mapping/WindowsRecentCandidateMapper.cs` | 明确 `HistoryScore=0`，不置顶 | 低；Recent 行 |
| `App/ViewModels/Search/Dispatch/SearchDispatchController.cs` | 构造 `RankContext`；预算 | 中；入口 |
| `App/ViewModels/Search/SearchStreamRenderer.cs` | 确认 `HighPriorityRoots` 覆盖路径型 AlwaysTop 根；不新增独立补搜 | 低；渐进渲染 |
| `App/ViewModels/Search/SearchResultTypePriority.cs` | 预计算 `typeId → rank` 字典（`BuildRanks`），比较期只查表 | 低；快速窗口 |
| `App/Views/Settings/General/GeneralSettingsSystemTab.xaml` + `GeneralSettingsViewModel.cs` / `GeneralSettingsApplier.cs` + `App/Helpers/SettingsSearchIndex.cs` + 7 语言 | 「启用排序校准日志」开关（通用 → 系统） | 低；纯设置 |
| `App/Views/Settings/IndexSettings/Templates.xaml:74-90`（编辑行） | “始终置顶”复选框 + 说明；切到非 High 时清空 | 中；UI/7 语言文案 |
| `App/Views/Settings/IndexSettings/ExclusionsTab.xaml:175` | 列表无需改结构；确认新复选框随模板显示 | 低 |
| `Core/Settings/UserSettings.cs` | `EnableRankingEventLog`（默认 true）。`RankingPolicyVersion` 做成代码常量、`UseLegacyRanking` 不实现，见 §5.5 | 低 |
| `benchmarks/Lertaro.Benchmarks/`（新） | BenchmarkDotNet 基准项目，只引用 Core；不进 `Lertaro.slnx`、CI 与 `Tests` | 低；仅开发机 |
| `Plugins/CoreExtensions/Resources/Translations/*/App.json` | 修正 `General_ResultTypeOrderHint`：类型顺序只压同段质量，低优先段反过来；历史仅 5+/3+ 置顶 | 低；7 语言 |
| `dev-docs/index-rules.md` | 实施后补 `AlwaysTop` 语义 | 文档 |
| `dev-docs/search-ranking.md` | 实施后更新梯子；先加一行指向本文 | 文档 |

`SearchResultMapper.cs:298` 的 `RankAndDedupe`、`:307-337` 的显示截断、`:348-356` 的 `RankedCandidate`、`:361-362` 的 `RankAndDedupe` 是主要落点；`RankedCandidateOrdering.cs:32-72` 的键序和 `:84-97` 的 `ApplicationOwned` 会被替换/保留。

## 7. 测试计划

### 7.1 单元测试

| 测试 | 钉住什么 |
| --- | --- |
| `RankScoreTests` | 26/18/16/8/44/34/22/6；`HighWeak < NormalStrong`；四段赋值；收藏弱匹配不加分 |
| `MatchClassTests` | whole/word-start/inside/initials/full-pinyin 为强；scattered/rare/no-name 为弱；多词 `Contiguity=0.5` 仍强 |
| `HistoryDecayTests` | 90 天减半；5 次置顶；90 天后 5 次掉出；上限 10；迁移幂等；Recent 不置顶；scope 过滤 |
| `HistoryBudgetTests` | （快速窗口名额）前 3 H5、接下来 10 H3、第 4 个 5+ 进 H3、第 14 个进 HistoryTail |
| `AlwaysTopTests` | 只对 High 生效；路径继承；子级 Normal 清除；模式规则合并；多 AlwaysTop 按分值 |
| `RankKeyComparerTests` | 6 档全序；Low 内质量在 TypeRank 前；Boosted 内 TypeRank 在质量前；HistoryTail 在 Low 后；路径全序 |
| `SearchResultRankingTests` | 完整窗口装饰：26/18/16/8/44/34/22 与低优先 10；`RankSortKey == 0` 算弱；收藏与历史注入；重复装饰幂等；尾分隔符参与查表 |
| `RankedSearchResultComparerTests` | 完整窗口段序与段内键序；同分走打包质量；公共尾（目录/名字/长度/盘/路径）；null 安全 |
| `SearchCandidateComparerTests` | 引擎截断只看质量，同质量落名字与路径，最后由完整键的低 32 位收口 |
| `SearchResultRankComparerTests` | 完整窗口无 TypeRank；历史注入；收藏；低优先 |
| `HistorySearchCandidateMapperTests` | 历史行字段、H5/H3、scope、Windows Recent 不置顶 |
| `RankedCandidateOrderingTests` | 合并/去重、ApplicationOwned、显示截断、空查询 |
| `SearchResultMapperTests` | 8/50 截断、N more、插件前缀不参与排序 |

### 7.2 回归与集成

- Core 现有 2075 条、App 现有 2170 条必须全绿；只改因新梯子而预期变化的断言，不做“大范围放宽”。
- 新增集成用例：
  1. 高优先弱匹配 vs 中优先强匹配：后者在前。
  2. 历史 5 次置顶，但第 4 个 5 次落 H3。
  3. 收藏弱匹配不置顶；收藏强匹配在 H5 前。
  4. AlwaysTop 弱匹配仍在第一。
  5. 低优先强匹配在正常弱匹配之后。
  6. 快速窗口同档内类型顺序压过质量；低优先组质量压过类型顺序。
  7. 1–2 次的纯历史行排在低优先强匹配之后（HistoryTail 在 Low 后）。
  8. 完整窗口同输入（无类型层）顺序正确。
- 黄金表：用 10–20 个候选覆盖 6 档、边界 0.8、历史 3/5、收藏、AlwaysTop、低优先；表驱动断言，失败时输出完整键值。

### 7.3 性能测试

已实施：`benchmarks/Lertaro.Benchmarks`（BenchmarkDotNet 0.15.8，只引用 Core，不进 `Lertaro.slnx`/CI/`Tests`）。三组基准对应三段热路径：快速窗口内核 `RankPolicy.BuildKey` + `Array.Sort`、引擎截断比较器（新旧同数据对照）、完整窗口十万行的最终排序。合成数据由 `Synthetic.cs` 的固定 xorshift 序列生成，同机重跑可比。

命令（Release，全部基准）：

```powershell
dotnet run -c Release --project benchmarks/Lertaro.Benchmarks -- --filter '*'
```

运行机器与运行时（BDN 自报头 + `dotnet --version`）：

- BenchmarkDotNet v0.15.8，Windows 11 (10.0.26300.9457)
- Intel Core i5-10400F CPU 2.90GHz，1 CPU，12 logical / 6 physical cores
- .NET SDK 10.0.402，Host：.NET 10.0.13 (10.0.1326.50504)，X64 RyuJIT x86-64-v3
- Job：`InvocationCount=1 UnrollFactor=1`，`[InProcess]`（原因见下）；每组 2 暖机 + 5 迭代，下表取 InProcess 那一行

| 基准 | 行数 | Mean | Error | Allocated | 对照 |
| --- | --- | ---: | ---: | ---: | --- |
| `QuickRankBenchmarks.DecorateAndSort` | 204 | 184.0 µs | 3.64 µs | 0 B | 取代 11 层 LINQ，无同数据旧值 |
| `QuickRankBenchmarks.DecorateAndSort` | 4000 | 4,330.5 µs | 859.89 µs | 3,648 B | 同上 |
| `SearchCandidateComparerBenchmarks.CandidateComparer` | 4000 | 702.6 µs | 11.10 µs | 4.94 KB | baseline |
| `SearchCandidateComparerBenchmarks.LegacyRankComparer` | 4000 | 1,294.6 µs | 25.12 µs | 4.94 KB | Ratio 1.84 |
| `SearchCandidateComparerBenchmarks.CandidateComparer` | 100000 | 24,280.8 µs | 677.05 µs | 4.94 KB | baseline |
| `SearchCandidateComparerBenchmarks.LegacyRankComparer` | 100000 | 40,969.8 µs | 1,532.26 µs | 4.94 KB | Ratio 1.70 |
| `RankedSearchResultComparerBenchmarks.SortDecoratedRows` | 100000 | 30.83 ms | 1.233 ms | 4.94 KB | 装饰在 GlobalSetup，不计入 |

结论：引擎截断那一步换掉比较器不但持平，而且更快——`SearchCandidateComparer` 对 legacy `SearchResultRankComparer` 的 Ratio 为 1.70（十万行）/1.84（四千行），即旧实现多花 65–84 %。`Allocated` 在四千行与十万行都是约 4.94 KB，说明分配不随行数增长，是每次调用的固定开销（`Array.Sort` 的内部与比较器的少量临时对象）；它**不能**证明“每次比较零分配”，那一条仍以代码审查为准（比较器只读字段、不做字典查找与字符串裁剪）。QuickRank 四千行的 3,648 B 是另一组基准（全量装饰 + 排序），与截断的 4.94 KB 不是同一口径，不要合并解读。完整窗口的十万行排序 30.83 ms 略高于截断的 24.28 ms，多出来的是段内公共尾（路径/名字）比较，落在 §8 标注的风险点上，但它只在前面几层键全平时才走到。

偏差与噪声：

- 本机 BDN 的默认（进程外）toolchain 生成 boilerplate 工程时会从仓库根递归找 csproj，撞上权限拒绝的 `debug/Data/Machine/indexes`，报 `System.UnauthorizedAccessException: Access to the path 'F:\LanguageLearning\Lertaro\debug\Data\Machine\indexes' is denied.`，那一行 Job 全部是 `Mean = NA`。所以三个类各加了 `[InProcess]`；没有换库、没有降级 TFM，`[SimpleJob(warmupCount: 2, iterationCount: 5)]` 保留原样，读表时只看 `Job = InProcess` 的行。
- 四千行及以下有 `MinIterationTime` 警告，四千行的 `DecorateAndSort` 两次实测为 1.99 ms / 4.33 ms——迭代数偏少，这一格按数量级读，别当准数。截断那两格重跑过一轮：新比较器 23.3 → 24.3 ms、legacy 38.3 → 41.0 ms（十万行），Ratio 仍落在 1.64–1.69；其余格子只跑过一轮。
- 未测：应用内 p50/p95（§11 的「快速窗口 p95 不高于现行」要跑起窗口才能取，这条还没法打勾），以及 51/204/4000/100000 全档位的 `RankPolicy.Sort` 曲线。

基线方法（原计划，保留）：第一阶段用 `Stopwatch` 包住 `RankAndDedupe` 和 `BuildQuickResults`（debug 开关）记录 p50/p95。没有基线数字前不承诺绝对毫秒数。

### 7.4 命令

```powershell
dotnet test Tests/Core/Core.csproj
dotnet test Tests/App/App.csproj
dotnet test Tests/Plugins/PinyinAlias/PinyinAlias.csproj
```

排序改动集中在 Core/App；PinyinAlias 用于确认拼音层级与强弱判定没有被改坏。性能基准单独运行，不放进常规测试。

## 8. 性能预算

- 快速窗口：候选典型 204、上限 4000；现行 11 层 LINQ 每次比较重新取键，`Priority` 字典查找与路径 `TrimEnd` 重复。新实现 O(n) 装饰 + O(n log n) 比较，比较器只读预计算字段；预计比较常数下降，内存增加一个 `RankKey[]`（4000 × 约 80 B ≈ 320 KB）。
- 完整窗口：十万级行不复制 `RankKey[]`；在 `SearchResult` 上装饰一次，比较器读字段。`RankScore/RankFlags/HistoryScoreX100` 增加少量对象体积，换取比较期零字典查找、零 `TrimEnd`。
- 引擎截断：比较器从“最终排序器”解耦后，截断只做质量键；行为与现行 `Instance` 等价，但不再承担历史/优先级语义。
- 排序稳定性：`Array.Sort` 不稳定，最后以 `NormalizedPath` 全序收口；去重后路径唯一，所以结果确定。
- 风险点：完整窗口 100000 行的字符串自然序比较仍是大头；只在前面键全平时才走到，预计不是瓶颈，但要基准确认。

## 9. 分阶段实施

| 阶段 | 内容 | 退出条件 |
| --- | --- | --- |
| 0. 基线 | 日志开关、`Stopwatch` 埋点、合成基准、抓取现行排序快照 | 有可对比的 p50/p95 与分配；日志可关。**状态：日志开关已落地；`Stopwatch` 埋点与 p50/p95 未实施，改用 §7.3 的 BDN 基准** |
| 1a. 内核 | `RankPolicy`、`MatchClass`、分值与键序、单元测试 | 6 档/边界/四段全绿；不接线。**已完成** |
| 1b. 历史与规则 | `Score` 迁移、HistoryBudget、`AlwaysTop` 模型与继承 | 迁移幂等；AlwaysTop 测试全绿。**已完成** |
| 1c. 快速窗口 | `RankedCandidateOrdering` 替换、合并/去重、截断、类型 rank 表 | 快速窗口集成用例全绿；p95 持平。**已完成（p95 待应用内测量）** |
| 1d. 完整窗口 | `SearchResult` 装饰、比较器、历史/收藏注入 | 完整窗口用例全绿；100k 基准持平。**已完成** |
| 2. 内联与引擎 | 内联历史名额降到 1/3（`RankContext.HistoryFiveMax`/`HistoryThreeMax`）；有界堆与堆内预留槽位推迟，见 5.4 | 内联名额用例全绿；路径型置顶规则由 `HighPriorityRoots` 分根补搜保证，模式型置顶规则在截断下 best-effort。**已完成** |
| 3. 校准 | 用日志调 0.8/90 天/3/10；决定 TypeRank 是否只做 tie-break | 有数据支撑的最终参数。**进行中：日志与基准已就绪，真实数据的参数校准待上线** |

灰度：不实现 `UseLegacyRanking` 开关（`RankPolicy.Version` 是代码常量，用户可编辑的版本号测不到任何东西）。回滚 = revert 对应提交；`StoredEntry.Score` 向后兼容，旧 JSON 缺字段按 `min(Count, 10)` 迁移，不需要回滚数据。

## 10. 风险与未决项

| 风险 | 影响 | 缓解 |
| --- | --- | --- |
| 0.8 是产品判断 | 可能过强/过弱 | 日志 + 灰度；参数集中在 `RankPolicy` 常量 |
| 历史 3/10 上限 | 第 4 个常用项被压到 H3 | 第 4 个 5+ 进 H3 而不是尾部；可调上限 |
| 非低优先 TypeRank 压过质量 | 同档内更好的匹配可能排后 | 沿用现行设置语义；文案写明；日志观察 |
| 低优先不可见 | 用户找不到低优先文件 | 已接受；完整窗口不截断；后续可加提示 |
| AlwaysTop 滥用 | 弱匹配长期霸榜 | 只对 High 生效；UI 说明；日志统计 |
| 完整窗口 100k 变慢 | 搜索卡顿 | 装饰一次、比较器零分配、基准门槛 |
| 历史迁移误判 | 老记录突然置顶/消失 | 按 `Time` 衰减；迁移幂等；保留 `Count` |
| 日志隐私 | 本地路径泄露 | 只存哈希；默认开但可关；30 天保留 |

可调参数（已有默认值，不阻塞实施）：0.8 是否改为 0.75/1.0；半衰期 90 天；H5/H3 上限 3/10；非低优先 TypeRank 是否降为质量后的 tie-break；内联窗口是否关闭置顶。

## 11. 验收标准

- [x] 档 1–4 严格按 26/18/16/8 排序；档 3 在档 2 之后。（`RankPolicyTests`）
- [x] 低优先整段在历史尾部之前；低优先内先质量、后显示优先级；HistoryTail 在最后。（`RankPolicyTests`、`RankAndDedupeTests`）
- [x] 收藏强匹配 44，位于 H5 之前；收藏弱匹配不加分。（`RankPolicyTests`、`RankAndDedupeTests`）
- [x] 历史 5+ 最多 3 条置顶；3–4 最多 10 条在档 1/2 之间（均为快速窗口；完整窗口按阈值即时判定，不设名额）；90 天衰减生效；Recent 不置顶；scope 外不置顶。（`HistoryBudgetTests`、`SearchHistoryStoreTests`、`HistorySearchCandidateMapperTests`）
- [x] AlwaysTop 压过所有段；只对 High 生效；子级 Normal 可清除。（`AlwaysTopTests`、`RankAndDedupeTests`）
- [x] 快速窗口同档内显示优先级生效；完整窗口无类型层。（`SearchResultMapperTests`、`RankedSearchResultComparerTests`）
- [ ] 快速窗口 p95 不高于现行；完整窗口 100k p95 不高于现行；比较路径零分配。**拆开看**：100k 排序与引擎截断有 BDN 数字（§7.3），比较器只读字段、不在比较期做查找或裁剪（代码审查），这两部分可以勾；**快速窗口 p95 仍未测**，需要跑起应用取，保持不勾。
- [x] Core/App/PinyinAlias 测试全绿；新增边界/黄金表用例覆盖。（Core 2222 带 `ExplorerStaInvokerTests`/`UpdaterRollbackTests` 过滤、App 2240、PinyinAlias 238）
- [x] 设置文案与实现一致；`search-ranking.md`、`index-rules.md`、README 已更新。
- [x] 日志可开/可关，字段不含原始查询/路径，30 天保留。（`RankingEventLogTests`、设置页开关、§5.5）

## 12. 参考

- Mozilla, Firefox urlbar ranking / Places frecency：<https://firefox-source-docs.mozilla.ac.cn/browser/urlbar/ranking.html>（英文原站当时被网络策略拦截，使用中文镜像；frecency 权重、最近 10 次采样、每日 0.975 衰减、adaptive input history 的 0.9/上限 10 均取自该页）。
- 现行排序与两窗差异：`search-ranking.md`。
- 索引规则语义：`index-rules.md`。
- 匹配质量键实现：`Core/SearchIndex/MatchRank.cs`、`Core/SearchIndex/HighlightMask.cs`。
