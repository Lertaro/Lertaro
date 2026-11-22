# 搜索性能：测量与经验

以下为 2026-11-10 的两轮测量。第一轮基线是 `e70449d3`，第二轮基线是第一轮修复后的未提交产品 DLL。样本、结果签名和程序集/源码哈希见 [第一轮 JSON](search-performance-results.json) 与 [第二轮 JSON](search-performance-checklist-results.json)。当前正则与缓存契约见 [search-grammar.md](search-grammar.md)。

## 测量口径

Windows build 26300、x64、12 逻辑处理器、Intel Family 6 Model 165、SDK 10.0.403、Release、`zh-CN`，禁用分层编译。3 对独立进程按 before/after、after/before、before/after 交替执行，采样期间不并行构建或测试。

- Core 第一轮使用真实 Snapshot/DeltaOverlay：60,000 文件、100 目录，预热 3 次，每场景测 10 次。`firstResultMs` 从索引 API 调用计到首次回调。
- App 第一轮使用真实 `ListCollectionView`：20,000/100,000 行筛留 10%，预热 3 次，每样本包含 20 次更新。共 3 个总量样本，p95 为最大总量。
- 第二轮 Top-N 预热 3 次，测 100 次固定种子乱序；目录预热 2 次、测 10 次；快捷快照预热 2 组、测 5 组，每组 20 个快照；标记排序预热 3 次、测 20 次。
- p95 使用 nearest rank。分配为进程总分配增量，语料构造不计时；直接目录的原始分配指标包含预热。
- 计时覆盖生产组件的受控负载；真实多盘、IPC 吞吐与屏幕首帧需另测。

## 测量结果

| 第一轮场景 | 修复前 → 后 | 含义 |
| --- | --- | --- |
| UI 20,000 行，20 次更新总耗时中位数 | 522.453 → 48.206 ms | 先过滤再排序/提交；排序与提交各 40 → 20 次，Reset 40 → 0 |
| UI 100,000 行，同口径 | 3,310.853 → 353.152 ms | 最终路径顺序签名一致，分配约降 88.6% |
| `report`，有界 204，单次中位数 | 10.381 → 10.569 ms | 基本持平 |
| `report`，无界 | 94.647 → 101.849 ms | 本轮较慢，前后不同轮次方向有波动 |
| `archive\report`，204，`*.txt` | 合格文件 0 → 204；13.813 → 14.857 ms | 截断前过滤恢复召回，增加了正确结果的处理工作 |
| 已取消的 60,000 候选名称精排 | 24.348 → 0.021 ms | 进入精排前检查已有取消，避免继续计算 |

| 第二轮场景 | 修复前 → 后，中位数 | 含义 |
| --- | --- | --- |
| 20,000 全同分候选取 204 | 0.0679 → 0.1567 ms | 每 100 次不同子集 100 → 1，确定性增加了计算成本 |
| 直接目录 11 项，子树另有 1,000 文件 | 1.5297 → 0.1108 ms | 枚举 1,011 → 11，显示仍为 11；123 B 文件的大小恢复为 123 |
| 快捷 `report`，4,000 文件、20 增长快照合计 | 138.2059 → 136.4694 ms | 约 -1.3%，分配 +0.9% |
| 快捷 `rprt`，同口径 | 150.3937 → 142.8300 ms | 约 -5.0%，分配 -4.0% |
| 当时的 `S-` 大小排序（现为 `<s`），100,000 行 | 56.5642 → 13.2201 ms | 强类型键避免逐行装箱，排序仍同步执行 |

## 可复用的改法

1. **消除多余 UI 工作。** 活动筛选只提交最终列表；比较可信前缀后决定是否 Reset。复用行身份时同步更新查询、高亮与动作参数，元数据变化时替换或刷新行。
2. **截断前应用约束。** 文件名和 category 贯穿名称、路径补全、网络及实时匹配。文件名模式只约束文件，目录由 category 决定；回归覆盖 base/delta、OR 及小 limit。
3. **准入和排序使用同一全序。** `FzfTopN` 的准入、quickselect 与最终排序统一按 `(SortKey, EntryIndex)`，避免提前丢掉同分候选。
4. **在重活边界检查取消。** 展开、精排、建行和合并检查 token，租用缓冲区在 finally 归还。异步批次核对查询/快照身份；同步排序及旧插件调用依赖自身返回。
5. **缓存不可变匹配证据。** 单查询缓存使 20 个增长快照精排从 42,000 次降至 4,000 次，UI 建行仍为 42,000 次。每次显示读取当前元数据并混合排序；下一查询从原始候选重新判断。
6. **完整定义缓存失效。** 精度、AND/OR、别名 revision 和禁用集合均影响匹配；provider 改规则后通知宿主。
7. **分开扫描所有权与查询交付。** 共享目录扫描由缓存所有，查询取消或正则失败只停止自己的投递；淘汰任务仍进入关闭等待。缓存键、TTL 与关闭上限见 [search-grammar.md](search-grammar.md)。
8. **核对测量实际执行的工作。** 分配计数独占进程，前后同时检查条数、有序签名与调用次数。移除上游门槛可能让下游多处理结果，需分别计时。

第一轮发现的同分子集不稳定和别名变化未失效已在第二轮修复。对照中的预期行为变化包括过滤召回恢复、稳定同分子集、目录元数据恢复及拒绝过期异步结果。

## 待处理的搜索问题

| 当前问题 | 复现证据 | 后续验收 |
| --- | --- | --- |
| 慢即时 provider 阻塞同一 pass | 注入 150 ms 等待时，文件就绪仍等 pass 锁 | 文件先显示；迟到结果及异常不覆盖新查询 |
| catalog 重算与加载重查 | 10,000 条 × 20 快照匹配 200,000 次；加载完成仍 DispatchSearch | 区分 catalog/settings/query 版本，检查额外文件重查 |
| 通用标记后过滤遗漏候选 | 6,000 匹配的尾部有 100 个 mp3，先取 4,000 后过滤为 0；截断前过滤为 100 | 覆盖合格项全在尾部的查询，并验证 GUI 标记是否接通下推 |
| 实时目录枚举截断无状态 | 10,020 项返回 10,000 项 | 区分 complete/truncated/unavailable，保留现有枚举上限的可见提示 |

慢网络扫描可能超过起算 2 秒的 TTL，完成后的下一查询会重扫，需测量重复工作。

### 宽查询的预选质量（#535）

`NameSearch` 对单字符和宽泛查询先按 raw FZF 分数保留 headroom，再用完整质量键精排。经验倍率可能丢掉最终质量键更优的候选，也增加 refinement 工作。

验收比较热扫描准入键与最终排序契约，覆盖单字符和多词宽查询；固定语料下核对有序签名、首行时间、refinement 与取消延迟。质量键的计算成本须有界。

### 慢消费者延迟索引写入（#534）

`LiveIndex.Read` 覆盖整次搜索及 `onResult`。向有界 channel 投递时，慢消费者会延迟同盘索引更新和压缩。容量 1,024 的受控实验中，消费者暂停 150 ms，writer 等待约 159–165 ms；取消后等待释放。

后续方案需保持一致的 snapshot/delta 视图，使用有界内存渐进交付，并使消费者暂停时索引更新仍可继续。复现入口为 `SearchChecklistWorkloadTests.Backpressure_BlockedConsumer_RecordsIndexWriterWait`。

## 文件夹离线恢复与扫描重试

#465 针对保存完整路径的文件夹索引，包含 UNC 与映射盘目录。路径恢复可达后，`WatcherManager` 按约 10 秒重试恢复监控并请求补扫；首次离线、没有缓存或扫描先于 watcher 就绪的缺口也会补扫。健康且已有缓存的手动刷新目录在启动时继续使用原缓存。

移除配置同时撤销监控、待执行刷新与状态。根路径不可达的刷新退出忙碌状态，保留缓存和条目数；移除配置不等待阻塞的 Windows 探测，迟到结果不能重建已移除的监控。

#305 将目录不存在、拒绝访问与路径无效等错误直接结束；已知临时 I/O 错误最多尝试 4 次，三次等待为 250/1000/3000 ms，取消可中断等待。枚举中途失败保留已取得记录，并标记该目录尚未完整捕获。

原生文件调用没有硬超时，持续断网可能逐目录消耗重试预算。WSL 路径继续使用定时/手动扫描；依赖 `FileSystemWatcher` 的自动恢复适用于支持该机制的文件系统。

回归入口（`Tests/Core`）：`DriveWatcherHostTests`、`WatcherManagerTests`、`SchedulerTests`、`NetworkIndexerTests`、`DriveRefreshRunnerTests`、`TreeBuilderTests`、`TreeBuilderEnumerationExtensionsTests`。

## 性能复现入口

| 工程 | 负载类别与文件 |
| --- | --- |
| Core | `SearchPerformance`：[第一轮](../Tests/Core/IndexV2/Search/SearchPerformanceWorkloadTests.cs)；`SearchChecklistPerformance`：[第二轮](../Tests/Core/IndexV2/Search/SearchChecklistWorkloadTests.cs) |
| App | `SearchPerformance`：[第一轮](../Tests/App/ViewModels/Search/SearchPerformanceWorkloadTests.cs)；`SearchChecklistPerformance`：[第二轮](../Tests/App/ViewModels/Search/SearchChecklistWorkloadTests.cs) |
| CoreExtensions | `SearchChecklistPerformance`：[标记排序](../Tests/Plugins/CoreExtensions/Providers/QueryTokens/SortFilterQueryTokenProviderTests.cs) |

按当前 runner 选择类别过滤，使用 Release、`DOTNET_TieredCompilation=0`、独立结果目录及 TRX/binlog。两边使用同一测试程序集并核对实际产品 DLL 哈希，避免测试构建覆盖基线。

当前源码可重跑当前负载；历史差值还需未入库的旧基线二进制。两轮基线分别保留，不能交叉混用。历史 Core 串行条件与 NU1900 记录见 [pitfalls.md](pitfalls.md)。
