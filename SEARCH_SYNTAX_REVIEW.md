# 搜索语法审查与决策落实

审查日期：2026-10-06。分支：`feat-search-reimpl`。

Rebase 已完成，基于本地 `main` 的 `feeae0ee`。按你的确认，审查和修复继续放在 `feat-search-reimpl`，没有创建 `reimpl` 分支。

## 审查结果

| 检查项 | 结果 |
| --- | --- |
| 搜索语法实现 | 修复正则解析、必需字面量提取和两位年份解析错误 |
| 语法冲突 | 修复前斜杠路径遮蔽后续正则；D1–D3 已按用户决定实施 |
| 漏洞与资源耗尽 | 修复自定义过滤器的无界回溯、重复引用指数展开和深递归风险；累计超时策略与结果上限见 D4 |
| 性能 | 复用正则编译缓存，限制可选预过滤器的扫描规模，消除未闭合引号的重复扫描，补充候选间取消检查 |
| 文档一致性 | 按代码校正中文，再同步英文、繁体中文（台湾、香港）、西班牙文、日文、韩文的搜索指南、SDK 页面和应用内帮助 |

本次使用 `powershell-safe-invocation`、`run-tests`、`writing-mstest-tests`、`analyzing-dotnet-performance` 和 `lieflat-less-ai-tone`。事实修正依据代码；中文表达清理仅按 `lieflat-less-ai-tone` 的明确规则进行，保留文档章节结构。

## 性能扫描执行清单

首轮扫描范围为 `Core/SearchIndex/Query`、`Core/SearchIndex/Fzf`、`Core/IndexV2/Search`、CoreExtensions 的查询标记提供者，以及应用中的 `QueryTokenDispatcher`、`SearchViewHints`。扫描排除纯注释行。下表保留 `f2bd8cb0` 提交时的语法命中数，不能直接视为缺陷数。本轮 D3 已进一步删除引号合词和普通空格转义的扫描逻辑。

已检查 critical、async、memory/strings、regex、collections/LINQ、structural 参考中的相关模式。

| 检查 | 命中数 |
| --- | ---: |
| `IndexOf(string)` 未显式指定比较方式 | 0 |
| `Substring` 调用 | 4 |
| `StartsWith/EndsWith(string)` 未显式指定比较方式 | 0 |
| `Contains(string)` 未显式指定比较方式 | 0 |
| 无参数 `ToLower/ToUpper` | 0 |
| 单行三个 `Replace` 调用 | 0 |
| `params` 参数 | 0 |
| 对字符使用 LINQ `All/Any` | 0 |
| `RegexOptions.Compiled` | 2 |
| `GeneratedRegex` | 0 |
| 显式 `new Regex` | 5 |
| `static readonly Dictionary` | 1 |
| `static readonly FrozenDictionary` | 0 |
| `new List`，排除 static/readonly 声明 | 26 |
| `new Dictionary`，排除 static/readonly 声明 | 12 |
| `StringComparer.CurrentCulture` | 0 |
| LINQ `Select/Where/Cast/Take/Aggregate` | 3 |
| `async void`，排除 event 行 | 0 |
| `.Result` / `.Wait()` 待检查调用 | 0 |
| `stackalloc` | 4 |
| 非 sealed/abstract/static 类声明 | 2 |
| sealed 类声明 | 13 |

四处 `Substring` 位于查询或路径解析阶段。正则由用户输入动态产生，不适合改为源生成；两个共享编译缓存的上限均为 256 项。活动查询另通过弱键保留各子句的编译结果，随查询回收，不受共享缓存清空影响。五个排序键的静态字典没有值得单独优化的证据。集合大多按查询、工作线程或配置分配，没有逐项替换。13/15 个普通类声明已 sealed，另外两个是公开提供者，保留扩展兼容性。

四处 `stackalloc` 中，两处匹配缓冲区上限为 512，另外两处排序缓冲区在循环外分配。所查范围未发现同步等待异步任务；跨方法分配检查未发现需要新增抽象的修复项。算法层面的实际问题列在下面。

## 已修复的问题

| 程度 | 问题与复现 | 修复与验证 |
| --- | --- | --- |
| 高：错误结果 | `C:/docs /report/` 遇到路径里的斜杠就停止寻找后续正则；转义的结尾斜杠也能被误当成闭合符 | [RegexQueryParser](Core/SearchIndex/Query/RegexQueryParser.cs) 继续扫描普通斜杠并验证结束符；新增路径与转义回归用例 |
| 高：漏结果 | `/^\u0061$/`、`/^\141$/` 等将转义参数当作必需文字，索引掩码会过滤掉实际匹配的 `a`；`a{00,2}b` 也可能错误要求 `a` | [RegexLiteralExtractor](Core/SearchIndex/Query/RegexLiteralExtractor.cs) 对复杂语法放弃预过滤；修正零次量词和字面的 `]`、`}`。测试同时验证真实 .NET 匹配和索引结果 |
| 高：资源耗尽 | 自定义过滤规则 `*a` 重复 24 次后接 `b`，遇到大量 `a` 后接 `c` 的文件名会引发回溯爆炸 | [CustomFilterQueryTokenProvider](Plugins/CoreExtensions/Providers/QueryTokens/CustomFilterQueryTokenProvider.cs) 优先非回溯，回退也有 250 毫秒预算，逐行检查取消；压力回归保留有效候选 |
| 高：资源耗尽 | 每层重复引用下一条规则的 5,000 层链会重复展开，递归链也有栈溢出风险 | [CustomFilterRuleResolver](Plugins/CoreExtensions/Providers/QueryTokens/CustomFilterRuleResolver.cs) 改用显式栈、名称索引和已访问集合；深链、重复引用与环测试通过，保留首次出现顺序 |
| 中：重复工作 | UI 查询正则是否可编译时重复编译；大量未闭合单引号重复扫描后续词 | [RegexClauses](Core/SearchIndex/Fzf/RegexClauses.cs) 复用缓存；D3 已从 [FzfPatternParser](Core/SearchIndex/Fzf/FzfPatternParser.cs) 删除引号合词逻辑，不再扫描闭合引号 |
| 中：重复编译 | 同一查询包含 257 个不同正则子句且都能匹配时，超过共享缓存容量，每个候选都会触发缓存清空和重新编译 | [RegexClauses](Core/SearchIndex/Fzf/RegexClauses.cs) 用 `ConditionalWeakTable` 保留活动查询的编译结果；回归验证后续候选不再重新填充共享缓存，容量仍不超过 256 项 |
| 中：解析开销 | 可选正则字面量扫描对嵌套分组反复扫描 | 超过 4,096 字符或 64 个左括号时跳过该优化，完整正则仍由引擎匹配 |
| 中：取消响应 | 名称、路径和增量行扫描可能在取消后继续整批运行慢正则 | 候选匹配前检查取消；目录扫描取消时通过 `finally` 归还工作对象。正在执行的单次回溯仍受原有 250 毫秒预算控制 |
| 中：日期错误 | `99-8-3` 会受 .NET 两位年份分界影响，偏离代码注释和文档都声明的 `20xx` 规则 | [SortFilterQueryTokenProvider](Plugins/CoreExtensions/Providers/QueryTokens/SortFilterQueryTokenProvider.cs) 显式补全年份；`99`、`50` 和完整 `1999` 的回归通过 |
| 中：错误提示 | UI 直接检查原始查询，未先摘出插件标记和排除绕过符，可能与实际执行解析不一致 | [SearchViewHints](App/ViewModels/Search/SearchViewHints.cs) 使用当前配置前缀完成相同的清理步骤 |

文档还修正了 `<f` 为文件在前、`>f` 为文件夹在前，盘符与排除词的冒号方向，独立盘符可以位于任意位置且最后一个生效，正则的大小写覆盖规则，以及引号按字面处理、空白统一分词的行为。

日期简写 `2008` / `2008.8` 分别是 `2008-01-01 00:00:00` / `2008-08-01 00:00:00` 的严格比较阈值。快速搜索的标记只处理已取回的最多 1,000 条文件候选，不能把它描述成全盘排序后的前 1,000 条。只输入标记不会启动文件搜索。SDK 示例已改为任意位置的标记及取消重载。

Rebase 冲突合并保留了 `main` 的通知队列时钟补位行为和运动结束检查，也保留了插件取消与会话锁定处理。四个原本期待关闭后立刻补位的通知测试已改为在显式 `Feed` 后检查补位。中文 SDK 页面未被冲突中的删除结果清空。

## 已确认并落实的决策

本节取代原 D1–D4 待决策清单；用户已集中确认，当前没有待答复事项。

| 决策 | 实际行为 |
| --- | --- |
| D1：路径尾分隔符 | 普通查询保留反斜杠。`D:\abc\ /xxx/` 搜索目录直属项目，并对名称应用正则；已存在的目录也支持 `D:\abc /xxx/`。正则目录查询不把目录自身作为命中。文档推荐目录末尾不加 `/` 或 `\` |
| D2：前缀冲突采用 A | 新配置阻止搜索语法字符、`#`、`$`、`%` 和已配置触发符冲突。结果类型和插件字段使用当前窗口待保存的前缀；不同窗口的草稿独立。旧值只提示，不阻止无关设置保存，启动时不再自动重置 `?` 或清空插件触发词。合法文件名字符前缀显示文本冲突提示 |
| D3：移除引号合词 | 删除单引号合词和普通查询的空格转义；UI 与底层匹配器均以空白拆词。引号按字面处理，连续短语用 `/final\sreport/`。插件标记原有的关键词空格转义保留 |
| D4：继续匹配，路径正则限制结果数 | 不添加整次查询超时或首次超时中断。完整搜索窗口的路径正则查询保留前 8,000 条到达的匹配，截断后续部分，再对保留的结果排序和过滤。额外取一条判断超限，发现第 8,001 条时在搜索条数旁显示“正则搜索结果超限”。普通模糊搜索与无路径正则不套用此结果上限；用户仍可取消 |

回归覆盖路径加正则的前后斜杠与无尾分隔符写法、空白与引号规则、不同窗口的前缀草稿、旧设置保留，以及 7,999 / 8,000 / 8,001 条结果的边界。正则匹配的单候选超时策略保持不变。

## 验证记录

使用 .NET SDK `10.0.401`、MSTest `4.4.1`，目标框架 `net10.0-windows`。以下命令均已成功执行：

| 命令（仓库根目录） | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| `dotnet test Tests/Core/Core.csproj --verbosity minimal` | 1913 | 0 | 0 |
| `dotnet test Tests/App/App.csproj --verbosity minimal` | 1958 | 0 | 0 |
| `dotnet test Tests/Plugins/CoreExtensions/CoreExtensions.csproj --verbosity minimal` | 461 | 0 | 0 |
| `dotnet test Tests/Cli/Cli.csproj --verbosity minimal` | 28 | 0 | 0 |
| `dotnet test Tests/PluginSdk/PluginSdk.csproj --verbosity minimal` | 93 | 0 | 0 |
| 合计 | 4453 | 0 | 0 |

`Site` 目录中的 `npm run site:build` 成功。修改的 7 个翻译 JSON 均可解析，每种语言包含同一组 758 个键。Markdown 围栏配对、`git diff --check`、冲突标记检查通过；`main` 是当前提交的祖先，没有残留 rebase 状态。

现有非阻断警告：App 测试的 `SettingsValidationGateTests.cs` 有可空泛型约束警告；Vite 提示部分配置文件的 CommonJS/ESM 兼容性。本次未调整这些与搜索修复无关的设置。

性能结论来自源码检查和压力回归，没有对用户真实磁盘索引进行基准测试，也没有测量生产环境的速度提升。安全检查覆盖搜索解析及其执行路径，D4 保留累计耗时风险，由用户决定是否取消。

## 交付状态

| 内容 | 状态 |
| --- | --- |
| Rebase 冲突 | 已解决并继续完成 |
| 不需要决策的实现与性能问题 | 已修复并验证 |
| 七种语言文档与帮助 | 已同步 |
| 修复、回归测试与审查记录 | 纳入本次本地提交；未推送 |
| D1–D4 | 已按用户确认实施，无待答复事项 |
