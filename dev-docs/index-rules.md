# 索引规则

规则模型与归一化在 [IndexRuleSetting](../Core/Settings/IndexRuleSetting.cs)，匹配和继承在 [ExclusionRuleSet](../Core/ExclusionRuleSet.cs)。持久化 `Pattern / Effect / AlwaysTop`，Path、Glob 和 Regex 类型从 Pattern 推导。

## 作用范围

- 本地卷全量索引，搜索期应用排除；网络遍历在建索引时跳过排除项。优先级只影响搜索。
- 排除子集变化刷新网络索引，并重建适用的非 NTFS 扫描式本地盘；Low/Normal/High 变化用于后续查询。
- 快速窗口自行构造的历史、Recent、收藏与可搜索项只检查隐藏/系统和不可达等条件，未套用同一份索引排除，但仍参与优先级排序。
- 即时插件与动作、完整窗口内容前缀及内嵌近邻分组有外层顺序，见 [search-ranking.md](search-ranking.md)。

## 判型与匹配

1. 清理空白和成对外引号，先识别 `/.../` 正则，再转换路径分隔符，保留正则正文。
2. 非正则把 `/` 转为 `\`、去尾分隔符并保留盘根。含 `*` 或 `?` 时优先判 Glob，设备路径 `\\?` 前缀的问号除外。
3. 完整路径按写法识别，随后展开环境变量和 Shell 虚拟路径；存在性不参与判型，未定义变量或无文件系统落点的虚拟路径解析失败。
4. 规则正则保留原正文，不展开变量，使用忽略大小写、文化无关选项及 50 ms 超时。查询正则有独立契约，见 [search-grammar.md](search-grammar.md)。

Glob 的 `*` 不跨目录，`**` 可跨目录；无分隔符的模式匹配叶名，并通过祖先判定影响子树。优先级 Regex 检查叶名与相对路径，排除 Regex 还检查绝对路径。

## 继承与合并

| 规则来源 | 合并规则 |
| --- | --- |
| 自身或最近祖先的显式路径规则 | 更近者整体替换更远者，包含 Normal 和 AlwaysTop |
| 自身及祖先的 Glob/Regex | 取最强档位 High > Low > Normal；同档 pin 取或 |
| 路径分量与模式分量相遇 | 取更强档；同档由路径规则决定 pin |

显式 Normal 路径覆盖祖先 Low 路径，另外命中的 Low/High 模式仍生效。`AlwaysTop` 只对 High 有效，候选仍须命中查询。子目录未置顶的 High 可关闭祖先 pin，同档路径规则也可覆盖全局置顶模式。

Start Menu 边界清掉继承的 Low 路径分量，保留对自身及其子树的显式规则。规则合并强度为 High > Low > Normal，最终排序的优先级为 High > Normal > Low，另受 V3 分段与证据等级约束。

祖先缓存分别保存路径和模式分量，并限制上溯深度。过早合并会使近处 Normal 无法恢复、pin 无法关闭；缓存随设置失效，带搜索根的实例按根隔离。

## 配置迁移

[IndexRuleMigration](../Core/Settings/IndexRuleMigration.cs) 在内存中迁移，下一次正常保存才落盘，避免启动依赖写权限。

- `IndexRules == null` 表示旧版本，空列表表示用户主动清空。
- 用户改过的旧排除项保留为 Exclude，再添加默认降权；保存时把排除子集镜像回旧字段，供降级读取。
- `/^[^.]+$/` 会命中无扩展名祖先目录并降权整个子树，现有迁移已删除该模式。
- 非空旧列表缺少系统设置降权模式时补入；合成路径使用稳定 provider 类型名，换语言后仍有效。
- 冲突提示只报告可证明的“排除覆盖优先级”，允许保存。

回归入口：[IndexRuleSyntaxTests](../Tests/Core/Settings/IndexRuleSyntaxTests.cs)、[IndexRulePriorityTests](../Tests/Core/Settings/IndexRulePriorityTests.cs)、[IndexRuleMigrationTests](../Tests/Core/Settings/IndexRuleMigrationTests.cs)。覆盖目录自身、子文件、近处 Normal、模式叠加与 pin。
