# 索引规则

一条索引规则由匹配方式和档位组成。匹配方式决定命中什么，写法是完整路径、通配符或正则三种之一，由程序判定；档位决定怎么处理，取排除、低、正常、高之一。这个功能回答的是「哪些东西排在前面」，排除只是四个档位里的一个。

下面从规则本身的写法讲到界面与文案的约定，最后记下踩过的坑和被推翻的说法。结果列表两条排序梯子的完整键序见 search-ranking.md。

## 适用范围与核对基线

代码位置按 `main` = 1986b835 复核（索引规则与排序相关代码自 9a6b9769 未改），功能由 ba6fea2c（统一索引规则）、714300ed（设置页编辑器）、8c754217（按档位排序）落地。文中路径相对 `<repo>`，`文件:行号` 逐个对过。

生效范围分两套，另有一条针对快速窗口自建行的例外：

- 本地盘（NTFS/ReFS 走 MFT 或 USN，其他文件系统走扫描式构建）全卷建索引，排除规则从不参与构建，只在搜索期过滤命中的结果；三档判定见 `Core/Services/Search/SearchServiceHelper.cs:39-48`。
- 网络盘与配置出去的远程源在遍历期就跳过排除项（`Core/Indexer/NetworkDrive/Walk/WalkFilter.cs:48`），交给遍历的只有排除子集（`Core/Indexer/NetworkDrive/Walk/WalkOptions.cs:31`），优先级不参与建索引。
- 排除只作用于索引结果与网络遍历。快速窗口自建的历史、Windows Recent、收藏、可搜项行不过排除规则，只过隐藏/系统与不可达两道门（`HistorySearchCandidateMapper.IsExcludedFromSearch`）；被排除目录里的历史项仍可能以历史行出现，并且照样参与档位排序。

改规则之后的重建代价按档位划分。变更检测只比较排除子集（`App/ViewModels/Settings/SettingsChangeSnapshot.cs:15`）：

- 排除子集变了：刷新网络索引（`App/ViewModels/Settings/SettingsViewModel.cs:275`），并且重建非 NTFS 的扫描式本地盘（`:302`，条件见 `SettingsApplyHelpers.cs:20`）。MFT/USN 索引的盘不重建。
- 低/正常/高之间移动，或增删这类规则：什么都不重建，下一次查询生效。

## 规则模型与档位

持久化的一条规则只有两个字段（`Core/Settings/IndexRuleSetting.cs:39`）：

```csharp
public sealed class IndexRuleSetting
{
    public string Pattern { get; set; } = "";
    public IndexRuleEffect Effect { get; set; } = IndexRuleEffect.Normal;
}

public enum IndexRuleEffect { Exclude = 0, Low = 1, Normal = 2, High = 3 }
public enum IndexRuleKind { Path, Glob, Regex }
```

判型结果不落盘。每次构建规则集都从写法重新判一次（`IndexRuleKind` 由 `IndexRuleSyntax` 推出），一条规则不会和它的匹配方式各说各话。列表里的图标与排序分组同样按重新判出的类型走。

档位语义：

- 排除让命中项不出现；低/正常/高只改变先后，不改变可见性。
- 同一层同时命中几个档位时按强度取一个，高 > 低 > 正常（`Core/ExclusionRuleSet.cs:373`）。这里的「正常」是有强度的真实取值（0），不是「不参与比较」：在强度合并里它永远输给低/高；但作为一条显式路径规则，它可以在路径继承里替换更浅的路径规则（见「优先级判定」）。
- 排除优先于任何档位：同一路径同时命中排除和优先级时，索引结果不会出现，优先级也不生效，页面把它当成冲突标出来。快速窗口自建的历史/Recent/收藏行不过排除规则（见「适用范围与核对基线」），是这条的例外。
- `PriorityRank` 是另一套顺序，只用于排序：高 0、正常 1、低 2（`Core/SearchResult.cs:59`），升序比较时高在前。注意它与合并强度的次序相反（合并时低档强于正常档，排序时正常档在低档前）。

原来的三条列表（路径 / 通配符 / 正则）收成一条 `IndexRules`（`Core/Settings/UserSettings.cs:36`）。

## 判型与归一化顺序

顺序是行为契约的一部分，改动它等于改判型结果。实现在 `Core/Settings/IndexRuleSetting.cs`（`IndexRuleSyntax`，:58）。

1. 去两端空白（空格、制表、`\r`、`\n`），再剥成对的引号：首尾同为 `"` 或 `'` 时去掉一层并重新去空白，最多剥 3 层，防止畸形输入把循环吃掉。
2. 先判正则，此时还没做分隔符转换：清洗后长度 ≥ 2 且首尾都是 `/` 即为正则，取中间的 body。body 为空报 `EmptyRegex`，编译不过报 `InvalidRegex`。正则体保持原样，`/` 不转成 `\`，否则 `/^\./` 无法表达。
3. 不是正则的，`/` 全部转成 `\`。
4. 去掉尾部 `\`；如果整串都是分隔符就保留原样；剩下光杆盘符（两字符的 `X:`）补回 `\`。`\\server\share\` 去尾后是 `\\server\share`。
5. 判型：含 `*` 或 `?` 一律是通配符，即使以盘符开头；否则写法像绝对路径就是完整路径；其余落到通配符。`\\?\` 与 `\\?\UNC\` 设备路径开头四个字符里的 `?` 是字面量，不算通配符（`HasWildcard`，:216）。
6. 完整路径再做环境变量展开与虚拟路径解析（`TryResolvePath`，:162）；通配符在编译时展开 `%VAR%`（`Core/ExclusionRuleSet.cs:89`）。正则不展开环境变量。
7. 正则的编译校验在第 2 步已经做过，失败即拒绝添加。

完整路径的判定只看写法，不要求路径真的存在，这样未挂载的目录也能提前写规则：

| 形式 | 判定用的正则 | 示例 |
| --- | --- | --- |
| 盘符根 | `^[A-Za-z]:\\` 或 `^[A-Za-z]:$` | `C:\`、`D:\Cache` |
| UNC | 以 `\\` 开头 | `\\server\share\dir`、`\\?\C:\very\long` |
| 环境变量开头 | `^%[^%\\]+%($|\\)` | `%APPDATA%\aaa`、`%SystemRoot%` |
| Shell 虚拟路径 | `^shell:`（忽略大小写）或 `^::` | `shell:Music\c`、`shell:::{CLSID}` |

边界按通配符处理的四种写法：`C:foo`（盘符后没有分隔符）、`\Windows`（单前导分隔符，锚定到根）、`..\x` 与 `.\x`（相对写法里的 `..`、`.` 当字面量目录名，匹配不到，等于一条无效规则）。含 `*` 或 `?` 的绝对写法（`C:\a\*.txt`）判为通配符；Windows 文件名里 `?` 非法，不会因此误伤真实路径。

通配符语义沿用现有实现（`Core/Indexer/GlobToRegex.cs`），没有新写匹配器：

| 写法 | 含义 |
| --- | --- |
| `?` | 单个非分隔符字符 |
| `*` | 任意多个非分隔符字符 |
| `**` | 任意多级，可跨分隔符 |
| 不含 `\` | 匹配任意层级下的叶名，目录名同样参与祖先判定 |
| 含 `\` 或以 `\` 开头 | 从搜索根或盘根锚定 |
| 尾随 `\` | 归一化时被去掉；目录本身及其子项靠祖先判定覆盖，`target\` 与 `target` 等价 |

`**\node_modules\**`、`Debug\**`、`target\`、`build\`、`node_modules` 都照旧工作。glob 的匹配超时是 1 秒兜底（`Core/Indexer/GlobToRegex.cs:17`）加必需字面量预筛，见「匹配与缓存」；正则的超时预算是 50ms。

虚拟路径与环境变量的处理：

- 环境变量用 `Environment.ExpandEnvironmentVariables`（经 `PluginSdk/Helpers/UserPathResolver.cs:22` 顺带 trim）。展开后仍留着 `%TOKEN%` 说明变量未定义，规则判为无效并报 `UnresolvedEnvironmentVariable`。通配符里的未定义变量不报错，保持原样交给匹配器。
- 虚拟路径按第一个 `\` 切成 token 与后缀，只把 token 交给 `ShellPathHelper.TryResolveVirtualPath`，因为 shell 助手认一个 token 而不认 token 加子目录。`shell:Music\c` 解析成 `<Music 真实路径>\c`。解析回来的还是 shell 名（没有文件系统对应项）时报 `VirtualPathWithoutFileSystem`。
- WSL 路径（`\\wsl$\...`、`\\wsl.localhost\...`）跳过 `Path.GetFullPath`，与索引侧一致（`Core/Settings/IndexRuleSetting.cs:191`、`Core/ExclusionRuleSet.cs:545`）。

正则语义：定界符是首尾各一个 `/`，body 里可以再有 `/`（`/a/b/` 的 body 是 `a/b`）。语法是 .NET `Regex`，选项 `IgnoreCase | CultureInvariant | Compiled`，超时 50ms。匹配对象按用途分成两套：

- 优先级匹配按判型分开：glob 测绝对路径、补尾分隔符的绝对路径、相对搜索根的路径、补尾分隔符的相对路径和叶名；regex 只测叶名与相对搜索根的路径（含补尾形式），不测绝对路径，否则每条规则都会额外看见盘符和父目录名，`/^[^.]+$/` 这类写法的结果会很怪。文件和目录都参与（`Core/ExclusionRuleSet.cs:330-362`）。相对路径的根取构造时带进来的搜索根，没有搜索根时退到盘根；裸盘根不给相对形式，免得 `^C:` 撞上它自己（`:388`）。
- 排除规则照老行为：叶名测，绝对路径测，相对搜索根的路径也测（`:510-536`）。

判型与归一化的表驱动用例落在 `Tests/Core/Settings/IndexRuleSyntaxTests.cs`（`CompletePaths_AreClassifiedAsPath`、`SlashSeparators_AreConvertedToBackslashes`、`ExtendedPathPrefixQuestionMark_IsNotAWildcard`、`EmptyRegex_ReportsEmptyRegex`、`ResolvePath_KeepsWildcardsUntouched` 等）。下面是现行行为样例，取值与实现和测试一致：

| 输入（原始） | 判型 | 归一化结果 |
| --- | --- | --- |
| `  "C:\abc\Def\\"  ` | 完整路径 | `C:\abc\Def` |
| `shell:Music/c` | 完整路径 | `<Music 真实路径>\c` |
| `%APPDATA%\aaa` | 完整路径 | `<AppData>\aaa` |
| `\\server\share\dir\` | 完整路径 | `\\server\share\dir` |
| `\\?\C:\very\long` | 完整路径 | 原样（前缀的 `?` 不算通配符） |
| `C:\a\*.txt` | 通配符 | 原样 |
| `**\node_modules\**` | 通配符 | 原样 |
| `target\` | 通配符 | `target`（子项靠祖先判定覆盖） |
| `node_modules` | 通配符 | 原样 |
| `C:foo` | 通配符 | 原样 |
| `/^\./` | 正则 | body `^\.` |
| `//` | 非法 | `EmptyRegex` |
| `/[/` | 非法 | `InvalidRegex` |

## 默认规则与旧配置迁移

新装默认是 1 条排除加 15 条低档（`Core/Settings/IndexRuleDefaults.cs:25`）：

| 档位 | Pattern |
| --- | --- |
| 排除 | `node_modules` |
| 低 | `__SEARCHABLE_ITEM__:SystemSettingsItemProvider:*` |
| 低 | `%SystemDrive%\Windows.old` |
| 低 | `%SystemDrive%\$WINDOWS.~BT` |
| 低 | `%SystemDrive%\$Windows.~WS` |
| 低 | `%ProgramData%` |
| 低 | `%SystemRoot%` |
| 低 | `%USERPROFILE%\AppData` |
| 低 | `%ProgramW6432%` |
| 低 | `%ProgramFiles(x86)%` |
| 低 | `build\` |
| 低 | `debug\` |
| 低 | `target\` |
| 低 | `/^\./` |
| 低 | `/^~/` |
| 低 | `/^\$/` |

系统设置项没有文件系统路径，合成路径是 `__SEARCHABLE_ITEM__:{provider.GetType().Name}:{Title}`（`SearchableItemMapper.SyntheticPath`）。provider 段取 CLR 类型名而不是本地化显示名，所以上面那条规则在 7 种界面语言下都命中；除此之外没有代码解析这一段，所有消费者只检查 `__SEARCHABLE_ITEM__:` 前缀。整组降到 Low 的取舍是：`Keyword` 命中仍能让英文查询找到中文设置项，但不再越过任何 Boosted 行——Windows 会给多个控制面板任务挂同一个关键词（`vpn` 同时出现在「查看网络连接」和「设置虚拟专用网络(VPN)连接」上），关键词身份不足以压过可见名称的直接命中。

旧的三条默认 glob（`.*`、`~*`、`\$*`）由三条低档正则接管（`/^\./`、`/^~/`、`/^\$/`），同一个意图从「隐藏」换成「降权」；`node_modules` 仍是排除。系统目录和应用目录从默认排除改成默认低档，属于行为变化：老用户会突然在结果里看到 `Program Files` 或 `AppData` 下的文件，它们排在最后而不是消失。

`/^[^.]+$/` 曾经是默认低档的一员，已从默认值退休（`IndexRuleDefaults.RetiredExtensionlessRegex`）。原因是对它的实测：regex 除了叶名还要测「相对搜索根的路径」，于是 `D:\tools\smalltools\Auslogics Duplicate File Finder.lnk` 会在祖先 `tools\smalltools` 那一层命中，整棵树继承 Low——任何把程序放在 `Program Files` 之外的人，程序目录都被无声压到最低档，这与「降权没有扩展名的名字」差得太远。`D:\tools\smalltools\重复文件`（文件夹）和同目录的 `.lnk` 都被压成 Low，结果就是一个文件夹压过它旁边的快捷方式，唯一在起作用的是「文件名匹配得更贴切」。想恢复这个意图，用显式路径规则（如 `-D:\tools`）或更窄的正则，别再用 `/^[^.]+$/`。现在它不只是从默认值退休：`IndexRuleMigration.Ensure` 每次加载都会把它从任何已保存列表里强制删掉，不看档位、不看列表是否被改过——它只会降权，删掉不会改变任何排除结果。

`IndexRules` 是可空字段，null 表示这份设置文件早于该字段，是迁移信号；空列表是用户主动清空，绝不再迁移（`Core/Settings/UserSettings.cs:30-36`），也不会被补加默认低档。旧的三列继续保留，读取时不参与匹配，只在保存时回写镜像。

迁移算法在 `IndexRuleMigration.Ensure`（`Core/Settings/IndexRuleMigration.cs:14`），加载设置后调用（`Core/Settings/UserSettingsPersistence.cs:47/86`），只在内存里算：

1. `IndexRules != null`，先规范化一次再用，旧三列忽略：无条件删掉 `/^[^.]+$/`；非空列表如果还没有 `__SEARCHABLE_ITEM__:SystemSettingsItemProvider:*` 这条低档，就补上。重复调用拿到同一个列表，幂等（`Ensure_IsIdempotentAndKeepsTheSameList`、`Ensure_PreviousStockList_DropsTheRetiredRule`、`Ensure_CustomisedList_StillDropsTheRetiredRule`、`Ensure_ExistingSystemSettingsDemotion_IsNotDuplicated`）。
2. 为 null，且旧三列与旧默认等价，换成新默认。等价判断走集合语义：每项先清洗，`/` 转 `\`，去掉尾部分隔符，再排序后忽略大小写逐项比；`IgnoredPathRegexes` 必须为空（`:101-118`）。全新安装落在这条路上，因为 `UserSettings` 的旧三列默认值就是那份旧默认。
3. 为 null 且用户改过，旧条目逐条转成 `Effect = Exclude`（正则补上 `/` 定界符，`WrapLegacyRegex`），然后把 15 条默认低档全部追加。跟同文本排除项重复的低档规则保留，运行期排除优先，冲突横幅是让用户看见并自行取舍的入口（`Ensure_CustomLegacyLists_KeepEveryEntryAsExclusionAndAppendDefaults`）。

迁移不在加载时写盘，避免启动期的写权限问题；下一次正常保存（应用设置）时落盘。重复启动会重复计算，结果一样。

保存时 `MirrorLegacy`（`UserSettingsPersistence.cs:114`）按判型把排除子集分回 `ExcludedPaths`、`IgnoredPathGlobs`、`IgnoredPathRegexes` 三列，降级到旧版本仍能排除；旧版本看不到低档规则，但不会误删文件（`MirrorLegacy_WritesOnlyTheExcludingSubset`）。`ExclusionRuleSet` 与 `WalkOptions` 一律只读 `IndexRules`，旧字段不再参与匹配。

## 匹配与缓存

没有新匹配器。`ExclusionRuleSet` 构造时把规则按判型分成排除三组和优先级三组（`Core/ExclusionRuleSet.cs:15-21`），两组共用同一个缓存实例。glob 仍走 `GlobMatcher.Compile` 与 `GlobToRegex`，正则仍是同一组选项与超时。类名保留，避免全仓无意义的改名。

实例缓存按设置对象认：`From(UserSettings)` 用 `ReferenceEquals` 比对来源（`:164`），设置读写之后由 `UserSettingsPersistence.cs:124/166` 调 `InvalidateCache()`，实例和它携带的两张缓存一起丢弃。带搜索根的 `From(settings, root)` 每次新建实例，因为根参与相对路径的计算。

两张祖先缓存都是 `ConcurrentDictionary`，因为流式搜索的本地盘任务和网络任务会同时进来：

- `_ancestorVerdicts`：目录到「这个目录或其某个祖先是否被忽略」。
- `_priorityVerdicts`：目录到继承的优先级，值把祖先路径规则与模式规则两个分量打包进一个整数，因为单个未命中不够用（`:302-311`）。

叶名的匹配不缓存，每次结果不同。收集未决层级后自顶向下回填，一次上溯把沿途每层都写进缓存，同树的兄弟目录也能直接拿到结果；写成迭代而不是递归，畸形的父路径链在这套代码里造成过无界遍历（`:458-501` 的注释）。

优先级判定 `GetPriority(path, isDirectory)`（`:235`）不是「自身命中就返回」，而是把自身分量与继承分量按强度合并：

1. 自身分量：`P` 这一层整层相等的路径规则，加上该层的模式规则，两者取强（`OwnPriority`，`:256`）。自身模式规则要参与，否则一条 `*.exe` 的高档规则会被祖先目录的低档盖掉。模式规则的匹配形态见上一节：glob 连绝对路径一起测，regex 只测叶名和相对路径。
2. 继承分量：从父目录逐级向上，最多 64 层（`:10`），维护两个独立值。路径分量取最近一层命中的显式路径规则，更近的规则替换更浅的，包括显式正常档；模式分量沿链取最强（`InheritedPriority`，`:264-298`）。缓存按目录同时存这两个值（`:302-311`）。
3. 合并：自身与继承再按强度取强，高 2 > 低 1 > 正常 0（`:364-378`）。同一层同时命中高与低时高胜（`HighBeatsLowAndNormalAtTheSameLevel`）。
4. 路径规则只认整层相等（`PathPriorityAt`，`:314`）；模式规则按第 1 步的形态匹配。目录和文件都参与；没有规则命中或路径为空，返回正常档（`:237-253`）。
5. 一个容易误读的边界：显式正常档路径规则只替换祖先链里的路径分量：命中那一层的**子项**在祖先链上没有更强的模式规则时继承到正常档；那一层自己的结果行仍会把自身路径规则与祖先路径值做强度合并，所以目录本身可能仍是低档。对文件写一条精确的正常档路径规则也不豁免它：自身是正常、祖先路径是低，最终仍是低。`ExplicitNormalStopsInheritance` 只断言了子文件，没有断言目录行。祖先链上若有低/高档 glob/regex，子项仍按模式分量的结果走，因为两个分量独立合并。

### 始终置顶（AlwaysTop）

`IndexRuleSetting.AlwaysTop`（`Core/Settings/IndexRuleSetting.cs:50`）是高优先档的一个修饰位，不是第五个档位：旧配置缺字段读为 false，序列化与迁移都不用改。解析层只在一个地方归一化，`IndexRuleSyntax.TryParse` 的 5 参重载把 `alwaysTop && effect == High` 写进 `ParsedIndexRule.AlwaysTop`（`:119`）；4 参重载等价于传 false，冲突检测继续用它。判定层再保证一次：`Verdict(effect, alwaysTop)`（`Core/ExclusionRuleSet.cs:324`）是所有 verdict 的唯一构造入口，所以 `Normal`/`Low` 永远不可能带着 pin 出来。

- 置顶不是无条件出现。候选仍然必须命中查询，Pinned 段只是把命中的行抬到最前，不会凭空塞进结果里。
- 路径规则：最近一层的显式路径规则**整体替换**祖先，连 pin 一起（`pathPart = PathPriorityAt(level) ?? pathPart`，`:314`）。因此子目录写一条未勾选的高档规则、甚至正常档规则，都能关掉祖先的置顶；兄弟子树不受影响。
- 模式规则：沿祖先链取最强档，同档时 pin 取或（`Strongest`，`:332`），所以两条同为高的模式规则里勾了一条就都算置顶；低档未勾选不会盖掉高档的置顶。
- 路径规则与模式规则相遇时（自身与继承的最后合并）取更强档，**同档时路径规则连 pin 一起胜出**，不取或（`PreferPath`，`:353`）。理由是路径规则是用户对某个文件夹的明示决定：`C:\work` 高优先未勾选必须压过全局 `*.ps1` 高优先已勾选，否则用户没法为某个目录关掉置顶。
- 缓存把两个分量各打包 4 bit（0 表示无规则，否则 `((1 + effect) << 1) | pin`，`Pack`/`Encode`，`:369-380`），目录键与上溯逻辑没变。
- `HighPriorityRoots` 仍只从路径规则取（`:139`）：模式规则即使置顶也不产生补搜根，glob/regex 型沿用原有限制。
- 消费方：快速窗口把 `verdict.AlwaysTop` 传进 `RankSignals`，`RankPolicy.Section` 让它进 Pinned 段；完整窗口在 `SearchResultRanking.Decorate` 里把段号写进 `RankFlags` 的 bit 0-1，`RankedSearchResultComparer` 按段号先比，所以两个窗口的置顶来自同一条规则。

排除语义保持不变。`IsExcluded` 里根前缀的判断和祖先层的 glob 与正则没有动，`exemptRoot` 和 WSL 分支也没有动，只是数据源从三条列表换成 `IndexRules` 的排除子集。网络侧 `WalkOptions.FromUserSettings` 从规则集派生三列，`WalkFilter` 未改。

成本上，完整窗口一次查询可能排十万级结果，靠逐目录缓存和每次排序调用内的字典（`App/ViewModels/Search/Mapping/RankedCandidateOrdering.cs:41`、`Core/SearchResult.cs:53/66`）摊薄：一次查询里每个目录只判一次，规则集取一次，循环内不再读设置。glob 先走必需字面量的 `Contains` 预筛（`Core/Indexer/NetworkDrive/Walk/GlobMatcher.cs:65`），预筛不中就跳过正则。还有一个斜杠形式的开关：只有当某个 pattern 可能区分两种分隔符写法（字符类里原样输出的 `\` 或 `/`，或用户正则里带 `/`）时，才为每条路径额外构造 `/` 分隔形式（`Core/ExclusionRuleSet.cs:144-146/510-536`）。

## 档位如何接入排序

档位是**进入排序的候选**的第一级键，但不是整个窗口列表的第一级键：快速窗口的插件即时结果与搜索动作行先以固定前缀放在列表顶部，不参与排序；完整窗口的内容 provider 行同样是排序之外的前缀；内联窗口还会在排序之后按目录邻近度重新分组。`PriorityRank` 见上。两条梯子的完整键序见 search-ranking.md，这里只讲为什么把档位排到最外层。

- 快速窗口：档位 → 结果类型顺序（`TypeRank`，只有这个窗口读设置；快捷方式的索引行取应用/文件两个身份里更靠前的那个，见 search-ranking.md 的类型加权一节）→ 六个质量键 → 收藏 → history band → history priority → 文件/文件夹 → 自然名序 → 名字长度 → 路径长度 → 盘 → 路径（`RankedCandidateOrdering.cs:54-66`）。
- 完整窗口：档位 → 高 32 位质量键 → 收藏 → history band → history priority → 文件/文件夹 → 自然名序 → 名字长度 → 路径长度 → 盘 → 路径 → 低 32 位（`SearchResultRankComparer`）。这个窗口**没有**结果类型顺序，也没有按类型分组：它只排索引返回的文件行，`ResultTypeOrder` 设置不在完整窗口生效；`Core/SearchResult.cs:102` 写的「结果类型分组在其上」是旧注释，与实现不符。
- 内联窗口：全局段复用快速梯子，但 `TypeRank` 传入空顺序，所有类型同值，等于没有类型层；随后当前文件夹段按邻近度排在全局段之前，直达子项的顺序不单独查档位（`InlineListSearchHelper.cs:97-109`、`ExplorerSearchHelper.RankMatches` 不传 resolver）。一个低档的当前文件夹文件仍会排在外部的高档文件前面。

这个功能的语义是「这些目录整体提权或降权」，不是「同等匹配时的平手裁决」。放在匹配质量或结果类型之后时，低档目录里的精确匹配仍会压过正常目录的模糊匹配，高档目录里的弱匹配也压不过一条普通应用结果（快速窗口），用户设了高档却看不到它，功能等于没生效。用户的要求是高档里匹配度最差的也要排在低档里匹配度最好的之前，只有档位当第一级键才能保证。代价是高档的弱匹配会排到正常档的强匹配前面，这是明确要的行为；类型分组退到档位内部（仅快速窗口），收藏与历史不能跨档位把低档行拉回来，只在同一档位内起作用。

设置里另有一处管程序先后：「设置 → 通用 → 快速搜索窗口 → 结果类型优先级」只决定快速窗口同一档位内哪种类型在前，两者不冲突。便携程序的快捷方式还要在「设置 → 插件 → 核心功能扩展插件 → 自定义应用搜索文件夹」里加目录，也可以在快速窗口输入 `set 自定义应用` 跳过去。

应用行与插件行要分开看。搜索可搜项/应用行进入候选列表，按它的快捷方式或可执行文件路径求档位（`App/ViewModels/Search/Mapping/SearchResultMapper.cs:250-257`），覆盖这些目录的规则同样作用到它。插件即时结果、插件搜索动作行，以及完整窗口的内容 provider 行不进入排序：快速/内联窗口把它们放在列表顶部的前缀里，完整窗口把内容行放在列表前缀里（`StreamingResultAccumulator` 的 `ContentPrefixCount`），档位对它们根本没有机会出手。进入排序的候选里，路径为空的和没有规则命中的都会拿到正常档（`Core/ExclusionRuleSet.cs:237-240`）。

三个窗口的档位位置不完全同一棵树，见踩坑第 9 条。

## 冲突检测与提示

静态、启发式的判定在 `Core/Settings/IndexRuleConflict.cs`（`IndexRuleConflictDetector.Detect`，:25），只看优先级规则与排除规则的配对，判型解析不出来的规则不参与检测（`:34-36`）。对每条优先级规则 `P` 与每条排除规则 `E`：

| P 类型 | E 类型 | 判定 |
| --- | --- | --- |
| 完整路径 | 完整路径 | `E` 等于 `P`，或 `E` 是 `P` 的祖先（前缀加分隔符边界，忽略大小写）。只报「排除盖住优先级」，`E` 在 `P` 之下不报——那时 `P` 对同目录其余部分仍然生效 |
| 完整路径 | 通配符 / 正则 | 拿 `E` 的匹配器测 `P` 解析出的路径，并沿它的祖先逐级上溯 |
| 通配符 / 正则 | 完整路径 | 只有能证明这条模式的全部匹配都落在 `E` 的子树内才报。以绝对路径开头、通配符之前不含 `..` 的 glob 取「第一个通配符之前的字面前缀」求覆盖根（前缀以 `\` 结尾就去掉尾分隔符，否则取它的目录），覆盖根等于 `E` 或在 `E` 之下才算；正则与相对 glob 一律不报 |
| 同判型 | 同判型 | 归一化文本相等（忽略大小写） |
| 通配符 | 正则 | 交集不可判定，不报 |
| 通配符 | 通配符（不同文本） | 只做文本相等，不报重叠 |

逐级上溯加上叶名比对是实施时补的：单段 glob（`node_modules`）要能在祖先层撞上 `C:\x\node_modules\y`，否则这类真冲突报不出来。匹配一层时 glob 测整路径、带尾分隔符的形式和叶名，正则再多加一个盘根相对形式（`:162-213`）。目录也能被 glob/正则排除，所以「`E` 命中 `P` 的路径」就当作整个子树被排除，不再区分文件和目录。

展示方式：页面顶部横幅给数量（`Exclusions_ConflictSummary`），点击展开逐行明细，格式是「优先级规则 ← 盖住它的排除规则」；被盖住的那行加警告图标（字形 E7BA）和 tooltip（`Exclusions_ConflictTooltip`，带上冲突的排除规则文本）。增删改后立即同步重算，N×M 很小，不异步。不弹模态框，也不阻塞保存，排除优先是确定行为，提示即可。

这份检测不假装完备，下面几条局限是写明在文档里的。

- 两条不同 glob 的重叠（`build\` 与 `**\build\**`）无法静态判定，运行期以排除优先，不会出错。
- 正则交集不可判定，只做「正则测试路径」和文本相等两种判断。
- 锚定到具体目录的正则（如 `^C:\\temp\\`）和相对 glob，即使实际被排除规则完全盖住也不会提示，这是宁漏勿错的保守选择。
- 路径规则与通配符规则的冲突依赖路径字面量，覆盖不到未来才出现的目录。
- 高与低两条优先级规则重叠不报（`GlobVersusGlob_IsNotStaticallyReported` 一类用例只覆盖排除方向），运行期取高。原文里「同一路径同时命中高与低也提示」这条没有实现，见踩坑第 11 条。

## UI 与文案约定

一张 `SettingsCard`，没有子页签（`App/Views/Settings/IndexSettings/ExclusionsTab.xaml`）。说明区顺序固定：规则是什么，档位怎么排序，程序优先级去哪调，正则更贵。档位说明放在输入框之前，因为这个页面主要回答「哪些东西排在前面」，排除只是其中一个档位。

- 一行里是输入框，右边配档位下拉和添加按钮。下拉默认「正常」，选项顺序是排除、低、正常、高。输入实时显示「识别为：X」（`Exclusions_DetectedTemplate`），判为完整路径时再显示解析后的路径（`Exclusions_ResolvedTemplate`）；错误行只在有错时出现，6 种 `Exclusions_Error*` 一一对应 `IndexRuleError` 的六个非 None 取值。解析不出来的路径规则按无效处理，添加与行内编辑都拦（`App/ViewModels/Settings/ExclusionSettingsViewModel.cs:239/340`）。
- 输入框也认档位前缀，`!` 排除、`-` 低、`+` 高，写了前缀以前缀为准，下拉只在无前缀时提供默认档位。
- 高优先行在操作图标前多一个置顶勾选框（`Templates.xaml` 显示行，图标 E718，tooltip `Exclusions_AlwaysTopTooltip`）：`IsEnabled`/`Visibility` 都绑 `CanPin`，档位改成低/正常/排除时 `ExclusionRuleItem.Effect` 的 setter 立即清掉 `AlwaysTop` 并隐藏勾选框。编辑行不提供勾选框，改回高档后要重新勾。批量文本不编码 pin（没有新前缀），但「应用到列表」对文本未变的 High 规则按 `Value` 保留原勾选；新增与改过文本的规则一律不带 pin。页面说明区多一行 `Exclusions_AlwaysTopHint`。
- 档位筛选是一排计数按钮（全部、排除、低、正常、高），列表用 `ListCollectionView`：`Filter` 按当前档位，`CustomSort` 按判型组序（完整路径在前，通配符次之，正则最后）加组内 `OrdinalIgnoreCase` 字母序（`App/ViewModels/Settings/ExclusionRuleItem.cs:168-179`），跟档位无关。计数汇总给 `Exclusions_CountSummary`。
- 编辑在行内完成，回车保存、Esc 取消，编辑态旁边出现档位下拉（绑定 `EditEffectValue`），文本与档位一起写回或一起丢弃。删除没有二次确认，跟现有一致。不做拖拽换档，WPF 拖拽成本高、容易误操作，列表排序又与档位无关。把行内档位徽章做成按钮（点击弹出四档菜单）的一步换档留给「确实频繁逐条整理」的时候，没做。
- 筛选态下把条目移出当前档，它会立刻从列表消失，状态行给 `Exclusions_MovedTemplate`（「规则已移到「X」，当前筛选看不到它」）并更新各计数；切到「全部」能在原排序位置看到它。移入或移出排除才触发网络索引刷新与扫描盘重建。
- 批量编辑区常驻在卡片下方（`ExclusionsTab.xaml:190-205`）。「从列表生成」把全部行导出成带前缀的文本，「应用到列表」整体替换。任一行无法解析就整批不生效，状态行列出行号（`Exclusions_BulkInvalid`），文本留在框里等修改，半应用的粘贴比被拒的粘贴难发现。行首的 `\` 只在它确实转义 `!`、`-`、`+`、`\` 时才剥掉，`\\server\share` 这样的 UNC 保留两个前导分隔符（`ExclusionSettingsViewModel.cs:465-492`）。
- 与现有规则文本忽略大小写相等时拒绝，给 `Exclusions_Duplicate`。
- 空列表提示 `Exclusions_Empty`（「还没有规则。」）绑的是规则总数（`ExclusionsTab.xaml:181`），筛选后为空不出现这句，只剩计数为 0。
- 图标沿用现有字体：完整路径 E8B7，通配符与正则 E8A5，警告 E7BA（`Templates.xaml:16-67`，模板键 `IndexRuleItemTemplate`）。
- 设置页搜索有五条入口（`App/Helpers/SettingsSearchIndex.cs:62-66`）：`Settings_Exclusions` 跳到本页并把筛选设为全部，另有 `Exclusions_SearchExclude`、`Exclusions_SearchLow`、`Exclusions_SearchNormal`、`Exclusions_SearchHigh` 四条按档位跳转。原来指向三个子页签的跳转项已删除。

文案在 `Plugins/CoreExtensions/Resources/Translations/<lang>/App.json`，7 份语言目录是 en-US、es-ES、ja-JP、ko-KR、zh-CN、zh-HK、zh-TW，各 51 个 `Exclusions_*` 键（zh-CN 见 `App.json:373-422`），漏键会把键名直接显示出来。页签名 `Settings_Exclusions` 从「排除规则」改成「索引规则」，`Exclusions_CardTitle` 改成「规则列表」，说明文字的主语从排除换成「一条规则由匹配方式和档位组成」。`General_ResultTypeOrderTitle` 与 `General_ResultTypeOrderHint` 保留原键不动，新说明只引用它。

zh-CN 的五段主文案（其余 6 语言保持同义，中文按口语重写）：

> 搜索时套用这些规则：排除的不出现，低/正常/高只影响先后。本地盘在搜索时才套用；网络盘建索引时就跳过排除项，应用后自动刷新。
>
> 一条规则由匹配方式和档位组成。匹配方式决定命中什么：完整路径覆盖该目录及全部子项，通配符按名称或相对路径匹配，正则两端用 / 包住。档位决定怎么处理：高、正常、低影响排序，排除则完全不显示。
>
> 档位只改排序：高优先级排在正常前面，正常排在低优先级前面；同一文件命中多档时，高 > 低 > 正常。排除优先于任何优先级，被排除规则盖住的优先级不会生效。
>
> 程序（快捷方式）的优先级不在这里调：到 设置 → 通用 → 快速搜索窗口 → 结果类型优先级。便携版程序的快捷方式还要在 设置 → 插件 → 核心功能扩展插件 → 自定义应用搜索文件夹 里加目录；也可以在快速搜索窗口输入 `set 自定义应用` 直接跳过去。
>
> 能用通配符就别写正则：正则每次匹配都要解释执行，更费资源。

## 踩坑与裁定

踩过的坑，含修复思路。

1. 高档目录在快速窗口整批看不见（用户反馈 m01576）。`D:\RegCoolX64` 设为高档后完整窗口正常，快速窗口搜 `readme` 看不到 `Readme.txt`。原因在排序之前，引擎按名称匹配质量截断候选池（`Core/IndexV2/Search/NameSearch.cs:80` 的 limit 闸门）。快速窗口默认向 App 要 51 条，`SearchService` 乘 4 后以 204 条发给本地管道（`Core/Services/Search/SearchService.cs:51`），所以那道闸门实际是 204；`README` 的整名匹配排在 `Readme.txt` 的前缀匹配之前，被高档目录保住的行落在池子尾部，还没轮到 App 排档位就被丢掉。多盘时 `SearchCoordinator` 还会用不带优先级/历史的 `SearchResultRankComparer.Instance` 做一次全局 `Take(limit)`（`SearchCoordinator.cs:68`），优先级同样在截断之后才生效。完整窗口的 limit 是 `int.MaxValue`，那道闸门对它不关闭（`NameSearch.cs:94` 注释），所以同一条规则在那边看起来正常。
2. 补搜的修法。`ExclusionRuleSet.HighPriorityRoots`（`Core/ExclusionRuleSet.cs:233`）交出高档完整路径规则解析出的目录，走 `SearchExecutionEngine`（`:149`）到 `SearchStreamRenderer`（`:225`）的客户端在正常查询之外，对每个目录各发一次限定目录的搜索，App 侧请求上限 50 条（`SearchStreamRenderer.cs:35`），管道侧同样乘 4 变成 200 条候选。结果并进同一结果流，由 App 统一按档位排序。补搜与主查询同时发起，不排在主查询后面；单个目录搜索失败会被吞掉，一条坏规则不该连累整次查询的正常结果。快速/内联窗口的常规池同时从 51 加宽到 1000（`App/ViewModels/Search/Dispatch/SearchDispatchController.cs:196`，常量在 `SearchViewModel.cs:28`），管道侧相应变成 4000。补搜的跳过条件只有三个：`HighPriorityRoots` 为空（没有高档完整路径规则；只有 glob/正则高档规则，或高档路径规则全是盘根时也为空）、绕过排除、以及带文件夹的显式文件过滤范围。路径模式不在这层判断，仍会发起补搜；它只是在 `SearchService` 里顺带绕过排除（`SearchService.cs:82`）。
3. 补搜只对完整路径型高档规则有效。glob 与正则描述的是名字形状，没有可限定的子树，它们的行仍靠加宽的池子。盘根规则被排除在 `HighPriorityRoots` 之外：把整块盘设为高等于没有优先级，还让每次查询多扫一遍（测试 `HighPriorityRoots_SkipsDriveRoots`）。
4. 「服务端完全不截断」这条路试过，实测（2026-10-19）撑不住：`--search a --limit int.MaxValue` 要 27 秒、308 MB，`readme` 要 6.5 秒、23 MB，逐键输入不可用。不截断会让引擎算完全部命中并走整索引的路径兜底。最后取「优先级目录豁免加宽常规池」，用户关心的行不受池限制，池只约束普通结果；显示层的截断仍在 App 排序之后，默认显示 50 条候选行（普通查询的 51→204 预算链与各路径的上限见 search-ranking.md 的「候选预算与显示截断」）。
5. glob 的匹配超时原来定在 50ms，跟正则同一量级，普通匹配在机器忙时就能撞上（当时正在并行跑十几套测试程序集）。`NetworkGlobPattern.IsMatch` 把超时当 false 处理（`GlobMatcher.cs:74`），负载下一排除规则会悄悄不再排除、一条忽略模式会悄悄不再忽略，只留一行日志。现在兜底提到 1 秒（`GlobToRegex.cs:17`）：它只需长到普通匹配碰不到，真正跑飞的 pattern 第一份输入就会超时，之后本会话放弃该 pattern（`GlobMatcher.cs:88`）。优先级与排除用的正则仍按 50ms。
6. 上溯祖先链要迭代并检查 `parent.Length >= current.Length`。父路径不严格变短就不是父路径，当成父路径是这套代码里无界遍历的来源；列表写法也不会像递归那样撑爆栈（`Core/ExclusionRuleSet.cs:458-501`、`:283`）。
7. 路径型优先级规则只认整层相等。实施时把「祖先任意层命中即算当层」改掉了，因为那样低档祖先会把更近的正常档覆盖掉，跟「最近祖先胜出」自相矛盾。留在代码里的形态是 `PathPriorityAt` 精确匹配（`:314`）。
8. `IndexRuleSyntax.TryResolvePath` 不拒绝通配符，因为 `Path.GetFullPath` 本身允许 `*`。它由调用方只对判为完整路径的规则调用（`Core/ExclusionRuleSet.cs:70`、`ExclusionSettingsViewModel.cs:239`），测试相应写成「绝对路径可解析，通配符保持原样」。
9. 三个窗口的档位位置不一样。快速窗口把它压在 `TypeRank` 之前（`RankedCandidateOrdering.cs:55`），同一档位内先类型后质量；完整窗口把它放在 `SearchResultRankComparer` 的第一位，但完整窗口**没有** `TypeRank`/结果类型分组，`Core/SearchResult.cs:102` 的「结果类型分组在其上」是改造前的注释，与实现不符；内联窗口的全局段用快速梯子但类型层同值，当前文件夹段按邻近度排在全局段之前且不单独查档位（`InlineListSearchHelper.cs:97-109`、`ExplorerSearchHelper.RankMatches` 不传 resolver）。所以「高档文件与普通应用行谁在前」只在快速窗口有可比性：完整窗口没有应用行，内联窗口先看目录邻近度。两处注释还留着改造前的说法：`App/ViewModels/Search/Mapping/SearchResultMapper.cs:128` 写类型顺序是最外层键，`Core/Settings/IndexRuleSetting.cs:7` 写档位在「其结果类型分组内」移动，都与实现和 `RankedCandidateOrdering.cs:9-25` 的头部注释相反，以代码为准。
10. 优先级查询没有单独做基准门槛。原计划给 `GetPriority` 定「20 万条合成路径单线程 < 50ms」，超限时降级为「只对前 N 条算优先级，其余按正常档」；落地后靠逐目录缓存与每次查询的字典，降级方案没有实施。
11. 设计稿里有四条说法与实现不符，留在这里说明以哪边为准。
    - 「应用行（无文件路径的插件行）取正常档，不查规则集」。搜索可搜项/应用行确实进入排序，并按它的快捷方式或可执行文件路径求值（`App/ViewModels/Search/Mapping/SearchResultMapper.cs:250-257`）；只有进入排序且路径为空的候选才在 `GetPriority` 里直接拿正常档（`Core/ExclusionRuleSet.cs:237`）；没有规则命中的路径同样返回正常档。插件即时结果、插件搜索动作行、完整窗口的内容 provider 行则根本不参与排序（快速/内联窗口是列表顶部前缀，完整窗口是内容前缀），所以它们不是「拿到正常档」，而是档位没有出手的机会。
    - 「`shell:AppsFolder\AUMID` 这类没有文件系统对应的虚拟路径，保存时允许、UI 标注对文件搜索不生效、运行期忽略并记日志」。实际是输入即拒，报 `VirtualPathWithoutFileSystem`（`Core/Settings/IndexRuleSetting.cs:181`），规则进不了列表。
    - 「同一路径同时命中高与低两条优先级规则时给提示，但不算错误」。冲突检测只做优先级与排除的配对（`Core/Settings/IndexRuleConflict.cs:39-47`），高与低重叠静默按高处理。
    - 「改规则不触发本地重建」。排除子集变化会重建非 NTFS 的扫描式本地盘（`App/ViewModels/Settings/SettingsViewModel.cs:302`），只有 MFT/USN 索引的盘不重建。
12. 迁移里「用户从未改过」的判断走的是集合语义比较（每项清洗，分隔符统一，去尾部分隔符，排序后忽略大小写逐项比），并且要求旧正则列为空（`Core/Settings/IndexRuleMigration.cs:101-118`，测试 `Ensure_UntouchedDefaults_AdoptsTheNewDefaults`）。
13. 空列表提示绑规则总数而不是当前可见行数，筛选后为空时只剩计数为 0。这是设计阶段就知道的取舍，写在这里免得被当 bug 修掉。

裁定写在这里，都是明确不做的范围。

- 不做每条规则的启用/禁用开关，删除即可。
- 不做拖拽排序；列表顺序固定为「完整路径 → 通配符 → 正则」加组内字母序。
- 不把用户输入的通配符自动改写成正则，也不自动补 `**`。
- 不把优先级写进索引记录，那会让改设置触发重建。
- 不改本地盘全量建索引的策略，也不改网络盘遍历期排除的语义。
- 不给应用或开始菜单项接入独立的文件优先级，程序先后仍由「结果类型优先级」控制。
- 不新增第三方依赖，不引入新的匹配器实现。
- 不做跨设备或云同步。
