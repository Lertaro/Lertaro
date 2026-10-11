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
