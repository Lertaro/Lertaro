# 搜索语法与维护约束

2026-11-22 按 `feat/search-regex` 的 `af1e74e2` 核对，基线为 `main` 的 `edf98124`。新增语法尚未合入 main。用户示例见[搜索语法](../Site/zh-CN/user-guide/search-syntax.md)，历史反例与测量见[增量审计](search-regex-incremental-audit.md)和[修复记录](search-regex-fixes.md)。

## 当前语法

| 输入 | 含义 |
| --- | --- |
| `report final` | AND，词序不限，普通词忽略大小写 |
| `report \| summary` | OR，竖线必须是独立词；默认 AND 优先，可切换为 OR 优先 |
| `report :draft` | 名称中排除连续子串 `draft`；排除词不展开别名 |
| `?report` | 对该词反转模糊/精确子串模式 |
| `/^report.*\.pdf$/` | .NET 正则匹配候选的原始名称 |
| `d: report` | 裸盘符词限定 D 盘；`d:report` 保留为普通文本 |
| `D:\docs\` | 列出该目录直属内容；路径内部空白保留 |
| `report \doc` | 插件分类标记，可出现在查询任意位置 |
| `report <s>20m` | 保留大小严格大于 20 MiB 的结果，再按大小升序排列 |
| `*node_modules` | 查询首字符的 `*` 绕过用户排除规则 |

普通查询中的 `!`、单/双引号、`^`、`$` 都按字面处理。普通词不支持引号合词、空格转义或括号分组；短语、锚定和复杂条件使用正则。排除词或插件标记单独成句时不启动普通文件搜索，需补充名称、盘符、路径或正则。

索引规则的 `/.../` 使用独立解析器和 50 ms 超时，见 [index-rules.md](index-rules.md)。快速面板来源过滤仍接受 `!glob`，并将旧分类写法迁移为 `\doc`；这是该设置字段的兼容规则。

## 解析与执行顺序

1. [QueryTokenScanner](../Core/SearchIndex/Query/QueryTokenScanner.cs) 先移除整句开关 `*`，再提取以 `\`、`<`、`>` 开头的标记。普通文本保留原始空白；双前缀 `\\` 保留为 UNC 文本。标记内部可用 `\ ` 表示关键词空格。
2. [RegexQueryParser](../Core/SearchIndex/Query/RegexQueryParser.cs) 提取原文正则，随后 [SearchQueryParser](../Core/SearchIndex/Query/SearchQueryParser.cs) 才归一化普通文本并判断盘符、路径模式。
3. [FzfPatternParser](../Core/SearchIndex/Fzf/FzfPatternParser.cs) 和 [TermTriggers](../Core/SearchIndex/Fzf/TermTriggers.cs) 构造普通词与布尔分组；[FzfPatternMatcher](../Core/SearchIndex/Fzf/FzfPatternMatcher.cs) 对候选名称求值。
4. 索引、实时目录和别名分支生成候选。普通词可由读音或别名满足，名称条件在候选名称上验证。
5. [QueryTokenDispatcher](../App/ViewModels/Search/QueryTokenDispatcher.cs) 按输入顺序调用首个认领标记的 provider，处理已召回的文件和目录子集。

快速、完整、内嵌窗口和 App 搜索管道均接入查询解析与标记派发。SDK 模糊匹配、设置搜索和本地列表过滤复用词与正则匹配规则。结果类型触发符由快速窗口读取，必须位于原始查询的第一个字符；插件触发词优先。

## 正则与名称条件

- 子句必须是完整非空词，以 `/` 开头、未转义的 `/` 结尾。正文中的字面斜杠写 `\/`，空白写 `\s`。未闭合子句、`//`、`C:/Users`、`/usr/local/` 保留为路径文本；不支持 `/regex/i` 尾随标志。
- 多个正则子句始终 AND，外层 `|` 不改变它们的关系；正则内的任选其一写 `/(foo|bar)/`。排除词参与普通词的布尔分组，例如直接标题 `report temp` 可满足 `report | :temp`。
- 默认忽略大小写，`/(?-i)Report/` 可区分大小写，`(?i)` 可恢复。正则读取原始名称；普通词与排除词继续使用既有 Unicode 归一化。
- 正则检查名称，排除词检查名称中的字面子串。进入别名、元数据、关键词或祖先回退前先验证名称条件，再用正向词匹配其他表示。标题直接匹配保留原有 OR 语义。
- [RegexClauses](../Core/SearchIndex/Fzf/RegexClauses.cs) 优先使用 NonBacktracking；不支持的合法模式回退到 Compiled 引擎，每次回退匹配预算 250 ms。一次超时使该解析查询失败，窗口清除部分结果；已开始的并行匹配各自退出。该预算约束单次匹配。
- 编译缓存上限 256，活动查询用弱键保留编译结果。失败状态属于解析查询，同模式的新查询可重试。无效模式不匹配，并由宿主提示具体子句。
- 必需字面量用于保守预筛；归一化会改变该字面量时放弃预筛。无必需字面量的正则可能扫描全部唯一名称。
- [TextHighlighter](../App/Converters/TextHighlighter.cs) 缓存完整查询计划。正则按原串坐标着色，不计入普通词的连续性和覆盖率；添加扩展名条件不会因此降低普通词的相关性。高亮超时停止该计划的正则绘制。

## 标记与设置

[SearchSyntaxReserved](../App/Helpers/SearchSyntaxReserved.cs) 定义保留字符。插件前缀须为 Windows 文件名非法字符且不与语法冲突，当前只接受 `\`。插件通过 [SearchSyntaxService.TokenPrefix](../PluginSdk/Services/SearchSyntaxService.cs) 读取宿主值。

内置排序键为 `s/c/m/a/f`（大小、创建、修改、访问、文件夹），`<` 升序、`>` 降序。第二个箭头表示严格比较：`<s>20m` 为大于，`<s<20m` 为小于，等于阈值的不保留。大小使用 1024 进位的 k/m/g/t；日期使用年在前的固定格式，两位年份按 20xx，缺月/日补 1。文件夹阈值接受 `f`、`folder`、`dir`，`>` 留目录、`<` 留文件。

空阈值 `<s>` 只排序。非空非法阈值或无人认领的标记清空文件/目录子集，并由宿主提示。多个稳定排序依次执行，最右排序为主键。分类与排序由 CoreExtensions 提供，需启用对应组件。

保存校验和启动迁移见 [settings-permissions.md](settings-permissions.md)。`ConfigFieldValidation.TriggerKeyword` 标识需要校验的插件触发词；`TokenKeyword` 用于展示带宿主前缀的完整标记。旧 `IsTriggerWord` 属性继续转发以兼容已编译插件，见 [pitfalls.md](pitfalls.md)。

## 候选、展示与取消

- 快速窗口含标记时，文件请求 limit 为 1,000；后处理只能筛选已召回候选。内建类型可在截断前下推，见 [entry-categories.md](entry-categories.md)。
- 完整窗口保留索引候选。路径模式且含正则时，经过相关性排序、标记、侧栏及列排序，最多展示 8,000 条。限额位于 [SearchViewResultRenderer](../App/ViewModels/Search/SearchViewResultRenderer.cs)，超限提示按最终列表重算；重新筛选和排序仍可使用原候选。
- 实时目录最多枚举 10,000 项；完整窗口的展示限额与目录枚举上限分别生效。宽泛查询的工作量与候选内存仍取决于召回规模。
- `IQueryTokenProvider` 的三参数 `ApplyAsync` 接收取消令牌，默认实现转发旧重载。宿主可取消等待，旧 provider 执行体仍可能继续；查询/批次身份用于拒绝迟到结果，迟到异常也须观察。
- provider 的 `CanHandle` 异常会跳过该 provider；`ApplyAsync` 失败或返回非宿主结果对象时清空文件子集；`GetHighlightText` 失败只丢弃附加高亮。
- [LiveScanCache](../Core/Services/Search/LiveScanCache.cs) 按规范路径及 direct/recursive 分开缓存，可复用条目上限 32。活动扫描可复用；已完成项在起算 2 秒后过期。淘汰与失效只移出缓存，活动任务独立跟踪，关闭时锁外取消并最多等待 2 秒。
- 冷扫描首次查询的取消或正则失败停止该请求的匹配与投递，原始遍历继续供后续查询复用。不可取消的文件系统调用可能超过关闭等待上限。
- 跨进程失败帧与配套版本见 [windows-integration.md](windows-integration.md)。

## 回归入口

| 曾出现的问题 | 维护要求与测试 |
| --- | --- |
| 正则斜杠触发路径模式 | 先提取子句再判断路径；`RegexQueryParsingTests` |
| 普通词归一化改写正则 | 原文正则读取原始名称，预筛保守降级；`RegexPrefilterTests` |
| 别名放回不合格标题 | 每个别名、关键词及祖先入口检查名称条件；`ExclusionAliasSearchTests`、`PathTermFallbackTests` |
| 到达顺序提前丢掉最佳行 | 最终过滤排序后限制展示；`StreamingResultAccumulatorTests`、`SearchViewResultRendererTests` |
| 每个候选重复超时 | 查询失败状态与编译缓存分离，跨管道传播；`RegexTimeoutTests`、`SearchRegexFailureTests` |
| 高亮逐行重建计划 | 缓存完整查询并按配置和别名版本失效；`TextHighlighterTests` |
| 淘汰扫描逃出关闭等待 | 活动任务独立跟踪；`LiveScanCacheTests` |
| 非法阈值看似生效 | 空阈值只排序，非空非法值失败；`SortFilterQueryTokenProviderTests` |

修改候选预算、超时策略、provider 线程模型或缓存生命周期时，以当前产物重新测量，并核对结果集合与顺序。
