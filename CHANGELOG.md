# 更新日志

<!--
格式：一条一行、符号标性质、中英双份、外层用代码块包住。
符号：* 核心功能　+ 小功能添加　# 修复。只写"改了什么"。
-->

## v5.11.1.3

## 更新内容

```
* 快速搜索可用 Tab / Shift+Tab 循环切换分类标签
+ 分类标签在结果出现时显示，没有结果时隐藏，并移到列表下方
# 同一程序的多个快捷方式合并为一条，不再一个应用占多行
# 搜索 lnk 时不合并，便于查看全部快捷方式
+ 自动更新只检查本仓库的发行
+ 保留鼠标平滑滚动
```

## What's changed

```
* The quick window cycles its category filters with Tab and Shift+Tab
+ The category bar appears with the results, stays hidden when there are none, and sits under the list
# Several shortcuts to one program collapse into a single row
# Searching for lnk skips that collapse, so every shortcut can be seen
+ Auto-update only checks this repository's releases
+ Keeps the smooth wheel scrolling
```

## v5.11.1.2

## 更新内容

```
* 快速搜索新增分类筛选：全部、文件夹、应用、设置、文档、图片、音频、视频
+ 引擎按分类检索，切到某一类时返回该类的全部命中，不再被综合排序挤出
# 修复类型顺序排在匹配质量之后才比较，用户设置的类型先后被忽略
# 修复系统设置项被归入应用分类
# 修复分类结果被重复过滤而丢失行
+ 自动更新只检查本仓库的发行
+ 保留鼠标平滑滚动
```

## What's changed

```
* The quick window gains category filters: all, folders, applications, settings, documents, images, audio, video
+ The engine searches by category, so a filter returns every match of that kind instead of being crowded out by the combined ranking
# Fixed the type order being compared only after match quality, which ignored the order the user set
# Fixed system settings entries being filed under applications
# Fixed category results being filtered twice and losing rows
+ Auto-update only checks this repository's releases
+ Keeps the smooth wheel scrolling
```

## v5.11.1.1

## 更新内容

```
# 排序改为命中位置优先：左侧、连续、覆盖率决定顺序，使用次数与类型排在其后
# 修复常用文件被当作纯历史行而丢失命中位置，排在只在中段匹配的名字之后
# 修复应用结果从别名取匹配位置，与文件行不在同一标准上比较
# 历史权重改为以打开次数为主，时间只占很小比例且不会归零
# 拼音读音不再自带权重，匹配方式不参与排序
* 同名快捷方式被去重，其打开次数转移给目标文件
+ 内联搜索可用单个字母通过拼音首字母命中中文名
+ 自动更新只检查本仓库的发行
+ 保留鼠标平滑滚动
```

## What's changed

```
# Ranking orders by match position first: left edge, contiguity and coverage decide, usage and type order follow
# Fixed a frequently used file losing its match position after being flagged history-only, which sorted it below names the query only touched mid-word
# Fixed applications taking their match position from an alias, so they were compared on a different key than file rows
# History weight now follows the open count, with time trimming only a small share and never retiring a habit
# Pinyin readings no longer carry weight of their own, so how a row was reached does not order it
* Same-named shortcuts are de-duplicated and their opens move onto the target
+ The inline card reaches han-character names from a single letter through the initials spelling
+ Auto-update only checks this repository's releases
+ Keeps the smooth wheel scrolling
```

## v5.10.0.3

## 更新内容

```
# 合并上游对查询缓存的加固：缓存页面挂在产生它的索引上、单页上限 512 行、按请求长度裁剪
# 修复索引预热存下的页面比客户端请求短而被拒绝，导致预热没有生效
# 修复服务在开机时启动失败后不再重试，需要手动去设置里启动
# 修复结果列表在非流式来源替换后仍沿用上一次的刷新判断
* 移除未接入任何路径的持久化结果缓存，不再向用户目录写文件
+ 自动更新只检查本仓库的发行
+ 保留鼠标平滑滚动
```

## What's changed

```
# Merged upstream hardening of the query page cache: a page belongs to the index that produced it, is capped at 512 rows and is trimmed to the request
# Fixed the index warm-up storing pages shorter than the client asks for, which made the warm-up a no-op
# Fixed the service not retrying after a failed start at boot, which needed a manual start from settings
# Fixed the result list keeping the previous refresh decision after a non-streamed replacement
* Removed the persisted result cache that nothing read, so nothing writes to the user profile any more
+ Auto-update only checks this repository's releases
+ Keeps the smooth wheel scrolling
```

## v5.10.0.2

## 更新内容

```
# 修复 Directory Opus 弹出右键菜单时内联搜索抢走按键，导致菜单快捷键失效
+ 自动更新只检查本仓库的发行
+ 保留鼠标平滑滚动
```

## What's changed

```
# Fix inline search taking keys while a Directory Opus context menu is open, which broke the menu's own shortcuts
+ Auto-update only checks this repository's releases
+ Keeps the smooth wheel scrolling
```

## v5.10.0.1

## 更新内容

```
* 合并上游 v5.10.0
* 排序改为：收藏/搜过 → 类型顺序 → 左侧位置 → 覆盖率 → 历史次数 → 索引规则 → 最近文件（同类型内优先）→ 其余匹配质量
* 搜索结果支持像素级平滑滚动
# 修复内联搜索无法用拼音首字母定位当前目录的文件
# 修复内联搜索结果中文件夹未成组排在文件之前
# 修复重复搜索时结果列表被反复重建造成的闪烁
# 修复窗口隐藏后图标缓存被清空导致的图标空白
# 修复结果行图标缓存键不一致导致的重复加载
# 提升结果列表的渲染速度
# 修复快速搜索第一个字偶尔不显示结果
# 提升重复查询的响应速度
+ 自动更新只检查本仓库的发行
+ 保留鼠标平滑滚动
```

## What's changed

```
* Merged upstream v5.10.0
* Ranked by favorites/searched-before, result type, left position, coverage, history count, index rules, recent files (leading their type), then the remaining match quality
* Smooth pixel-level scrolling for search results
# Fix inline search not locating current-folder files by pinyin initials
# Fix folders not grouping ahead of files in the inline search results
# Fix the result list being rebuilt and flickering on repeated searches
# Fix icons going blank after the search window hides
# Fix duplicate icon loads caused by mismatched cache keys
# Faster result list rendering
# Fix the result list occasionally staying empty on the first keystroke
# Faster response for repeated queries
+ Auto-update only checks this repository's releases
+ Keeps the smooth wheel scrolling
```


## v5.9.8.1

## 更新内容

```
* 合并上游 v5.9.8（窗口不透明化、排版圆角与焦点框、内联卡片复用与尺寸、索引规则优先级与统一规则设置页、安装器 CET 修复、右键 logo 居中）
* 排序改为：收藏/搜过 → 类型顺序 → 左侧位置 → 覆盖率 → 历史次数 → 索引规则 → 最近文件（同类型内优先）→ 其余匹配质量
+ 自动更新只检查本仓库的发行
+ 保留鼠标平滑滚动
```

## What's changed

```
* Merged upstream v5.9.8 (opaque window surfaces, typography and focus rectangles, inline card reuse and sizing, index rule priorities with a unified rules editor, installer CET fix, right-click logo to center)
* Ranked by favorites/searched-before, result type, left position, coverage, history count, index rules, recent files (leading their type), then the remaining match quality
+ Auto-update only checks this repository's releases
+ Keeps the smooth wheel scrolling
```


## v5.9.7.2

## 更新内容

```
* 排序改为：左侧连续 → 连续度 → 收藏/历史 → 类型顺序 → 覆盖率 → 档位 → 目录优先 → 名字序 → 名字长度
+ 自动更新只检查本仓库的发行
+ 保留鼠标平滑滚动
```

## What's changed

```
* Ranked by left-contiguous hit, contiguity, history, type order, coverage, tier, folder, natural name order, then name length
+ Auto-update only checks this repository's releases
+ Keeps the smooth wheel scrolling
```


## v5.9.7.1

## 更新内容

```
* 合并上游 v5.9.7（拼音读音改为实时匹配、读音表按词频、索引快照 v13，剪贴板文本与截图，内联卡片定位与焦点修复）
* 排序改为：左侧连续 → 连续度 → 收藏/历史 → 覆盖率 → 档位 → 类型顺序 → 目录优先 → 名字序 → 名字长度
+ 完整搜索窗也按左侧命中优先排序（形状、时间、深度不再参与）
+ 多词查询保留左侧与覆盖率信号，不再算作连续命中
+ 混拼查询里只有韵母的片段不参与匹配
+ 自动更新只检查本仓库的发行
+ 保留鼠标平滑滚动
```

## What's changed

```
* Merged upstream v5.9.7 (live pinyin reading matching, frequency-ordered reading table, index snapshot v13, clipboard text and screenshots, inline card locate and focus fixes)
* Ranked by left-contiguous hit, contiguity, history, coverage, tier, type order, folder, natural name order, then name length
+ The full search window orders by left-side match position too (shape, recency and depth no longer take part)
+ A spaced query keeps its left-side and coverage signal instead of counting as a contiguous hit
+ A final-only piece no longer takes part in a mixed pinyin query
+ Auto-update only checks this repository's releases
+ Keeps the smooth wheel scrolling
```

## v5.9.6.1

## 更新内容

```
* 合并上游 v5.9.6（拼音简拼与全拼混搜、Everything 目录大小性能、搜索排序与提示、设置与权限修复）
+ 左侧优先恢复为排序的首要匹配键（上游新增的形状、时间、深度不再抢先）
# Ctrl+G 恢复记忆优先：跳到当前在用的文件管理器目录，桌面不算文件管理器
+ 自动更新只检查本仓库的发行
+ 保留鼠标平滑滚动
```

## What's changed

```
* Merged upstream v5.9.6 (mixed pinyin abbreviations and full readings, Everything folder-size performance, search ranking and tips, settings and permissions fixes)
+ Left-side match priority leads the ladder again (upstream's shape, recency and depth keys no longer outrank it)
# Quick Switch prefers the folder you were working in, and the desktop is not a file manager
+ Auto-update only checks this repository's releases
+ Keeps the smooth wheel scrolling
```

## v5.9.1.7

## 更新内容

```
+ 启动后空闲时预热搜索
+ 连续搜索复用历史与收藏候选（一秒内不重算）
+ 自动更新只检查本仓库的发行
+ 保留鼠标平滑滚动
```

## What's changed

```
+ Search is warmed up while the app is idle after startup
+ Consecutive searches reuse the history and favorite candidates for a second
+ Auto-update only checks this repository's releases
+ Keeps the smooth wheel scrolling
```

## v5.9.1.6

## 更新内容

```
# 关于页显示完整的四段版本号
+ 启动后空闲时预热搜索
# 热路径日志不再先拼字符串
+ 快速导航的图标改在渲染线程构建
+ 级联菜单的图标改在渲染线程构建
+ 自动更新只检查本仓库的发行
+ 保留鼠标平滑滚动
```

## What's changed

```
# About shows the full four-part version
+ Search is warmed up while the app is idle after startup
# Hot-path log lines are no longer built before the level check
+ Quick-nav icons are built on the rendering thread
+ Cascade-menu icons are built on the rendering thread
+ Auto-update only checks this repository's releases
+ Keeps the smooth wheel scrolling
```

## v5.9.1.5

## 更新内容

```
# 修复 Quick Switch（Ctrl+G）在应用刚启动、以及开着多个文件管理器窗口时切不过去
# 目录跳转与内联搜索统一按"最后激活的文件管理器目录"判定（桌面不再被当作文件管理器）
+ 内联窗口不再重复显示「上次目录」（Ctrl+G 已经负责跳回上次目录）
+ 自动更新只检查本仓库的发行
+ 保留鼠标平滑滚动
```

## What's changed

```
# Quick Switch (Ctrl+G) works right after the app starts and with several file-manager windows open
# Folder switching and inline search share one "last activated file manager folder" resolution (the desktop is not one)
+ Inline search no longer repeats a "last directory" row (Ctrl+G already jumps back there)
+ Auto-update only checks this repository's releases
+ Keeps the smooth wheel scrolling
```

## v5.9.1.4

## 更新内容

```
# 修复 Quick Switch（Ctrl+G）在应用刚启动、以及开着多个文件管理器窗口时切不过去
+ 内联搜索的已打开文件夹按最近激活排序（Ctrl+1 是最新激活的那个窗口）
+ 自动更新只检查本仓库的发行
+ 保留鼠标平滑滚动
```

## What's changed

```
# Quick Switch (Ctrl+G) works right after the app starts and with several file-manager windows open
+ Opened folders in inline search are ordered by most recently activated (Ctrl+1 is the latest window)
+ Auto-update only checks this repository's releases
+ Keeps the smooth wheel scrolling
```
# 更新日志

<!--
格式：一条一行、符号标性质、中英双份、外层用代码块包住。
符号：* 核心功能　+ 小功能添加　# 修复。只写"改了什么"。
-->


## v5.9.1.3

## 更新内容

```
# 搜索恢复原有速度（撤销查询路径上的权限检查，上游 61cfcc1c）
# 快速窗口恢复显示常用项、收藏与开始菜单应用
+ 打字时搜索结果刷新更快（候选项每次搜索只算一遍）
+ 自动更新只检查本仓库的发行
+ 设置里的插件列表首次滚动不再卡一下
+ 保留鼠标平滑滚动
```

## What's changed

```
# Search speed restored (the permission check on the query path is reverted, upstream 61cfcc1c)
# Quick window shows frequently used, favorite and Start Menu entries again
+ Faster result refresh while typing (the candidate rows are built once per search)
+ Auto-update only checks this repository's releases
+ The first wheel scroll over the plugin list no longer stalls
+ Keeps the smooth wheel scrolling
```

## v5.9.1.2

## 更新内容

```
# 修复：Quick Switch（Ctrl+G）跳回文件管理器目录时用的是上一次的路径
+ 保留鼠标平滑滚动
```

## What's changed

```
# Fix: Quick Switch (Ctrl+G) navigated to the previous folder instead of the one the file manager shows
+ Keeps the smooth wheel scrolling
```

## v5.9.1.1

## 更新内容

```
+ 保留鼠标平滑滚动（基线：上游 v5.9.1）
```

## What's changed

```
+ Keeps the smooth wheel scrolling (base: upstream v5.9.1)
```

## v5.9.0.1

## 更新内容

```
* 平滑滚动：模型 3.0 + 应用侧规则（速度预算决定行程、两条轴固定窗口、TopSpeedMul = 1.0）
+ 触控板不缓动，按 TouchpadSpeed 单独降速
+ 设备分类：OS 触摸标记优先，其次按子格幅值的规律性
+ 新增显式标记：命名事件 Local\Lertaro.SmoothScroll.Active，平滑滚动启用期间存在
```

## What's changed

```
* Smooth wheel scrolling: model 3.0 plus the app-side rules (the speed budget sets the travel, two axes
  with fixed windows, TopSpeedMul = 1.0)
+ A touchpad is not eased; it is scaled down by TouchpadSpeed
+ Device classification: the OS touch marker first, then the regularity of the sub-notch magnitudes
+ New explicit marker: the named event Local\Lertaro.SmoothScroll.Active, present while smooth scrolling is on
```

## v5.8.5.1

## 更新内容

```
* 平滑滚动：模型 3.0 + 应用侧规则（速度预算决定行程、两条轴固定窗口、TopSpeedMul = 1.0）
+ 触控板不缓动，按 TouchpadSpeed 单独降速
+ 设备分类：OS 触摸标记优先，其次按子格幅值的规律性
+ 新增显式标记：命名事件 Local\Lertaro.SmoothScroll.Active，平滑滚动启用期间存在
```

## What's changed

```
* Smooth wheel scrolling: model 3.0 plus the app-side rules (the speed budget sets the travel, two axes
  with fixed windows, TopSpeedMul = 1.0)
+ A touchpad is not eased; it is scaled down by TouchpadSpeed
+ Device classification: the OS touch marker first, then the regularity of the sub-notch magnitudes
+ New explicit marker: the named event Local\Lertaro.SmoothScroll.Active, present while smooth scrolling is on
```

## v5.8.4.2

## 更新内容

```
* 平滑滚动改用模型 3.0（收多少发多少，只把时间摊开）
+ 新增应用侧规则：速度预算决定一格走多远、窗口长度由 release 模型决定、缓动形状用应用自己的规则
+ 滚动的接递改成两条轴（单格 / 滚动中），各自固定窗口长度
+ 触控板不缓动，按 TouchpadSpeed 单独降速
+ 新增设备分类：OS 触摸标记优先，其次按子格幅值的规律性
# 修复 App 读不到 machine-settings.json（改由服务经管道提供）
+ 版本号 v5.8.4.2（不高于上游已发布的版本线）
```

## What's changed

```
* Smooth wheel scrolling now uses model 3.0 (take how much, give how much, spread over a stated time)
+ New app-side rules: the speed budget sets how far a notch travels, the release model sets the window
  length, and the payout shape is the app's own
+ The scroll delivery now runs two axes (lone / rolling), each with a fixed window length
+ A touchpad is not eased; it is scaled down by TouchpadSpeed
+ New device classification: the OS touch marker first, then the regularity of the sub-notch magnitudes
# Fixed the App being unable to read machine-settings.json (the service hands its copy over the pipe)
+ Version v5.8.4.2 (never above the released upstream line)
```

## v5.8.4.1

## 更新内容

```
* 平滑滚动恢复为应用级缓动（撤销上游"改回原生滚轮"那一版）
+ 甩动速度上限调低（两次各减半）
# 修复 App 读不到 machine-settings.json（当时改由服务经管道提供）
```

## What's changed

```
* Smooth wheel scrolling restored as an app-wide behaviour (upstream's "restore native wheel scrolling" is reverted)
+ The spin's speed ceiling is halved (twice)
# Fixed the App being unable to read machine-settings.json (the service handed its copy over the pipe at the time)
```

## v5.8.2.1

## 更新内容

```
+ 内联搜索：召唤时读实时目录，目录到位后重跑搜索
+ 内联搜索：结果里直接子项靠左命中优先，同位置时目录优先
+ 对话框跟随：打开/保存对话框自动定位到当前文件夹
+ 定位：非资源管理器宿主走它自己的适配器
+ Core 测试工程改用 Microsoft.Testing.Platform
```

## What's changed

```
+ Inline search: reads the live folder when summoned and re-runs the search once it arrives
+ Inline search: a direct child that matches at the left wins, and a folder wins at the same position
+ Dialog follow: an open/save dialog locates to the current folder by itself
+ Locating: a non-Explorer host goes through its own adapter
+ The Core test project runs through Microsoft.Testing.Platform
```
