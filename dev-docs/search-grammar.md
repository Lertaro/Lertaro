# 搜索语法的维护边界

当前入口是 [SearchQuerySortParser](../Core/SearchIndex/Query/SearchQuerySortParser.cs)、[SearchQueryParser](../Core/SearchIndex/Query/SearchQueryParser.cs) 和 [FzfPatternParser](../Core/SearchIndex/Fzf/FzfPatternParser.cs)。旧 `feat-search-reimpl` 分支的 `:term` 排除、`?term` 反转和查询 `/regex/` 方案不属于当前实现。

## 当前语法

| 形状 | 当前含义 |
| --- | --- |
| 普通词之间的空格 | AND；普通词忽略大小写 |
| 两侧用空格分开的 `\|` | OR；AND/OR 优先级由设置决定 |
| `!word` | 名称级排除，按字面精确匹配，不展开拼音别名 |
| `'word` | 翻转普通词的模糊/精确匹配 |
| `'word'`、`^word`、`word$`、`^word$` | 边界、前缀、后缀、整名匹配 |
| `'hello world'` | 名称匹配层的单引号短语 |
| `d:report` | 限定盘符，`report` 仍参与匹配 |
| 含路径分隔符 | 进入路径模式；末尾分隔符表示直属内容 |
| 尾部 `:a,b,c` | 插件 Token 段；前缀可配置，分类示例是 `:@doc` |
| 查询第一个字符 `*` | 本次查询绕过用户排除规则 |

Token 段支持引号与转义，其规则与名称匹配层不同；不能因 Token 接受双引号，就推断名称层也有同样的短语语法。索引设置中的 `/regex/` 由 `IndexRuleSyntax` 处理，见 [index-rules.md](index-rules.md)，不能直接套进搜索框。

## 容易破坏的边界

1. **解析层分工。** 先剥整句开关与尾部 Token，再把干净查询用于搜索和高亮；Token 的业务含义交给 `IQueryTokenProvider`。没有合法尾部段时保留原查询，不能顺手压缩路径内部空格。
2. **名称要求不能借别的拼写满足。** 排除由候选自己的名称回答；新增读音、别名或父路径匹配分支时，必须继续执行名称级否决。匹配与高亮必须使用同样的精度和查询形态。
3. **路径先保真。** 查询文本在解析前归一全角和 Unicode 组合形式。不要把盘符冒号、UNC、路径分隔符当作任意插件标记；新增语法必须检查这些冲突。
4. **后处理影响不了未召回候选。** Token 在结果返回后执行，快速窗口的有限候选池不能据此承诺“全索引最大文件”或“全部分类结果”。现有内建类别下推与通用 Token 是两条能力，见 [search-performance.md](search-performance.md)。
5. **取消与迟到结果。** `QueryTokenDispatcher` 可停止宿主等待；SDK 的两参数 `ApplyAsync` 不能中止 provider 本体。控制器还要按批次取消并检查查询/快照身份，观察迟到异常，防止旧批次覆盖新查询。

语法改动应同时核对解析、引擎、插件接缝、高亮、设置校验与各语言帮助；不要用未合入方案覆盖现行契约。现有验证入口包括 [SearchQuerySortParserTests](../Tests/Core/SearchIndex/Query/SearchQuerySortParserTests.cs) 与 [QueryTokenDispatcherTests](../Tests/App/ViewModels/Search/QueryTokenDispatcherTests.cs)。
