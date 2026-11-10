# regex 分支增量审计

2026-11-10。比较基线为本地 `main`：`5f735e70`；原本地 `feat/search-regex` 为 `2b2a0e50`，rebase 后审计树为 `f7e84517`。只报告分支相对 main 新增或改变的搜索行为，main 已有的候选倍率、读锁背压、快捷全快照排序不重新算作本分支缺陷。完整样本见 [search-regex-incremental-results.json](search-regex-incremental-results.json)。

## rebase 的收口

37 个本地分支提交已重放到 main。备份引用 `backup/search-regex-before-rebase-2b2a0e50` 保留原始树。起始分支落后已有 `origin/feat/search-regex` 9 个提交；本次按用户指定处理当前本地分支，没有 fetch、pull 或推送远端。

冲突逐块合并，保留了 main 的候选过滤、category、取消、稳定 Top-N、强类型 token 排序、缓存范围/过期和 UI 更新改进。regex 的新解析器、名称级正则子句、SDK 取消重载、provider 故障隔离也保留。`LiveScanCache` 接入 main 的 direct/recursive 分离、两秒过期与显式失效；冷请求保留只匹配一次。退役的 SearchQuerySortParserTests 经用户批准删除，将 main 的连续路径空格回归迁移到 QueryTokenScannerTests，并使扫描器保留普通文本内部空白。两边的 token dispatcher 测试均保留，旧排序测试改用 `<s`/`>s`。

审计发现的下列缺陷尚未修复；本次产品改动限于让 rebase 同时保留两边已存在的语义和修复。不能把“测试通过”解释为下列缺陷已不存在。

## 发现

### R1 高：插件目录的名称级条件仍能被别名绕过

`App/ViewModels/Search/Mapping/SearchableItemMapper.cs:165`、`:177` 对 Alias/ProviderAlias 分别调用 `query.RankWithEvidence(alias, ...)`，正则与排除条件因此改在别名文本上执行；在返回最佳别名前没有对 Title 做统一否决。关键词前缀分支的 `ExclusionsPass(Title)` 位置更晚，保护不到这两个分支。

实际复现：Title=`blocked.txt`、CuratedAlias=`report.pdf`，查询 `report /\.pdf$/` 返回匹配；Title=`temp target`、Alias=`report`，查询 `report :temp` 也返回匹配。这和分支在索引/拼音层已建立的“`:term` 与 `/.../` 只回答候选自己的名称”契约冲突。main 没有这种正则语法；`:term` 在本分支首次成为该条件，属于增量语义缺口。

建议：在目录入口按原始 Title 验证名称级子句，再让别名只回答正向词；与索引统一，而不是把整条 query 对每个别名再跑一遍。补包括 curated、shortcut target、烘焙别名与关键词兜底的跨层对照。

### R2 高：8,000 条路径正则上限按到达顺序丢行

`StreamingResultAccumulator.cs:240` 在排名装饰和合并之前，以 `_ranked.Count + chunk.Count` 截断。第一批 8,000 行到达后，第二批排名更好的 `report-best.txt` 仍被丢弃。真实 accumulator 复现：最终 8,000 条、`RegexLimitExceeded=true`、最佳行不存在。该限额随后还先于 token 过滤，可能把满足阈值的后续行全部排除。

上游 `SearchQueryDispatchController.cs:210` 请求 8,001，SearchService 仍将本地预算乘 4；因此 8,000 上限既不保证保留最终最佳集合，也没有把服务端工作量限制到 8,000。建议明确它是展示上限还是候选上限；若承诺最佳 8,000，必须在统一排名后维护有界集合；若只承诺前到的 8,000，需要明确产品行为，不能借“排序”暗示全量最优。限额本身是分支已记录的产品决定，本次没有擅自删除。

### R3 高：回退正则只有单候选预算，没有查询总预算

`RegexClauses.cs:132` 对 lookaround/backreference 回退到 250ms 的 Compiled 引擎，`AllMatch` 超时后仅当作当前候选不匹配。用 `/(?=(a+)+$)/` 的等价锚定形式 `/^(?=(a+)+$)/` 对 4 个 `a×30 + !` 的候选串行调用，三轮中位约 **995.6ms**。每个候选重新支付预算；日志节流只减少日志，不能停止这些计算。主搜索可在多个 worker 上累加 CPU，UI 高亮也使用同一单候选预算。

建议：增加查询级超时/失败额度，并让取消进入子句执行边界；定义一次模式超时后是终止查询还是明确显示不完整。不能只降低日志频率，也不能宣称 NonBacktracking 的优先路径保护了所有模式。这里是受控病理模式实验，没有观察到生产攻击或实际死锁。

### R4 中：高亮每行重新解析正则，绕过已有计划缓存

`TextHighlighter.cs:143` 每次 Split，`:160` 的 ClauseMask 重新包裹 `/.../`、拼接字符串、`FuzzyQuery.Parse`，而 PlanFor 只缓存去掉正则后的文本。Regex 对象本身有缓存，所以**不是每行重新编译 Regex**；重复的是分割、分配、构建 FzfPattern 和必需字面量分析。

10,000 次掩码：普通 `report` 在 main/分支都分配约 960,872 B；分支 `report /\.txt$/` 分配 **13,365,992 B**，约 13.9 倍。每 1,000 次掩码中位 0.579→1.999ms。建议把解析好的正则查询对象一并纳入完整查询计划，key 同时包含 main 已增加的匹配配置/别名 revision；避免破坏内联大小写标记。

### R5 中：实时目录不再识别裸盘符词

分支将 `LiveDirectorySearcher.MatchAndStream` 改成 `FzfPattern.ParseText(query, regexes)`（`:163`），丢失 Parse 中剥离裸盘符的处理。相同 `z: report`：main 实时返回 `Z:\Archive\report.txt`，rebased regex 实时返回 0；两边索引均有结果（本语料截断到 204）。显式目录范围不意味着输入中不能再含盘符，现有解析契约仍宣称支持裸盘符。

建议区分“由完整查询传入的 text”与“从路径提取的尾部片段”，在完整查询路径保留盘符识别和来源限制，同时携带正则。不能全局将 ParseText 改成 Parse 而不检查路径片段语义。

### R6 中：普通文本归一化改写了正则正文

`FzfPatternParser.cs:14` 与 `SearchQueryParser.cs:7` 在摘出正则之前 Normalize 整条查询；匹配名称也使用普通搜索的归一化。实际：`/^Ａ$/` 可以匹配半角 `A`，而 ` /\uFF21/ ` 对全角 `Ａ` 不匹配。两种表达同一全角字符的 .NET 正则，在本分支结果不同；直接正则对后者原名本应匹配。

建议先保留正则原文，再归一普通词；明确正则匹配原始名称还是归一化名称，并使语法中的字面字符与 Unicode escape 结果一致。不要把这项写成普通词全角折叠有问题，普通词折叠是原有预期。

### R7 中：LiveScanCache 的退役扫描仍脱离等待集合

新增缓存为每次扫描创建 linked CTS，注释明确不 Dispose；满额 eviction、显式失效和过期只从 `_scans` 移除。`Dispose` 在 `:162` 只获取仍在字典中的任务，因此不覆盖被移出的活动任务。实测 maxEntries=1：第一扫描被第二项挤出，Dispose 返回时第一任务仍未完成，之后才结束。原始记录的 `disposeMs` 包含随后等待退出的时间，只能用 `completedOnReturn=false` 判断该行为，不能误称 Dispose 自身用了约 150ms。

main 本来也没有“关闭后等待全部扫描”保证；这里报告的是分支新增保证没有覆盖 eviction 的实现缺口，而不是 main 已解决的回归。linked CTS 不释放还会把子源保持到 owner 结束，具体内存增长未采样。建议分别跟踪活动任务与缓存结果，完成时释放 CTS；Dispose 的同步最长两秒等待仍需注意 UI 调用路径。

### R8 低：阈值校验的失败策略不一致

内置 provider 认领 `<s>garbage` 后不做过滤，保留文件和目录；`<f>wrong` 则只保留文件。二者都不是用户输入的有效阈值。分支源码明确选择大小/日期解析失败时“不缩小结果”，所以这是需要产品确认的宽松语义，不将其伪装成未定义异常。建议统一非法阈值与键入中间态：临时不完整可只排序，最终非法表达应能提示，避免看起来像条件已生效。

## 旧语法与实现完整性核查

- 已删除生产侧 `SearchQuerySortParser`、`MergeQuotedPhrases`、`FzfTermKind.Prefix/Suffix/Equal/ExactBoundary`，索引、公开 FuzzyMatcher、目录、CLI 不再保留第二套这些旧运算符。`!`、`'`、`^`、`$` 在普通查询中按字面处理。
- QuickPanel 源过滤仍接受 `!glob`，并将旧 `:@doc`/`@doc` 迁移为 `\doc`；这是该设置字段的明确兼容规则，不能当成查询语法没有删干净。`SearchCategoryClassifier` 等未改动文件中仍有旧 `:@` 注释，本次不计作分支功能缺陷。
- 原分支的 token 扫描压缩连续空格问题在 rebase 时已按 main 的路径回归收口；普通路径内部连续空格保留。只有原测试文件被删除，测试行为没有丢弃。
- 新 SDK 的取消重载已真实接到 QueryTokenDispatcher；main 的取消等待、批次失效和迟到异常观察保留，不能继续引用旧报告中“未传取消”的结论。
- 日期两位年已显式使用 20xx 规则：本次 `49.1.1`→2049、`50.1.1`→2050、`99.1.1`→2099；未发现世纪窗口回退。路径正则限额有状态提示，但 R2 的候选选择仍未完成。
- 名称级正则在 base/delta/alias/拼音/祖先补全已有专门否决和测试；R1 是目录候选的另一入口。不要把该缺口扩大为“所有别名搜索都漏过滤”。

## main 对照测量

从本地 main 导出只读源码到 workspace scratch，独立构建 main 产品；为避免继承 regex 根目录的 CPM，main 参考构建明确 `ManagePackageVersionsCentrally=false`，包版本仍取 main 自身项目。探针直接引用两边产品 DLL，未重写匹配器。Release、SDK 10.0.403、Windows 10.0.26300、12 逻辑处理器，进程级 `DOTNET_TieredCompilation=0`；3 对进程交替顺序，共每场景 30 样本。

索引语料固定 60,000 文件：6,000 个 report 名称、54,000 个 notes 名称，查询 limit=204，预热 3 次、测 10 次。普通查询比较相同语义；正则查询只展示分支绝对成本，main 没有相同语法，因此不能直接称为相对 main 的回退倍数。

| 查询 | main 中位 ms | regex 中位 ms | regex p95 ms | 每查询分配 B（main→regex） |
| --- | ---: | ---: | ---: | ---: |
| `report` | 3.399 | 3.503 | 6.968 | 2,058,215 → 2,070,027 |
| `rprt` | 5.782 | 5.220 | 7.718 | 2,631,494 → 2,672,774 |
| `archive report` | 1.463 | 1.417 | 2.386 | 1,381,112 → 1,394,626 |
| `/report/` | 不适用 | 5.486 | 9.491 | 5,054,224 |
| `/report.*\.txt$/` | 不适用 | 5.770 | 8.379 | 5,061,398 |
| `report /\.txt$/` | 不适用 | 9.128 | 12.151 | 7,422,882 |
| `/.*[0-9]/` | 不适用 | 30.850 | 32.397 | 39,647,785 |

全部测量返回 204 行。普通查询没有证据显示大幅回退；无必需字面量正则会扫描更多唯一名称、物化/精排更多候选，显示 204 行并不代表只处理 204 个。R3 的病理回退模式不包含在上表中。

高亮预热 100 次、每样本计算 1,000 个相同文本的 mask，3 轮各 10 样本。普通 report：main 0.314ms、分支 0.579ms；分支带扩展名正则 1.999ms，纯正则 1.888ms。分配按每轮 10,000 次报告，详见 R4。它不测 WPF Run 创建、屏幕绘制或用户首屏。

## 扫描执行清单与验证

范围是相对 main 修改/新增的 49 个搜索生产 C# 文件，计数为这些文件的模式命中，不是新增问题数：无比较参数字面量 IndexOf/StartsWith/EndsWith/Contains=0；Substring=7、Replace=3、三连 Replace=0；async void=0、GetAwaiter.GetResult/Wait=0、ValueTask=0；stackalloc=4；无文化大小写=0、params=0、char LINQ=0；CompiledRegex=2、GeneratedRegex=0、显式 new Regex=5；static Dictionary=1/FrozenDictionary=0；new List=52/new Dictionary=12；Select/Where/Cast/Take/Aggregate=22、排序/分组=8、Any/All=11、ContainsKey=1；HttpClient/JsonOptions/JSON 调用=0；普通类 sealed=14/非 sealed=4。字面配方不覆盖 target-typed new 或 Task.WaitAll；所以不能由同步等待命中 0 推断不存在阻塞。

审计没有把动态用户正则建议改为 GeneratedRegex；编译缓存有 256 条上限和活动查询弱键保护。名称唯一值扫描、ASCII 快路、main 稳定 Top-N、取消覆盖和 UI 更新优化均保留。模式命中经过调用链复核，真正风险列为 R1–R8：高 3、中 4、低 1。

验证结果：Core 全套 **Passed: 2710, Failed: 0, Skipped: 0**（串行设置）；App 全套 **Passed: 2653, Failed: 0, Skipped: 0**；内置 token 相关 **Passed: 100, Failed: 0, Skipped: 0**。Release 产品解决方案构建成功，0 警告/0 错误。最初沙箱 Core 搜索跑有 1 个 ACL 用例失败，非沙箱复跑相关 841 项通过；不改 ACL 测试断言来掩盖环境差异。测试工程曾有 NU1900 或既有 nullable 告警，不代表漏洞数据请求验证成功。

CLI 与 SDK 最初也有受限环境失败：CLI 不能读用户设置，SDK 两个 shell:Profile 解析用例受身份/桌面环境影响；非沙箱复跑分别 **Passed: 28, Failed: 0, Skipped: 0** 与 **Passed: 184, Failed: 0, Skipped: 0**。

本次用只读探针确认缺陷，未把“现状错误结果”写成永久通过的产品测试。探针源码、执行脚本、main 参考树、TRX、binlog 在 `scratch/regex-incremental-audit/`；持久化 JSON 留有每轮样本和实际产品 SHA-256。审计只覆盖明确的增量问题，不声称穷尽所有正则/文化/文件系统组合。

## 提交整理

按模块重组全部相对 main 的增量，依次为：依赖与项目、SDK 契约、搜索核心、实时目录、插件 token、通知生命周期、搜索 UI、设置校验/迁移、IPC/CLI、安全相关测试及适配器测试整理、本地化文档、增量审计记录，共 12 批。跨层语法依赖以最终树整体构建和回归验收，中间每个模块提交未分别构建。

重组前保留原始分支与已 rebase 审计树，重组后要求最终 Git tree 与整理前快照完全一致。历史整理仅改提交边界、顺序与说明，不借此混入额外业务修复；没有推送远端。
