# 拼音匹配

现行实现由 [PinyinTermMatcher](../Plugins/PinyinAlias/PinyinTermMatcher.cs) 在查询时运行位并行读音 NFA，[PinyinNameEvaluator](../Core/IndexV2/Search/PinyinNameEvaluator.cs) 负责名称证据。索引不烘焙读音组合；字符预筛掩码包含可能读音的字母，字符串别名保留繁简转换等表示。

## 规则与边界

- 拼音词为 2–63 个 ASCII 字母或数字，且至少有一个字母；名称最多处理 512 个码点。单个字母不进入拼音匹配。
- 一个词对应名称中的连续位置，每个位置至少消耗一个字母。汉字可用读音的非空前缀，ASCII 按字面；不能跳过中间字符。
- `pj / pjia / piji / pingji` 可以匹配“评价”；`glzs` 不能跳过“管理用户证书”中间的字符。不同块需要用空格分词。
- 常用读音分支接受字表前两个高频读音，第三个及以后属于稀有读音。它不查词表，不能把逐字频率顺序解释成词义消歧。
- 同一个可由拼音解释的词，在精确和模糊模式下保持读音规则一致；普通字面、字符串别名与混合字母回退仍有自己的边界，不能推广成“所有查询两模式同答”。

## 别名不能绕过精度

繁转简等同文种别名不是音译，不应获得精确模式下的第二种散点读法。反例是 `zbl` 对“著作zsbsls.txt”：别名中 z/b/l 的跨度很短，紧凑度门仍会放行，但名称层已经拒绝该词。正确判断在 `AliasMatchRules.IsTransliteration` 与带 alias 的 `ReadingFor`，不是继续调跨度阈值。

排除词只否决候选自己的名称，不从读音或别名获得另一种解释。名字包含真实 `|` 时分段判定，不能跨标题分隔符匹配；别名编码也要区分真实竖线与内部读音分隔符。

## 同一份匹配证据

至少两个合格 ASCII 正词可同时评估逐词分支和同 provider 的去空格拼接分支；OR、排除、显式操作符、跨 provider 或超长形态不应强行拼接。保留逐词语义，由实际匹配质量选出胜者。

排序与高亮复用胜出分支的 provider/tier/span，避免重新匹配时选到另一段。文件匹配缓存还要按查询、精度、优先级、禁用别名集合和 provider revision 失效；完整窗口缺少胜出分支信息的限制见 [search-ranking.md](search-ranking.md)。

[IAliasLiveMatcherProvider](../PluginSdk/Abstractions/Plugins/IAliasLiveMatcherProvider.cs) 的 `GetReadingSets` 返回可复用线程缓冲区，可能长于文本，且下次同线程调用后失效；跨调用保留前必须复制有效部分。

## 留下的教训

- 不要枚举多音字组合再按长度或字节预算截断。预算可能同时消灭全拼与简拼，使整条名称静默失去召回；重建索引只会重复生成相同缺口。
- 比较编码大小前先证明两份表示覆盖同一读音空间；较小的表示可能只是截断丢失了读音。
- 质量改善先测误召回形状。重新加全局跨度或分数门，可能把名称中段真实缩写与巧合匹配一起删除。
- 覆盖率按去扩展名的词干计算，避免同名文件仅因扩展名比文件夹吃亏。
- provider 版本指纹变化会触发后台重新生成别名。说明这次压缩的成本，不轻易要求用户手动重建索引。
- 旧网格架构的存储量、召回率和耗时不适用于当前 NFA。真实大盘延迟与误召回仍需当前实现测量，不能外推早期小语料或竞品机器数字。

验证入口：[PinyinSyllableRuleTests](../Tests/Plugins/PinyinAlias/PinyinSyllableRuleTests.cs)、[PinyinExactModeTests](../Tests/Plugins/PinyinAlias/PinyinExactModeTests.cs)、[PinyinRobustnessTests](../Tests/Plugins/PinyinAlias/PinyinRobustnessTests.cs)。新增案例同时检查引擎、直接匹配接缝与高亮；长名、半截读音、同文种别名及反例都要保留。
