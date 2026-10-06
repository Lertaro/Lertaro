# 搜索语法审查与待决策事项

审查日期：2026-10-06。分支：`feat-search-reimpl`。

Rebase 已完成，基于本地 `main` 的 `feeae0ee`。按你的确认，审查和修复继续放在 `feat-search-reimpl`，没有创建 `reimpl` 分支。

## 审查结果

| 检查项 | 结果 |
| --- | --- |
| 搜索语法实现 | 修复正则解析、必需字面量提取和两位年份解析错误 |
| 语法冲突 | 修复前斜杠路径遮蔽后续正则；另有 3 项兼容性选择，见 D1–D3 |
| 漏洞与资源耗尽 | 修复自定义过滤器的无界回溯、重复引用指数展开和深递归风险；累计搜索预算见 D4 |
| 性能 | 复用正则编译缓存，限制可选预过滤器的扫描规模，消除未闭合引号的重复扫描，补充候选间取消检查 |
| 文档一致性 | 按代码校正中文，再同步英文、繁体中文（台湾、香港）、西班牙文、日文、韩文的搜索指南、SDK 页面和应用内帮助 |

本次使用 `powershell-safe-invocation`、`run-tests`、`writing-mstest-tests`、`analyzing-dotnet-performance` 和 `lieflat-less-ai-tone`。事实修正依据代码；中文表达清理仅按 `lieflat-less-ai-tone` 的明确规则进行，保留文档章节结构。

## 性能扫描执行清单

范围为 `Core/SearchIndex/Query`、`Core/SearchIndex/Fzf`、`Core/IndexV2/Search`、CoreExtensions 的查询标记提供者，以及应用中的 `QueryTokenDispatcher`、`SearchViewHints`。扫描排除纯注释行。下表是语法命中数，不能直接视为缺陷数。

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
| 中：重复工作 | UI 查询正则是否可编译时重复编译；大量未闭合单引号重复扫描后续词 | [RegexClauses](Core/SearchIndex/Fzf/RegexClauses.cs) 复用缓存；[FzfPatternParser](Core/SearchIndex/Fzf/FzfPatternParser.cs) 记住没有闭合符的区间，5,000 个未闭合词后接合法引号段的回归通过 |
| 中：重复编译 | 同一查询包含 257 个不同正则子句且都能匹配时，超过共享缓存容量，每个候选都会触发缓存清空和重新编译 | [RegexClauses](Core/SearchIndex/Fzf/RegexClauses.cs) 用 `ConditionalWeakTable` 保留活动查询的编译结果；回归验证后续候选不再重新填充共享缓存，容量仍不超过 256 项 |
| 中：解析开销 | 可选正则字面量扫描对嵌套分组反复扫描 | 超过 4,096 字符或 64 个左括号时跳过该优化，完整正则仍由引擎匹配 |
| 中：取消响应 | 名称、路径和增量行扫描可能在取消后继续整批运行慢正则 | 候选匹配前检查取消；目录扫描取消时通过 `finally` 归还工作对象。正在执行的单次回溯仍受原有 250 毫秒预算控制 |
| 中：日期错误 | `99-8-3` 会受 .NET 两位年份分界影响，偏离代码注释和文档都声明的 `20xx` 规则 | [SortFilterQueryTokenProvider](Plugins/CoreExtensions/Providers/QueryTokens/SortFilterQueryTokenProvider.cs) 显式补全年份；`99`、`50` 和完整 `1999` 的回归通过 |
| 中：错误提示 | UI 直接检查原始查询，未先摘出插件标记和排除绕过符，可能与实际执行解析不一致 | [SearchViewHints](App/ViewModels/Search/SearchViewHints.cs) 使用当前配置前缀完成相同的清理步骤 |

文档还修正了 `<f` 为文件在前、`>f` 为文件夹在前，盘符与排除词的冒号方向，独立盘符可以位于任意位置且最后一个生效，正则的大小写覆盖规则，以及成对单引号目前的实际行为。

日期简写 `2008` / `2008.8` 分别是 `2008-01-01 00:00:00` / `2008-08-01 00:00:00` 的严格比较阈值。快速搜索的标记只处理已取回的最多 1,000 条文件候选，不能把它描述成全盘排序后的前 1,000 条。只输入标记不会启动文件搜索。SDK 示例已改为任意位置的标记及取消重载。

Rebase 冲突合并保留了 `main` 的通知队列时钟补位行为和运动结束检查，也保留了插件取消与会话锁定处理。四个原本期待关闭后立刻补位的通知测试已改为在显式 `Feed` 后检查补位。中文 SDK 页面未被冲突中的删除结果清空。

## 待决策事项

以下行为暂时保留；其余确定性修复已完成。可以直接回复 `D1=A，D2=A，D3=B，D4=A`，或按编号补充要求。

### D1：反斜杠紧接空白时，是否只在插件标记中转义？

复现：`QueryTokenScanner.Scan(@"D:\Projects\ /\.md$/")` 会移除目录末尾用于转义空格的反斜杠，剩余搜索文本变成 `D:\Projects /\.md$/`。路径失去末尾分隔符，无法保留原本的直接子项查询含义。普通 `final\ report` 目前也会变成两个 AND 词，这是已有测试明确固定的行为。

临时可用 `D:/Projects/ /\.md$/`。相关代码：[QueryTokenScanner](Core/SearchIndex/Query/QueryTokenScanner.cs)。

| 选项 | 行为与代价 |
| --- | --- |
| A，建议 | 只在已识别的插件标记内处理 `\ `；普通查询保留路径分隔符。路径组合更直接，但旧的普通词转义行为会改变 |
| B | 保留当前转义规则，文档继续推荐前斜杠路径；兼容已有输入，但保留路径歧义 |

### D2：自定义前缀与命令、结果类型、即时答案冲突时，如何校验和迁移？

当前 `GlobalPrefixConflict("#")` 通过校验，已有测试也要求接受。设置为 `#` 后，扫描器会先把 `#dir` 当成插件标记取走，命令提供者无法按原意识别它；`$`、`%` 同样可能遮蔽内置命令或环境变量。结果类型与即时答案的保留字符检查仍固定使用默认 `\`，没有动态保留实际配置的插件前缀。普通文件名字符作为前缀也会改变以该字符开头的词的含义。

相关代码：[QueryTokenPrefixRules](App/Helpers/QueryTokenPrefixRules.cs)、[SearchSyntaxReserved](App/Helpers/SearchSyntaxReserved.cs)、[查询分派](App/ViewModels/Search/Dispatch/SearchQueryDispatchController.cs)。复现仅需配置校验和扫描器，不需要执行命令。

| 选项 | 行为与代价 |
| --- | --- |
| A，建议 | 新配置阻止占用 `#`、`$`、`%` 及其他已配置触发符；各设置页共享当前或待保存前缀校验。已有冲突值给出提示，等待用户修改；合法文件名字符前缀提示文本冲突，不自动迁移 |
| B | 允许重复配置，明确各提供者优先级并显示警告；兼容性较强，但部分功能仍可能无法触发 |

需要明确旧配置的处理方式，才能避免一次修复悄悄更改已有命令入口。

### D3：是否彻底移除成对单引号合词，并统一 UI 与 SDK 的空白规则？

扫描器将引号视为普通字符，但 `FzfPatternParser` 仍把 `'final report'` 合成一个包含单引号和空格的词。单引号在 Windows 文件名中合法，所以该词确实能匹配对应文件名。直接调用底层匹配器时，`\ ` 的处理也与经过 UI 扫描器的路径不同。

本次保留行为，修复其重复扫描开销并按实际行为写入文档。相关代码：[FzfPatternParser](Core/SearchIndex/Fzf/FzfPatternParser.cs)、[QueryTokenScanner](Core/SearchIndex/Query/QueryTokenScanner.cs)。

| 选项 | 行为与代价 |
| --- | --- |
| A | 移除单引号合词；普通空白统一为 AND，引号只作为字符。连续短语使用 `/final\sreport/`；符合简化语法的方向，但改变已有引号查询 |
| B | 保留并正式支持当前合词规则，明确 SDK 哪个入口接收原始查询；兼容性较好，但需要继续维护两层解析规则 |

D1 选择 A 时，也应同步明确 SDK 是否负责处理原始插件标记和转义。

### D4：正则累计耗时与完整搜索结果需要什么预算？

`/(?=(a+)+$)/` 对长串 `a` 后接 `!` 的候选会用完回溯预算。单个候选有 250 毫秒限制，但许多不同文件名可以连续超时。本次补充的取消检查能在候选之间停止工作，不能限制用户一直等待的查询。完整搜索的 `FullSearchFileLimit` 为 `int.MaxValue`，广泛命中的结果仍可能占用大量内存；正则编译本身也没有整次查询预算。

相关代码：[RegexClauses](Core/SearchIndex/Fzf/RegexClauses.cs)、[SearchViewModel](App/ViewModels/Search/SearchViewModel.cs)。当前行为是跳过单个超时候选并继续扫描，不能把它描述为整次查询最多 250 毫秒。

| 选项 | 行为与代价 |
| --- | --- |
| A，建议先做 | 第一次正则匹配超时就终止该查询，并明确显示原因；如保留已有结果，必须标明不完整。大幅限制连续回溯开销，但可能失去后面本可匹配的结果 |
| B | 为整次查询设置时间、结果数量及表达式长度预算；请给出默认值或允许后续用真实大索引测量后定值。约束更完整，但会改变完整搜索的承诺 |
| C | 继续跳过单个超时候选，依赖用户取消；保留当前结果策略，也保留累计资源开销 |

## 验证记录

使用 .NET SDK `10.0.401`、MSTest `4.4.1`，目标框架 `net10.0-windows`。以下命令均已成功执行：

| 命令（仓库根目录） | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| `dotnet test Tests/Core/Core.csproj --verbosity minimal` | 1905 | 0 | 0 |
| `dotnet test Tests/App/App.csproj --verbosity minimal` | 1941 | 0 | 0 |
| `dotnet test Tests/Plugins/CoreExtensions/CoreExtensions.csproj --verbosity minimal` | 461 | 0 | 0 |
| `dotnet test Tests/Cli/Cli.csproj --verbosity minimal` | 28 | 0 | 0 |
| `dotnet test Tests/PluginSdk/PluginSdk.csproj --verbosity minimal` | 93 | 0 | 0 |
| 合计 | 4428 | 0 | 0 |

`Site` 目录中的 `npm run site:build` 成功。修改的 7 个翻译 JSON 均可解析，每种语言包含同一组 754 个键。Markdown 围栏配对、`git diff --check`、冲突标记检查通过；`main` 是当前提交的祖先，没有残留 rebase 状态。

现有非阻断警告：App 测试的 `SettingsValidationGateTests.cs:176` 有可空泛型约束警告；Vite 提示部分配置文件的 CommonJS/ESM 兼容性。本次未调整这些与搜索修复无关的设置。

性能结论来自源码检查和压力回归，没有对用户真实磁盘索引进行基准测试，也没有测量生产环境的速度提升。安全检查覆盖搜索解析及其执行路径，D4 的累计资源预算仍待选择。

## 交付状态

| 内容 | 状态 |
| --- | --- |
| Rebase 冲突 | 已解决并继续完成 |
| 不需要决策的实现与性能问题 | 已修复并验证 |
| 七种语言文档与帮助 | 已同步 |
| 修复、回归测试与审查记录 | 纳入本次本地提交；未推送 |
| D1–D4 | 暂时保留现状，等待集中答复 |
