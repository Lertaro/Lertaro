# 搜索排序

生产内核为 V3：[RankPolicy](../Core/SearchIndex/RankPolicy.cs) 供快速/内联窗口使用，[RankedSearchResultComparer](../Core/SearchIndex/RankedSearchResultComparer.cs) 排完整窗口的预装饰行。比较结构化键；诊断分数不参与跨档换算。

## 先分清召回与排序

- 找不到候选，先查 provider、索引约束和预算；提高排序权重不能补回未召回的行。
- 快速窗口合并索引、历史关联、Recent、收藏和可搜索项；即时插件结果与动作是列表前缀。完整窗口的内容 provider 同样是排序外前缀。内联窗口另有当前目录近邻分组，不能只看全局排序器推断全列表。
- 同一规范化路径只保留一行，可执行的 Application 表示持有 UI 行和执行委托；合并更强文本证据、收藏、历史频率及来源。学习关联不能冒充直接文本命中。
- 无范围全局查询在主查询结束、且没有 `.lnk/.url` 时才补查快捷方式；显式路径或目录范围不补无关优先目录。不要恢复每次按键无条件第二遍扫描。
- 快速窗口默认显示上限与引擎候选预算是不同层。改候选倍率、过滤位置或补查范围，必须同时验证召回和首屏成本，见 [search-performance.md](search-performance.md)。

## 排序边界

| 段，按先后 | 进入条件 |
| --- | --- |
| `Pinned` | High + AlwaysTop，或具备合格强文本证据的收藏 |
| `Boosted` | 非 Low 的文本命中 |
| `Low` | Low 规则下的文本命中 |
| `HistoryTail` | 仅靠查询关联召回的行 |

置顶判定在 Low 之前，因此强匹配收藏可以进入 Pinned。其余偏好遵守证据等级，普通 High 和历史频率不能跨级压过更强文本证据。

| 证据等级，按先后 | 含义 |
| --- | --- |
| `ExactIdentity` | 直接名称/词干完整命中，或合格的 provider 关键词前缀 |
| `DirectStrong` | 强直接命中，包括合格的拼音读法 |
| `DirectApproximate` | 散点、稀有读音等弱直接命中 |
| `SecondaryField` | 目标、元数据、provider 别名或无名称回退 |
| `LearnedAlias` | 学习到的查询关联 |

目录身份有条件降级：快速窗口在其他内建类型排在文件之前时，把精确同名目录降为 `DirectStrong`；文件类型排第一时保留其 `ExactIdentity`。完整窗口仍将目录身份降为 `DirectStrong`。不能再写“所有目录永远没有身份等级”。

段内完整键序以比较器为准。最容易误改的是快速窗口的质量顺序：**位置质量 → 类型顺序 → 覆盖率与读音层级**。连续性、形状和起点仍优先；同位置下，短文件名的覆盖率不能压过用户配置的“应用优先”。多词连续性允许每个词各自连续。

类型只区分应用、系统设置、文件三个内建桶，其他 provider 共用其后的一个秩。不能对某些行对跳过类型比较：与覆盖率组合后可能形成比较环。公共尾键最终以路径和引擎 tie 决胜，必须保持反对称和传递性。

默认索引规则把系统设置合成路径降入 Low。关键词可以跨语言召回，但不能据此压过 Boosted 的直接名称命中；合成路径使用 provider 的 CLR 类型名，不能改成本地化显示名。

## 历史反馈与证据复用

- `search-history.json` 的使用频率与 `search-query-associations.json` 的查询关联分开保存。频率按 90 天半衰期衰减；H5/H3 的快速窗口配额为 3/10，内联为 1/3，完整窗口不设配额。
- 查询关联按规范化键精确查找，至少 3 次自然打开才可跨名召回；从 LearnedAlias 再次打开不增加自然打开次数。Low 目标不进入关联召回，每次最多补 1 条，避免自强化。
- 匹配、排序与高亮共用胜出的 provider/tier/span 证据。带空格拼音的拼接分支见 [pinyin-matching.md](pinyin-matching.md)。
- 完整窗口只收到打包质量键，未携带拼音胜出分支；某些拼接胜出行在快速窗口是 ExactIdentity、完整窗口仅 DirectStrong。加上类型秩、历史配额和质量量化差异，不能承诺两窗完全同序。

## 反馈与验证

应用不写持续排序日志。按需导出本页排序详情包含明文查询、名称与路径；进入回放夹具前必须脱敏。回放入口在 [Tests/Core/SearchRanking](../Tests/Core/SearchRanking)，生产键比较测试在 [RankPolicyTests](../Tests/Core/SearchIndex/RankPolicyTests.cs)。

新增排序案例至少区分：候选缺失、同路径合并错误、证据降级错误和段内顺序错误。不要为单一案例新增跨段加分；旧 V2 回放仅用于对照，不是第二条生产策略。
