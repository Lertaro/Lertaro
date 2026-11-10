# 搜索性能：测量与教训

测量日期：2026-11-10。第一轮基线是 `e70449d3`；第二轮基线是第一轮修复后的产品 DLL，**不是同一个起点**。完整样本、汇总、结果签名和程序集/源码哈希分别在 [第一轮 JSON](search-performance-results.json) 与 [第二轮 JSON](search-performance-checklist-results.json)。本次文档精简未重新运行实验。

## 测量口径

Windows build 26300、x64、12 逻辑处理器、Intel Family 6 Model 165、SDK 10.0.403、Release、`zh-CN`；两边均禁用分层编译。3 对独立进程按 before/after、after/before、before/after 交替运行，避免与构建或其他测试并行。

- Core 第一轮使用真实 Snapshot/DeltaOverlay：60,000 文件、100 目录，预热 3 次、每进程每场景测 10 次。`firstResultMs` 是索引 API 首次回调，不是输入到首屏。
- App 第一轮使用真实 `ListCollectionView`，20,000/100,000 行筛留 10%，预热 3 次，每样本包含 20 次更新；只有 3 个总量样本，p95 等于最大总量，不能写成逐帧 p95。
- 第二轮 Top-N 每进程预热 3 次、100 次固定种子乱序；目录预热 2 次、测 10 次；快捷快照预热 2 组、测 5 组，每组 20 个快照；Token 排序预热 3 次、测 20 次。
- p95 使用 nearest rank。分配为进程总分配差，不是存活堆或峰值；建语料不计时。直接目录的分配原始指标包含预热，不能与“每次分配”混算。
- 均为生产组件的受控工作负载，没有测真实多盘/管道吞吐、运行中 App 的用户首屏或屏幕逐帧变化。

## 结果及其含义

| 第一轮场景 | 修复前 → 后 | 结论 |
| --- | --- | --- |
| UI 20,000 行，20 次更新总耗时中位数 | 522.453 → 48.206 ms | 先过滤再排序/提交；排序与提交次数各 40 → 20，Reset 40 → 0 |
| UI 100,000 行，同口径 | 3,310.853 → 353.152 ms | 最终路径顺序签名一致，分配约降 88.6%；不能外推成所有查询快 9 倍 |
| `report`，有界 204，单次中位数 | 10.381 → 10.569 ms | 基本持平 |
| `report`，无界 | 94.647 → 101.849 ms | 本轮未证明索引吞吐提升，前后不同轮次方向有波动 |
| `archive\report`，204，`*.txt` | 合格文件 0 → 204；13.813 → 14.857 ms | 截断前过滤修复召回；错误结果的低耗时不能作为优化目标 |
| 已取消的 60,000 候选名称精排 | 24.348 → 0.021 ms | 边界处已有取消时，避免继续精排；不是任意指令处的停止延迟 |

| 第二轮场景 | 修复前 → 后，中位数 | 结论 |
| --- | --- | --- |
| 20,000 全同分候选取 204 | 0.0679 → 0.1567 ms | 每 100 次不同子集 100 → 1；确定性有成本 |
| 直接目录 11 项，子树另有 1,000 文件 | 1.5297 → 0.1108 ms | 枚举 1,011 → 11；显示仍 11，123 B 文件的大小元数据恢复为 123 |
| 快捷 `report`，4,000 文件、20 增长快照合计 | 138.2059 → 136.4694 ms | 约 -1.3%，基本持平；分配 +0.9% |
| 快捷 `rprt`，同口径 | 150.3937 → 142.8300 ms | 约 -5.0%；分配 -4.0%，没有早期单轮所示的 10% |
| S- 排序 100,000 行 | 56.5642 → 13.2201 ms | 强类型键避免逐行装箱；仍是同步工作，不能说 UI 成本已消失 |

## 可复用的改法

1. **先消除无效 UI 工作。** 活动筛选只提交最终列表；先比较可信前缀，再决定是否 Reset。保留行身份时也要更新查询、高亮和动作参数，元数据变化需要替换或刷新，不能让少通知变成陈旧界面。
2. **约束要进入截断前。** 文件名与 category 贯穿名称、路径、路径补全、网络和实时匹配。文件名模式只约束文件，目录是否可见由 category 决定。覆盖 base/delta、OR 分支及小 limit，不能只断言显示层没有违规行。
3. **保留与排序必须使用同一全序。** `FzfTopN` 的准入、quickselect 和最终排序统一按 `(SortKey, EntryIndex)`。只修最终排序，救不回已经随机丢掉的同分候选。
4. **取消到达重活边界。** 展开、精排、建行和合并检查 token，租用缓冲区在 finally 归还。异步批次除取消外还要检查查询/快照身份；宿主停止等待不代表同步排序或插件调用可抢占。
5. **缓存原始数据或不可变证据。** 单查询文件匹配缓存把 20 快照的精排 42,000 次降到 4,000 次，但 UI 建行仍是 42,000 次。每次 paint 仍读当前元数据并混合排序；不可从前一查询的截断结果推导下一查询。
6. **缓存失效是行为契约。** 精度、AND/OR 优先级、别名 revision 与禁用集合都影响匹配。provider 内部改变规则却不通知宿主，仍超出可观察范围。
7. **区分共享扫描与查询交付。** `Lazy<Task<...>>` 只让获胜缓存启动枚举；冷查询取消可停止回调，缓存仍继续填充。direct/recursive 分开缓存，路径规范化，inline watcher 失效；无 watcher 时已完成扫描在起算 2 秒后过期，不是每两秒主动刷新。
8. **检查仪器。** 进程分配计数不能与其他测试并行；只移除某层门槛可能增加下游结果，wall clock 变慢不代表该层更贵。前后比较同时核对条数、有序签名与实际执行次数。

第一轮记录的“有界同分子集不稳定”和“别名变化未失效”已由第二轮修复。两轮有意改变的行为包括正确过滤召回、稳定同分子集、真实目录元数据和拒绝旧异步结果；不能笼统要求所有旧输出逐字不变。

## 当前边界与再次改动的验收条件

| 边界 | 已有证据 | 验收重点 |
| --- | --- | --- |
| 慢即时 provider 阻塞 | provider 挂起 150 ms 时，文件已就绪却仍等同一 pass 锁 | 文件能先显示；晚到结果或异常不能覆盖新查询；单纯搬到后台不够 |
| catalog 重算与加载重查 | 10,000 条 × 20 快照仍匹配 200,000 次；加载完成仍 DispatchSearch | 区分 catalog/settings/query 版本，验证加载事件是否额外重查文件 |
| 通用 Token 候选不完整 | 6,000 匹配尾部 100 个 mp3，先取 4,000 后过滤为 0，截断前过滤为 100 | 覆盖合格项全在尾部的查询；现有对照不代表 GUI Token 已接通下推 |
| 实时目录枚举上限 | 10,020 项只返回 10,000，仍无截断状态 | 区分 complete/truncated/unavailable；TTL、direct 和元数据修复不解决完整性 |
| 读锁内背压 | 容量 1,024 的 channel 堵塞 150 ms，真实 LiveIndex writer 等待约 159–165 ms；取消能释放 | 测生产发生率，再设计保持一致快照、有界内存和可取消的锁外发送；不能写成已证生产死锁 |

候选倍率、完整窗口全量语义和插件线程模型都不能为局部速度静默改变。慢网络扫描可能超过两秒 TTL，完成后的下一查询便会重扫，需单独测量重复工作。

## 复现入口

| 工程 | 负载类别与文件 |
| --- | --- |
| Core | `SearchPerformance`：[第一轮](../Tests/Core/IndexV2/Search/SearchPerformanceWorkloadTests.cs)；`SearchChecklistPerformance`：[第二轮](../Tests/Core/IndexV2/Search/SearchChecklistWorkloadTests.cs) |
| App | `SearchPerformance`：[第一轮](../Tests/App/ViewModels/Search/SearchPerformanceWorkloadTests.cs)；`SearchChecklistPerformance`：[第二轮](../Tests/App/ViewModels/Search/SearchChecklistWorkloadTests.cs) |
| CoreExtensions | `SearchChecklistPerformance`：[Token 排序](../Tests/Plugins/CoreExtensions/Providers/QueryTokens/SortFilterQueryTokenProviderTests.cs) |

按当前工程 runner 选择类别过滤，使用 Release、进程环境 `DOTNET_TieredCompilation=0`、独立结果目录和 TRX/binlog。比较基线时两边使用同一测试程序集，保留各自产品 DLL 并核对 JSON 中的哈希；不要让测试构建覆盖基线产品。

当前源码可重跑当前负载；历史前后对照还需要当时保存的基线二进制，JSON 不包含这些文件。原 `scratch/search-performance-audit/` 与 `scratch/search-performance-checklist/` 均非受版本控制的复现依赖，缺失时不能声称重新跑出了历史差值。历史验证中的 Core 串行条件与 NU1900 限制见 [pitfalls.md](pitfalls.md)。
