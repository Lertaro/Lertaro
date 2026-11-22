# 分类与筛选

[SearchCategoryClassifier](../PluginSdk/Helpers/SearchCategoryClassifier.cs) 统一维护分类枚举、扩展名和默认 glob，供 Core、App 与插件使用。`.ts` 按现有约定归视频；新增扩展名只修改这份表。

## 统一配置与旧行迁移

`CustomFilters` 保存 `Enabled / Keyword / Label / Rule / ShowAsRow / Icon`。启用项可通过前缀加关键词使用，例如 `\doc`；启用且 `ShowAsRow` 为 true、名称和规则非空的项也显示在侧边栏与类型筛选栏。

`MergeSidebarRowsIntoFiltersOnce()` 将旧 `SidebarCustomFilters` 合入统一列表。同关键词只设 `ShowAsRow=true`，保留现有启用状态和规则，缺图标时补入旧图标；新增项保留启用状态、规则和图标，以关键词作显示名。合并后移除旧键，读取与设置页共用此入口。

## 自定义规则

[CustomFilterQueryTokenProvider](../Plugins/CoreExtensions/Providers/QueryTokens/CustomFilterQueryTokenProvider.cs) 在启用项中取匹配输入前缀最长的关键词，避免 `audio` 被 `a` 提前认领。未知关键词或空规则返回空结果。

规则按逗号、分号或空格分词，不处理引号和转义；通配符或扩展名按 OR 合并。`folder`、`dir`、`:f` 选目录，`file` 或 `:-f` 选文件。这些写法属于规则字段；查询框语法见 [search-grammar.md](search-grammar.md)。

[CustomFilterRuleResolver](../Plugins/CoreExtensions/Providers/QueryTokens/CustomFilterRuleResolver.cs) 先展开 `\keyword` 引用，再匹配规则。它用显式栈，每个名称只访问一次，去重后保留首次出现顺序，避免循环引用与深递归。查询标记只展开启用引用，侧边栏允许引用禁用项的规则定义。

标记中的名称模式只选择文件；侧边栏通配符可匹配目录名，目录/文件规则则在两端按类型判断。查询标记把名称通配符转为 Regex，使用 IgnoreCase + CultureInvariant，缓存上限 256、单次匹配预算 250 ms；超时只跳过当前行。该策略独立于查询正则的整查询失败策略。

回归入口：[CustomFilterQueryTokenProviderTests](../Tests/Plugins/CoreExtensions/Providers/QueryTokens/CustomFilterQueryTokenProviderTests.cs)、[TypeFilterProviderTests](../Tests/Plugins/CoreExtensions/Providers/Filters/TypeFilterProviderTests.cs)。

## 截断前过滤

- `SidebarFilterItem.Category` 是可下推的类别身份，传入名称、路径补全、网络索引和实时匹配。回归覆盖小 limit、大量不合格候选及 base/delta/OR。
- 自定义规则行没有 Category，逐行处理已召回候选；显示名和 Id 只用于界面身份。
- 引擎放行快捷方式，App 按目标分类，保留开始菜单应用。
- 名称扫描使用裸名称，避免逐候选沿父链重建路径；需要路径信息的规则在相应层判断。

“全部”的 `ActiveCategoryFilter` 为 null；永远返回 true 的谓词仍会触发活动筛选路径，改变显示上限。快速窗口标签按 Id 保持选择，`RememberCategoryFilter` 只记当前会话。

## 协议与术语

搜索请求 v12 的 Category 占一个 flag byte，完整协议见 [windows-integration.md](windows-integration.md)。新增字段同时更新长度预估、写入、读取和版本，测试核对预计长度与实际帧长，避免池化缓冲区掩盖预租大小错误。

界面使用“类型筛选栏”“标签”“侧边栏筛选”；“分类”指数据身份，“档位”用于索引优先级。
