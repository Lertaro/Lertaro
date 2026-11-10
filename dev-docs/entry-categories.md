# 分类与筛选

分类的唯一表在 [SearchCategoryClassifier](../PluginSdk/Helpers/SearchCategoryClassifier.cs)：枚举、扩展名集合、名称分类、结果分类和默认 glob 都从这里派生。它位于所有消费者均能引用的 PluginSdk，避免 Core、App 和插件各维护一份表。

## 一份配置，两种入口

`CustomFilters` 是用户可编辑的统一列表，包含 `Enabled / Keyword / Label / Rule / ShowAsRow / Icon`。输入 Token 前缀加关键词（默认前缀 `\`）使用启用项；`ShowAsRow` 决定是否同时出现在完整窗口侧边栏和快速窗口类型筛选栏。

`CustomFilterQueryTokenProvider.MergeSidebarRowsIntoFiltersOnce()` 在读取与设置页构建时合并旧 `SidebarCustomFilters`。同关键词启用现有项，其余保留规则、图标与显示名，随后删除旧键。迁移须幂等，不能依赖哪个调用方先读。

## 截断前过滤

- `SidebarFilterItem.Category` 是可下推的类别身份。内建分类在名称、路径、路径补全、网络索引和实时匹配中传递；11 月 10 日已修复漏传分支。测试必须覆盖小 limit、大量不合格候选及 base/delta/OR 分支，单看最终列表纯净无法证明没有漏召回。
- 自定义规则行没有类别身份，仍逐行处理已返回候选。不能从显示名或 `Id` 猜类别，也不能把所有插件规则都说成已下推。
- 快捷方式在引擎侧放行，由 App 根据目标分类；引擎没有解析目标的能力。提前剔除会丢失开始菜单应用。
- 名称扫描使用裸名称，避免每个候选都沿父链重建路径。需要路径信息的判断应留在有该信息的层。
- `.ts` 当前归视频，这是分类表的既有约定。新增扩展名只改统一表，不手抄到设置默认值。

“全部”的 `ActiveCategoryFilter` 必须为 null；传一个永远返回 true 的谓词仍会被当成活动筛选，改变显示上限。快速窗口标签按 `Id` 保持选择；`RememberCategoryFilter` 只记当前会话，重启后回到全部。

## 协议经验

[SearchRequestBinarySerializer](../Core/Wire/SearchRequestBinarySerializer.cs) 当前为 v12，`Category` 占一个 flag byte，版本不同直接拒绝解析。新增字段时同时修改长度预估、写入、读取和版本，不能只做往返测试。

长度预估错误可能被字符串预留空间和池化缓冲区掩盖：实际帧长按写入偏移重算，并不代表预租大小正确。协议测试应覆盖边界长度与字段值。

界面术语固定为“类型筛选栏”“标签”“侧边栏筛选”；“分类”指数据身份，“档位”留给索引规则优先级。
