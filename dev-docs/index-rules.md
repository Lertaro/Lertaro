# 索引规则

规则模型与归一化在 [IndexRuleSetting.cs](../Core/Settings/IndexRuleSetting.cs)，匹配和继承在 [ExclusionRuleSet.cs](../Core/ExclusionRuleSet.cs)。持久化 `Pattern / Effect / AlwaysTop`，Path、Glob、Regex 类型从 Pattern 推导，避免保存两份互相矛盾的状态。

## 作用范围

- 本地卷全量索引，搜索期套排除；网络遍历在建索引时跳过排除项。优先级不参与建索引。
- 排除子集变化会刷新网络索引，并触发适用的非 NTFS 扫描式本地盘重建；低/正常/高变化只影响后续查询。
- 快速窗口自行构造的历史、Recent、收藏和可搜索项不过同一套索引排除，只检查隐藏/系统与不可达等条件；它们仍参与优先级排序。不要把“排除优先”误写成所有来源绝不出现。
- 即时插件/动作前缀、完整窗口内容前缀以及内联近邻分组另有外层顺序，见 [search-ranking.md](search-ranking.md)。

## 判型顺序不能交换

1. 清理空白和成对外引号；**先识别 `/.../` 正则，再转换路径分隔符**。否则正则里的斜杠会被破坏。
2. 非正则把 `/` 转为 `\`、去尾分隔符并保留盘根。含 `*` 或 `?` 的写法优先判 Glob；设备路径 `\\?\` 前缀里的问号不算通配符。
3. 完整路径按写法识别，不要求当前存在；随后展开环境变量和 Shell 虚拟路径。未定义变量与没有文件系统落点的虚拟路径不能假装解析成功。
4. 规则正则保留原 body，不展开环境变量；当前使用忽略大小写、文化无关选项及 50 ms 匹配超时。与查询语法分开维护。

Glob 的 `*` 不跨目录，`**` 可跨目录；无分隔符的模式匹配叶名，并通过祖先判定影响子树。优先级 regex 检查叶名与相对路径，排除 regex 还检查绝对路径；复用这两条路径时不能丢掉范围差异。

## 路径与模式分别继承

| 规则来源 | 合并规则 |
| --- | --- |
| 自身或最近祖先的显式路径规则 | 更近者整体替换更远者，包含 Normal 和 AlwaysTop |
| 自身及祖先的 Glob/Regex | 取最强档位 High > Low > Normal；同档 pin 取或 |
| 路径分量与模式分量相遇 | 取更强档；同档由路径规则决定 pin |

因此显式 Normal 路径可覆盖祖先的 Low 路径，作用于该路径自身及其子项；它不能覆盖另外命中的 Low/High 模式规则。旧记录“目录自身仍低、精确文件 Normal 不生效”已与实现不符。

`AlwaysTop` 只对 High 有效，候选仍须命中查询。子目录的未置顶 High 可以关掉祖先 pin；同档路径规则还可覆盖全局置顶模式。Start Menu 边界会清掉继承的 Low 路径分量，但保留对 Start Menu 自身及其子树的显式规则。

合并强度 High > Low > Normal 与最终优先级顺序 High > Normal > Low 不同。V3 还先比较段与证据等级；不能再把旧版“档位永远是第一键、高档弱匹配压过正常强匹配”当现行规则。

祖先缓存必须分别保存路径与模式分量，并有有界上溯。把它们过早合成一个档位，会使近处 Normal 无法恢复，也无法正确关闭 pin。规则缓存随设置失效；带搜索根的实例不能跨根复用。

## 配置迁移经验

[IndexRuleMigration](../Core/Settings/IndexRuleMigration.cs) 在内存中迁移，下一次正常保存才落盘，避免启动依赖写权限。

- `IndexRules == null` 表示旧版本，需要迁移；空列表表示用户主动清空，不能补回默认值。
- 用户改过的旧排除项全部保留为 Exclude，再添加默认降权；保存时仅把排除子集镜像回旧字段，保证降级可读。
- `/^[^.]+$/` 已退休：它会命中无扩展名祖先目录，将整个子树降权。现有迁移会从保存列表中删除这个模式。
- 非空旧列表缺少系统设置降权模式时补入；合成路径使用稳定的 provider 类型名，避免换语言后失效。
- 冲突提示仅对能证明的“排除覆盖优先级”给出警告，不阻止保存，也不声称能判定任意正则/Glob 的交集。

验证入口：[IndexRuleSyntaxTests](../Tests/Core/Settings/IndexRuleSyntaxTests.cs)、[IndexRulePriorityTests](../Tests/Core/Settings/IndexRulePriorityTests.cs)、[IndexRuleMigrationTests](../Tests/Core/Settings/IndexRuleMigrationTests.cs)。测试要同时覆盖目录自身、子文件、近处 Normal、模式叠加和 pin，不能只用一个子文件代表整条继承语义。
