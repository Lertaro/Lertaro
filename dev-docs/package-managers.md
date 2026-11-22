# WinGet 与 Scoop 分发

清单使用正式 GitHub Release 的固定版本地址，复用 x64/ARM64 官方安装器。当前清单对应 `v5.13.7`，两个 SHA-256 已与 GitHub release 附件 API 的 `digest` 及发布说明核对一致；`5.13.5`、`5.13.6` 目录保留为历史清单。

2026-11-23 更新：Scoop 自建 bucket 清单已更新至 `5.13.7`；WinGet [收录 PR #484976](https://github.com/microsoft/winget-pkgs/pull/484976) 已更新为同一版本，提交为 `7aa80dab15915d882db885513674271fa260a1e7`。WinGet 本地验证及 Scoop schema 校验通过；审核与合并状态以该 PR 为准。安装器沿用静默安装不启动应用的行为。

## WinGet

提交目录是 [packaging/winget/manifests/l/Lertaro/Lertaro/5.13.7](../packaging/winget/manifests/l/Lertaro/Lertaro/5.13.7)，采用 `1.28.0` schema。`InstallerType: inno` 使用 WinGet 的 Inno 静默参数，额外选择开始菜单与 `lff` PATH 任务。固定 AppId 让 WinGet 关联手工安装和后续升级，依赖声明使用 `.NET 10 Desktop Runtime`，安装器自身也能补装运行时。

本地校验：

```powershell
winget validate --manifest ./packaging/winget/manifests/l/Lertaro/Lertaro/5.13.7
```

首次收录可用 WinGetCreate 提交，需要有效 GitHub 登录及个人 fork 推送权限：

```powershell
wingetcreate submit ./packaging/winget/manifests/l/Lertaro/Lertaro/5.13.7
```

审核合并并更新源索引后，安装命令为 `winget install --exact --id Lertaro.Lertaro`，升级命令为 `winget upgrade --exact --id Lertaro.Lertaro`。

## Scoop 安装与维护

[bucket/lertaro-np.json](../bucket/lertaro-np.json) 使用非便携安装器包的 `-np` 名称。普通终端先添加 bucket：

```powershell
scoop bucket add lertaro https://github.com/Lertaro/Lertaro
```

管理员终端安装：

```powershell
scoop install --global lertaro/lertaro-np
```

升级使用 `scoop update --global lertaro-np`，卸载使用 `scoop uninstall --global lertaro-np`。

程序安装到已注册目录或默认 Program Files；Scoop 版本目录保存安装器、`install.log` 和包元数据。安装后读取 HKLM 的 InstallLocation，核对 App、Service、CLI 与共享依赖共 13 个必需文件。缺文件时报出实际路径及日志位置。

升级阶段跳过 uninstaller hook 的真正卸载，由新安装器原地更新，保持服务路径稳定。显式卸载才调用注册目录中的 Inno 卸载器，失败抛错；安装失败且没有卸载项、或程序已在系统中卸载时，允许清理包记录。注册表拒绝访问与空安装路径报错，非管理员或非全局安装拒绝执行。

安装器包沿用程序的目录 ACL；便携 ZIP 的管理员写保护与 Scoop 数据 junction、普通用户元数据写入存在冲突。设置保存在 LocalAppData/ProgramData，卸载保留设置。各渠道共用同一 AppId，同一安装选择一种工具维护；内置更新不会回写 Scoop 版本记录。

### 文件缺失时修复

[#553](https://github.com/Lertaro/Lertaro/issues/553) 报告应用本体与 `Lertaro.Service.runtimeconfig.json` 缺失。先更新 bucket，再在管理员终端重装：

```powershell
scoop update
scoop update --global --force --no-cache lertaro-np
```

`--force` 重新执行已记录版本的安装器，`--no-cache` 重新下载并校验哈希。尚未安装时使用上面的安装命令。启动入口为开始菜单或 `Lertaro.App.exe`；`Lertaro.Service.exe` 是后台服务。

手动安装的已有注册目录会继续使用，例如 `C:\app\Lertaro`。仍缺文件时保存 Scoop 输出与版本目录的 `install.log`，核对安装位置和复制结果。`scoop reset` 只重建 Scoop 元数据，不执行安装器。

## 发布清单更新

先发布正式 Release 与完整附件，再更新固定版本 URL 和哈希。已收录附件保持不变，重新打包使用新版本。

Scoop 用自带 checkver 更新清单：

```powershell
$checkver = Join-Path (scoop prefix scoop) 'bin/checkver.ps1'
& $checkver -App lertaro-np -Dir ./bucket -Update
```

checkver 查正式 Release，autoupdate 生成两个架构的 URL 并取得哈希。核对 diff 和附件后提交 bucket。`scoop update` 读取已发布清单，维护者仍须生成新清单。

WinGet 首次收录后，用 WinGetCreate 更新版本、两个安装器 URL 与哈希，校验后提交上游 PR。新清单放入新版本目录，保留旧版本。两渠道均可本地维护，已发布附件和清单独立于 Actions 的运行状态。

## 历史验证与经验

2026-11-20/21：WinGet 官方 validate 通过，依赖 ID 可查询；Scoop schema、脚本语法及官方 checkver 生成的 URL/哈希核对通过。31 个 hook 回归通过。Scoop 源码 `e6aa3b366bdee8ed138c1e0f7b85192ebdd35d0f` 的解包和 hook 调度分别在 PowerShell 5.1/7、64bit/arm64 下检查，四组通过。

公开安装器经 Inno Unpacker 2.71.4 解包，x64 的 75 个、ARM64 的 74 个载荷文件与对应 ZIP 哈希一致，13 个必需文件齐全。#553 的具体丢失环节仍待原始安装命令和日志确认。

[PackageManagerManifestTests](../Tests/Core/Services/Update/PackageManagerManifestTests.cs) 在隔离 PowerShell 进程替换安装命令与注册表边界，验证权限、安装完整性、升级和卸载失败路径。真实首次安装、升级和卸载在干净 Windows 环境验证，ARM64 使用对应系统。

`v5.13.5` 已发布安装器缺少 `skipifsilent`，静默安装结束会启动界面。[Installer/installer.iss](../Installer/installer.iss) 已修正，下次构建生效；保留现有附件与哈希。

参考：[WinGet 清单](https://learn.microsoft.com/windows/package-manager/package/manifest)、[WinGetCreate](https://github.com/microsoft/winget-create)、[Scoop 清单](https://github.com/ScoopInstaller/Scoop/wiki/App-Manifests)、[Scoop 自动更新](https://github.com/ScoopInstaller/Scoop/wiki/App-Manifest-Autoupdate)、[非便携 bucket](https://github.com/ScoopInstaller/Nonportable)。
