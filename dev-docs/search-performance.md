# 搜索性能复核与改进

核对日期：2026-11-10。修复前代码基线：`e70449d3`，工作区起初无改动。被复核材料：`scratch/search-performance-audit/report.md`（`fcf16b2e`）（未含在git追踪内）。本文是当前说明；原报告保留作历史证据，不能当作当前实现清单。

最新状态：已完成第二轮清单排查、五项局部改进与交替测量，见文末“第二轮清单排查与测量”。前面的第一轮数字和源码哈希作为历史证据保留；当前源码与第二轮样本以 [search-performance-checklist-results.json](search-performance-checklist-results.json) 为准。

## 记录前的复核

依次完成两轮检查：先按入口、调用参数、候选截断位置与展示回调核对旧报告；再用相反情形检查建议是否成立（已有路径保空格、完整窗口增量映射、无界查询仍可能由目录补查发现未索引文件、SDK 当前无取消重载）。实现方案以第二轮结论为准。压测先构建基线，再改业务代码；时间指标不作为单测门限。

| 原条目 | 修复前现状与复核结论 | 当前证据 |
| --- | --- | --- |
| 1 候选放大 | 仍成立：51→204→8,160；1,000→4,000→160,000；完整窗口无界。候选受实际索引与命中数约束，不是实测耗时。 | `SearchService`、`NameSearch`、`SearchCoordinator` |
| 2 取消覆盖 | 仍成立：名称展开、精排、路径精排、完整窗口建行和分块排序有缺口；已有匹配取消不能覆盖这些工作。 | `NameSearch`、`PathSearchFuzzy`、`StreamingResultAccumulator` |
| 3 补查 | 需改写。优先目录条件与注释不符；快捷方式补查已经排除无界查询。**不能无条件删除完整窗口的优先目录补查**：`SearchService` 对未索引目录会实时扫描，主查询未必覆盖它。 | `SearchStreamRenderer`、`ScopedSearchRunner`、`SearchServiceHelper.CheckNeedsLiveSearch` |
| 4 快捷快照重算 | 仍成立：每次快照重新映射文件和目录候选；即时 provider 缓存仍在锁内执行。尚无真实慢插件采样，不能声称已定位其耗时占比。 | `SearchResultMapper.BuildQuickResults/CollectInstantPass`、`SearchableItemMapper` |
| 5 UI 重复提交 | 仍成立：筛选前后各排序/提交一次；`ReconcileTo` 在比较前按尾部长度决定 Reset；完整窗口已有增量 accumulator。 | `DynamicFilterCoordinator`、`ObservableRangeCollection`、`SearchViewResultRenderer` |
| 6 token 召回 | 原语法过时。现为 `report :.txt`、`report :S-` 等后置处理；有限候选仍不能保证全索引筛选/排序。原大小阈值例子已不是当前内置语法。 | `SearchQuerySortParser`、`SortFilterQueryTokenProvider`、`SearchDispatchController` |
| 7 分支约束 | 仍成立。路径入口不传 category/文件名约束；路径补全漏文件名约束；实时回退漏两者；网络类别也未传到索引。folder-only 已修名称和路径补全，不能称为全面缺失。 | `IndexV2Searcher`、`PathTermFallback`、`LiveDirectorySearcher`、`NetworkIndexerSearchExtensions` |
| 8 连续空格 | 已失效：`QueryTokenScanner` 已删除；当前 suffix parser 返回原查询/原前缀，保留路径内部空格。旧 probe 引用已删文件，无法直接复跑。 | `SearchQuerySortParser.Strip`、`SearchQueryParser.Parse` |
| 9 异步 token | 快捷批次倒序覆盖与高亮累加风险仍成立。**SDK 当前只有两参数 ApplyAsync，没有取消重载**，旧报告关于 SDK 的结论已失效；完整窗口已有快照引用保护。 | `QueryTokenDispatcher`、两个 dispatch controller、`IQueryTokenProvider` |
| 10 实时回退 | `LiveScanCache` 已删除，缓存实际在 `SearchService._sessionDirectoryCache`。冷扫描后重复匹配、10,000 条枚举上限、窗口生命周期陈旧、元数据缺失仍在；直接目录场景另有专用枚举缓存，不能泛称都走递归回退。 | `SearchService`、`LiveDirectorySearcher`、`DirectChildrenListingCache` |
| 11 高亮 | 正则重复拆解已随旧语法删除。普通计划按字符串缓存仍未区分匹配配置；应针对同词改配置验证。 | `TextHighlighter.PlanFor`、`SearchContext` |
| 12 读锁背压 | 条件风险仍成立；尚无本机生产写锁停顿证据。不能用一次全量复制代替有界背压，否则改变大查询内存与首屏行为。 | `IndexV2Searcher`、`SearchStreamPump` |

## 经复核的实施方案

以下五步已依次实施、测试并复核；未覆盖的结构性工作单列在文末。

1. **减少 UI 工作**：侧栏先筛选再排序，只提交有效列表；大列表先识别不变前缀再决定 Reset；沿用既有身份比较与展示状态同步，保留选择、查询高亮、操作参数与大变化的单次 Reset。
2. **在截断前应用已有约束**：将文件名/category 贯穿精确路径、模糊路径、路径补全、网络索引与实时匹配。文件名模式仍只约束文件，文件夹通行；category 单独决定文件夹是否可见。加入小 limit、大量不合格候选、base/delta 和 OR 分支测试，避免“显示层过滤正确但召回仍错误”。
3. **停止失效工作**：补名称/路径精排、候选展开与完整窗口映射的取消检查；归还租用的工作缓冲。快捷 token 批次只允许最新结果提交，两个入口都可取消宿主等待；SDK 不支持中止 provider 本体，明确保留这一边界。高亮从本次基础查询生成。
4. **收紧可证实的范围与缓存错误**：指定路径或目录范围时不额外查询无关优先目录/快捷方式；保留完整窗口对未索引优先目录的补查能力。高亮计划识别匹配配置变化。实时冷扫描避免对已经交付的条目再次执行匹配，并按查询约束处理匹配结果。
5. **验收与复核文档**：同一机器、Release、相同语料与预热/重复次数，保留修复前测试产物；对比时间、分配量、首条结果、排序/提交/Reset 次数、条数与有序结果 SHA-256。跑受影响 MSTest 及相关工程回归，最终核对文档路径、数字、代码与已知边界。

不在没有证据时调整 4×/8×/5× 候选预算，不把完整窗口改为分页。快捷查询级缓存、慢即时 provider 解耦、通用 token 下推/补取、实时缓存失效及截断状态、锁外发送批次需要单独设计：缓存需明确 settings/catalog/index revision，插件线程模型未承诺后台安全，分页与候选完整性涉及产品/SDK 契约。保留这些问题，不把本轮局部修复写成整条链路已解决。

## 测量方法与证据

压测使用真实生产类和真实 Snapshot/DeltaOverlay，无新依赖。两份可重复运行的工作负载位于 `Tests/App/ViewModels/Search/SearchPerformanceWorkloadTests.cs` 与 `Tests/Core/IndexV2/Search/SearchPerformanceWorkloadTests.cs`，测试类别 `SearchPerformance`。Core：60,000 个文件、100 个目录，固定名称与扩展名分布；预热 3 次、测量 10 次。App：20,000/100,000 行，保留 10% 的侧栏条件、Name 排序、真实 `ListCollectionView` 消费集合通知；预热 3 次、重复 20 次。构建和语料准备不计入计时。

这是可控组件压力测试，不是运行中 App 的输入到首屏时间或真实多盘/管道吞吐量；`firstResultMs` 是索引 API 首次回调。分配量为进程总分配字节差（含并行工作），不是峰值存活内存。不会将毫秒差异直接外推成用户首屏提升。

原始 TRX、构建 binlog、修复前 DLL 与过程日志保存在 `scratch/search-performance-audit/`。可入库的全部最终样本、汇总、结果签名和产物/源码 SHA-256 在 [search-performance-results.json](search-performance-results.json)。无需依靠临时目录里的日志才能核对本文数字。

初次 Core 语料的目录/扩展名分布过度相关，已改为每目录含两种扩展名；正式基线产物为 `baseline-core-v2-bin`。App 首次编译缺少测试文件的命名空间导入，未执行测试；更正后保存 `baseline-app-bin`。这些失败与早期数据不计入最终汇总。

机器：Windows 10.0.26300、x64、12 个逻辑处理器、Intel Family 6 Model 165、文化 `zh-CN`，SDK `10.0.403`、Release。两边均设置进程级 `DOTNET_TieredCompilation=0`；3 对独立进程按 before/after、after/before、before/after 交替运行，没有同时运行构建/其他测试。Core 每场景 30 个样本；App 每规模 3 个“20 次更新”总量样本。p95 用 nearest rank；App 只有 3 个总量样本，其 p95 等同最大总量，不是逐帧 p95。

最后一次索引比较只运行 `Search_FixedCorpus`，取消比较另进程运行，避免两类测试在不同版本中执行顺序不同而扰动 GC。原始目录分别为 `comparison-index-isolated`（索引）、`comparison-verified`（取消）、`comparison-app-final`（最终 App 的 UI）；脚本是 `run-comparison.ps1`、`summarize.ps1`、`export-evidence.ps1`。取消压力使用相同反射调用器调用两版本真实精排方法：旧签名没有 token，只能继续完成；新签名传入已取消 token。该测量回答“到达精排边界时已有取消，剩余工作多少”，**不是**任意指令处取消的端到端停止时间。

可在当前代码重复执行（日志目录名应按运行另取）：

```powershell
dotnet test Tests/Core/Core.csproj -c Release --filter 'TestCategory=SearchPerformance' --logger trx --results-directory scratch/search-performance-audit/reproduce-core '-bl:scratch/search-performance-audit/reproduce-core-{}.binlog'
dotnet test Tests/App/App.csproj -c Release --filter 'TestCategory=SearchPerformance' --logger trx --results-directory scratch/search-performance-audit/reproduce-app '-bl:scratch/search-performance-audit/reproduce-app-{}.binlog'
```

与保存的基线比较时，两边均设置 `$env:DOTNET_TieredCompilation = '0'`，使用 `dotnet vstest <测试 DLL> '/TestCaseFilter:TestCategory=SearchPerformance' '/Logger:trx' '/ResultsDirectory:<输出目录>'`。只将同一测试 DLL 更新到基线目录，**保留其中原始 Core/App 产品 DLL**；两版本使用同一程序集名，不可把当前产品 DLL 一起覆盖过去。表内 SHA-256 固定了本次比较的实际文件，源码哈希是业务修改加回归测试后的状态，不含二进制构建目录。

## 第一轮实施结果（历史对照）

### 改动、原因与复核

| 实施项 | 改进前来源 | 当前实现与检查 |
| --- | --- | --- |
| 侧栏一次提交 | `DynamicFilterCoordinator.Apply` 无条件先 sort/render，再筛选后重复 sort/render | 活动筛选只提交最终有效列表；异步等待期间保留上一显示。无筛选仍同步提交。异步过滤旧快照不能提交，相关测试验证一次排序/一次渲染。没有擅自将插件委托搬到后台。 |
| 不变列表不 Reset | `ObservableRangeCollection.ReconcileTo` 在比较前用 512 行长度阈值 Reset | 从可信前缀继续比较，确定首个不同位置后再应用原 512 阈值；511/512/100,000 行不变副本零通知，小尾变更 Replace、大追加仍一个 Reset。 |
| 保留行的显示状态 | 大列表过去常被 Reset 掩盖元数据陈旧；同步状态总扫描共享前缀 | 继续传递可信前缀；同身份行更新查询、Index、操作参数等，元数据/目录标志不同则替换行，防止日期缓存过期。600 行选择保持、高亮通知、动作参数、元数据回归均覆盖。 |
| 路径与网络过滤 | `IndexV2Searcher` 路径分支、`PathTermFallback`、网络各层漏传参数 | 精确路径与模糊路径在候选进入 Top-N 前检查；路径补全覆盖 OR 分支；网络单源/多源/无界分支传 category；base、delta override、delta added 均校验。精确路径同时尊重 directoryFilter。 |
| 避免新增路径分配 | 第一版过滤修复在扫描和展开各解码、判断一次 | Phase A 每个唯一名称只判断一次，布尔结果随候选传递；通配符检查使用现有 worker 的字符缓冲，候选只存真正使用的 Score，移除多余匹配结构字段。未增加依赖或改排名公式。 |
| 查询取消 | 展开/精排/建行循环不观察 token | 查询 token 贯穿映射委托；名称展开、delta、名称/路径精排、完整窗口建行与合并检查取消，排序前后和并行分块边界检查；新增 `finally` 归还相关缓冲。已取消 accumulator 不发布半批结果。单次底层排序/插件调用仍不可抢占。 |
| token 批次与高亮 | 快捷同一查询的不同快照共享字符串/引用检查；共享行高亮 `+=` | 每批用 linked CTS，新批取消旧批；保留查询/列表身份检查；dispatcher 用 `WaitAsync` 停止宿主等待，并用既有 `KeepObserved` 观察被放弃的异步 provider 的迟到异常（同步完成不额外挂 continuation）；高亮由 cleanQuery 重建。完整窗口与 App 搜索管道也传取消。真实快捷控制器测试故意让旧快照晚于最终快照完成，验证仅提交最终两行及最终状态。SDK 契约未变。 |
| 补查范围 | 优先目录条件与注释不一致；路径请求仍可能补查 | 指定目录、路径模式或自定义范围不补无关优先目录；快捷方式也排除路径模式。完整全局查询保留优先目录补查，避免丢失实时回退才找到的内容。 |
| 冷目录重复计算 | 扫描边匹配，完成后又 `MatchAndStream`；ConcurrentDictionary 的 factory 可竞争启动多次扫描 | 缓存值改为 `Lazy<Task<List<SearchResult>>>`，只有获胜 Lazy 启动枚举；冷调用只交付一次，后续调用对原始缓存按自身约束匹配。取消查询停止回调但服务仍可填充缓存。实际临时目录测试验证冷/热各 20 条且不重复，冷查询取消后热查询仍取得全部 20 条。 |
| 高亮计划 | key 只有查询文本 | key 增加模糊开关与 AND/OR 优先级；同词从模糊改精确不再复用旧计划。别名 provider 动态变化的失效仍是文末待办。 |

### 最终性能对比

UI 数字是 **20 次连续更新总耗时**，不是单次搜索首屏。每次最终只显示 10% 的筛选结果，使用真实 WPF `ListCollectionView`，未打开真实窗口。

| 工作负载 | 修复前中位总耗时 | 修复后中位总耗时 | 每次更新分配字节（前→后） | 20 次的排序/提交/Reset（前→后） |
| --- | ---: | ---: | ---: | --- |
| 20,000 行，保留 2,000 | 522.453 ms | 48.206 ms（-90.8%） | 634,368 → 72,858 | 40/40/40 → 20/20/0 |
| 100,000 行，保留 10,000 | 3,310.853 ms | 353.152 ms（-89.3%） | 3,162,772 → 360,858 | 40/40/40 → 20/20/0 |

两规模最终路径顺序的 SHA-256 在所有前后轮次一致。分配降低约 88.5%/88.6%。这是删除一次全量排序和无效提交的收益，不能推导其他查询也加速 9–12 倍。

索引数字为单次查询中位值，分配为每次查询字节数。无界是 `int.MaxValue`，有界是直接调用索引的 204（相当于普通快捷 51×4 的本地预算）。

| 查询 / 约束 | 总耗时 ms 前→后 | p95 ms 前→后 | 首条回调 ms 前→后 | 分配字节前→后 | 结果 |
| --- | ---: | ---: | ---: | ---: | --- |
| `report`，204 | 10.381 → 10.569 | 12.169 → 12.552 | 10.301 → 10.483 | 11,049,038 → 11,042,751 | 204，签名一致 |
| `report`，无界 | 94.647 → 101.849 | 119.906 → 129.889 | 34.425 → 36.886 | 68,970,297 → 68,760,311 | 60,000，签名一致 |
| `archive report`，无界 | 105.428 → 107.017 | 141.178 → 137.829 | 13.276 → 13.533 | 62,086,326 → 62,289,354 | 60,000，签名一致 |
| `archive\report`，无界 | 121.344 → 127.532 | 149.944 → 165.066 | 64.136 → 66.493 | 62,309,953 → 59,546,817 | 60,000，签名一致 |
| `archive\report`，204，`*.txt` | 13.813 → 14.857 | 16.253 → 22.032 | 13.733 → 14.666 | 11,068,130 → 9,492,706 | 合格文件 0 → 204 |
| `z:\archive000\`，204，`*.txt` | 0.119 → 0.175 | 0.131 → 0.181 | 0.053 → 0.109 | 252,421 → 301,013 | 合格文件 143 → 203，另有目录自身 1 条 |

**本轮没有证明索引吞吐量提升。** 无过滤有界查询基本持平，无界名称场景本轮约慢 7.6%，路径无界约慢 5.1%；检查取消增加成本，调度/GC 也有噪声，不能把差异全部归于某一个原因。前两次对照里名称无界分别是 94.469→90.558、80.460→94.898 ms，方向不稳定；全部历史汇总留在 JSON。最终隔离测量补记各代 GC 次数以便后续归因。过滤场景的时间增加同时换来了正确召回，不能拿错误结果的低耗时当作优化目标。

首版过滤修复曾测得路径过滤分配 20,194,902 B/次，第二版 16,652,354 B/次；复核发现重复名称解码并移除后，最终为 9,492,706 B/次，低于基线约 14.2%。保留这条回退证据，避免只报告最后的有利数字。

| 已取消的 60,000 候选精排 | 剩余耗时中位 ms 前→后 | p95 ms 前→后 | 每 20 次仍被重写的候选数前→后 |
| --- | ---: | ---: | ---: |
| NameSearch | 24.348 → 0.021 | 29.700 → 0.061 | 1,200,000 → 0 |
| PathSearchFuzzy（仅文件部分） | 17.210 → 0.021 | 20.172 → 0.053 | 1,200,000 → 0 |

取消比较包含反射与异常成本；数据准备不计时。未覆盖正在执行的 DP 单次匹配、排序内部、管道/读锁等待和慢 provider 自身停止时间。

### 结果呈现与当前基线是否一致

- **保持一致的部分**：四组无过滤索引结果的路径、目录标志、排序键和顺序；两规模 UI 最终路径顺序；正常查询的数量限制和完整窗口的全量语义。行保留时查询高亮、操作参数仍更新；元数据变化会刷新日期；小变化仍使用细粒度通知，大变化仍单次 Reset。
- **有意修正的差异**：路径/网络/实时分支不再返回违反已有约束的候选，之前被挤掉的合格结果可出现；活动侧栏筛选不再短暂展示未筛选的全量列表；异步 token 旧批次不再覆盖最终快照，高亮不再重复追加；显式范围不再混入无关补查。
- **不能声称完全确定的部分**：有界模糊路径的过滤结果虽每轮均为 204 个合格文件，但同分截断时多个轮次 SHA 不同。`FzfTopN` 只按 SortKey 保留边界，路径并行 worker 合并顺序会影响同分候选选择；原代码已有这个约束，过滤后候选分布让它在本语料上显现。本轮未改 Top-N tie-break 语义；无界同查询结果签名一致。这里没有声称有界同分子集稳定。
- **未进行的验证**：没有启动用户正在使用的 App/服务，没有替换安装产物、重建索引或测真实多盘；没有屏幕截图/真实窗口逐帧视觉比较。呈现验收是 WPF 集合视图、行状态与高亮/选择/异步控制器测试，不能替代真实 UI 首屏测试。

### 测试与构建

当前产品解决方案 `dotnet build Lertaro.slnx -c Release`（附唯一 binlog）成功，0 警告、0 错误。App 测试构建有既有设置测试 nullable 与未使用事件告警，未改无关代码消警。

| 验证 | Passed | Failed | Skipped | 证据目录 |
| --- | ---: | ---: | ---: | --- |
| Core 全量，单工作线程 | 2,435 | 0 | 0 | `verified-core-serial` |
| App 全量 | 2,454 | 0 | 0 | `verified-app-observed` |
| CoreExtensions QueryToken | 50 | 0 | 0 | `verified-tokens` |
| 新增真实快捷控制器乱序 + dispatcher | 3 | 0 | 0 | `controller-app-retry`（也包含在 App 全量） |

Core 默认 MethodLevel 并行时，第一次全量 2,435 全通过，最终版本全量 2,434 通过/1 失败；单独并行复跑 STA 类为 2 通过/4 失败。`ExplorerStaInvokerTests.ResetAbandonedReads_*` 用反射改共享静态 `_abandonedReads`，与同类其他方法并行时可导致立即 fallback；沙箱外复跑同样失败，不能归咎于沙箱。使用临时 runsettings 将 Workers=1 后全量通过，未修改该类、放宽超时或删断言。这个测试隔离问题与本轮搜索代码无关，仍保留记录。

网络测试和真实 QuickSearchViewModel 初始化最初分别被沙箱禁止 AppData 目录创建/设置读取；获准在沙箱外运行后通过。上述成功计数均来自实际 TRX，构建失败、未发现测试不计作通过。

精确命令：

```powershell
dotnet test Tests/Core/Core.csproj -c Release --no-build --settings scratch/search-performance-audit/serial.runsettings --logger trx --results-directory scratch/search-performance-audit/verified-core-serial '-bl:scratch/search-performance-audit/core-serial-{}.binlog'
dotnet test Tests/App/App.csproj -c Release --logger trx --results-directory scratch/search-performance-audit/verified-app-observed '-bl:scratch/search-performance-audit/verified-app-observed-{}.binlog'
dotnet test Tests/Plugins/CoreExtensions/CoreExtensions.csproj -c Release --filter 'FullyQualifiedName~QueryToken' --logger trx --results-directory scratch/search-performance-audit/verified-tokens '-bl:scratch/search-performance-audit/tokens-{}.binlog'
```

`--no-build` 仅在当前测试工程已实际构建后使用；产品解决方案不构建测试。临时 runsettings 内容为 `<RunSettings><MSTest><Parallelize><Workers>1</Workers><Scope>MethodLevel</Scope></Parallelize></MSTest></RunSettings>`。

### 扫描执行清单（第一轮快照）

相同搜索范围现有 143 个 C# 文件，原报告的 150 不能沿用。排除整行注释后的模式命中：IndexOf 字面量无比较参数 0；StartsWith/EndsWith 2；Contains 0；Substring 24；Replace 21；三连 Replace 0；无文化 ToLower/ToUpper 0；params 0；char LINQ 0；async void 0；同步等待 1；ValueTask 0；stackalloc 6；CompiledRegex 1 / GeneratedRegex 0 / 显式 new Regex 0；static Dictionary 2 / FrozenDictionary 0；new List 100 / new Dictionary 22；CurrentCulture comparer 6；Select/Where/Cast/Take/Aggregate 97；排序/分组 26；Any/All 18；ContainsKey 6；new HttpClient 0；new JsonSerializerOptions 0；JSON 调用 3；普通类 sealed 42 / 非 sealed 12。

这些是文本命中而非问题数量（target-typed `new` 不被 `new Regex` 捕获）。同步背压不是已证死锁，CurrentCulture 是展示排序约定；未按清单批量改 LINQ、sealed 或正则。实际优先级由行为复现和压力测试决定。

## 第二轮清单排查与测量

本轮继续排查（2026-11-10）：以第一轮完成后的未提交产物作为第二轮基线，原第一轮数字保留。先做复现，再复核最小改法。新增测量位于 `SearchChecklistWorkloadTests`（Core/App），原始结果在 `scratch/search-performance-checklist/`。

首轮复现确认：20,000 候选的 100 次乱序输入产生 100 种 Top-N 子集；仅列直接子项仍枚举 1,011 条却只交付 11 条，123 字节文件的 Metadata.Size 为 0；6,000 原始匹配中尾部 100 个 mp3 在 4,000 候选后过滤时全部丢失，截断前过滤得到 100；容量 1,024 的通道被消费者阻塞 150ms 时，真实 LiveIndex 写锁同步等待。以上都是可控实验，不是运行中 App 的采样。

经复核的第二轮改法：统一 Top-N 的 `(SortKey, EntryIndex)` 保留与排序边界；direct-only 枚举不递归且缓存区分模式，复用系统枚举预取的元数据；对别名注册/启用变化发布 revision，使高亮和查询级文件匹配缓存失效；快捷文件匹配仅在单查询内复用不可变证据，保持全量混合排序与新 UI 行。慢插件解耦、通用 token 协议与锁外快照先记录实测，避免静默改变全量、线程或插件语义。

### 已实施与结果一致性

1. **同分 Top-N**：`FzfTopN` 的准入阈值、quickselect 和最终排序统一使用 `(SortKey, EntryIndex)`。之前只在最后排序时考虑 EntryIndex，无法追回前面随机丢掉的同分行。增加 100 次随机打乱、四 worker 反向合并、负 EntryIndex 和 25 次真实并行路径查询的回归。固定输入现在始终得到同一子集；这会有意改变原先随机保留的有界同分子集，非同分优先级与无界语义不变。
2. **直接目录与元数据**：`LiveDirectorySearcher` 的 direct-only 分支不再将子目录入队；从枚举得到的 FileSystemInfo 填充大小和日期，文件消失或拒绝访问时保留未知元数据。`SearchService` 缓存键加入 direct/recursive，避免直接列表污染递归查询；规范化完整路径，保持根目录语义。接入现有 inline watcher 的失效调用；无 watcher 时，已完成缓存从扫描开始计时两秒后过期，进行中的扫描继续共享。冷查询取消、冷热范围互换、新增/删除后的显式失效、无 watcher 过期均有真实临时目录用例。**两秒是允许陈旧的上界，不是周期刷新**；若一次扫描超过两秒，完成后的下一查询会重扫，网络大目录仍需进一步测量。
3. **别名缓存失效**：`AliasProviderRegistry` 对注册、FilterFunc 替换和插件设置通知维护 QueryRevision；App 刷新启用组件时也递增。高亮计划和文件匹配缓存按 revision 失效，不在每行遍历 provider。测试同词禁用→启用→更改查询别名设置→禁用，字符掩码分别符合当前配置。provider 在内部私自改变规则而不通知宿主，不在该机制可观察范围内。
4. **快捷匹配复用**：新增 `SearchResultMapper.QuickFileMatchCache`，由一个查询的 dispatch 创建，只缓存 `(Name, Path, IsDir)` 的不可变 MatchEvidenceResult 和解析计划。每次 paint 仍创建新 UI 行、读取当前元数据并进行混合排序，保证动作、索引位置、history/favorite/type 等规则不被缓存冻结。query、精度、优先级、别名 revision、disabled alias 集合变化均清空缓存。此缓存不会用前一查询的截断结果推导下一查询，也不缓存最终列表。4000 文件在 20 个增长快照中的文件精排次数从 42,000 降到 4,000；**UI 建行次数仍为 42,000**。
5. **内置 token 排序**：实测后发现 `SortFilterQueryTokenProvider` 将 long/DateTime key 统一转为 IComparable，逐行装箱。改为强类型 OrderBy/OrderByDescending，保留 LINQ 稳定排序。S/C/M/A 两方向、相同 key 的原顺序、零值与最大值均验证。SDK 未变，调用仍同步完成，没有把任意插件放到后台线程。

快捷两个查询最终路径顺序 SHA-256 在所有前后轮次均为 `F80B485844707A06BCECC97AEEF373A0D3DC2D62089B5272308631A9C24C1A26`。直接目录的显示条数仍为 11，递归枚举的隐藏工作被删除；元数据由缺失改为真实值，按大小/时间筛选的结果因此可能修正。第一轮关于“有界同分路径不稳定”和“别名变化未失效”的记录是当时现象，第二轮已覆盖。

### 测量方法与最终数字

3 组独立进程按 before/after、after/before、before/after 顺序运行；两边都是 Release、`DOTNET_TieredCompilation=0`。Core/App/插件负载均禁止并行执行；只有负载本身调用的搜索并行存在。保存的基线为 `scratch/search-performance-checklist/before-*-bin`，包含第一轮修复后的产品 DLL，**不是 git HEAD 的原始产品**。两边测试 DLL 相同。完整原始样本、摘要、基线和当前产品 hash、源码 hash 在 [search-performance-checklist-results.json](search-performance-checklist-results.json)。

Top-N 每进程预热 3 次，随后 100 种固定种子的乱序输入，三轮合计每形状 300 次；直接目录预热 2 次、测 10 次，合计 30 次；快捷快照预热 2 组、测 5 组，每组 20 个增长快照，合计 15 组；token 排序预热 3 次、测 20 次，合计每规模 60 次。下面中位/p95 都来自相应样本，p95 用 nearest rank。文件创建和语料构造不计时。

| 场景 | 中位 ms 前→后 | p95 ms 前→后 | 结果或成本 |
| --- | ---: | ---: | --- |
| 20,000 候选全部同分，取 204 | 0.0679 → 0.1567 | 0.0775 → 0.2573 | 每 100 次的不同子集数 100 → 1 |
| 20,000 候选混合分值（仍有同分） | 0.1173 → 0.1698 | 0.1545 → 0.2655 | 每 100 次的不同子集数 100 → 1 |
| 直接目录：11 项，子树另有 1,000 文件 | 1.5297 → 0.1108 | 3.5382 → 0.1517 | 枚举 1,011 → 11；显示仍 11；123 B 文件元数据 0 → 123 |
| 快捷 `report`：4,000 文件，20 快照合计 | 138.2059 → 136.4694 | 154.0269 → 153.9998 | 耗时约 -1.3%，基本持平；每组分配 72,994,776 → 73,665,726 B（+0.9%） |
| 快捷 `rprt`：同上 | 150.3937 → 142.8300 | 171.7766 → 165.3668 | 耗时约 -5.0%；每组分配 74,854,792 → 71,874,838 B（-4.0%） |
| S- 排序，4,000 行，一次 | 1.2982 → 0.2898 | 1.6836 → 0.3719 | 每次分配 208,583 → 112,583 B |
| S- 排序，100,000 行，一次 | 56.5642 → 13.2201 | 72.5801 → 16.0543 | 耗时 -76.6%；每次分配 5,207,326 → 2,802,862 B（约 -46.2%） |

Top-N 增加的是确定性代价，不写成加速。快捷匹配缓存的收益有限，主要剩余成本在新 UI 行、混合排序和其他候选来源；早期一次复测 `rprt` 曾约快 10%，最终三轮约快 5%，采用最终值。缓存临时内存随单查询候选量增长，查询结束后释放。直接目录分配的原始指标包含两次预热，单列 JSON 中的 `allocatedBytesIncludingWarmups`，未拿它和其他“每次分配”混算。进程总分配差不等于峰值或存活内存。

### 其余问题的运行证据

| 清单项 | 实验与当前结果 | 结论 |
| --- | --- | --- |
| 慢即时 provider 阻塞 | gated provider 保持 150ms 不返回；文件早已就绪。当前三轮 paint 完成用时 173.281/164.697/171.356 ms，等待期间均未完成；provider 仍只调用 1 次。 | 已从源码风险升级为可复现阻塞。仅把 provider 移到已有后台 Task 并不能解决，文件 mapper 会等同一 pass 锁。需要独立完成状态、晚到结果合并和防止旧结果覆盖；本轮未实现。 |
| catalog 重算 | 10,000 条已缓存目录项，每快照调用生产 MatchCatalogEntryWithEvidence；20 快照共匹配 200,000 次。当前每次中位 3.200ms，p95 6.935ms；20 次约分配 6,401,376 B。 | 目录缓存没有消除查询匹配重算。这里未计建 UI 行、provider 首次加载或文件重查；`ProviderLoaded` 的 150ms 定时器仍直接 DispatchSearch，重查次数只核对了调用关系，未伪称完成整条 IPC 量测。 |
| token 候选不完整 | 构造 6,000 个匹配，尾部 100 个 `.mp3`；按原词取 4,000 后过滤，当前三轮都是 0；把同一扩展名传入索引截断前，三轮都是 100。 | 下推可修召回，但这是直接调用现有 fileNameFilter 的对照，不是 GUI token 已接通下推。通用 provider 的优先级/任意重排仍需契约设计。两种调用未做充分单独预热，不把它们一次耗时当性能收益。 |
| 实时目录截断 | 临时目录实际 10,020 文件，当前 ScanDirectory 返回 10,000；一次扫描约 24.985ms（默认 JIT）。 | 20 项仍被静默截掉；TTL/元数据/direct 修复都不解决完整性。此场景只验证截断，单次数字不作前后比较。 |
| 读锁背压 | 真实 IndexV2Searcher + 真实容量 1,024 channel；消费者不读，第一处 blocked write 后启动 LiveIndex.Mutate，并保持阻塞 150ms。三轮 writer 等待 158.957/165.027/164.118ms；取消后 9.213/6.077/5.994ms 内 producer 与 writer 都退出。 | 读锁延长已在受控实验中确认，取消可解除；不是生产死锁证据。保留无界查询一致快照和有界内存，尚未替换为锁外发送。 |

没有安装慢插件、停止正在运行的服务、改变真实索引或启动真实搜索窗口。以上新增“压力/等待”数据仍是进程内生产组件测量；没有声称测得用户输入到首屏、真实磁盘网络分布或实际 GC 暂停。

测量工具也经过复核：最早慢 provider 用例没声明 inline 支持，被宿主正确跳过，已改为 quick 模式，旧失败不计入结果；最早 catalog 用例每项构造 queryWords 数组，已改为每查询一次；最早 token 大小两组因 MethodLevel 并行污染进程分配计数，最终增加 DoNotParallelize，并在两版本使用同一测试 DLL 重新测量。没有用这些早期数字支持最终性能结论。

### 验证与复现

| 验证 | Passed | Failed | Skipped | 原始目录（`scratch/search-performance-checklist/` 下） |
| --- | ---: | ---: | ---: | --- |
| Core 全套（Workers=1） | 2,445 | 0 | 0 | `full-core` |
| App 全套 | 2,462 | 0 | 0 | `full-app` |
| 最后补充的路径键规范化/设置通知，Core 相关 | 46 | 0 | 0 | `final-core-targeted` |
| 最后补充的设置变化高亮/匹配，App 相关 | 22 | 0 | 0 | `final-app-targeted` |
| CoreExtensions token 行为及测量 | 60 | 0 | 0 | `final-tokens` |

产品 Release 解决方案构建成功，0 错误；Service 的 NuGet 漏洞数据请求失败，产生 2 条 NU1900 警告，沙箱外重试仍有同样警告。不能写作本轮“零警告”或“漏洞扫描已通过”；编译和测试成功不依赖该请求。

```powershell
dotnet test Tests/Core/Core.csproj -c Release --settings scratch/search-performance-audit/serial.runsettings --logger trx --results-directory scratch/search-performance-checklist/full-core '-bl:scratch/search-performance-checklist/full-core-{}.binlog'
dotnet test Tests/App/App.csproj -c Release --logger trx --results-directory scratch/search-performance-checklist/full-app '-bl:scratch/search-performance-checklist/full-app-{}.binlog'
dotnet test Tests/Plugins/CoreExtensions/CoreExtensions.csproj -c Release --filter 'FullyQualifiedName~QueryToken' --logger trx --results-directory scratch/search-performance-checklist/final-tokens '-bl:scratch/search-performance-checklist/final-tokens-{}.binlog'
```

压力测试类别是 `SearchChecklistPerformance`。交替脚本 `scratch/search-performance-checklist/compare.ps1` 为每次比较更新**测试 DLL**，不覆盖基线产品 DLL；`live-cap` 单独记录，不在交替循环中重复建删万文件。测试资料/样本在 JSON 中持久保存，TRX 与 binlog 在 scratch 中保留。

## 当前剩余工作与验收门

| 项目 | 当前边界 | 下一步可实施方案与验收门 |
| --- | --- | --- |
| 快捷 UI 建行、catalog 匹配与加载重查 | 文件精排已缓存；新 UI 行与最终排序仍每快照重算；catalog 每 20 快照仍匹配 200,000 次，加载完成仍重查文件 | 为 catalog 增加版本化的查询匹配复用；在 engine 暴露只重绘当前文件快照的路径，检查 snapshot/settings/query 版本。要求加载事件不增加文件索引查询次数，结果内容和最终顺序相同。 |
| 慢即时 provider / UI 同步变换 | provider 锁阻塞已复现；内置排序已减少装箱，但 100k 仍同步约 13ms | 将 provider 的完成信号接入流式渲染，文件页能先显示、provider 完成后合并；后台纯变换需确认线程约定、取消与不可变快照。要求 gated provider 不延后文件首屏，晚到/异常不会覆盖新查询。 |
| token 的候选完整性 | 快捷仍为有限原始候选后过滤/排序，不保证全索引最大/全部 | 内置可表达条件下推到每个来源截断前；任意插件需补取或明确候选受限状态。要求超过 4,000 个原始匹配、合格项全在尾部的用例仍召回；SDK/协议设计先单独复核。 |
| 实时目录完整性及大目录刷新 | direct、元数据、范围缓存、TTL/inline watcher 已修；10,000 枚举上限与无截断状态仍在，eligibility cache 仍按窗口存活 | 区分 complete/truncated/unavailable 状态，再讨论继续枚举和 UI 提示；测慢网络扫描超过 TTL 的重复工作。要求超上限不再静默宣称全量，枚举模式切换不漏项。 |
| 候选倍率、无界首屏和读锁背压 | 受控慢消费者已能让 writer 等待约 160ms；真实生产发生频率尚未测 | 先记录生产 channel/写锁等待分布，再设计可固定 snapshot/overlay revision 的锁外有界批次。要求全量结果一致、无无界复制、取消能释放资源；分页仍不能静默改变完整窗口语义。 |

第二轮最终记录前依次复核：确认新基线是第一轮产物；检查实验使用生产类与相同负载；修正测量工具的并发/分配偏差；按 TRX 核对计数；按 JSON 核对数字、稳定性与签名；检查剩余清单没有把已修复项留作未解决，也没有把受控实验写成真实窗口数据。变更按 Top-N、索引过滤与取消、实时目录、UI 更新、查询执行与缓存、token 排序、压测与文档七批提交，未部署。
