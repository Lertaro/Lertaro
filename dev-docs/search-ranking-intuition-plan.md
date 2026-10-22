# 搜索“直觉排序”改进计划：相关性护栏、快捷方式身份、历史去偏与拼音空格等价

## 适用范围、基线与文档地位

- 核对基线：`main` / 当前工作树 `c13746c`（2026-10-23）。本文逐行核对了排序、快速窗口候选合并、历史加行、开始菜单应用 provider、拼音实时匹配与现有测试。
- 本文是**下一轮改进的权威实施计划**，当前尚未实施。现行行为仍以 [search-ranking.md](search-ranking.md) 为准；上一轮已经落地的设计与性能数据仍见 [search-ranking-redesign.md](search-ranking-redesign.md)；[search-ranking-score-model.md](search-ranking-score-model.md) 只保留为历史方案，不能再直接照做。
- 范围：快速窗口为第一目标，同时保证完整窗口、内联窗口、文件引擎截断、可搜索项 provider 与拼音匹配不会继续分叉。
- 用户指定 `#437`、`#440` 为典型案例。当前 checkout 的远端 `Mikachu2333/Lertaro` 是关闭 Issues 的 fork；两份原始工单实际位于其 parent `Lertaro/Lertaro`，已核对 [#437](https://github.com/Lertaro/Lertaro/issues/437) 与 [#440](https://github.com/Lertaro/Lertaro/issues/440) 的正文和全部文字评论。截图/视频只用来补名次，不覆盖评论里可复制的查询、文件名、路径和设置事实。
- 当前基线已经包含两轮针对性修补：#437 触发了类型加权、shortcut 借用应用秩和错误默认 Low 规则退休；#440 触发了 alias 接受门与高亮 tightness 对齐。本文必须把“基线已经修掉的机制”和“工单仍暴露的模型缺口”分开，不能把整个工单重新宣称为未修复，也不能因有一个回归测试就宣称所有排序直觉已经解决。
- 本轮不是“再找一组更漂亮的魔法权重”。目标是先建立不会被偏好信号突破的相关性护栏，再用可回放数据校准护栏内部的顺序。

## 0. 结论速览

1. **当前模型工程上严谨，统计上尚未校准，产品约束也彼此冲突。** 共用内核、全序比较、历史衰减、测试与性能设计都合理；但 `10/8/16`、90 天、3/10 等是先验，类型的序数位置被当作 `16/8/4/1` 的基数，点击日志又无法把一次打开准确连接到当时展示的名次。
2. #437 不是一个单点 bug：`cfwj` 想找“重复文件”应用快捷方式，却被 `ETC服务监督热线` 这类可解释为 `C + 服(f) + 务(w) + 监(j)` 的偶合强命中及普通文件/文件夹压住；报告还包含 `db` 想找“Telegram/电报/TG”、历史为空却出现 `FSCapture.exe`、Low 规则与“加空格后正常”等现象，横跨匹配、身份、规则和截断。
3. 基线针对 #437 已做三项修补：快速窗类型加 `16/8/4/1`、`.lnk/.url/.appref-ms` 文件行借用更优的应用秩、退休会让无扩展名祖先下整棵工具树误入 Low 的 `/^[^.]+$/` 默认规则。它们修掉了具体机制，却也制造了新的数学反例：正常弱第一类型可得 `8+16=24`，高于正常强第三类型的 `18+4=22`；高优先弱第一类型甚至可到 32。
4. #440 是**召回接受门与高亮证据不一致**，不是终局排序：精确查询 `zbl` 曾通过 alias lane 在文件名中的 `z-library` 与 `1lib` 间散点命中，跨度 15；高亮只接受上限 12，所以结果出现却没有可见证据。当前基线已让 matcher 与 painter 共用 tightness gate，并有原始文件名回归测试；后续必须保留这条 witness 一致性。
5. 历史还存在独立的越级：纯历史行只需其旧 keyword 模糊命中当前查询，文件名本身可以完全不匹配；有效历史分达到 H5/H3 后仍可进入 Boosted，分别拿 34/22。这是有意的“学习型别名”，也是最容易形成自我强化的来源；但 #437 明确称历史已清空，因此不能拿这条机制替 `FSCapture.exe` 个案草率结案。
6. `.lnk` 的最终类型秩修补已经存在，但发生得太晚：它只能改**已经到达 App 的候选**，救不回引擎截断、provider 未加载、未被应用目录收录或被 Low 段压住的快捷方式；同时原始文件行按带 `.lnk` 的文件名打分，应用 provider 行按去扩展名的标题打分，同一路径的两个身份并不真正同质。
7. “相关词搜不到”在当前实现中是预期边界：文件索引没有通用同义词/语义模型，开始菜单项又主要只搜标题；provider curated alias/keyword、受控目标 exe stem、用户别名和 learned alias 才是可解释的扩召回入口。不能用无来源的全局语义扩展换来 #440 一类 false positive。
8. `sousuojieguo` 与 `sousuo jieguo` 的差异首先是**查询语义差异**，其次才是排序差异。无空格是一个必须落在相邻字符上的连续拼音词；有空格是两个相互独立、顺序自由的 AND 词。单词查询还独享“拼满整个词干 +40”，多词没有，所以即便都命中“搜索结果”，引擎初排分也不同。
9. 目标模型采用“**召回 → 证据分级 → 有界偏好重排 → 多样性/截断**”四步。文本证据等级先于普通类型、High/Normal 优先级和历史；这些偏好不能再把弱/生僻/历史别名行抬过直接强命中。`AlwaysTop` 与强匹配收藏保留为显式例外，Low 保留为显式降级。
10. 类型优先级是一个**序数**，默认只在同一相关性等级内比较，不再映射成未经校准的 `16/8/4/1`。应用 `.lnk` 若与文件/文件夹具有同等级证据，应用类型可以稳定胜出；若只是偶然弱命中，则不能靠类型身份越级。
11. 历史拆成两类：已有直接文本证据的结果只用 frecency 做同等级细排；文件名不匹配的“学习型别名”必须是规范化查询的精确关联，单独放在直接文本结果之后，且首屏默认最多一条。
12. 拼音空格不应在全局 parser 里简单删除。只在“无操作符、两个以上纯拉丁正词、拼接后长度 ≤63、候选确实由 live 拼音 provider 处理”的分支额外评估 joined 形态；原始多词语义仍保留，最终取证据更强的一支。
13. 先做观测与回放，再改排序。没有候选阶段快照时，任何“调权重”都无法区分“没召回”“被截断”“被合并错身份”和“最终排低”。

## 1. 工单事实与问题分层

### 1.1 #437：排序投诉实际包含四条链

来源：[Lertaro/Lertaro#437，v5.10.0 搜索结果排序效果不理想](https://github.com/Lertaro/Lertaro/issues/437)（2026-10-21 至 2026-10-22 的正文与评论）。可从文字直接确认：

| 子案例 | 实际输入/设置 | 实际结果 | 用户期望 | 所属层 |
| --- | --- | --- | --- | --- |
| 437-A | `cfwj`；历史清空，后来也试过删除全部规则 | `ETC服务监督热线` 等普通项排在应用前；这项可由连续混合序列 `C + 服(f) + 务(w) + 监(j)` 解释 | “重复文件”相关应用/快捷方式先出现，尤其下面列出的 `.lnk` | evidence + rerank |
| 437-B | `db`，意图是 Telegram / “电报” / TG | 大量不想要的文件，目标难找 | `电报` 的常用拼音首字母命中优先；低语义关联项不要占首屏 | evidence + rerank；也要核对目标 recall |
| 437-C | 评论称历史为空且删除过 keyword，`FSCapture.exe` 仍进入前十；给文件名末尾加 `1` 后不再出现 | 一个表面不含意图词的 exe 被召回；确切查询只在截图上下文中，文字评论没有重新抄出 | 若没有可展示的匹配 witness，就不应进入结果；不能把它误归因于历史 | recall/alias/provider，待 trace 定源 |
| 437-D | 中间加入空格后“排序好像正常”；本任务进一步给出 `sousuojieguo` / `sousuo jieguo` | 空格改变召回和排序 | 合理的拼音词边界不应让同一目标发生断崖 | parser + pinyin evaluator + truncation |

工单给出的目标快捷方式都在一个第三方启动器的“快捷方式大全”目录，实际文件名为：

```text
重复文件.lnk
重复文件删除.lnk
NoClone 重复文件.lnk
重复文件搜索 DuplicateCleaner.lnk
重复文件文档图片照片视频音频查找搜索清理Duplicate Cleaner 5.lnk
```

对应程序/文件夹位于同一用户工具树。工单还贴出当时规则，其中 `/^[^.]+$/` 为 Low；它不只匹配“无扩展名叶名”，还会在相对路径的无扩展名祖先上成立，导致整棵工具树继承 Low。当前 HEAD 已在 `e62621ae` 退休未修改的旧默认规则；在 `f8bc512f` 让 shortcut 文件行借应用秩；在 `bf7473df` 增加类型 bonus 和每类型截断名额。这说明 437-A 的**规则误伤和 shortcut 类型身份已有定点修复**，但 `cfwj` 的证据分级、空格稳定性、候选来源和类型 bonus 是否越级仍是本文要解决的剩余问题。

这里尤其不能混淆两个 `FSCapture.exe` 故事：`search-ranking.md` 的“屏幕截图 → FSCapture.exe”是历史 keyword 学习型别名的机制示例；#437 的报告明确说历史为空，而且改文件名会改变召回。两者显示名相同，不等于根因相同。

### 1.2 #440：已经定位并在当前基线回归锁定的 false positive

来源：[Lertaro/Lertaro#440，5.10.0 版，搜索出不应有的结果](https://github.com/Lertaro/Lertaro/issues/440)。原始复现是关闭模糊匹配（精确模式）后查询 `zbl`，却返回：

```text
商务印书馆汉译世界学术名著丛书28册套装 (约翰.罗 等) (z-library.sk, 1lib.sk, z-lib.sk).epub
C:\0Work Floder0\书籍读物\社会人文
```

首张截图没有高亮。报告者进一步缩减后确认：要保留一个触发拼音 alias provider 的汉字“著”，并保留括号中的 ASCII `z`、`b`、`l` 才复现；去掉“著”后不再进入该 lane。源码与修复提交 `3bda77df` 给出精确机制：alias 的 loose reading 在 `z-library` 与 `1lib` 之间得到跨度 15 的散点匹配，旧召回门允许最多 20，但高亮 `AliasMatchRules.IsTightOccurrence` 对 3 字符只允许最多 12，于是出现“能排名、不能解释”的行。

当前 HEAD `c13746c` 已包含：

- `FzfPatternAliasMatchExtensions.IsAcceptableAliasMatch` 与 painter 共用 tightness 判定；
- `Tests/Core/SearchIndex/AliasLooseReadingTests.ExactMode_DoesNotReachANameByAliasScatter` 用工单原文件名断言精确 `zbl` 不命中，同时保留紧凑的 `zL`；
- live 拼音路径继续由 `PinyinExactModeTests.BothModes_ReachTheSameNamesForPinyinQueries` 约束 fuzzy/exact 结果域不因额外 literal scatter 分叉。

因此 #440 在本计划中是**已修复的召回/证据一致性回归样本**，不是等待 V3 排序去压低的行。V3 要做的是让 match witness 成为召回、Grade 和高亮共享的数据，防止同类缺口重新出现。

### 1.3 四类现象不能用同一个比较器修

用户表述里的四类现象落在不同层：

| 现象 | 所属层 | 排序器能否单独修复 |
| --- | --- | --- |
| 低匹配度内容靠前 | 终局重排 / 历史提升 | 能，但要改越级规则，不只是调常数 |
| 应用类 `.lnk` 未提前 | 身份识别、provider 召回、引擎截断、终局重排 | 不能；必须先知道它在哪一层丢失 |
| 搜不到相关词 | 查询解析、可搜索字段、provider readiness、候选截断 | 不能；没有候选就无从排序 |
| `sousuojieguo` 与 `sousuo jieguo` 截然不同 | 查询语法、拼音判定、初排打分 | 不能只改终局分值 |

建议以后把一次搜索固定画成下面五层，并要求诊断日志能回答每一层有多少行：

```text
Q0 原始输入
 → Q1 解析/规范化后的查询变体
 → C0 各来源召回的候选（索引、应用 provider、收藏、历史、Recent、插件）
 → C1 合并身份后的候选
 → R0 文本证据分级与偏好重排
 → D0 类型覆盖/显示上限后的真实列表
```

如果期望项不在 C0，是 recall 问题；在 C0 不在 C1，是去重/身份问题；在 C1 不在 D0，才是排序或截断问题。

## 2. 现行算法的科学性审计

### 2.1 做得对的部分

1. **确定性与可解释性。** `RankPolicy`、`RankKeyComparer` 与公共尾给出全序；同一输入不会因集合枚举顺序随机抖动。
2. **双窗共用核心。** 快速窗口与完整窗口共享 section、score 与质量比较，避免以前两个比较器各自漂移。
3. **特征预计算。** 排序前装饰，比较期不反复裁路径、查字典，复杂度与性能预算清楚。
4. **行为信号有衰减和上限。** 90 天半衰期、封顶 10、H5/H3 名额比永久裸次数科学得多。
5. **显式偏好与推测偏好有区分。** `AlwaysTop`、收藏、结果类型顺序来自用户设置，不是模型暗猜。
6. **测试覆盖的是全序性质而非几个截图。** 反对称、传递、去重、历史预算、两窗一致性都有测试。
7. **没有为了“高级”而直接上学习排序。** 单用户、本地短查询、低反馈量的场景，先用可解释规则是合理选择。
8. **#440 的修法符合证据一致性。** matcher 与 painter 共用 `IsTightOccurrence`，把“可召回”与“可解释”对齐，并用真实反例 + 紧凑正例守住边界；这比给无高亮结果降几分科学。

这些基础应保留。本轮不是推倒重做匹配器或引入云模型。

### 2.2 当前模型为何仍会违背直觉

#### A. 类型加成可以大于相关性差

现行常量：

```text
强/弱差 = 10
一个优先档 = 8
第一类型加成 = 16
第二/三/四类型 = 8/4/1
```

代码与测试允许：

```text
正常 + 弱/生僻 + 第一类型 = 8 + 16 = 24
正常 + 强       + 第三类型 = 18 + 4 = 22
```

`RankAndDedupe_TypeBonusLiftsAReasonableMatchOfTheLeadingType` 正在断言前者获胜。更极端的是：

```text
高优先 + 弱 + 第一类型 = 16 + 16 = 32
H5 历史                         = 34
高优先 + 强 + 未列出类型 = 26
```

因此“类型、规则、历史混合时低匹配靠前”是规则的数学结果。`Match.Weight = Coverage × Contiguity` 也不能完全保护：一个生僻读音可能覆盖完整且连续，`IsStrong=false` 但 `Weight=1`，仍吃满 16 分。

#### B. 类型位置是序数，却被当成基数

用户只表达了“应用在文件之前”，并没有表达“第一名值 16、第二名正好值 8”。`16/8/4/1` 假定了指数衰减和可加单位，但没有标注数据支持。线性加法本身不是不科学；问题是相加前没有证明这些特征处在可比较尺度上。

#### C. 二值 Strong 产生断崖

`ShapeInsideWord/TierFull` 边界两侧只差一个离散值，却相差 10 分；边界内再按连续质量细排。它容易解释，但把“完全相等、词首、词内短子串、多词强命中”都压进同一位 `C=1`，又把散点、生僻读音、路径兜底压进 `C=0`。当类型再加 16 时，这个粗化尤其危险。

#### D. 历史混合了“常用”与“别名”

`HistorySearchCandidateMapper` 比较的是历史 entry 的 `Keyword`，不是结果显示名；命中后用旧路径加行并标记 `IsHistoryOnly`。`BuildHistoryBudget` 明确允许历史纯行进入 H5/H3，即使 `MatchRank.NoMatch` 被用于最终名称质量。于是：

- 用户过去在查询 A 下打开过路径 P；
- 当前查询只要模糊匹配 A，P 就可再次出现；
- P 被再次打开又强化同一关联；
- H5 以 34 分压过大多数直接文本命中。

这是典型的曝光/位置反馈回路。时间衰减只能让旧关联变弱，不能消除“越靠前越容易再点、越点越靠前”的内生偏差。

#### E. 候选截断与终局目标不同

引擎截断只读匹配质量，不知道 App 的类型、历史、收藏和规则。App 里的 `.lnk` 类型改写、类型覆盖名额、历史提升全发生在候选到达之后。终局排序再正确，也救不回已经被 `Take(limit)` 丢掉的项。

#### F. 目前日志不足以校准

现有日志记录排序前 10 行及单独的 open 行，但 open 行没有 impression/page id、当时 rank、窗口和最终显示列表。流式重排后只能靠时间与 hash 猜一次打开对应哪一页；也没有候选来源、匹配字段、查询变体、各阶段丢弃原因。因此不能可靠计算 MRR、nDCG、候选 Recall@K 或位置偏差。

### 2.3 关于“线性模型是否科学”的准确结论

- `S = 10·C + 8·P` 在只有六个离散格子时，本质是一张可解释查表，不因“线性”二字自动错误。
- 它准确实现了上一轮明确给出的偏好：正常强（18）压过高弱（16）。若该偏好仍成立，`10/8` 无须因本轮问题被一并否定。
- 真正不科学的是：把未经校准的类型序数、二值文本等级、规则档和行为反馈继续放进同一可加尺度，然后把一次点击当作无偏真值。
- 硬分段也不是天然科学；它只是把产品约束写清。分段边界仍需黄金集和真实反馈验证。
- 因而本轮应先建立**单调约束**：“更差的文本证据不能仅靠普通类型/历史越过更好的文本证据”，再在约束内部校准，而不是直接拟合一个自由加权和。

## 3. “符合直觉”的可测试契约

“直觉”不能只写成一句口号。以下契约应成为属性测试与黄金用例。

### I1. 查询满足优先

完整覆盖全部正词的名字/标题命中，必须排在只靠散点、生僻读音、路径或历史 keyword 的结果之前。普通类型顺序、High/Normal 与打开次数不得突破该边界。

### I2. 偏好有界

类型顺序和 High/Normal 只在**同一文本证据等级**内重排。若以后有数据证明应允许跨一级，必须单独改契约与版本，不能靠把常数从 16 改成 12 暗中发生。

### I3. 显式过滤强于加权

类型触发字符是用户明确限定结果域，应在候选阶段过滤；它不是一个巨大 bonus。`AlwaysTop` 是显式越级出口；Low 是显式降级出口。普通拖拽类型顺序不等价于 pin。

### I4. 行为不能伪造文本证据

一个路径有直接名字/标题命中时，历史只细排同等级结果。一个路径只有历史 keyword 关联时，必须标成 learned alias，放在直接文本结果之后；不得冒充名字匹配。

### I5. 同一实体只保留一个最可执行身份

同一路径的 File/Application/History/Favorite 视图合并后，保留能正确启动的 Application 行，同时合并收藏、历史、规则与类型信号。身份选择不能由类型分值碰巧决定。

### I6. 拼音合理空格近似不变

对无操作符的纯拼音查询，把词边界处的空格加上或去掉：

- 目标中文名必须都能召回；
- 若拼接后正好连续拼满同一名字，目标获得同一证据等级；
- 受控候选集里目标名次差不超过 1；
- 原始多词的独立、无序召回仍可保留，但不能因此把连续目标降级。

### I7. 排序不能修复召回

若期望项不在合并前候选集，任务必须标记为 retrieval/provider/index 问题；禁止通过给同类型其它行加分来假装修复。

### I8. 两窗同证据同顺序

忽略快速窗口独有类型偏好后，同一批候选的文本证据等级、规则档和历史直接信号必须一致。窗口差异只能来自明确记录的候选源与显示预算。

### I9. 召回证据必须可高亮

任何因名称、拼音或 alias 进入普通结果域的行，Grade 与高亮必须消费同一个已验收 witness。若 painter 拒绝该跨度，matcher 也必须拒绝，不能重现 #440 的“有结果、无证据”；无法安全展示的 secondary/learned 证据必须显示明确来源标签，而不是伪造字符高亮。

## 4. 目标架构：四步而不是一个总分

```text
1. Retrieve
   各来源独立召回 + 记录来源/字段/查询变体

2. Evidence
   为每一行生成 MatchEvidence 与 EvidenceGrade

3. Rerank
   显式 pin / Low 分段
   → EvidenceGrade
   → 同级类型、High/Normal、直接历史
   → 现有细质量键

4. Present
   路径身份合并、类型覆盖/多样性、显示截断、解释日志
```

### 4.1 `MatchEvidence`（建议形状）

名称可调整，但信息不能再只剩一个 `MatchRank`：

```text
MatchEvidence
  Field             DisplayTitle | FileName | CuratedAlias | UserAlias |
                    ShortcutTarget | Keyword | ParentPath | HistoryKeyword
  Mode              Literal | PinyinInitials | PinyinFull | PinyinRare |
                    Fuzzy | LearnedAlias
  QueryVariant      Original | PinyinJoined | ProviderAlias | HistoryExact
  AllPositiveTerms  bool
  DirectText        bool
  Witness           provider id + matched spans/positions + accepted branch
  MatchRank         现有 tier/shape/start/coverage/contiguity
  Grade             0..4
```

候选源还应独立携带：

```text
CandidateIdentity
  Source            FileIndex | SearchableProvider | Favorite | History | Recent | Plugin
  KindFlags         File | Directory | Application | Shortcut
  SearchTitle       用于匹配的标题；应用快捷方式通常去扩展名
  PathKey           规范化实体键
```

`ResultKind` 仍可服务 UI，但不应同时承担“如何执行、属于什么类型、从哪来、用什么字段命中”四种职责。

### 4.2 文本证据等级（V3 初始规则）

数字越小越好；先采用离散规则，后续只在黄金集支持时调整边界。

| Grade | 条件 | 例子 |
| --- | --- | --- |
| 0 ExactIdentity | 规范化查询等于标题/文件茎；或 joined 全拼/简拼从 0 开始覆盖整个中文词干，且非生僻读音 | `sousuojieguo` / `sousuo jieguo` → `搜索结果.lnk`；`cfwj` → `重复文件.lnk` |
| 1 DirectStrong | 全部正词在显示标题/文件名上形成 whole/word-start/inside 连续命中；常用简拼/全拼可进入 | `jieguo` → `搜索结果`；`cfwj` → `ETC服务监督热线` 的内部混合连续段 |
| 2 DirectApproximate | 名字上全部词成立，但为散点、模糊、低覆盖或生僻读音 | 拼写近似、罕见读音、长名中的弱散点 |
| 3 SecondaryField | 全部词只由可信次级字段解释：curated/user alias、快捷方式目标名、provider keyword、父路径 | 应用标题不含词，但 `电报` 用户别名或 target exe 的受控别名含词 |
| 4 LearnedAlias | 只有规范化查询→路径的历史关联，显示名本身不匹配 | 用户过去为该查询选择过的工具 |

`NoPositiveTerms`、无法解释全部词的行不进入普通竞争；纯排除查询另有专门规则，不借 Grade 0。

### 4.3 建议的终局键

```text
Section
  0 ExplicitPinnedText   // AlwaysTop；强证据收藏。仍须是本次候选
  1 TextualHighNormal   // Priority High/Normal，Grade 0..3
  2 TextualLow          // Priority Low，Grade 0..3
  3 LearnedAliasOnly    // Grade 4；首屏默认最多一条

段内：
  EvidenceGrade ↑
  → PreferenceEligible ? TypeRank ↑ : neutral
  → High before Normal
  → DirectHistoryTier / frecency ↓
  → MatchRank.CompareQuality
  → 公共稳定尾
```

规则说明：

- 类型顺序只是一条 ordinal key，不再转成 `16/8/4/1`。Grade 0–1 默认可用；Grade 2 是否允许类型重排由黄金集决定，初版建议禁用，避免弱匹配应用再次越级。
- `.lnk` 与同名文件夹都达到 Grade 0/1 时，Application/Shortcut 的类型秩可以让 `.lnk` 提前；一个 Grade 2 的应用不能压过 Grade 0/1 文件。
- H5/H3 不再生成 34/22 的跨等级分数。它们只在直接命中项的同 Grade 内排序与显示“常用”徽标。
- learned alias 独立放最后；当没有直接文本结果时它自然成为第一，并仍然有价值。
- `AlwaysTop` 保留真正的越级语义，因为用户明确勾选了它；但日志必须能看见它越过了几个 Grade。
- 强匹配收藏可保持 pin；弱收藏仍按自己的 Grade 竞争。
- Low 继续是硬降级，尊重已有设置。#437 已证明旧默认规则会误伤应用目录；这类问题应修默认规则、迁移或身份，而不是让排序偷偷绕过用户显式 Low。

### 4.4 一个目标示例

查询 `sousuo jieguo`，应用类型排第一：

| 候选 | 证据 | Grade | 结果 |
| --- | --- | ---: | --- |
| `搜索结果.lnk`（应用行） | joined 常用拼音完整覆盖标题词干 | 0 | 同 Grade 内按应用类型优先，可第 1 |
| `搜索结果` 文件夹 | joined 常用拼音完整覆盖 | 0 | 紧随其后 |
| `搜索结果旧版.txt` | 标题起始的完整词 | 1 | Grade 0 之后 |
| `搜-x-索-y-结-z-果.txt` | 散点/近似 | 2 | 无论类型或历史，不能越过 Grade 0/1 |
| `CaptureTool.exe`（假设的旧历史 keyword；非 #437 归因） | 只有历史关联 | 4 | 直接文本结果之后；无直接结果时仍可展示 |

## 5. 拼音空格：根因与改法

### 5.1 现行差异是确定的

- `FzfPatternParser` 把 Unicode 空白切成 AND term sets。
- `PinyinTermMatcher` 对每个 term 要求候选名中的一段连续字符，每个字符至少消费一个读音前缀。
- `sousuojieguo` 是一个连续 walk；`sousuo jieguo` 是两个独立 walk，当前不要求顺序，也不要求相邻。
- `PinyinNameEvaluator` 只有“恰好一个 plain positive term”才设置 `_loneWord`；只有该形态可拿覆盖整个词干时的 +40。多词查询即使涂亮同样字符，也没有这 40 分。
- 因此两种输入可能有不同候选集、不同引擎初排分、不同截断幸存者；App 最后的比较器无法完全补救。

### 5.2 不采用的修法

1. **全局删除 ASCII 空格。** 会把 `visual studio`、`fast stone` 等正常多词语义改成一个词，并破坏排除、OR、引号与路径语法。
2. **要求用户永远不要加空格。** 违背中文输入习惯，也放弃已明确出现的用户反馈。
3. **只让两个查询最终分数相同。** 若较低分的一支已在引擎截断前消失，终局同分没有意义。
4. **把所有多词都强制有序相邻。** 会损失当前有价值的“多个块独立命中长名字”能力。

### 5.3 joined 分支

只在 live 拼音层增加一个额外候选解释，不改变 parser 的通用语法：

1. 查询有至少两个正词；
2. 所有词都是无 `! ^ $ ' |` 的 plain Exact/Fuzzy term；
3. 每词可由同一个 live provider 建 matcher；
4. 拼接后长度在 2..63；
5. 候选文本确实由该 provider 处理（拼音即含可读汉字），不能让英文名偷偷走 joined；
6. 构造 `joined = concat(terms)`，按“单个拼音词”评估；
7. 原始多词分支照常评估；
8. 取证据更强的一支。若 joined 完整覆盖词干，它与用户直接输入无空格串获得相同 Grade 与初排语义；
9. 保留 witness/variant，让高亮、终局 Grade、日志都知道胜出的是 `PinyinJoined`，不得判定走 joined、展示时又按原多词重算出另一套质量；
10. 反序输入 `jieguo sousuo` 可由原始无序分支召回，但 joined 为 `jieguosousuo`，不能获得“搜索结果”的 Grade 0。

### 5.4 拼音阶段的必测矩阵

| 查询 | 候选 | 必须 |
| --- | --- | --- |
| `sousuojieguo` / `sousuo jieguo` | `搜索结果.lnk`、`搜索结果.txt` | 都召回；同 Grade；受控集名次差 ≤1；高亮一致 |
| `sou suo jie guo` | `搜索结果` | joined 可完整覆盖 |
| `jieguo sousuo` | `搜索结果` | 可被原多词召回，但不得拿正序 joined 的 ExactIdentity |
| `visual studio` | ASCII 应用标题 | 与改动前完全一致，不走拼音 joined |
| `fast stone` | `FastStone Capture` | 保留普通多词行为 |
| `sou | suo`、`!sou suo`、`^sou suo`、引号/转义空格 | 任意 | 不生成 joined |
| 拼接后 64 字母 | 中文名 | 不进入 63 位 NFA；不得异常或静默截断 |
| `搜-索结果` | `sousuojieguo` | 单词连续规则仍拒绝跨 `-`；空格分支是否召回由原多词规则决定 |

## 6. `.lnk` / 应用结果：先修身份和召回，再谈加成

### 6.1 当前已有但不充分的机制

- 开始菜单/自定义应用目录 provider 把其中扫描到的 `.lnk/.url/.appref-ms/.exe` 作为 `Application`，标题去扩展名，执行委托也正确；这不等于文件索引里的任意 `.exe` 都是应用。
- 原始文件索引行仍是 `File`；`ApplyShortcutTypeRank` 在 App 排序前让扩展名匹配的文件行借用应用与文件中更好的 `TypeRank`。
- 同路径合并时 Application 行优先，并合并历史、收藏与最小类型秩。
- 显示 50 条前 `EnsureTypeCoverage` 为每个已有类型保留 3 行。

### 6.2 仍能产生“应用未提前”的五条路径

1. **引擎先截断。** 文件行没到 App，扩展名改 rank 无从执行。
2. **provider 尚未加载或目录未收录。** provider cache miss 会暂时没有 Application 行；自定义应用目录之外的快捷方式只有 File 身份。
3. **Low 段先于类型。** 快捷方式借到应用 rank，也仍可能因默认/用户规则落在 Low；Boosted 中的文件夹会整体压过它。
4. **匹配文本不同。** provider 行对去扩展名 Title 打分，File 行对 `xxx.lnk` 打分；coverage、stem 与别名可能不同。
5. **身份推断过宽又过晚。** 当前仅凭扩展名让所有 `.lnk/.url/.appref-ms` 借应用秩，但没有保留“provider 认证的应用”与“疑似快捷方式”两种强度。

这些都是源码可证的机制。#437 已确认过“shortcut 只带文件身份”和 stock Low 规则两条链，但当前版本是否还在 provider readiness、引擎截断或标题差异处丢候选，仍必须由 P0/P1 trace 决定；#440 与 `.lnk` 无关。

### 6.3 目标处理

1. 候选建立时携带 `KindFlags`，允许同一项同时是 File + Shortcut + Application；不要用单一字符串覆盖事实。
2. provider 认证的项标 `Application`；仅凭扩展名的原始行至少标 `Shortcut`，是否映射到应用类型作为显式策略，而不是路径后缀临时改一个整数。
3. 快捷方式用于匹配/Grade 的 `SearchTitle` 默认去已知快捷方式扩展名；显示与执行仍保留真实路径。
4. provider 缓存必须记录 readiness；“本轮 provider 未加载”与“加载完成但 0 命中”在诊断上不能相同。
5. 合并同路径时保留 Application 的执行委托、标题、图标与来源，同时把文件索引的规则裁决和引擎质量带上。
6. 应用目录内可预计算低成本别名：快捷方式标题、目标 exe 文件茎、明确提供的 provider aliases。不要在每次按键时做 COM 解析。
7. 只有 P2 的 Recall@K 证明 raw shortcut 在引擎截断前丢失时，才加“应用 source 独立 top-K / 二次限额搜索”；不要先把所有文件预算从 51 拉到 1000。
8. V3 排序里同 Grade 按应用类型在前；Grade 2 的弱应用不得越过 Grade 0/1 文件。

### 6.4 “相关词搜不到”要用有来源的召回，不是放松所有 fuzzy

当前能力边界必须说清：

- 文件引擎主要搜索名字/路径的 literal、fuzzy 与拼音读法；它不会从“聊天”推断 Telegram，也不会从“截图”自动知道任意截图软件。
- `SearchableItemMapper` 能搜索 provider 的 `Title`、alias 和 `Keywords`；keyword-only 命中当前被赋予零 coverage/contiguity，也没有标题高亮。开始菜单 provider 当前只填标题和执行路径，没有把 target exe stem、产品别名或同义词填入 `Keywords`。
- history keyword 能在用户**已经点过一次之后**学习 query→path，但不是冷启动相关词词典；Windows Recent 和插件又是不同来源。

因此把“相关词”拆成四种，分别定策略：

| 关系 | 例子 | 允许来源 | 初始 Grade |
| --- | --- | --- | ---: |
| 同一名字的写法变化 | 全/简拼、简繁、大小写、合理空格 | matcher 自身，必须有字符 witness | 0–2 |
| 产品显式别名/缩写 | `TG`、`Telegram`、`电报` 指向同一应用 | provider metadata、内置小表或用户别名，必须携带 provenance | 3（经黄金集可提升） |
| 执行身份 | shortcut 标题与 target exe stem | 应用 provider 加载时解析并缓存 | 3 |
| 用户学出的意图 | `截图` → 某个自己常用的工具 | query-path learned alias | 4 |

落地约束：

1. 优先让 provider 提供少量 curated aliases/keywords，并允许用户为单个结果维护本地别名；不要建立对所有文件生效的庞大同义词扩展。
2. 开始菜单项可以在 provider 加载时缓存 target exe stem、package identity 中可展示的产品名；查询热路径不得再做 COM 或磁盘解析。
3. secondary alias 必须记录 `Field/Provider/Provenance/Witness`。它可以解决冷启动 recall，但默认不能伪装成标题直接命中，也不能靠应用类型越过 Grade 0–2。
4. `Keywords` 的 prefix-all 规则要进入统一 evidence，而不是继续返回一个无高亮、零权重的匿名 fallback；UI 可显示“匹配别名：电报”这类来源标签。
5. 通用 embedding、网络语义服务和自动抓取描述不在本轮范围：本地短查询样本不足、解释困难，还会增加隐私、体积与 false-positive 风险。
6. 指标分开报告 cold-start Alias Recall@10 与 alias false-positive rate；不能只看“零结果率下降”。#437 的 `db` 案既要目标进首屏，也要审核新增干扰项。

## 7. 历史：把 frecency 与 learned alias 分开

### 7.1 两种信号不是一回事

- **Direct frecency**：候选本来就因标题/文件名/可信 alias 命中；历史只说明“同样相关时我更常用它”。
- **Learned alias**：候选显示文本不命中，只有过去的 query→path 行为说明用户可能想要它。这是在创造召回，不是给已有相关性加一点分。

把两者都塞进 H5/H3 是现行混排失真的核心。

### 7.2 V3 规则

1. direct 候选保留现有指数衰减、上限 10 与 H5/H3 徽标，但历史只在同 Grade 内细排。
2. learned alias 只接受**规范化查询精确相等**，不再用一般 fuzzy 去扩散旧 keyword。大小写、全角/NFC、首尾/重复空白可归一；拼音空格只有在 P4 判定为纯拼音等价形时才共享 canonical key。
3. 数据模型从“一个 path 当前只属于一个 keyword”改为 query-path pair；否则用户最后一次搜索会抹掉同一程序的另一个真实别名。
4. learned alias 使用独立衰减分与置信度，首屏默认最多 1 条；其余只在“更多结果”或历史页展示。
5. 仅由 learned alias 注入后再次点击，不应按无偏正例计数。至少记录 `organic/direct` 与 `learned-injected` 来源；前者强化更大，后者递减或只刷新时间，防止自我维持。
6. learned alias 行应有“历史关联/常用”可解释标识，并提供删除该关联的入口；用户无需编辑 JSON。
7. Windows Recent 没有 query-path 学习分，继续只按其可见标题匹配。
8. 旧历史迁移要保守：旧 keyword-path 可迁成低置信 pair，不能迁完立刻 H5 置顶。

## 8. 观测与评测设计

### 8.1 日志 schema 必须先升级

建议 schema 3 至少增加：

```text
pageId / searchSessionId / window / querySeq
rawQueryHash / canonicalQueryHash / queryVariantFlags
source / field / mode / evidenceGrade / kindFlags
providerReady / engineCandidate / merged / displayed
preMergeCount / postMergeCount / finalShownCount / dropReason
finalDisplayedRank
```

open 事件必须携带 `pageId + pathHash + displayedRank`；若列表在打开前重排，UI 行对象应保留自己最后一次 impression id，而不是打开时重新猜。

隐私原则不变：原始 query、标题、路径不落盘；字段都用小枚举，文本仍只存截断 hash。调试版可提供“复制本行排序解释”，由用户主动粘贴到工单，默认日志不存明文。

### 8.2 三组指标必须分开

| 层 | 指标 | 回答的问题 |
| --- | --- | --- |
| 召回 | expected Recall@20/@50、零结果率、各 source recall | 相关项有没有进入候选 |
| 排序 | MRR、Success@1/@3、nDCG@10、pairwise inversion rate | 进入候选后顺序是否合理 |
| 稳定性 | 拼音变体 target rank delta、Jaccard@20、Kendall tau | 加空格/大小写/全角是否导致突变 |
| 产品专项 | app/shortcut Recall@10、cold-start Alias Recall@10、alias false-positive rate、weak-over-strong inversion、history-only top-3 rate | 本轮四个投诉是否改善 |
| 性能 | 按键到首屏 p50/p95、候选扫描 p95、排序分配 | 正确性是否以卡顿换来 |

`weak-over-strong inversion` 在 V3 应是硬 0：若 Grade 更差的行只因普通 type/history/High 越过更好 Grade，测试直接失败。

### 8.3 点击不是金标准

- 现有排序决定曝光，曝光决定点击；直接最大化点击会固化旧排序。
- 主判断用脱敏黄金集与成对人工偏好；点击只做线上护栏。
- 若样本足够，比较 V2/V3 用 team-draft interleaving 或小比例随机交换近邻，才能估位置偏差；单用户不足 30 次有效打开时不下统计结论。
- 报告置信区间，不以“平均 nDCG 高 0.01”作为结论。

### 8.4 黄金集格式

每个 case 至少记录：

```text
caseId, window, rawQuery, canonicalExpectation
fuzzyEnabled, typeOrder, indexRules, historySnapshot, providerReadiness
candidate source + path token + display title token + expected relevance grade
mustAppearBefore / mustBeTopK / mustBeAbsent
```

路径与用户数据必须换成合成 token；#437/#440 的截图内容先转成这种 fixture，不能把真实绝对路径提交进仓库。

## 9. 详细分阶段实施（供其他模型逐步执行）

下面每个阶段都应单独完成、单独评审。禁止一个模型同时“补日志 + 改拼音 + 换排序”，否则回归无法归因。

### P0：冻结 #437 / #440 复现证据（只加文档/fixture，不改行为）

**前置**：工单正文和文字评论已核对，事实见 §1.1/§1.2；截图里没有被文字重抄的精确名次仍标 `unknown`，不得凭像素印象补写。

步骤：

1. 建立五个独立 case，而不是各工单只做一个大 fixture：`437-cfwj-order`、`437-db-intent`、`437-fscapture-source`、`437-pinyin-space`、`440-zbl-alias-scatter`。
2. `437-cfwj-order` 使用合成路径和至少三个候选：全词干简拼 `cfwj → 重复文件.lnk`、内部混合连续命中 `cfwj → ETC服务监督热线`、同名文件夹。分别记录无规则、stock-old-Low、显式 Low，并钉住 current HEAD 对 stock migration 与 shortcut rank 的现状。
3. `437-db-intent` 记录查询 `db`、目标“电报”应用和一组干扰文件；先只断言目标是否进 C0/首屏，不从“用户想找 Telegram”直接推导所有含 `db` 项都应删除。
4. `437-fscapture-source` 不预置历史归因：候选快照分别覆盖 FileIndex、SearchableProvider、Recent、History、Plugin；trace 后只保留真实来源。由于评论没有重抄当时查询，fixture 的 `rawQuery` 在取得截图转录前标 unknown，禁止借用另一案例的 `db` 或文档示例的“屏幕截图”。
5. `437-pinyin-space` 固定 `sousuojieguo` / `sousuo jieguo` 的最小语料及 20 行干扰语料，记录每支 query variant、C0、Grade、最终 rank 与高亮。
6. `440-zbl-alias-scatter` 保留结构而非真实书名：一个 Han 字触发 alias provider，ASCII 区域让 `z/b/l` 跨 15 字符散点，断言精确模式 `mustBeAbsent`；另放跨度 ≤12 的紧凑正例，防止把 alias lane 整体关闭。关联现有 `AliasLooseReadingTests`，避免重复造另一套语义。
7. 对每个 case 同时记录“5.10.0 工单实际”“current HEAD 现状”“V3 期望”。#440 和 #437 的 stock rule/shortcut 子机制在 current HEAD 应已通过；它们是回归保护，不应被错误列作 V3 新增收益。
8. 将所有用户路径换成结构等价 token；保留 `.lnk`、中文字符数、空格位置、相对父层和规则命中层级，不提交真实盘符或用户名。

退出条件：任何开发者只靠 fixture 能区分“工单旧故障、基线已修部分、V3 待修部分”；未知截图字段被明确标注，不存在看图猜分。

禁止：再声称工单不可访问、把 #437 的 FSCapture 强行解释为历史、先调常数、把用户真实路径提交进 Git。

### P1：建立端到端 ranking trace（行为不变）

主要文件：

- `Core/SearchIndex/RankingEventLog.cs`
- `App/ViewModels/Search/Mapping/RankedCandidateOrdering.cs`
- `App/ViewModels/Search/Mapping/SearchResultMapper.cs`
- 打开结果的记录入口

步骤：

1. 引入 `pageId/searchSessionId`，让 page/row/open 可精确 join。
2. 日志改记录**最终显示 rank**，同时保留排序前总候选数；插件前缀与最终 50 条截断要有明确阶段字段。
3. 给候选加只用于诊断的 `Source`，记录 provider readiness 与 drop stage。
4. 增加“复制排序解释”纯文本 formatter；输入结构化字段，禁止直接散落 UI 字符串拼接。
5. schema bump；旧日志 reader 必须忽略未知字段，新 writer 不改旧文件。
6. 测试：join、流式重排、关闭日志、轮转、失败吞吐、无明文、hash 稳定、最终 rank 正确。

退出条件：对 #437 的每个当前复现能回答“期望项在哪一层消失/为何排这里”；对 #440 能记录 alias witness 被哪一条接受门拒绝，证明回归保护仍生效。

禁止：利用新字段改变排序。

### P2：候选录制与离线回放器（行为不变）

建议位置：`Tests/Core/SearchRanking/` 或独立开发工具；不得进入用户运行时热路径。

步骤：

1. 定义不含真实文本的 `RankingCase` / `CandidateSnapshot`。
2. 能把同一候选集分别交给 V2 与后续 V3 纯函数排序。
3. 实现 Recall@K、MRR、nDCG@10、pairwise inversion、target rank delta。
4. 导入 P0 的五个 case，并补 `.lnk`、direct history 与 learned alias 的成对反例。
5. 输出基线报告到本文或独立小型 Markdown；不要提交大型 JSON/用户日志。
6. 性质测试：候选枚举顺序不影响结果；比较器反对称/传递；指标计算用手算小例验证。

退出条件：有 V2 基线数字，后续每阶段都能给出同候选对比。

### P3：引入 MatchEvidence / CandidateIdentity（影子字段，行为不变）

主要文件：

- `Core/SearchIndex/MatchRank.cs` 邻近的新 evidence 类型
- `App/ViewModels/Search/Mapping/SearchResultMapper.cs`
- `SearchableItemMapper.cs`、`HistorySearchCandidateMapper.cs`、`WindowsRecentCandidateMapper.cs`
- `RankedCandidateOrdering.cs`

步骤：

1. 定义小枚举与只读结构，避免字符串比较进入热路径。
2. 每个候选源明确填 `Source/Field/Mode/KindFlags/SearchTitle`。
3. 让 direct/alias matcher 的适配层在现有结果旁携带已胜出的 branch、provider id 与 span/positions；只复用匹配时已有数据，不为影子日志在排序期重跑 matcher。暂时拿不到的协议行必须标 `LegacyUnknown`，不能编造 witness。
4. 先从现有 `MatchRank` + 可用 witness 纯函数推导 Grade；V2 仍只读旧 key。#440 的 loose alias 必须在 acceptance 阶段被拒绝，不能靠 Grade 4 隐藏。
5. 同路径合并时逐字段定义取值规则：执行身份、最强 evidence、收藏 OR、历史 max、规则从真实路径解析；胜出 evidence 与高亮 witness 一起保留。
6. 日志写影子 Grade/Witness，并验证它不改变当前顺序。
7. 快速/完整窗口对可表达的同一行应算出同 Grade；若服务协议缺字段，先记录缺口，不在此阶段顺手扩大协议。

退出条件：V2 页面逐项相同；每行都能解释“由哪个字段、何种模式、哪段 witness 命中”；`LegacyUnknown` 有清单而不是静默当 direct。

### P4：修复拼音空格等价分支

主要文件：

- `Core/IndexV2/Search/PinyinNameEvaluator.cs`
- `Plugins/PinyinAlias/PinyinTermMatcher.cs`（只有确需复用 eligibility 时改）
- `Core/SearchIndex/HighlightMask.cs`
- `Tests/Core/IndexV2/Search/PinyinNameEvaluatorTests.cs`
- `Tests/Plugins/PinyinAlias/*`

步骤：

1. 写 joined eligibility 的纯函数并先补负例（操作符、ASCII、64 字符、反序）。
2. evaluator 为同一 provider 构造 joined matcher；只在 provider 能处理候选时运行。
3. 同时评估 original 与 joined，返回胜出 variant/witness；不要只返回更高 raw score而丢解释。
4. joined 完整拼满词干时复用单词查询的 whole-stem 语义，保证有/无空格同 Grade。
5. 高亮读取同一 witness；禁止决定与高亮各自再跑一套分支。
6. 检查字符 mask prefilter：joined 与分词所需字母并集应等价；若不等价，修 prefilter 后补 snapshot/delta 双路径测试。
7. 跑 PinyinAlias、Core 搜索与 App 高亮测试；记录真实语料耗时。

退出条件：I6 全部成立，普通英文/语法测试零变化，p95 不劣化超过 10%。

### P5：统一应用/快捷方式身份与匹配标题

主要文件：

- `SearchResultTypePriority.cs`
- `SearchResultMapper.cs`
- `RankedCandidateOrdering.cs`
- `SearchableItemMapper.cs`
- `Plugins/CoreExtensions/Providers/Indexing/StartMenuAppItemProvider.cs`
- `App/ViewModels/Search/SearchableItemCache.cs`
- `PluginSdk/Abstractions/Plugins/ISearchableItemProvider.cs`
- 若 P0 触发用户别名：对应 settings store 与设置页（先沿用现有设置持久化，不另起数据库）

步骤：

1. 用 `KindFlags` 代替“扩展名临时改 TypeRank”的唯一表达；保留兼容包装直到所有调用迁移。
2. provider 应用、原始 shortcut、普通文件分别打身份；`.exe` 继续不自动当应用。
3. shortcut 的 ranking `SearchTitle` 去扩展名；结果真实路径和执行委托不变。
4. 把 provider 现有 `Aliases/Keywords` 映射成 Grade 3 evidence，保留具体命中词和来源；替换当前“keyword-only = 零权重且无解释”的匿名 fallback。
5. 开始菜单 provider 在加载期缓存受控 aliases（目标 exe stem、package/product metadata），不得把完整路径任意分词灌入，也不得在按键热路径解析 COM。
6. 若 `437-db-intent` 在可信 metadata 下仍无冷启动召回，先增加单结果的本地用户别名入口；不要先引入全局自动同义词词典。用户别名同样是 Grade 3，并可单条删除。
7. **新增的** target/user aliases 在 V3 尚未切换时只进入 shadow candidate/trace，不进入 V2 可见列表；否则 V2 会把它们误当强标题命中而越级。现有 provider Keywords 保持 P3 记录的基线行为，直到 P7 一次性切换。
8. provider readiness 进入 trace；验证加载完成事件会重跑当前查询。
9. 合并测试：Application 行拥有执行身份；File 行贡献索引规则/质量；secondary alias、历史与收藏不丢。
10. 用 P0 trace 判断是否需要应用 source 额外 top-K。只有 recall 失败才进入 P9，不在本阶段扩大总预算。

退出条件：#437 中属于身份/标题的问题已修，可信相关词 shadow Recall 达标；同 Grade `.lnk` 可按应用类型提前；新增 secondary alias 尚未污染 V2 列表；#440 不属于本阶段。

### P6：实现 RankPolicy V3 影子排序（不切换 UI）

主要文件：

- 新的 V3 纯函数文件，或在 `RankPolicy` 内隔离版本；最终只能留一个生产策略
- 离线回放器
- 排名日志

步骤：

1. 按 §4.2 固化 Grade 规则与属性测试。
2. 按 §4.3 构造 V3 key；先不删除 V2 常量。
3. 每次快速窗口排序后，在日志开启时对前若干候选计算 V3 rank delta；不得做第二次昂贵匹配，只读 P3 预计算字段。
4. 黄金集比较 V2/V3：weak-over-strong inversion 必须降到 0；#437 的 `mustAppearBefore/mustBeTopK` 通过；#440 继续 `mustBeAbsent`；其它基线 nDCG 不下降。
5. 检查全序、稳定尾、同路径去重与快速/完整一致性。
6. 文档记录所有 Grade 边界反例；边界争议不能藏进代码注释。

退出条件：影子结果达到发布门槛，性能分配可接受，仍未影响用户列表。

### P7：切换普通偏好排序到 V3

步骤：

1. `RankPolicy.Version` bump；日志同时 bump policyVersion，而非仅 schema。
2. 移除生产路径的 `TypeBonusMax/16/8/4/1`，TypeRank 只在可偏好 Grade 内作为 ordinal key。
3. High/Normal 不跨 Grade；Low 与 AlwaysTop 按 §4.3 保持显式段。
4. direct H5/H3 改为同 Grade tie-break；徽标继续显示。
5. learned alias 先统一落 Grade 4/最后段，未做 P8 数据迁移前仍读旧存储但不再越级。
6. 启用 P5 的 provider/target/user secondary alias 候选，固定为 Grade 3，并展示来源标签；启用前重跑 alias false-positive 黄金集。
7. 更新 7 语言设置说明：类型顺序“在相关性相近的结果中优先”，不再承诺弱匹配也可领先。
8. 删除或改写那些主动断言弱类型行越过强命中的旧测试，替换为 I1/I2 属性测试。
9. 完整窗口和内联窗口逐入口接线，不能只改快速窗口。

退出条件：所有测试通过；P0 case 通过；V2/V3 差异报告经人工审阅；无普通偏好跨 Grade。

### P8：历史 query-path pair 与去反馈回路

主要文件：

- `Core/SearchHistoryStore.cs`
- `Core/SearchHistoryBucketSupport.cs`
- `HistorySearchCandidateMapper.cs`
- 历史设置页与迁移测试

步骤：

1. 新存储格式将 direct frecency 与 query-path association 分开；保留旧字段只为迁移。
2. 实现 canonical query key；拼音空格等价只能复用 P4 的资格判定，禁止无条件删空格。
3. 旧 entry 迁为低置信 pair，迁移幂等且可回读；旧版本读不到新字段时不损坏原始数据。
4. history mapper 对 learned alias 只做 canonical exact lookup，不做一般 fuzzy。
5. 记录 organic/direct 与 learned-injected 打开来源，采用不同更新幅度并封顶。
6. 首屏 learned alias 默认最多一条；增加删除单条关联的 UI/命令。
7. 测试衰减、迁移、多 query 同 path、同 query 多 path、拼音 canonical、反馈循环、scope、Recent。

退出条件：纯历史行不能压过直接文本行；用户仍可通过稳定的精确查询召回自己学出的别名。

### P9：只按测量修候选截断

触发门：P2/P5 证明期望应用或高优先项根本不在 C0/C1。

备选按成本从低到高：

1. 修 provider 根目录/readiness/缓存失效；
2. 每个候选 source 在合并前保留小额 top-K（应用例如 8），再做全局重排；
3. 对明确应用类型偏好的查询增加 shortcut 专用二次检索；
4. 最后才考虑把 EvidenceGrade 编进引擎 top-N 或改管道协议。

实施要求：

- 记录各 source 扫描量、首行延迟和内存；
- 不把历史/用户设置整个传进提权服务；
- 不因存在一条高档规则就无条件让所有查询回到 1000/4000；
- 路径型补搜、网络源、跨盘全局 Take 都要有对照测试；
- recall 提升必须能对应具体丢失 case。

退出条件：黄金集 expected Recall@50 = 100%；快速窗口 p95 不劣化超过 10%，或有明确获批的性能换正确性决策。

### P10：校准、灰度与收尾

1. 用黄金集网格检查 Grade 2 边界、类型是否允许作用于 Grade 2、learned alias 名额。
2. 至少两周真实日志或足够会话后报告 Success@1/3、MRR、history-only top-3、app Recall@10、cold-start Alias Recall@10、alias false-positive rate 与 p95；样本不足就写“无结论”。
3. 若要比较 V2/V3，使用 interleaving/影子回放；不直接比较两个时期的裸点击率。
4. 删除 V2 死代码、兼容 wrapper 与过时测试；生产最终只留一套排序策略。
5. 重写 `search-ranking.md` 为 V3 现行行为；把本文阶段打勾，并把被否证的判断压成“坑与教训”。
6. 更新用户文档：类型顺序边界、历史关联解释、拼音空格语义、应用搜索目录、相关词来自哪些可信别名以及如何删除用户别名。

退出条件：代码、测试、设置文案、用户文档与 dev-docs 对同一规则没有冲突。

## 10. 阶段依赖与可并行性

```text
P0 → P1 → P2 → P3
                 ├→ P4 拼音 ─┐
                 └→ P5 应用 ─┴→ P6 → P7 → P8
                                      └────→ P9（仅被数据触发）
P7/P8/P9 → P10
```

P4 与 P5 可以由不同模型并行，但都必须建立在 P3 的 evidence 字段上，并且各自不能切排序策略。P9 不是固定必做；如果 provider 修复后 Recall 已满足，跳过它才是正确结果。

## 11. 测试总表

### 11.1 纯排序属性

- 任一普通 type/history/High 变化不得让更差 Grade 越过更好 Grade。
- TypeRank 变化只影响 preference-eligible Grade 内的相对顺序。
- Low、AlwaysTop 的显式段行为各自钉住。
- 比较器全序、反对称、传递；输入枚举顺序不影响结果。
- 快速/完整在 TypeRank 中性时两两顺序一致。

### 11.2 来源与身份

- 同 `.lnk` 的 File + Application + History + Favorite 合成一行。
- 合成行保留 Application 执行委托和去扩展名标题。
- 原始文件行的规则 priority 不因 Application 行获胜而丢失。
- provider 未就绪、就绪 0 命中、有命中是三种可诊断状态。
- Title、provider keyword、target exe stem、用户别名各自产生正确的 `Field/Provenance/Grade`；secondary alias 命中可解释且不能伪造标题高亮。
- `db` 的可信“电报/Telegram/TG”别名能提高目标 Recall@10；无关应用不会因全局语义扩展一起涌入。
- 文件索引里的任意 `.exe` 不因扩展名自动升级；provider 认证的 `.exe/.url` 可以是 Application；名字以 `.lnk` 结尾的目录不被错误升级。

### 11.3 历史

- direct H5 只能在同 Grade 内前移。
- history-only H5 仍是 LearnedAlias，不进入 direct 段。
- fuzzy 相似旧 keyword 不再注入；canonical exact 才注入。
- 同一 path 可属于多个 query pair；迁移幂等。
- learned 注入产生的点击不会无限自强化。

### 11.4 拼音

除 §5.4 外，保留现有 `fdbjf`、长名多块、常用/生僻读音、63/64、512 码点、繁简、混合字母与排除词全套回归。joined 是新增解释，不得放宽单个 token 跨分隔符/跳字符的规则。

### 11.5 候选与性能

- expected item 分别在 C0/C1/R0/D0 的存在性断言。
- 默认 51→204、宽预算 1000→4000 与完整窗口无限额各有测试。
- 候选录制器不进入 Release 热路径。
- 快速窗口 204/4000、完整窗口 100k、拼音真实 provider 语料均跑基准。

## 12. 发布门槛

功能门：

- #437 脱敏 fixture 的 `mustBeTopK/mustAppearBefore` 全通过；#440 的精确 `zbl` fixture 保持 `mustBeAbsent`，紧凑 alias 正例仍可召回；
- `sousuojieguo` / `sousuo jieguo` 目标都召回、同 Grade，受控集 rank delta ≤1；
- ordinary preference 的 weak-over-strong inversion = 0；
- 黄金集 expected Recall@50 = 100%；
- 旧黄金集 nDCG@10 不下降，或每个下降 case 有明确人工决策。

性能门：

- 快速窗口端到端 p95 不高于 V2 的 110%；
- 排序比较期不引入字符串解析、COM、文件 I/O；
- provider 别名在加载时缓存，不在按键热路径解析快捷方式；
- 日志关闭时新增观测分配接近 0；
- 任何扩大候选预算都附扫描量与内存数字。

隐私门：

- 默认日志没有原始 query/title/path；
- fixture 没有用户真实路径或用户名；
- “复制解释”由用户主动触发，并清楚展示将复制什么。

## 13. 不做什么

- 不把 `TypeBonusMax` 从 16 随手改成 12 后宣布修复；那只移动反例边界。
- 不全局移除拼音查询空格。
- 不用历史点击直接训练自由 LTR 模型；当前日志与样本都不足以去位置偏差。
- 不用 BM25 替代现有短文件名匹配；这里缺少稳定词频/文档长度信号，问题主要是跨层越级和召回管线。
- 不为所有 `.exe` 自动赋 Application；构建产物会淹没真正应用。
- 不在没有 Recall 证据时扩大全局搜索上限或把所有偏好传进服务进程。
- 不永久维护 V2/V3 两套用户可选排序；影子期结束后只留一套，回滚靠版本提交。
- 不把整个 #437 一句话宣称“已修复”：必须逐个子案例报告；也不把已在当前基线修复并有回归测试的 #440 重新包装成 V3 功劳。

## 14. 给后续模型的任务模板

每次只发一个阶段，并附下面约束：

```text
你只实施 dev-docs/search-ranking-intuition-plan.md 的 Px，不提前做下一阶段。
先读 search-ranking.md、pinyin-vs-winshun.md 与本阶段列出的源码。
开始前输出：现状调用链、计划改动文件、不会改的行为。
实现后必须：
1. 补本阶段要求的 MSTest；
2. 跑 Core/App/PinyinAlias 中受影响项目；
3. 报告测试命令、通过数、性能或未能运行的原因；
4. 更新本文 Px 状态与发现；
5. 检查 git diff，不能夹带格式化或无关重构。
若发现期望项在更早阶段已丢失，停止调排序并把 case 标成 retrieval 问题。
```

评审每个阶段只问五件事：

1. 它修的是 recall、evidence、rerank 还是 display？
2. 有无用 fixture 复现，而不是只写合成单元函数？
3. 是否新增了普通偏好跨 Grade 的通道？
4. 是否把磁盘/COM/解析放进按键或比较器热路径？
5. 日志和文档能否解释这条为何在另一条之前？

## 15. 当前审计对应的关键源码

| 主题 | 位置 |
| --- | --- |
| V2 分值、类型加成、history section | `Core/SearchIndex/RankPolicy.cs` |
| 快速窗口装饰、合并与排序 | `App/ViewModels/Search/Mapping/RankedCandidateOrdering.cs` |
| 候选源汇总、shortcut rank、显示截断 | `App/ViewModels/Search/Mapping/SearchResultMapper.cs` |
| 类型秩与扩展名快捷方式识别 | `App/ViewModels/Search/SearchResultTypePriority.cs` |
| 应用/设置 provider 候选匹配 | `App/ViewModels/Search/Mapping/SearchableItemMapper.cs` |
| 开始菜单与自定义应用目录 | `Plugins/CoreExtensions/Providers/Indexing/StartMenuAppItemProvider.cs` |
| 历史 keyword 加行 | `App/ViewModels/Search/Mapping/HistorySearchCandidateMapper.cs` |
| 历史持久化与 frecency | `Core/SearchHistoryStore.cs`、`Core/SearchHistoryBucketSupport.cs` |
| 查询空格与操作符解析 | `Core/SearchIndex/Fzf/FzfPatternParser.cs` |
| #440 alias 接受门 / tightness | `Core/SearchIndex/Fzf/FzfPatternAliasMatchExtensions.cs`、`Core/SearchIndex/AliasMatchRules.cs`、`Tests/Core/SearchIndex/AliasLooseReadingTests.cs` |
| 拼音多词/单词初排 | `Core/IndexV2/Search/PinyinNameEvaluator.cs` |
| 单拼音词连续 NFA | `Plugins/PinyinAlias/PinyinTermMatcher.cs` |
| 高亮与最终 MatchRank | `Core/SearchIndex/HighlightMask.cs` |
| 引擎质量打包/截断 | `Core/IndexV2/Search/SearchResultRelevance.cs`、`Core/SearchIndex/SearchCandidateComparer.cs` |
| 现有校准日志 | `Core/SearchIndex/RankingEventLog.cs` |

## 16. #437 / #440 案例矩阵（来源已核对，P0 fixture 待落盘）

| Case | 原始查询 | 工单环境/期望 | 5.10.0 实际 | 当前基线判断 | Fixture 断言 |
| --- | --- | --- | --- | --- | --- |
| 437-cfwj-order | `cfwj` | 快速窗；历史清空，后续试过无规则；“重复文件”应用快捷方式应先于偶合项 | `ETC服务监督热线` 等普通项靠前，五个“重复文件” `.lnk` 靠后 | stock Low 误伤与 shortcut 类型秩已修；`重复文件` whole-stem 与 `ETC...` inside-span 的证据分级仍需 V3 固化 | 目标 Grade 0；偶合项 ≥1；所有目标 `mustAppearBefore` 偶合项；App Recall@10 |
| 437-db-intent | `db` | 想找 Telegram / 电报 / TG | 不想要的文件过多，目标难找 | 需 current trace 区分目标未召回与弱候选占位 | `电报`候选 `mustBeTopK`；记录 C0，不预判所有干扰项 absent |
| 437-fscapture-source | 截图中查询未由文字重抄 | 历史和 keyword 已清；无可见关联的 `FSCapture.exe` 不应进前十 | `FSCapture.exe` 仍在前十；文件名末尾加 `1` 后消失 | 不能归因于 learned history；可能是旧 alias gate、provider keyword、Recent、插件或截图上下文中的其它查询 | rawQuery 未转录前不猜；trace 后钉住真实 `Source/Field/Witness`；无 witness 则 `mustBeAbsent` |
| 437-pinyin-space | 工单称“中间打空格正常”；本任务给出 `sousuojieguo` / `sousuo jieguo` | 同一设置、同一目标 `搜索结果` | 空格导致结果/排序突变 | parser 单词 vs 多词、whole-stem +40 与截断共同造成 | 两者召回；同 Grade；target rank delta ≤1；高亮一致 |
| 437-shortcut-low | `cfwj` 等 | `.lnk` 位于有无扩展名祖先的用户工具树；应用类型靠前 | 祖先命中 `/^[^.]+$/` 后整树 Low，folder/file 可压住 shortcut | `e62621ae`、`f8bc512f` 已修 stock 默认与 shortcut rank；显式 Low 仍应生效 | stock-old 迁移后非 Low；显式 Low 仍 Low；同 Grade app-first |
| 440-zbl-alias-scatter | `zbl` | 精确模式；长 epub 名不应命中 | 返回结果且无高亮；ASCII `z/b/l` 散点跨度 15，而 painter 上限 12 | `3bda77df` 已修，current HEAD 有原文件名回归测试 | `mustBeAbsent`；紧凑 `zL` 正例 `mustBePresent`；matcher/painter 同 witness |

P0 的剩余工作不是“寻找工单正文”，而是把上表变成脱敏、可执行的 fixture，并在 Windows/.NET 环境录制 **current HEAD** 的候选阶段快照。截图中没有文字转录的精确 rank 与 437-fscapture-source 的 raw query 保持 unknown，直到有人手工核对原附件。
