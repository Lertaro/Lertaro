# 拼音匹配

[PinyinTermMatcher](../Plugins/PinyinAlias/PinyinTermMatcher.cs) 在查询时运行位并行读音 NFA，[PinyinNameEvaluator](../Core/IndexV2/Search/PinyinNameEvaluator.cs) 生成名称证据。索引的字符预筛掩码包含可能读音的字母，繁简转换等表示使用字符串别名。

## 读音规则

- 拼音词为 2–63 个 ASCII 字母或数字，至少含一个字母；名称最多处理 512 个码点。
- 一个词对应名称中的连续位置，每个位置至少消耗一个字母。汉字接受读音的非空前缀，ASCII 按字面匹配。
- `pj / pjia / piji / pingji` 可匹配“评价”；`glzs` 无法跳过“管理用户证书”的中间字符。不同块用空格分词。
- 字表前两个读音视为常用，其余为稀有；这是逐字频率规则，不做词义消歧。
- 可由拼音解释的同一词在精确和模糊模式下遵循同一读音规则；普通字面、字符串别名与混合字母回退分别按自己的精度判断。

## 名称条件与别名精度

繁简转换等同文种别名使用严格读法。反例 `zbl` 对“著作zsbsls.txt”：别名的紧凑跨度会放行散点，但名称层已经拒绝该词。判定由 `AliasMatchRules.IsTransliteration` 与带 alias 的 `ReadingFor` 决定。

排除词检查名称字面子串，正则检查原始名称，读音与别名只提供正向词证据。标题含真实 `|` 时逐段匹配，别名编码区分标题竖线与内部读音分隔符。名称条件和 OR 关系见 [search-grammar.md](search-grammar.md)。

## 复用胜出证据

至少两个纯 ASCII 字母正词可同时评估逐词分支和同 provider 的去空格拼接分支，拼接总长最多 63。OR、排除词、精度反转或盘符作用域会关闭拼接；独立正则仍在原始名称上验证。实际匹配质量决定胜者，保留逐词分支。

排序和高亮复用胜出 provider/tier/span，文件匹配缓存按查询、精度、AND/OR、优先级、禁用别名集合和 provider revision 失效。完整窗口携带证据的差异见 [search-ranking.md](search-ranking.md)。

[IAliasLiveMatcherProvider.GetReadingSets](../PluginSdk/Abstractions/Plugins/IAliasLiveMatcherProvider.cs) 返回可复用线程缓冲区，可能长于文本；下次同线程调用后失效，跨调用保留须复制有效部分。

## 经验与验证

- 旧组合算法若按长度或字节预算截断，可能同时丢掉全拼和简拼。保持算法不变时，重建索引会重现缺口。
- 比较编码大小前核对两份表示是否覆盖相同读音；较小体积可能来自漏读音。
- 新增跨度或分数门前，同时检查真实中段缩写与巧合匹配，避免一起剔除。
- `HighlightMask.RankFromMarks` 按传入文本全长计算覆盖率；`MatchEvidence` 判定文件 ExactIdentity 时才去扩展名。这两个分母分别维护。
- provider 版本指纹变化触发后台重新生成别名。修改表示时先确认自动更新与重建成本。
- 当前 NFA 的大盘延迟和误召回使用当前产物测量；旧网格的存储量与耗时保留在 Git 历史。

回归入口：[PinyinSyllableRuleTests](../Tests/Plugins/PinyinAlias/PinyinSyllableRuleTests.cs)、[PinyinExactModeTests](../Tests/Plugins/PinyinAlias/PinyinExactModeTests.cs)、[PinyinRobustnessTests](../Tests/Plugins/PinyinAlias/PinyinRobustnessTests.cs)。新增案例同时检查引擎、直接匹配与高亮，覆盖长名、半截读音和同文种别名反例。
