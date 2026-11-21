<p align="center">
  <img src="../logo.svg" alt="Lertaro logo" width="120">
</p>

# Lertaro

[English](../README.md) | 简体中文 | [繁體中文（香港）](zh-HK.md) | [繁體中文（台灣）](zh-TW.md) | [日本語](ja-JP.md) | [한국어](ko-KR.md) | [Español](es-ES.md)

> [!CAUTION]
> **安全警告：请仅从官方来源下载 Lertaro。** 为确保安全，请仅从官方仓库、官方网站和 [GitHub Releases](https://github.com/Lertaro/Lertaro/releases) 下载 Lertaro。请勿下载或运行来自未经验证的来源或仓库的文件。

Lertaro 是面向 Windows 的开源文件搜索与效率启动工具，基于 .NET 10 / WPF。通过后台索引快速查找文件、文件夹和应用，并将搜索、预览与目录跳转融入日常工作。

[用户手册](https://lertaro.github.io/zh-CN/user-guide/) · [开发手册](https://lertaro.github.io/zh-CN/dev-guide/) · [发布下载](https://github.com/Lertaro/Lertaro/releases) · [问题反馈](https://github.com/Lertaro/Lertaro/issues)

## 主要功能

- **文件搜索**：NTFS 使用 MFT 建立索引，NTFS / ReFS 通过 USN 日志跟踪变动；支持 FAT32 / exFAT 和网络共享。提供模糊匹配、多关键词、路径与类型筛选，以及中文全拼和首字母搜索。
- **文档内容搜索**：内容搜索插件可索引文本、Markdown、PDF 和 Office 文档正文，显示匹配片段；配置监测目录后，用 `cs 关键词` 搜索。
- **窗口与目录导航**：快速窗口、完整搜索窗口和内嵌搜索，支持 Windows 打开/保存对话框、资源管理器及 Total Commander、Directory Opus 等文件管理器；提供快速跳转与导航菜单。
- **文件操作与预览**：复制、移动、重命名、删除、原生右键菜单，以及图片、文本、音视频和文件夹预览；可选接入第三方 QuickLook。
- **收藏与工作区**：快速启动集中展示常用项目、收藏和历史；快速面板按工作区组织文件夹、最近文件与拖放操作，支持关联前台应用。
- **空间分析**：利用已有索引生成磁盘空间矩形树图，逐层查看大文件和文件夹。
- **效率插件**：计算器、网页搜索、翻译、浏览器书签与历史、进程管理、窗口切换、自定义命令等；提供 C# 插件 SDK 与 Flow Launcher 插件桥接。
- **终端与外部集成**：`lff` 支持交互查找、脚本查询和 JSON 输出；`lertaro://` 可从外部打开搜索或设置，另有可选的 Everything IPC 兼容服务。
- **局域网传输与个性化**：内置 LocalSend 协议兼容传输，可发送文件、文件夹和文本；支持主题、多语言、快捷键自定义与设置备份恢复。

本地文件索引与检索可离线使用。网页搜索、翻译和更新检查等功能按需联网；LocalSend 使用局域网。索引服务、前台界面与窗口集成辅助进程分开运行。

## 下载与安装

支持 **Windows 10 / 11，x64 与 ARM64**。运行需要对应架构的 **.NET 10 Desktop Runtime**；安装器会检测并补装，便携版可运行随包的 `install-dotnet-runtime.bat` 安装。

请从本仓库的 [GitHub Releases](https://github.com/Lertaro/Lertaro/releases) 获取官方发布包：

| 架构 | 安装版（推荐） | 便携版 |
| --- | --- | --- |
| x64（Intel / AMD） | [Lertaro-Setup.exe](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup.exe) | [Lertaro-Portable.zip](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Portable.zip) |
| ARM64（Windows on ARM） | [Lertaro-Setup-arm64.exe](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup-arm64.exe) | [Lertaro-Portable-arm64.zip](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Portable-arm64.zip) |

安装版运行安装器，便携版完整解压后运行 `Lertaro.App.exe`。首次启动需要管理员确认以安装索引服务。便携版安装服务后，更新或删除程序目录也需要管理员权限；删除前先运行 `portable-cleanup.bat` 清理服务。详见[快速上手](https://lertaro.github.io/zh-CN/user-guide/getting-started)。

### 使用 Scoop

已安装 [Scoop](https://scoop.sh/) 后，先在普通 PowerShell 终端添加官方 bucket：

```powershell
scoop bucket add lertaro https://github.com/Lertaro/Lertaro
```

然后在同一用户的**管理员 PowerShell 终端**中安装：

```powershell
scoop install --global lertaro/lertaro-np
```

升级时先刷新 bucket，再更新软件（管理员终端）：

```powershell
scoop update
scoop update --global lertaro-np
```

卸载（管理员终端）：

```powershell
scoop uninstall --global lertaro-np
```

`lertaro-np` 通过官方安装器安装到已有注册目录或默认的 Program Files，Scoop 目录用于保存安装器和包记录。升级保留原安装目录，卸载保留用户设置。安装后从开始菜单启动 Lertaro；新开终端即可使用 `lff`。

Scoop、手动安装和 WinGet 共用一份安装，应选择一个渠道维护；内置更新不会同步 Scoop 的版本记录。WinGet 收录状态、同版本强制重装和故障日志说明见[包管理器指南](../dev-docs/package-managers.md)。

## 快速上手

1. 启动 `Lertaro.App.exe`，完成服务安装，在 **设置 → 运行状态** 查看首次索引进度；在索引设置中选择驱动器或添加文件夹。
2. 双击同一个 `Ctrl` 键呼出快速窗口，输入文件名、应用名或拼音，按 `↑` / `↓` 选择，`Enter` 打开，`Ctrl+Enter` 定位所在目录。
3. 在 **设置 → 插件** 启用和配置扩展功能；内容搜索需要先设置监测文件夹。

| 默认按键 | 功能与使用位置 |
| --- | --- |
| 双击 `Ctrl` | 呼出/隐藏快速窗口 |
| `Ctrl+F` | 在快速窗口中打开完整搜索窗口 |
| `Ctrl+O` | 在快速窗口中打开动作菜单；完整窗口使用右键菜单 |
| `Alt+P` | 在支持预览的窗口切换预览面板 |
| `Ctrl+G` | 在文件对话框中跳转到最近浏览的目录 |
| `Ctrl+F2` | 呼出快速面板 |

`lff` 查询需要 Lertaro App 正在运行。安装版勾选添加到 PATH（Scoop 安装会自动选择），然后新开终端：

```powershell
lff --search 'report' --limit 20
```

更多用法：[搜索语法](https://lertaro.github.io/zh-CN/user-guide/search-syntax) · [快捷键](https://lertaro.github.io/zh-CN/user-guide/hotkeys) · [命令行](https://lertaro.github.io/zh-CN/user-guide/cli) · [故障排查](https://lertaro.github.io/zh-CN/user-guide/troubleshooting)

## 从源码构建

在 Windows 上安装 .NET 10 SDK，获取源码后运行：

```powershell
git clone https://github.com/Lertaro/Lertaro.git
cd Lertaro
.\build_and_run.bat
```

该脚本会停止正在运行的 Lertaro 界面和服务，编译 App、Service、CLI 与插件，再启动开发版本。

发布打包使用 `make.bat`，还需 PowerShell 7、64 位 Inno Setup 7，以及与更新器公钥匹配的 `LERTARO_SIGNING_KEY`。它在 `dist/` 生成两个架构的安装器、便携包和签名；配置方法见[发布链路](../dev-docs/gitlab-self-hosted-runner.md)，插件开发见[开发手册](https://lertaro.github.io/zh-CN/dev-guide/)。

## 反馈与贡献

欢迎提交 [Issue](https://github.com/Lertaro/Lertaro/issues) 或 Pull Request。报告问题时请附版本号、Windows 版本与架构、安装方式、复现步骤和相关日志；开发前可查阅[维护文档](../dev-docs/README.md)。

## 许可证

[MIT License](../LICENSE)。
