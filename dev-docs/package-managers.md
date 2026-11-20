# WinGet 与 Scoop 分发

清单使用正式 GitHub Release 的固定版本地址，复用 x64/ARM64 官方安装器，不重新打包。首份清单对应 `v5.13.5`，两个 SHA-256 已通过下载公开附件重新计算核对。清单落库不等于上游已收录；WinGet 需要向 `microsoft/winget-pkgs` 提交并合并 PR，Scoop 自建 bucket 需要先将 `bucket/` 发布到本仓库默认分支。

## WinGet

提交目录是 [packaging/winget/manifests/l/Lertaro/Lertaro/5.13.5](../packaging/winget/manifests/l/Lertaro/Lertaro/5.13.5)。`InstallerType: inno` 使用 WinGet 的 Inno 静默参数，额外选择开始菜单与 `lff` PATH 任务。固定 AppId 让 WinGet 关联手工安装和后续升级，依赖声明使用 `.NET 10 Desktop Runtime`，安装器自身也能补装运行时。

本地校验：

```powershell
winget validate --manifest ./packaging/winget/manifests/l/Lertaro/Lertaro/5.13.5
```

首次提交可使用 Microsoft 的 WinGetCreate 工具：

```powershell
wingetcreate submit ./packaging/winget/manifests/l/Lertaro/Lertaro/5.13.5
```

提交需要有效 GitHub 登录和向个人 fork 推送分支的权限。审核合并、源索引更新后，用户才可运行：

```powershell
winget install --exact --id Lertaro.Lertaro
winget upgrade --exact --id Lertaro.Lertaro
```

## Scoop

[bucket/lertaro-np.json](../bucket/lertaro-np.json) 是自建 bucket 的安装器包，名称遵循 Scoop 非便携软件的 `-np` 约定。发布到默认分支后，用户先在普通终端添加 bucket，再在管理员终端执行全局安装：

```powershell
scoop bucket add lertaro https://github.com/Lertaro/Lertaro
scoop install --global lertaro/lertaro-np
scoop update --global lertaro-np
scoop uninstall --global lertaro-np
```

这几条命令展示安装、升级与卸载三个独立操作，不应一次全部执行。首版不宣称进入 Scoop Extras 或 Nonportable 官方 bucket。

程序由官方安装器安装到现有注册目录或默认 Program Files 目录；Scoop 自己的版本目录只保存安装器和包元数据。因此后台服务的可执行路径稳定，Scoop 更新阶段的 uninstaller hook 明确跳过真正卸载，新的安装器完成原地升级。主动 `scoop uninstall` 才从固定 HKLM 卸载项读取 `InstallLocation` 并调用 Inno 卸载器；执行失败会抛错，不继续伪报成功。普通用户或非全局安装会拒绝执行。

不使用 ZIP 加 `persist: Data`：程序会把目录保护为管理员可写，并拒绝特权目录下的链接；Scoop 的数据 junction 和普通用户写包元数据与这些约束冲突。设置沿用安装版的 LocalAppData / ProgramData，卸载程序不清除设置。Scoop、WinGet 和手动安装共用同一个 AppId，应选一种工具维护同一份安装。内置更新仍可运行，但其版本不会自动回写 Scoop 的包记录。

## 每次发布与手动发布

发布正式 Release，上传完整附件后再更新清单。不能引用 `releases/latest/download`，也不能替换已经入库版本的安装器却保留旧哈希；出现这种情况应发布新版本。

Scoop 直接使用已安装 Scoop 附带的工具检查并更新，不需要新增脚本或 Actions：

```powershell
$checkver = Join-Path (scoop prefix scoop) 'bin/checkver.ps1'
& $checkver -App lertaro-np -Dir ./bucket -Update
```

`checkver` 查找正式 GitHub Release；`autoupdate` 生成两个架构的固定版本地址，Scoop 工具取得哈希。检查 diff、核对附件后提交并发布 bucket 的更新。`scoop update` 不会替维护者生成新清单。

WinGet 首次收录后，用 WinGetCreate 更新已收录包的版本、两个安装器 URL 和哈希，再校验、提交上游 PR。新的三份清单应放在新版本目录，保留旧版本。两个渠道的维护均可在本机完成，GitHub Actions 停止不会影响已发布清单和附件，未更新清单则不会出现新渠道版本。

## 验证边界

Scoop hooks 由 `PackageManagerManifestTests` 使用隔离 PowerShell 进程执行，替换外部安装命令和注册表读取，覆盖权限拒绝、安装失败、升级不卸载、显式卸载及卸载失败。它不在开发机上安装、停止服务或卸载现有软件。实际首次安装、升级、卸载仍应在干净 Windows 虚拟机验证，ARM64 要用对应系统。

2026-11-20 验证：WinGet 官方 `validate` 通过，依赖 ID 通过 `winget show` 核实；Scoop 官方 schema 与脚本语法校验通过，官方 `checkver.ps1 -ForceUpdate -Version 5.13.5` 在临时目录重新生成的两个 URL 与哈希与清单一致；12 个 hook 回归测试全部通过。

`v5.13.5` 的已发布安装器缺少 `skipifsilent`，静默安装结束会启动界面。源码已修正 [Installer/installer.iss](../Installer/installer.iss) 的这项行为；修正要到下次构建才生效，不替换 `v5.13.5` 附件以免破坏已记录哈希。

官方参考：[WinGet 清单](https://learn.microsoft.com/windows/package-manager/package/manifest)、[WinGetCreate](https://github.com/microsoft/winget-create)、[Scoop 清单](https://github.com/ScoopInstaller/Scoop/wiki/App-Manifests)、[Scoop 自动更新](https://github.com/ScoopInstaller/Scoop/wiki/App-Manifest-Autoupdate)、[非便携 bucket](https://github.com/ScoopInstaller/Nonportable)。
