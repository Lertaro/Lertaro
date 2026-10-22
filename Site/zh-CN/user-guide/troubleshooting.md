# 故障排查

在使用 Lertaro 的过程中遇到问题？请按照以下步骤逐步排查常见故障原因。

## 1. 全局快捷键没有反应

- **检查后台服务运行状态**：进入[**设置 → 运行状态**](./settings/service-status)，确认后台索引服务处于“运行中”。虽然普通快捷键由 App 处理，但提权挂钩进程依赖后台服务拉起。
- **管理员权限隔离（UIPI）**：若当前前台活动窗口是以管理员身份运行的（例如以管理员打开的终端或任务管理器），Windows 系统的安全机制（UIPI）会阻止低权限进程向高权限窗口发送快捷键。Lertaro 会自动尝试提权快捷键监听进程；若仍无响应，请确保后台服务处于正常运行状态。
- **检查进程黑名单**：在[**设置 → 快捷键**](./settings/hotkeys-page#_3-进程黑名单-process-blacklist)中检查当前前台程序是否被列入了“进程黑名单”。处于黑名单中的程序在前台时，Lertaro 会主动放行按键不做拦截。
- **全屏游戏/独占应用放行**：当检测到前台有独占全屏应用程序（如 3D 游戏、全屏播放器）时，Lertaro 默认会自动放行快捷键以防干扰操作。如果你希望在全屏下依然能够呼出搜索框，请在**设置 → 快捷键**中勾选**全屏应用聚焦时仍然响应**。

## 2. 搜索结果未更新或过时

- **本地 NTFS / ReFS 磁盘**：Lertaro 通过读取底层 USN 变更日志实现近乎零延迟的毫秒级实时更新。
- **FAT32 / exFAT 磁盘**：通过系统文件变更事件持续监听。
- **手动重建索引**：若由于系统意外断电等原因导致索引不一致，请前往[**设置 → 索引 → 本地驱动器**](./settings/index-drives)，在对应的磁盘上点击**重建索引**即可重新生成干净的内存索引。

## 3. 网络驱动器未自动刷新

- **定时扫描机制**：网络共享（SMB / NAS）没有本地 NTFS 卷的底层变更日志，因此依赖定时轮询机制。
- **检查刷新模式**：在[**设置 → 索引 → 网络驱动器**](./settings/index-drives#_2-网络驱动器-network-drives)中检查该驱动器的刷新模式是否被误设为了“手动”；若设为手动，必须点击“重建索引”或改为定时刷新。
- **符号链接环路保护**：对于 NAS 共享中可能存在的递归符号链接，Lertaro 内置了环路检测引擎，会自动跳过死循环目录。

## 4. 某个文件或文件夹完全搜不到

- **检查排除规则**：进入[**设置 → 索引 → 排除规则**](./settings/index-drives#_5-排除规则-exclusions)，确认目标文件路径是否被精确路径、Glob 通配符或正则表达式规则意外命中。
- **检查磁盘启用状态**：在[**设置 → 索引 → 本地驱动器**](./settings/index-drives)中确认该文件所在的物理驱动器或目录是否处于开启状态。

## 5. 内嵌窗口中中文输入法候选框未弹出

- **无焦点设计原理**：[内嵌窗口](./getting-started#_3-三种窗口形态)为了在关闭后把焦点归还给宿主软件，特意采用了无焦点设计。输入法候选框依赖 Windows 真实键盘焦点，因此在内嵌窗口中可能不显示候选浮窗。
- **推荐解决方案**：
  1. **直接打拼音**：Lertaro 内置了拼音别名引擎，直接输入拼音字母即可跳跃命中中文文件名（详见[**搜索语法**](./search-syntax#_7-多语言智能别名)），无需调用输入法候选框。
  2. **切换为快速窗口**：按下双击 `Ctrl` 唤出拥有完整焦点的快速窗口，即可正常使用任何输入法。

## 6. 数据目录、权限与更新失败

- **数据目录搬迁**：把安装版的 `%LocalAppData%\Lertaro` 或便携版的 `Data\Users` 用符号链接或 Junction 迁到其它分区是受支持的，但链接目标必须是**本地固定卷**。若链接指向网络位置或可移动卷，程序会在启动时拒绝并说明原因（详见[**设置 → 关于**](./settings/about)）。
- **启动被拒绝：数据目录属于另一个身份**：若用户数据目录的 owner 不是你的账户，Lertaro 会拒绝启动，并先尝试以提权方式修复。提示中会同时给出**实际 owner 与期望 SID**，这就是完整的诊断信息。该修复的每一步（包括超时被终止时仍停留在的那一步）都会写进 `%TEMP%\Lertaro\repair.log`，反馈问题时请附上该文件。

若该目录的 owner 是 `BUILTIN\Administrators`，而权限其实已经授予**你** FullControl，只需把 owner 改回自己即可。下面脚本只修改 `%LOCALAPPDATA%\Lertaro` 范围内的所有者，不改权限、也不改任何文件内容：

```powershell
$sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
$root = Join-Path $env:LOCALAPPDATA 'Lertaro'
Get-Item -LiteralPath $root | ForEach-Object {
    $acl = $_.GetAccessControl('Owner')
    $acl.SetOwner($sid)
    $_.SetAccessControl($acl)
}
Get-ChildItem -LiteralPath $root -Recurse -Force | ForEach-Object {
    $acl = $_.GetAccessControl('Owner')
    $acl.SetOwner($sid)
    $_.SetAccessControl($acl)
}
```

- **上次静默更新失败**：更新程序会把结论（状态、失败原因与回滚目录）写到安装目录旁的 `update-result.json`。Lertaro 会在下次启动时把这个原因提示一次，同一份结论不会重复提示；若提示反复出现，可以删除该文件，或手动覆盖安装新版本。
- **数据目录以下的链接一律拒绝**：只有数据目录自身这条链路允许是链接，它内部的子目录不允许（详见[**设置 → 关于**](./settings/about)）。

## 7. 启动后立刻退出

- **安装完成却从未出现窗口**：在 Windows 10 22H2（内部版本 19045）及更早的系统中，如果 CPU 支持硬件 CET（Intel 12 代及更新、AMD Zen 3 及更新），而该系统尚未完整支持用户模式 CET，.NET 会在 Lertaro 自身代码运行之前中止启动。事件查看器会记录一条 **.NET Runtime** 错误：*"Your Windows doesn't fully support CET. Please install all available Windows updates"*，而 Lertaro 自己的日志一条都不会写入，因为进程在日志系统启动前就已经退出。
- **安装版会自动处理**：安装向导会读取系统内部版本，并在 Windows 10 22H2 及更早的系统中清除该安装目录下三个程序的用户模式影子堆栈策略。它只作用于这三个文件，不影响计算机上的其他程序；重新运行安装程序即可再次写入。
- **便携版**：安装程序无法覆盖便携版目录，请以管理员身份打开 PowerShell，把示例路径替换为你自己的目录后执行以下三条命令。

```powershell
Set-ProcessMitigation -Name 'D:\Lertaro\Lertaro.App.exe' -Disable UserShadowStack
Set-ProcessMitigation -Name 'D:\Lertaro\Lertaro.Service.exe' -Disable UserShadowStack
Set-ProcessMitigation -Name 'D:\Lertaro\lff.exe' -Disable UserShadowStack
```

- **为系统安装全部可用更新**是上面那条 .NET 错误自己给出的建议，同样可能解决该问题。若仍然无法启动，请按下一节收集日志并提交 Issue。

## 8. 日志查看与提交 Issue

若上述步骤均未能解决你的问题，请进入[**设置 → 运行状态**](./settings/service-status)查看详细的运行日志：

- **Service 日志**：记录后台索引服务、USN 日志读取、网络盘扫描与 IPC 通信状态。
- **App 日志**：记录前台 UI 渲染、插件加载、快捷键响应与配置变动。
- **Hook 日志**：记录全局键盘钩子与鼠标导航手势的事件流。

你可以在日志面板中使用搜索框过滤关键词，或按日志等级（Info / Warn / Error）快速定位异常堆栈，并在 GitHub 提交 Issue 时附上相关日志以便快速定位。
