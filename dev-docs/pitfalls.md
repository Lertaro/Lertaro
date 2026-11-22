# 维护经验

## 先证明问题

- 先读日志和采样，再加探针。核对运行进程、版本和日志级别，用当前实现与复现判断原因。与假设矛盾的证据应推翻假设。
- 决定性的诊断使用默认开启的日志级别。插桩会改变时序，尤其是 UI 线程、锁内日志和进程级分配计数；回答完问题后移除一次性探针。
- 验证完整用户路径：证书加载后继续测 TLS 握手，集合排序后继续测窗口呈现，取消等待后确认生产者的状态。
- 放宽匹配门槛时，在全量语料中检查短词、简拼和多词查询，并覆盖长文件名。小探针用于定位；召回率与噪声要按实际语料测量。
- 移除疑似多余依赖后执行真实路径。性能比较同时核对结果条数、内容和顺序，避免以漏结果换取低耗时。

## 构建与验证

- [Lertaro.slnx](../Lertaro.slnx) 包含产品工程，测试入口是 [Tests/Tests.slnx](../Tests/Tests.slnx)。产品构建不会更新测试 DLL；跳过测试构建前先核对产物。
- 测试成功必须有发现数、实际用例名与结果记录。筛选未命中或空工程也可能返回退出码 0。
- STA 测试仍需隔离共享静态状态。`ExplorerStaInvokerTests` 曾并行修改弃读计数，串行后通过；此类失败先查共享状态。
- 只需 HWND 的 WPF 测试使用 `EnsureHandle()`，避免可见窗口抢前台。焦点、DPI 与宿主交互使用真机验证。
- 发布流水线负责构建与发布，本地完成受影响测试。历史测试计数保留原日期；NU1900 表示漏洞数据源请求失败，应记录具体失败。
- 行尾按 [.gitattributes](../.gitattributes) 与 [.editorconfig](../.editorconfig) 使用 LF，格式检查限定改动文件。
- 依赖版本集中在 [Directory.Packages.props](../Directory.Packages.props)。比较旧提交时保留旧项目自身的版本配置；嵌套参考 checkout 可能继承外层 CPM。

## 插件契约与加载

- App、Service 和 Hook 共用 [EnabledAssemblyFiles](../Core/Services/Plugin/Loading/ServicePluginLoader.cs) 选择 `Lertaro.Plugins.*.dll`。同名重复入口优先取较浅路径；依赖 DLL 由加载器解析，避免直接作为入口加载时的同名程序集冲突。
- Service/Hook 将入口按路径排序后注册。别名 provider 的数值 ID 取决于注册顺序，跨进程修改入口选择时核对 ID；可写入插件目录的代码按宿主权限运行。
- SDK 2.0.0 保留 `PluginConfigField.IsTriggerWord` 的 getter/setter，转发至新 `Validation`。删除旧 setter 曾使已编译插件在读取 schema 时抛 `MissingMethodException`，连带丢失设置页和默认值。新代码使用 `Validation`；旧 setter 的 false 不覆盖已有 `TokenKeyword`。
- 插件设置页构造时已填充列表，`LoadAsync` 仅在空列表时补建，避免重复行。刷新语言后保留组件开关；保存以完整组件列表为准。
- 插件依赖须进入打包清单，并从部署目录验证加载。

回归入口：[ServicePluginEntryAssemblyTests](../Tests/Core/Services/Plugin/ServicePluginEntryAssemblyTests.cs)、[PluginConfigFieldCompatTests](../Tests/PluginSdk/Abstractions/PluginConfigFieldCompatTests.cs)、[PluginManagementViewModelTests](../Tests/App/ViewModels/Settings/Plugins/PluginManagementViewModelTests.cs)。

## 实现与交付

- 优先使用标准库、平台能力和现有依赖。有意保留的上限用 `ponytail:` 写明代价和重开条件。
- 改动副作用调用顺序后，复查下游早退条件、初始化状态与资源生命周期。窗口复用会把“每窗口一次”变成“每进程一次”。
- 缓存说明身份、失效条件与所有权。复用匹配证据时仍更新 UI 行的元数据、动作参数、选择和高亮。
- 合并冲突逐块看三方内容，核对双方已修复的行为。文档按合并后的代码更新。
- 原生结构按 SDK 核对字段宽度与对齐。`READ_USN_JOURNAL_DATA` 的 Timeout、BytesToWaitFor 均为 8 字节；V0 为 40 字节，V1 尾部对齐后为 48 字节。[UsnJournalReadTests](../Tests/Core/DriveMonitoring/UsnJournalReadTests.cs) 固定大小与偏移。
- 设置和排序导出含路径、查询或令牌，进入报告与回放夹具前先脱敏。

## 文案与站点

- 用户可见名称先查语言资源，七种语言同步修改。查询语法、索引规则和快速面板来源过滤分别维护。
- NFC/NFD 标题可能产生不同锚点；标题与目录使用同一归一化规则，改锚点后构建并检查链接。
