# regex 增量审计修复与验证

2026-11-10。对应 [增量审计 R1–R8](search-regex-incremental-audit.md)，修复起点为 `0a87b10f`，分支基于本地 `main` 的 `5f735e70`。此前重组提交没有改变审计树，本次才修复发现的问题。原审计及其 JSON 保留发现时的证据；本次原始样本、汇总、产物 SHA-256 记录在 [search-regex-fixes-results.json](search-regex-fixes-results.json)。

记录前依次复核了生产调用链、对应回归测试、独立进程对照测量、结果与提示的呈现路径，最后核对探针加载的 Core/App/CoreExtensions DLL 与当前 Release 产物的 SHA-256。三者一致后才形成本文。这里的“当前一致”指本次工作区构建与组件级验证，不代表已经更新机器上运行的服务或完成真实窗口的人工操作验收。

## 问题来源、方案与实施

| 项目 | 修复前来源 | 已实施方案与检查 |
| --- | --- | --- |
| R1 目录别名绕过名称条件 | `SearchableItemMapper` 把整句分别对 alias 求值，别名满足正则或不含排除词就能复活不合格标题 | 标题直接匹配保留原有 OR 语义；进入别名/关键词兜底前统一检查标题的名称级条件。别名使用缓存的正向词视图；烘焙别名、Curated/ShortcutTarget/ProductMetadata、shadow 和关键词入口均覆盖。合法标题的别名不必再满足扩展名正则。 |
| R2 先到的 8,000 行挤掉后到的最佳结果 | accumulator 装饰/排序前丢行，上游同时把请求缩成 8,001；token、侧栏和列排序只能处理剩余行 | 恢复完整窗口的全量请求，accumulator 保留候选。`SearchViewResultRenderer` 在现有排名、token、侧栏、列排序之后最多显示 8,000 行，超限状态按本次最终列表计算；不修改原列表，方便重新过滤和排序。普通查询与无路径正则不使用此展示上限。 |
| R3 每个候选重复耗尽回溯预算 | `AllMatch` 捕获 250ms 超时后返回 false，剩余候选继续重复计算；服务流又把异常当正常结束 | 一次超时使该解析查询失败，后续匹配立即抛 `SearchRegexTimeoutException`。失败状态按子句数组弱键保存，与共享编译缓存分离，新查询可重试。多盘、网络盘、范围补搜、实时目录、窗口和 CLI 透传失败；窗口清掉部分结果并显示超时提示。 |
| R4 高亮逐行重建正则查询 | `ComputeMask` 每行 Split，重新包裹子句、拼接、Parse；原计划缓存只含普通文本 | 完整高亮字符串作为缓存 key，计划同时持有普通词和正则查询。继续按 fuzzy、AND/OR 设置和 alias revision 失效；大小写内联选项不被改写。 |
| R5 实时搜索丢失裸盘符 | 所有实时入口都走不识别盘符的 `ParseText` | 完整查询使用 `Parse`，路径尾部显式标为 fragment 使用 `ParseText`。按候选路径核对盘符；盘符单独成句可列出匹配盘，空操作符不会变成 match-all。冷扫描与缓存重放一致。 |
| R6 正则正文与候选被普通词归一化改写 | 先 Normalize 整句，再提取正则；多个匹配入口把已归一化名称交给 Regex | 先摘出原文子句，再归一化普通文本。base/delta、路径、实时目录、公开 FuzzyMatcher、拼音和别名层传递原始名称用于正则；普通词及排除词保留已有折叠。正则高亮直接标记原串坐标，普通词仍通过归一化映射还原。对归一化会改变的必需字面量保守放弃预筛。 |
| R7 退役扫描不在 Dispose 等待集合 | eviction/Invalidate 只移除字典；Dispose 只等字典内任务；每扫描 linked CTS 未释放 | 单独跟踪 active tasks，完成即移出；淘汰/失效不取消仍被其他查询等待的扫描。任务直接用 owner token，去掉不必要的 linked CTS。关闭时先禁止注册并取得活动任务，再在锁外取消和等待。 |
| R8 非法阈值有的放行、有的筛文件 | 大小/日期解析失败不缩小结果，folder 任意字符串按前缀或默认 files 处理 | 空阈值（`<s>`）只排序；非空无效阈值不被 provider 认领且直接调用也返回空。沿用 host 的未认领 token 路径，并在完整/快速窗口及 CLI 显示具体提示。folder 只接受 `f`、`folder`、`dir`，`>` 选目录、`<` 选文件；负数大小也拒绝。日期 20xx 规则与稳定排序保留。 |

R3 的补充边界：共享冷目录扫描不能因最初调用者的坏正则而让后来查询一起失败。扫描在超时后停掉该请求的匹配/投递，通过独立失败任务通知调用者，原始目录遍历仍可由下一次查询复用。高亮属于显示回调，不能把异常抛进 WPF；它停止该缓存计划的正则绘制，避免每行再支付预算。已经开始的并行匹配不能由 CancellationToken 中断 Regex 内部执行，仍可能等到自身 250ms 预算退出；这不是“整个跨进程搜索严格在 250ms 内完成”的保证。

服务响应协议从 v6 到 v7，App→CLI 高亮响应从 v2 到 v3，增加明确的查询失败帧。超时不再伪装成正常 End 或服务不可用；非法 token 在 CLI 查询开始前报告。部署时 Service、App、CLI 必须使用配套版本。

R7 保留最多两秒的关闭等待：卡在不可取消的文件系统调用中的扫描仍可能超过该上限。这里只修复“已淘汰任务根本没有被等到”与 CTS 生命周期，不把同步关闭改为无限等待或声称所有磁盘 I/O 可立即取消。

## 行为与呈现对照

| 场景 | 修复前 | 当前复核 |
| --- | --- | --- |
| 标题 `blocked.txt`、别名 `report.pdf`，查询 `report /\.pdf$/` | 匹配 | 不匹配；反向对照 `target.pdf` + alias `report` 匹配 |
| 标题 `temp target`、别名 `report`，查询 `report :temp` | 匹配 | 不匹配；直接标题 `report temp` 对 `report \| :temp` 保留 OR 语义 |
| 8,000 个较差结果后到达最佳行 | 最佳行丢失 | 原候选 8,001、展示 8,000，最佳行在展示中；超限提示为 true |
| token/侧栏处理只剩上述晚到行 | 已不可恢复 | 展示 1 行且超限提示消失；列排序可把原尾部放到首位 |
| `z: report` 搜索实时目录 | 0 行，索引仍有匹配 | 实时 1 行；该索引语料仍返回 204 行；其他盘的行不准入 |
| `/^Ａ$/` 与 `/^\uFF21$/` | 字面与 escape 不一致 | 两者匹配全角 `Ａ`，不匹配 ASCII `A`；普通词 `A` 仍可折叠匹配 `Ａ` |
| NFD `e\u0301` 的正则和高亮 | 原字符语义会被折叠 | 精确选中原始两码元，并标记原串；NFC `é` 不被等同为该正则 |
| eviction/Invalidate 后关闭缓存 | 旧活动任务未结束即返回 | 测试阻塞旧任务，Dispose 等到释放后才返回；探针 `completedOnReturn=true` |
| `<s>garbage` / `<f>wrong` | 前者保留全部，后者保留文件 | 两者 `claimed=false`，结果为空，并走无效 token 提示；`<s>` 保留只排序 |
| 正则超时及过期查询 | 超时当普通 miss | 失败帧读为异常；窗口用失败行替换部分结果，完整窗口改显示提示；已取消或旧版本的失败不覆盖新查询 |

呈现验证使用真实 `SearchViewResultRenderer`、结果集合、超限回调、`TextHighlighter.ComputeMask` 和搜索状态回调；未测量 WPF Run 创建、布局、屏幕帧率或人工视觉体验。正常 `report` 每万次掩码仍标记 60,000 字符，两个正则场景仍各标记 100,000 字符；优化没有靠少画字符取得结果。原始全角/组合字符的变化是 R6 的有意纠正。CLI 非交互模式在成功完成前不输出结果，失败返回非零退出码；交互模式收到失败后清空当前结果并显示错误。

## 对照测量

使用已保存的审计版本产品 DLL 作为 before，当前 Release 产品作为 after，不将 main 没有的正则语法当作可直接比较的性能基线。SDK 10.0.403、Windows 10.0.26300、12 个逻辑处理器，进程环境 `DOTNET_TieredCompilation=0`。每组三轮独立进程，轮间交换执行顺序；压测时没有并行构建或测试。

索引固定 60,000 个文件（6,000 report、54,000 notes），limit=204，预热 3 次，每轮 10 个样本，共 30 个。各查询每次均返回 204 行。分配是进程总分配增量除以查询次数，不是进程驻留内存。

| 查询 | before 中位 ms | after 中位 ms | before→after p95 ms | 每查询分配 B：before→after |
| --- | ---: | ---: | ---: | ---: |
| `report` | 3.213 | 3.313 | 7.084 → 6.971 | 2,077,014 → 2,057,342 |
| `rprt` | 5.282 | 5.489 | 6.819 → 6.574 | 2,660,227 → 2,607,386 |
| `archive report` | 1.756 | 1.750 | 2.567 → 2.804 | 1,421,828 → 1,394,576 |
| `/report/` | 5.182 | 5.611 | 6.103 → 6.481 | 4,932,250 → 4,986,342 |
| `/report.*\.txt$/` | 5.338 | 5.615 | 6.246 → 5.969 | 5,005,142 → 4,925,290 |
| `report /\.txt$/` | 8.836 | 8.878 | 9.774 → 9.840 | 7,283,355 → 7,332,650 |
| `/.*[0-9]/` | 31.071 | 30.856 | 32.404 → 33.498 | 39,795,486 → 40,257,948 |

这些样本不支持“索引正则普遍提速”的结论。无必需字面量的正则仍要处理更多名称；普通查询和大多数正则在这一轮处于相近量级，部分 p95 上升。优化的主要收益在高亮重复解析和病理回溯，不是消除了全扫描。纯正则 `/report/` 本轮中位增加约 0.43ms（8.3%），查询失败状态检查也有成本；未将这项变化隐藏为全面提速。

高亮预热 100 次，每样本 1,000 次 mask，共 30 样本；分配按每轮 10,000 次统计中位：

| 查询 | 每千次中位 ms：before→after | 每万次分配 B：before→after |
| --- | ---: | ---: |
| `report` | 0.317 → 0.305 | 960,872 → 960,872 |
| `report /\.txt$/` | 1.970 → 0.740 | 13,365,992 → 1,440,784 |
| `/report.*\.txt$/` | 1.698 → 0.548 | 13,288,672 → 960,784 |

带扩展名子句的高亮分配减少 **89.2%**，中位耗时减少 **62.4%**。这复用的是已解析查询，原来的 Regex 编译缓存仍保留；不能描述为“过去每行重新编译 Regex”。

病理模式 `/^(?=(a+)+$)/` 对 `a×30 + !` 连续调用四次，三轮中位 **1,005.5 → 261.7ms**。after 四次都报告查询失败：第一次耗尽预算，后三次立即失败；新解析查询对简单匹配仍成功。约 262ms 包含调用与首次触发开销，不等于固定 250ms 端到端时限。

R2 的代价单独测量：每批 1,000 行、预热 2 次、每轮 5 个样本、三轮共 15 个。计时覆盖输入记录构造、装饰与排序合并；分配包含被丢弃的输入记录，不是保留堆大小。此处不包含真实磁盘/IPC/WPF 渲染。

| 输入候选 | 保留行 before→after | 中位 ms before→after | 分配 B before→after | 最后一行保留 before→after |
| ---: | ---: | ---: | ---: | --- |
| 8,000 | 8,000 → 8,000 | 8.145 → 6.261 | 3,064,568 → 3,064,568 | 是 → 是 |
| 50,000 | 8,000 → 50,000 | 16.037 → 56.355 | 14,506,376 → 19,904,720 | 否 → 是 |

8,000 行同等工作的耗时在先导与最终轮次中有明显波动，分配一致，不据此宣布更快或更慢的普遍结论。50,000 行则明确多做了正确性所需的工作，不能把“最终只展示 8,000”当作候选内存上界。完整窗口的宽泛查询仍可能昂贵；实时目录自身原有的 10,000 枚举上限也仍存在，本次没有把它扩展为无限磁盘遍历。

## 验证与复现

完整回归之后，将失败状态检查收紧到每条子句入口，避免并行请求失败后再启动下一条子句；相关正则/多盘回归 **Passed: 205, Failed: 0, Skipped: 0**。最终测量使用收紧后的重新构建产物。

完整回归：Core **Passed: 2733, Failed: 0, Skipped: 0**（串行设置），App **Passed: 2666, Failed: 0, Skipped: 0**，CoreExtensions token **Passed: 109, Failed: 0, Skipped: 0**，CLI **Passed: 28, Failed: 0, Skipped: 0**。产品 Release 解决方案构建 0 警告、0 错误。测试构建仍有既有 nullable/未使用事件警告及 NU1900 漏洞数据源不可达警告；没有修改断言来掩盖环境问题。纯 Core 探针有 WindowsBase 引用冲突警告，但构建和全部采样成功，产品构建没有该警告。

核心回归文件包括 `RegexPrefilterTests`（base、delta、路径及 Unicode）、`RegexTimeoutTests`（新旧查询隔离）、本地/网络多盘测试（异常不能被吞掉）、`SearchRegexFailureTests`（两种 wire 协议）、`LiveDirectorySearcherTests`（冷/热及失败隔离）、`LiveScanCacheTests`（eviction/Invalidate）。App 覆盖目录/关键词/shadow、原字符高亮、最终展示截断、失败替换和旧查询保护；token 覆盖非法阈值、合法 folder/file 与未完成输入。

本地复现命令（PowerShell，仓库根目录）：

```powershell
dotnet test Tests/Core/Core.csproj -c Release --no-restore --settings scratch/search-performance-audit/serial.runsettings --logger trx --results-directory scratch/regex-fixes/verified-core '-bl:scratch/regex-fixes/core-{}.binlog'
dotnet test Tests/App/App.csproj -c Release --no-restore --logger trx --results-directory scratch/regex-fixes/verified-app '-bl:scratch/regex-fixes/app-{}.binlog'
dotnet test Tests/Plugins/CoreExtensions/CoreExtensions.csproj -c Release --no-restore --filter 'FullyQualifiedName~QueryToken' --logger trx --results-directory scratch/regex-fixes/final-tokens '-bl:scratch/regex-fixes/tokens-{}.binlog'
dotnet test Tests/Cli/Cli.csproj -c Release --no-restore --logger trx --results-directory scratch/regex-fixes/final-cli '-bl:scratch/regex-fixes/cli-{}.binlog'
dotnet build Lertaro.slnx -c Release --no-restore '-bl:scratch/regex-fixes/build-{}.binlog'
```

探针源代码在 `scratch/regex-fixes/{CoreProbe,AppProbe,LimitProbe}`，最终三轮原始输出在 `scratch/regex-fixes/final-measurements/`；旧审计 DLL 位于 `scratch/regex-incremental-audit/regex-{core,app}-probe/`，没有覆盖。`run-probes.ps1` 交替执行旧/新产品，`export-results.py` 生成持久化 JSON。scratch 属于本机证据目录，未纳入产品构建或新增依赖；可迁移的行为回归已加入现有测试工程。

最后对相对 main 的 63 个搜索生产 C# 文件重跑同组模式扫描（范围增加了 IPC/CLI/协调器，不能与旧 49 文件的命中数直接比较）。异步 void=0、无文化大小写=0、三连 Replace=0、CompiledRegex=2、new Regex=5、static Dictionary=1、ContainsKey=1；24 个普通类中 20 个 sealed。同步等待命中 3 处属于已有有界 channel 写入，`Task.WaitAll` 另由 R7 单独复核；这些是文本命中，不是新缺陷数量。动态用户模式仍使用有界编译缓存，没有套用 GeneratedRegex 建议。

七种语言的应用内帮助与站点搜索语法已同步原字符规则、终止查询、最终展示上限和阈值失败语义，并检查不再残留“先保留 8,001”“非法阈值只排序”“任意 folder 前缀均有效”等旧说明。main 原有候选过滤、稳定排序、取消和两位年规则的回归保持通过。
