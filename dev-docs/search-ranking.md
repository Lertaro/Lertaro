# 搜索排序

当前使用 V3：[RankPolicy](../Core/SearchIndex/RankPolicy.cs) 服务于快速和内嵌窗口，[RankedSearchResultComparer](../Core/SearchIndex/RankedSearchResultComparer.cs) 比较完整窗口预计算的字段。诊断分数用于解释，实际顺序由排序字段决定。

## 召回与列表组成

- 候选缺失时先查 provider、索引约束和预算，排序只处理已经召回的行。
- 快速窗口合并索引、历史关联、Recent、收藏与可搜索项，即时插件结果和动作放在前缀。完整窗口的内容 provider 也在排序外；内嵌窗口另有当前目录近邻分组。
- 同一规范化路径只保留一行，可执行的 Application 表示持有 UI 行和委托；合并更强文本证据、收藏、频率和来源。学习关联保留自己的证据等级。
- 无范围全局查询结束且未含 `.lnk/.url` 时补查快捷方式；显式路径或目录范围限制补查范围。
- 快速窗口显示数量与引擎候选预算分别维护。修改倍率、过滤位置或补查范围时核对召回与首行成本，见 [search-performance.md](search-performance.md)。

## 分段与证据等级

| 分段，按先后 | 进入条件 |
| --- | --- |
| `Pinned` | High + AlwaysTop，或有合格强文本证据的收藏 |
| `Boosted` | 非 Low 的文本命中 |
| `Low` | Low 规则下的文本命中 |
| `HistoryTail` | 仅靠查询关联召回 |

置顶判断先于 Low，强匹配收藏可进入 Pinned。其余偏好遵守证据等级，普通 High 与历史频率在等级内生效。

| 证据等级，按先后 | 含义 |
| --- | --- |
| `ExactIdentity` | 直接名称/词干完整命中，或合格的 provider 关键词前缀 |
| `DirectStrong` | 强直接命中，包括合格拼音读法 |
| `DirectApproximate` | 散点、稀有读音等弱直接命中 |
| `SecondaryField` | 目标、元数据、provider 别名或无名称回退 |
| `LearnedAlias` | 学习到的查询关联 |

快速窗口中，应用或系统设置排在文件夹之前时，同名目录降为 DirectStrong；前面只有文件或没有其他类型时保留 ExactIdentity。内嵌与完整窗口的同名目录按 DirectStrong 排序。

文件使用类似规则：快速窗口在其他内建类型排在文件之前时，仍处于文件类型秩的精确同名文件降为 DirectStrong，同位置下按类型顺序排列。例如 NVIDIA app 可排在 nvidia.ico 前。借用应用类型秩的快捷方式保留 ExactIdentity；内嵌与完整窗口不对文件降级。

普通结果在同一有效匹配等级内，先比较优先级、历史档位与分数，再按**位置质量 → 类型顺序 → 覆盖率与读音层级**排序。位置质量包含连续性、形状和起点，多词允许各词分别连续。其他分段的字段顺序见比较器。

类型秩区分应用、系统设置、文件和文件夹，其他 provider 共用其后的秩。各来源目录均使用文件夹秩；旧顺序缺文件夹时将其插在文件后。类型触发符的文件与文件夹分别筛选。类型比较对所有行对使用同一规则，避免条件跳过与覆盖率比较形成环；尾键按路径和引擎 tie 决胜，保持反对称与传递性。

默认索引规则将系统设置合成路径降入 Low。跨语言关键词可以召回，仍按该分段排序；合成路径使用 provider 的 CLR 类型名。

## 历史反馈与匹配证据

- `search-history.json` 保存使用频率，`search-query-associations.json` 保存查询关联。频率按 90 天半衰期衰减，H5/H3 快速窗口配额为 3/10，内嵌为 1/3，完整窗口无配额。
- 查询关联按规范化键精确查找，至少 3 次自然打开才跨名召回；再次从 LearnedAlias 打开不增加该次数。Low 不进入关联召回，每次最多补 1 条。
- 匹配、排序与高亮共用胜出 provider/tier/span。拼音拼接见 [pinyin-matching.md](pinyin-matching.md)。
- 查询正则按原始名称检查和着色，不参与普通词的连续性及覆盖率。路径正则的展示截断位于最终过滤排序之后，见 [search-grammar.md](search-grammar.md)。
- 完整窗口收到打包质量键，未携带拼音胜出分支；部分拼接行在快速窗口为 ExactIdentity、完整窗口为 DirectStrong。类型秩、历史配额和质量量化也会使两窗顺序不同。

## 排序反馈与回归

排序详情按需导出，包含明文查询、名称和路径，进入回放夹具前脱敏。回放入口在 [Tests/Core/SearchRanking](../Tests/Core/SearchRanking)，生产键比较测试在 [RankPolicyTests](../Tests/Core/SearchIndex/RankPolicyTests.cs)。

新增案例分别定位候选缺失、同路径合并、证据降级或段内顺序，再修改对应机制。V2 回放用于历史对照。
