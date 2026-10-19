# 搜索语法

启动器搜索框的查询语法规格：哪些字符在哪一层生效，为什么这些字符可以安全地当触发符用，以及 `:term`、`/.../` 这类名字级子句为什么必须由候选自己的名称回答。

## 适用范围与核对基线

本文写的是 `feat-search-reimpl` 分支的语法（`bf43109e`，`origin/feat/search-regex` 指向同一提交），该分支仍未合入 main。核对基线：main = `9a6b9769`，分支侧 22 个提交未进 main，main 另有 45 个提交不在分支上，合并基 `3081efed`。引用任何 `:term`、`/regex/` 结论前先确认在哪条线上。

只在 `feat-search-reimpl` 上成立的语法：

| 项目 | `feat-search-reimpl` | main |
|---|---|---|
| 排除 | `:term`，恒按连续子串 | `!term` |
| 精确性 | `?term` 做模糊↔精确反转 | `'term` 翻转 |
| 锚定 | 无锚定语法，交给 `/^word/`、`/word$/` | `^word`、`word$`、`'word'` |
| `FzfTermKind` | 只有 `Fuzzy` 与 `Exact` | 另有 `ExactBoundary`/`Prefix`/`Suffix`/`Equal` |
| 正则子句 | `/.../` 整词子句 | 无 `RegexQueryParser`/`RegexClauses`/`TermTriggers`/`SearchSyntaxReserved`/`FzfPatternMatcher`/`QueryTokenScanner` |
| 插件 Token | 词首单一可配置前缀（默认 `\`），可在查询任意位置 | 尾部 `:a,b,c` 段式 Token，`WildcardQueryTokenProvider`/`PathExclusionQueryTokenProvider` 仍在 |
| 裸盘符 | `d:` 是恰好 ASCII 字母加冒号的整词，`d:report` 保持字面 | `d:report` 取 `d` 盘并把 `report` 当词 |
| 引号 | `"` `'` 按字面处理 | `'...'` 合词（`MergeQuotedPhrases`） |
| 路径模式判定 | 先摘除正则子句再判 | 直接判 |
| 路径正则结果 8000 条截断上限 | 有 | 无 |

两条线上都成立的：普通词空格 AND、两侧带空格的 `|` OR 与优先级开关、模糊开关、普通词恒忽略大小写、含分隔符进入全路径模式、路径末尾分隔符列出直属内容。

设置侧的规则语法是另一套判定，别和查询侧混：`IndexRuleSyntax`（main 的 `Core/Settings/IndexRuleSetting.cs:58`，Path/Glob/Regex 三个 kind，同样以 `/.../` 包裹正则、基选项带 `IgnoreCase`、每条 50ms 匹配预算，正则判定排在分隔符归一之前）只存在于 main，`feat-search-reimpl` 上还没有这个文件。

已否决的方向不在本文范围内：`/regex/i` 尾随标志位、`>cs` / `>ci` 大小写标记、`+keyword`、`~keyword`、`#tag`。大小写能力的现状见「大小写」一节。文末附录给出每条事实的 `file:line`。

## 核心判断

这套语法已完成最关键的一步：多套解析规则收敛成统一的触发字符入口，插件只通过宿主的单一前缀认领 Token。语法因此从实现细节变成了用户契约，兼容性的重要程度接近插件 API。

未来不需要再造特殊字符串规则。保持 **Token → Handler → 结果** 的形状，把业务行为放在 provider，把语法判断留在扫描器与解析器，Token 数量翻倍时复杂度仍近似线性增长。

## 触发字符为什么安全

软件只运行在 Windows，现有全部顶级触发字符 `\ < > : * / ? |` 都是文件名非法字符，所以 `:draft`、`?report`、`<s>20m` 天然不可能与真实文件名重名。唯一的例外是 UNC 路径：词首双倍前缀会被扫描器显式放行回搜索文本。极端场景由 `/regex/` 兜住。

这条前提是**字符集合的性质，不是语法结构的性质**。`#` `$` `%` 合法于文件名，也已经被即时答案占用（`#` 以管理员身份运行、`$` 以普通身份运行、`%` 环境变量），保留字表把它们单列为 `ProviderClaimedCharacters`。以后要开新触发字符，先要求它落在文件名非法字符集合里。

main 上那套旧运算符正好是这个判据的反例：`'` `^` `$` 都合法于文件名，所以它们当触发符时，同一个字符串在搜索框与插件目录 seam 里读成两种意思（`!temp` 在 seam 里排除、在文件列表里是字面量），分支把这四个统一改成字面字符正是为了收住这一类分裂。

「顶级触发字符」这句话只覆盖语法自己拥有的那一套。结果类型触发符是用户可配的单字符，同样读整条查询的第一个字符，而校验只挡住语法字符与 `#` `$` `%`（`SearchSyntaxReserved.UnusableTokenPrefixCharacters`），`;` 这类合法文件名字符是可以配的。

## 分层

```
原始输入
  └─ QueryTokenScanner.StripExclusionBypass     整句级 '*' 绕过排除（只看第一个字符）
  └─ QueryTokenScanner.Scan                     词首判定，产出 (剩余文本 + Token[])
  └─ RegexQueryParser.Split                     摘出 /.../ 子句（必须在路径判定之前）
  └─ SearchQueryParser.Parse                    路径模式判定 + 裸盘符作用域
  └─ FzfPatternParser / TermTriggers            词级语法：空格 AND、' | ' OR、':排除、?反转
        ↓ 匹配 / 打分 / 高亮（服务进程内）
  └─ QueryTokenDispatcher → IQueryTokenProvider  对已取回的结果做排序、分类过滤
```

四处约束需要记住。

**摘除顺序是契约。** `/\.exe$/` 同时含 `\` 与 `/`，晚摘一步整条查询就会被读成一条不可能存在的路径，`SearchQueryParser.Parse` 方法开头因此调用正则摘除（`Core/SearchIndex/Query/SearchQueryParser.cs:12`）。

**词级语法不在 Token 链里。** `:` `?` `|` 在 `FzfPatternParser` 内部读取，`QueryTokenScanner` 只负责可配置前缀与 `<` `>`。两层是不同的东西。

**Token 链跑在匹配之后。** `QueryTokenDispatcher` 的输入是搜索已经产出的文件行；快速窗口先取有限候选再执行排序与分类过滤，未取回的匹配不参与处理。任何需要影响匹配判定的开关都放不进这一层。

**`:term` 与 `/re/` 只回答候选自己的名称。** 两者都不走拼音别名、不看路径段、也不在任何 mask 里占位：`:temp` 否决的是名字里含 `temp` 的行，`/\.pdf$/` 描述的是名字里真实存在的字符。凡是改成"从读音、别名或路径段判定一行"的层，都必须把名称一并交进去、在门口先否决这两项，否则一个读音命中就把名称不合格式的行收下。展开的经过与判据见下一节。

词首触发字符被扫描器摘出后，如果没有 provider 认领，结果是空集，不会退回普通文本。

## 语法清单

下表是 `feat-search-reimpl` 的语法，main 侧的差异见开头那张表。

| 类别 | 作用 | 实际语法 |
|---|---|---|
| 查询型 | 影响匹配结果 | 普通词（AND）、`a \| b`（OR）、`:term`（排除，恒按连续子串）、`?term`（模糊↔精确反转）、`/re/`（子句）；`:term` 与 `/re/` 是名称级要求，只看候选自己的名称 |
| 过滤型 | 减少结果 | `\doc` `\img` `\video` `\audio` `\zip` 与自定义分类；`<f>f` / `<f>dir` 仅文件夹；`<s>20m` 阈值 |
| 排序型 | 只改顺序 | `<s` `>s` 大小、`<c` `>c` 创建、`<m` `>m` 修改、`<a` `>a` 访问、`<f` `>f` 文件夹/文件；多个排序标记中最右者为主键 |
| 范围型 | 改变搜索空间 | 裸盘符词 `d:`（恰好 ASCII 字母加冒号的整词，`d:report` 不算）、含分隔符的全路径模式、路径末尾分隔符列出直属内容 |
| 整句开关 | 改规则不改词 | 词首单个 `*` 绕过用户排除规则；正则内的 `(?i)` / `(?-i)` |

阈值必须写成 `<s>20m` 这一支形状：第二个触发符只表比较方向，`>` 是下界、`<` 是上界，与排序箭头无关。

OR 已经存在（两侧带空格的 `|`，默认 AND 结合更紧，优先级在「通用 → 系统」可切），分组括号则不支持。这条边界应当守住：保持词加少量布尔算子加前缀 Token 的线性结构，不引入括号分组、不引入字段语法、不引入嵌套表达式，复杂表达一律交给正则。

## 名字级子句的落点

`:term` 与 `/.../` 必须由候选自己的名称回答，这条规则在本分支上被修过三次，每次都是新写法的消费者没被告知这件事。文档留在这里，是为了下次再加一层判定时能一句话想起来。规则本身在 `Core/SearchIndex/Fzf/FzfPatternMatcher.cs` 的 `TryMatchAlias(pattern, alias, name, ...)` 注释里，以及 `Site/*/user-guide/search-syntax.md`。

三次落点：

1. **ancestor pass（`PathTermFallback`）** —— `:term` 在分支语法下不占 mask 位（`HasPositiveTerm` 让纯排除项的逐词 pattern 零命中），占位就永远填不满，整条 fallback 对带排除项的查询返回空；而 `/.../` 在这个 pass 根本没人问，靠文件夹就能把名称不合格式的行捞回来。改法：排除项集合不进 `termPatterns`，名称级子句统一由 `NamePassesClauses` 在**行的名称**上否决。
2. **grid 层（已随 main 的 `b8e1061c` 删除）** —— 曾经给 `TryMatchEntry`/`TryMatchPayload` 加过 `ReadOnlySpan<char> name`，让物化读音只回答词、子句回答名称。grid 没了，那两次提交（`c85b31ab`、`dc50cc03`）在本次 rebase 里被跳过/清空，规则改由下面第 3 条承接。注意 `3a8ad26b` 现在只剩别名视图测试的迁移，commit message 还在讲 TryMatchProvider —— squash 时改掉。
3. **live 读音判定（`PinyinNameEvaluator`）** —— main 用它取代了 `FzfPattern.TryMatch` 作为名称 tier。它逐词读 NFA、并且自己处理排除项（`term.Inverse` 命中字面就 fail，缺席就满足），但整个类里没有 `Regexes` 这个词：查询带 `/.../` 时，一个读音命中就把名称不合格式的行收下。修前量出来的一条：`bg /\.pdf$/` 对 `报告` 返回 true（`FakeLiveProvider` 供 `bg` 的跨度）。改法是在 `TryScore` 门口按**整个名称**判一次子句，位置在 `'|'` 分段之前 —— 子句说的是名称，不是它的某一段。覆盖：`PinyinNameEvaluatorTests.RegexClause_IsAnsweredFromTheNameNeverFromTheReadings`（两侧各留一条对照，免得"任何读音都供不出的子句"和"根本没被查过的子句"混成一件事）。

新增一层"从读音/别名/路径段判定"的地方时，问两句：

- 它拿去做匹配的文本，是**候选的名称**还是**名称的另一种写法**？后者必须把名称一并交进去。
- `FzfPattern` 的哪些要求不在这次判定的词汇里？现在有两个：`Regexes` 与 `:term`。

## 大小写

普通词恒忽略大小写，没有开关，两条线都一样。`FzfPatternParser` 读取每个词时执行大写归一并固定传 `CaseSensitive: false`，smart case 已退役。底层 `FzfAlgorithm`、`FzfExactMatcher`、`FzfByteExactMatcher`、`HighlightMask`、`AliasHighlightMarker` 都接收 `bool caseSensitive`，参数已经通到匹配与高亮，只是没有任何调用方传 true。

分支上正则子句可以区分大小写，写法是内联选项 `(?-i)`。`RegexClauses.BaseOptions` 带 `IgnoreCase`，内联选项在运行时覆盖构造参数，`/(?-i)Report/` 只命中 `Report`；NonBacktracking 与回退引擎结果一致，已由 `FuzzyMatcherTests` 走公开 seam 两侧各验一次。这条已经写进 in-app help 的正则章节与「基础与布尔」章节，也写进站点 7 个 locale 的正则小节。

将来若要普通词也敏感，工作量在大写归一那一步以及把标志位穿过 term 构造与高亮，属于跨层改动加文档加设置校验；`<` `>` 那一层只能携带标志位，承担不了匹配判定。

## Regex 战略

这一节整节只在 `feat-search-reimpl` 上成立。不再新增精细匹配语法，精细表达全部交给正则。

| 想要的表达 | 写法 |
|---|---|
| 前缀 | `/^report/` |
| 后缀 | `/\.pdf$/` |
| 短语与含空格的名字 | `/final\sreport/` |
| 任选其一 | `/(foo\|bar)/`，多个子句之间恒为 AND，外侧的 `\|` 不改变它们的关系 |
| 大小写敏感 | `/(?-i)Report/` |

正则侧的既有约束已经是对用户的承诺，改动时必须同步：子句是以 `/` 开头、以未转义 `/` 结尾的整词；体内未转义的 `/` 让整个词退回路径文本，所以 `C:/Users`、`/usr/local/`、`/mnt/c/Users` 仍是路径；字面斜杠写 `\/`；空子句 `//` 无效；子句内不能有裸空白；完整窗口的路径正则结果有 8000 条截断上限；匹配优先走 NonBacktracking，回退引擎下每个候选 250ms 预算。

## 新增 Token 的成本

| 想加的东西 | 改扫描器 | 改保留字符表 |
|---|---|---|
| `\git`、`\go` 这类新关键词 | 不用 | 不用 |
| 新的 `<` / `>` 键 | 不用 | 不用（受 `CanHandle` 形状约束） |
| 新的顶级触发字符 | 要（`IsToken` 里 `<` `>` 是硬编码常量） | 要（两张表加设置校验加迁移提示） |

单一前缀把「插件能加什么」收敛成「只能加关键词」，这正是扫描器不必改的原因。新增顶级触发字符则是一次完整的 API 变更，扫描器、保留字表、前缀校验、7 份 in-app help、7 份站点文档和旧设置迁移都要同批走。

`QueryTokenPrefixRules` 已经把冲突分级实现了一半：用户新输入的值拒绝保存，从旧版本带过来的值只警告，配套一次性的启动通知，避免把用户锁在设置窗外。

## 插件 Token 治理

冲突消解目前只有两条规则：同一 provider 内按关键词最长匹配（同时存在 `\a` 与 `\audio` 时取后者），跨 provider 按注册顺序第一个 `CanHandle` 胜出，先到先得且没有提示。

设置页的「插件标记」章节是静态说明文本，不列当前已注册的规则。`SearchSyntaxService` 存在，但只向插件暴露宿主前缀这一个读回点。

要补的三件事按顺序排：把已注册 Token 清单变成用户可见；provider 之间的静默覆盖要有说法；启动期冲突检测最后做。第三件事要谨慎，历史上「另一个插件已认领该字符」这项检查是被主动删掉的，理由是扫描器按全局前缀摘词并把字符一起交给 provider，provider 的自有值要么与之一致、要么什么都不是，那个检查命中的恰好是唯一能用的值。新增检测前先确认它报错的对象不是正确配置。

## 向后兼容

退役写法已经有记录，散在提交说明、代码注释、测试和帮助文本里，还没有机器可读的表。

| 已退役写法 | 现在的含义 | 替代 |
|---|---|---|
| `!word` `'word` `^word` `word$` | 字面字符 | `:word`、`?word`、`/^word/`、`/word$/` |
| 尾部 `:a,b,c` 段式 Token | 已废除 | 词首触发字符，可在查询任意位置 |
| `"` `'` 引号合词 | 按字面处理 | 词内空白交给 `/final\sreport/`；插件 Token 关键词仍支持 `\ ` 转义 |
| `:audio` 分类过滤 | 现为排除词 | `\audio` |
| 通配符与路径排除型 Token Provider | 已删除 | `/\.pdf$/` 等正则子句 |

保留字体系实际已经有两张表：`LeadingCharacters` 与由配置 schema 驱动的触发词校验。缺的是废弃表这一张。补法建议把上面这张 Markdown 表变成 `SearchSyntaxReserved` 里的只读列表，让设置页与站点文档都从它生成，避免出现表和帮助文本两份真相。

一处尚未收口：快速面板的存储过滤器模式里 `!` 取反语义仍在（`!*.xxx` 在正向集合算完后剔除，读取时还继续归一旧版 `:@doc` 写法），它已经不属于查询语法。两条线都还是这样。这是文档与实现偏离那条风险上目前唯一还亮着的条目。

## 已删除的锚定 kind 与裁定

分支退役 `^ $ ' !` 之后没有任何地方再产出锚定 `FzfTermKind`，只剩 switch 在读。已按"删掉 kind 与对应 switch 逻辑"落地（`fb6cda90`）：`FzfTermKind` 现在只有 `Fuzzy` 与 `Exact`；`FzfAlgorithm.Match` 与 `FzfBytePattern.Match` 各去掉 4 条 arm；两个 `ExactMatch` 去掉 `boundaryCheck` 形参和那条只在边界判定下成立的加分公式；`PrefixMatch`/`SuffixMatch`/`EqualMatch`/`IsBoundaryMatch`/`SpanEquals`/`FzfAlgorithm.LeadingWhitespaces`/`TrailingWhitespaces` 全删。顺带 `PinyinNameEvaluator` 里因此变成恒真的两处 `Kind is Exact or Fuzzy` 也去掉了。`OtherReading` 与两个 `Match` 的 `_ =>` arm 留着：C# 对 enum switch 要求穷尽，成员减少并不等于值域减少。

main 有两条测试原先靠旧语法守着这些 kind：`OperatorTerms_AreLiteralOnly`（`^bg`）改写成分支真有的那个运算符 `?bg` 的行为，`AliasReading_LeavesAnOperatorsOwnKindAlone` 直接删除。

## 未来方向

**Token 治理收口。** 退役表成为唯一真相源，设置页列出当前已注册的 Token，顺带解决可发现性。

**SearchPlan 显式化。** 代码里目前只有 `ParsedSearchQuery`，覆盖路径模式、盘符和已摘出的子句，排序与 Token 不在内。显式化它的动机是诊断和解释，向用户说明「report :temp >s /2026/」为什么是这个结果，以及一个没认领的 Token 为什么把查询筛空。

**插件 Token 生命周期。** 先解决跨 provider 的静默覆盖，再谈冲突检测。

长期风险排名：`QueryTokenScanner` 演变成 God Object、`QueryTokenDispatcher` 规则持续膨胀、插件 Token 名称冲突、文档与实现再次偏离。Provider 顺序依赖这一条已经不是风险而是现状，跨 provider 就是先到先得。

## 评分

```
鲁棒性     9/10
可维护性   8.5/10
兼容性     8/10
可扩展性   9/10
可发现性   7/10
```

## 附录：证据

以下 `file:line` 逐条对着 `feat-search-reimpl`（`bf43109e`）与 main（`9a6b9769`）的当前工作树核过。分支每次 rebase 都会让行号过期，核对不上时以文件与函数名为准——行号随分支变动。

| 事实 | 出处 |
|---|---|
| 顶级触发字符集合 `\ < > : * / ? \|`，`# $ %` 由 provider 认领 | `App/Helpers/SearchSyntaxReserved.cs:8-20` |
| 扫描器只认 `<` `>` 与可配置前缀；双倍前缀留给 UNC | `Core/SearchIndex/Query/QueryTokenScanner.cs:51-63`（`IsToken`） |
| 词内转义空白仅对 Token 生效；整句 `*` 必须在 Scan 之前读取 | `QueryTokenScanner.cs:66`（`SplitWords`，转义判定在 `:86`）、`:116`（`StripExclusionBypass`） |
| 正则子句的形状规则与三条路径保护 | `Core/SearchIndex/Query/RegexQueryParser.cs:13-24`（类注释）、`:83-115`（`TryReadClause`） |
| 摘除早于路径判定 | `RegexQueryParser.cs:5-12`，`Core/SearchIndex/Query/SearchQueryParser.cs:7-15` |
| 含分隔符进入全路径模式；裸盘符恰好两字符 | `SearchQueryParser.cs:14-36`、`:61-62`（`IsBareDriveSpec`） |
| main 的裸盘符取盘后余文仍是词；路径判定无正则摘除 | main `Core/SearchIndex/Fzf/FzfPatternParser.cs:17-25`，main `Core/SearchIndex/Query/SearchQueryParser.cs:8` |
| 正则基选项带 `IgnoreCase`，共享缓存 256 条，活动查询另有弱键表 | `Core/SearchIndex/Fzf/RegexClauses.cs:19-31`、`:90-105`（NonBacktracking 与 250ms 回退） |
| 普通词恒忽略大小写，smart case 已退役 | `Core/SearchIndex/Fzf/FzfPatternParser.cs:188-189`，main 同文件 `:194-196` |
| `caseSensitive` 参数已通到匹配与高亮 | `Core/SearchIndex/Fzf/FzfAlgorithm.cs:40`、`FzfByteExactMatcher.cs:9`、`Core/SearchIndex/HighlightMask.cs:133-168`、`Core/SearchIndex/AliasHighlightMarker.cs:64-102` |
| `(?-i)` 覆盖 `IgnoreCase`，两种引擎一致 | `Tests/Core/SearchIndex/FuzzyMatcherTests.cs:34-38`（`/(?-i)Report/` 对 `report` 为 false，`(?=…)` 写法走回退引擎） |
| `:exclusion` 恒 Exact，`?` 是模糊↔精确反转，`:` 先于 `?` 读取 | `Core/SearchIndex/Fzf/TermTriggers.cs:20-25, 37-66` |
| 名称级子句在每一层都由名称否决 | `Core/SearchIndex/Fzf/FzfPatternMatcher.cs:11-21, 86-96`（注释写明读的是名称）、`Core/IndexV2/Search/PathTermFallback.cs:264-273`（`NamePassesClauses`）、`Core/IndexV2/Search/PinyinNameEvaluator.cs:146`、`SearchMatcher.cs:296`、`PathMode/SearchMatcherPath.cs:125`、`Core/IndexV2/Search/SearchMatcherRow.cs:35, 120` |
| 名称级对照测试两侧各一条 | `Tests/Core/IndexV2/Search/PinyinNameEvaluatorTests.cs:199`（`RegexClause_IsAnsweredFromTheNameNeverFromTheReadings`） |
| 纯排除的 pattern 不占 mask 位 | `Core/SearchIndex/Fzf/FzfPattern.cs:112`（`HasPositiveTerm`）、`Core/IndexV2/Search/PathTermFallback.cs:195-196`；main 侧为 `FzfPattern.cs:53` 的 `HasPositiveTerms` |
| `! ' ^ $` 退役为字面字符 | `TermTriggers.cs:10`，`Tests/Core/SearchIndex/FuzzyMatcherTests.cs:71-83`（`IsMatch_RetiredOperatorCharacters_AreLiteralText`） |
| main 靠旧语法守 kind 的两条测试 | main `Tests/Core/IndexV2/Search/PinyinNameEvaluatorTests.cs:156`（`OperatorTerms_AreLiteralOnly`）、main `Tests/Core/SearchIndex/Fzf/FzfPatternAliasReadingTests.cs:52`（`AliasReading_LeavesAnOperatorsOwnKindAlone`） |
| 锚定 kind 的删除 | `fb6cda90`；`Core/SearchIndex/Fzf/FzfAlgorithm.cs:3-6`（`FzfTermKind` 只剩两个成员）、`:36`（`Match` 只剩 `Fuzzy`/`Exact` 两条 arm）、`FzfBytePattern.cs:78` 同形，main 同文件 `:3-10` 仍带 `ExactBoundary`/`Prefix`/`Suffix`/`Equal` |
| grid 层的删除 | `b8e1061c`（main）；两条分支上已消失的提交 `c85b31ab`、`dc50cc03`，`3a8ad26b` 只剩 `FzfPatternAliasReadingTests.cs` 的迁移 |
| 排序键只有 `s c m a f`，阈值需要第二个触发符 | `Plugins/CoreExtensions/Providers/QueryTokens/SortFilterQueryTokenProvider.cs:27-28, 31-38, 64`（`AscendingTrigger`/`DescendingTrigger`、`KeysByLetter`、`IsTrigger`）、`:125-136` |
| 无人认领的 Token 得到空结果 | `App/ViewModels/Search/QueryTokenDispatcher.cs:50-51` |
| Token 链在已取回的结果之上 | `QueryTokenDispatcher.cs:19`，调用点 `App/ViewModels/Search/Dispatch/SearchQueryDispatchController.cs:429`、`SearchDispatchController.cs:359`、`App/Services/Pipe/AppSearchPipeService.cs:289`；main 的 `QueryTokenDispatcher.cs:6` 注释仍写尾部 `:a,b,c` 段 |
| OR 已存在且优先级可配 | `FzfPatternParser.cs:70-75, 84, 123`，`Core/SearchContext.cs:45-53` |
| 路径正则结果 8000 条上限 | `App/ViewModels/Search/Mapping/StreamingResultAccumulator.cs:27`（`RegexPathResultLimit`），main 无此常量 |
| 前缀冲突分级与旧值只警告 | `App/Helpers/QueryTokenPrefixRules.cs:40-54`（`GlobalPrefixConflict`）、`:78`（`BlocksSaving`） |
| 「另一插件已认领该字符」检查被删除及其理由 | `QueryTokenPrefixRules.cs:12-15` |
| 插件通过宿主单一前缀认领 Token | `PluginSdk/Services/SearchSyntaxService.cs:17-27`，`CustomFilterQueryTokenProvider.cs:48-52, 63-67, 345`；单一前缀改造是 `3d879e38` |
| 关键词最长匹配 | `CustomFilterQueryTokenProvider.cs:143-144` |
| 设置页章节为静态帮助文本 | `App/Views/Settings/SearchSyntaxSettingsPage.xaml(.cs)`，`Plugins/CoreExtensions/Resources/Translations/<locale>/App.json:52, 57`（`SearchSyntax_Tab_Tokens` 与 `SearchSyntax_Tokens_Body`） |
| 大小写写法已进入 7 locale 文档 | `Site/*/user-guide/search-syntax.md` 正则小节（含 `Site/user-guide/` 共 7 份），`Plugins/CoreExtensions/Resources/Translations/*/App.json:54, 55`（`SearchSyntax_Basic_Body` 与 `SearchSyntax_Regex_Body`） |
| 通配符与路径排除 provider 的删除 | `3d879e38` 删除 `WildcardQueryTokenProvider.cs` 与 `PathExclusionQueryTokenProvider.cs`，两者在 main 上仍在 |
| 快速面板仍解析 `!` 取反 glob | `App/Services/QuickPanel/QuickPanelFilterParser.cs:9, 65, 116` |
| `#` 以管理员、`$` 以普通身份、`%` 环境变量 | `Plugins/CoreExtensions/Providers/InstantAnswers/CommandInstantProvider.cs:22-23`、`CommandQuickNavigationProvider.cs:52-54`、`EnvironmentVariableInstantProvider.cs:22-28` |
| 设置侧规则语法只在 main | `Core/Settings/IndexRuleSetting.cs:58`（`IndexRuleSyntax`，`TryParse` 在 `:109` 先查 `/.../`，`:126` 才归一分隔符；`MatchTimeout` `:64`），`feat-search-reimpl` 无此文件 |
