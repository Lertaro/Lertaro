# 设置保存、权限与数据目录

字段校验由 [SettingsValidationGate](../App/ViewModels/Settings/SettingsValidationGate.cs) 汇总，数据目录写权限由 [UserDataAccess.Verify](../Core/Services/Installation/UserDataAccess.cs) 的实际写探针判断。安装与便携目录加固分别在 [InstallDirectoryLock](../Core/Services/Installation/InstallDirectoryLock.cs)、[PortableDirectoryLock](../Core/Services/Installation/PortableDirectoryLock.cs)。

## 搜索配置的保存与迁移

| 情况 | 当前处理 |
| --- | --- |
| 新输入的非法非空前缀或触发词 | 拒绝保存，字段显示原因，状态栏显示阻塞错误数 |
| 未改动的非法旧值 | 字段提示问题，允许保存其他设置 |
| 空插件标记前缀 | 保存为默认前缀；运行时不可用的旧前缀也回退为默认值 |
| 标记 `RequireNonEmpty` 的插件字段被清空 | 删除保存的覆盖值，恢复 schema 默认值 |
| 不可用的结果类型触发符 | 启动时移除并保存，回到无触发符的默认行为 |
| 废弃的 CoreExtensions `CustomFilterPrefix` | 启动通知流程清理该键；与宿主前缀不同则提示，插件统一读取宿主值 |
| 旧插件触发词或冲突的宿主前缀 | 保留保存值并给出一次启动提示；插件触发词按 schema 标记识别 |

保存门禁只读取已创建页面和已加载子字段。页面自行计算的错误要主动汇总；这些错误不会自动触发 WPF 的 `Validation.Error`。门禁的绑定属性公开并发出 `PropertyChanged`，确保拒绝保存的状态可见。语法保留字符及 SDK 兼容见 [search-grammar.md](search-grammar.md)。

回归入口：[QueryTokenPrefixRulesTests](../Tests/App/Helpers/QueryTokenPrefixRulesTests.cs)、[SettingsValidationGateTests](../Tests/App/ViewModels/Settings/SettingsValidationGateTests.cs)、[LegacySettingsAdvisorTests](../Tests/Core/Settings/LegacySettingsAdvisorTests.cs)、[PluginTriggerKeywordMigrationTests](../Tests/App/Helpers/PluginTriggerKeywordMigrationTests.cs)。

## 访问失败的诊断顺序

1. 确认目标进程令牌、目录 DACL 与完整性标签，并核对实际访问路径。构建目录标签、沙箱权限和用户会话按本次环境记录。
2. 对实际数据根验证读、创建、替换和备份路径。设置文件 mtime 未变时，先确认保存是否触发。
3. 分别验证服务双向管道、注册表、证书私钥库和临时目录，见 [windows-integration.md](windows-integration.md)。
4. 受限令牌问题使用同令牌的产品进程验证。

## 目录修复的取舍

旧数据目录降 Low 标签机制已删除：受限进程无法通过双向服务管道完成该操作，提权执行又会降低用户配置与证书的完整性保护。当前写探针报告不可写的路径与原因。

profile 修复仍拒绝相关链接，并保留 owner 回读等后置检查。ACL 与重解析点操作有持久影响；修改时在可抛弃环境验证实际目标、属主与失败恢复。测试 junction 清理先移除链接，再处理测试根。

安装形态影响数据根选择。将字符串路径比较改为真实路径解析会改变路径别名对应的数据目录，须验证用户能否继续读取原设置。

## 已验证的失败方式

| 问题 | 经验与现行实现 |
| --- | --- |
| LocalSend 证书加载失败 | `LocalSendCertificate.LoadFlags` 为 Exportable。2026-10-13 的本机实验中，EphemeralKeySet 避开加载故障却破坏后续 TLS；证书修改须验证握手 |
| 窗口出现但启动未完成 | `DispatcherExceptionHandler.StartupCompleted` 之前的未处理异常报告后退出，避免留下未初始化的进程 |
| Hook 启动持续失败 | `HookIpcClient` 从 5 秒倍增退避到 60 秒，成功后复位，并保留失败原因 |
| SQLite 主库能打开，全扫报错 | `ContentSearchDatabase.OpenConnection` 每个连接设置 `PRAGMA temp_store = MEMORY`，避免临时表依赖不可写的临时目录；仅在建表连接设置无法覆盖后续连接 |

`temp_store = MEMORY` 让临时候选列表占用内存；规模过大时再评估主库中的有键表。LocalSend 当前没有受限令牌专用降级分支。

日志缺席时先检查日志目录的写权限。目录修复、设置传输和安装判断需覆盖 installed/portable、链接目标及普通/提权/受限进程；回退日志位置还须控制路径与凭据的暴露。
