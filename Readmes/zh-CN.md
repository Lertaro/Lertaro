<p align="center">
  <img src="../App/logo.png" alt="Lertaro logo" width="120">
</p>

# ⚡ Lertaro

[English](../README.md) | 简体中文 | [繁體中文（香港）](zh-HK.md) | [繁體中文（台灣）](zh-TW.md) | [日本語](ja-JP.md) | [한국어](ko-KR.md) | [Español](es-ES.md)

> [!CAUTION]
> **安全警告：请仅从官方来源下载 Lertaro。** 仓库 `github.com/adelmagical742/Lertaro` 和网站 `adelmagical742.github.io` 正在冒充 Lertaro 并传播恶意下载。请勿下载或运行来自这些地址的任何文件。唯一官方仓库是 [Lertaro/Lertaro](https://github.com/Lertaro/Lertaro)，唯一官方网站是 [lertaro.github.io](https://lertaro.github.io/)，官方程序仅通过 [GitHub Releases](https://github.com/Lertaro/Lertaro/releases) 发布。即使文件名或内容发生变化，也请始终将上述假冒来源视为不可信。

Lertaro 是一款基于 **.NET 10 (WPF)** 打造的超轻量、极速、高度可扩展的 Windows 全局搜索与效率启动工具，是 **Listary** 和 **Everything** 的现代化开源替代——通过读取 NTFS **USN 日志** 与 $MFT 直接索引本地磁盘，实现毫秒级、低资源占用的检索体验。

📖 **[完整文档、用户手册与开发手册](https://lertaro.github.io/zh-CN/)**

## 核心特性

- ⚡ **USN 与 MFT 底层索引** —— 直接读取 NTFS / ReFS 磁盘底层 USN Journal 与 $MFT，秒级建立全盘索引，支持 FAT32 / exFAT 变动监听与网络共享缓存。
- 🎯 **fzf 模糊匹配与拼音别名** —— 支持字符跳跃模糊命中、路径定向操作符与中文拼音首字母/全拼极速检索。
- 📂 **三大搜索形态与深度挂载** —— 居中快速浮窗、完整主窗口，并自动挂载于 Windows 原生 Open/Save 对话框与主流文件管理器（Explorer、Total Commander、Directory Opus、OneCommander）。
- 🎬 **动作菜单与 QuickLook 预览** —— `Ctrl+O` 呼出动作菜单与原生 Shell 右键，`Alt+P` 触发 QuickLook 即时预览文档与影音。
- 📊 **即时磁盘空间透视分析** —— 基于已有内存索引直接生成矩形树（Treemap）空间占用图，免去漫长的磁盘重扫过程。
- 🧩 **开放插件 SDK 与生态兼容** —— 基于 .NET 10 的官方强类型 C# SDK，并兼容运行 Flow Launcher 社区插件与自定义工作流。
- 🛡️ **三进程架构与离线隐私** —— SYSTEM 索引服务（`Lertaro.Service`）、用户态 App（`Lertaro.App`）与独立 Hook 进程（`Lertaro.Service --hook`）安全隔离；纯本地离线运行，零云端遥测。

搜索语法、每一个热键、每一项设置详见[用户手册](https://lertaro.github.io/zh-CN/user-guide/)；架构设计与插件 SDK 参考详见[开发手册](https://lertaro.github.io/zh-CN/dev-guide/)。

## 下载

在[项目主页](https://lertaro.github.io/zh-CN/)获取最新版本，或直接下载：

- **x64 版本（Intel / AMD 处理器）**
  - [安装包 Lertaro-Setup.exe](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup.exe) —— 推荐，支持后台系统服务。
  - [便携版 Lertaro-Portable.zip](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Portable.zip) —— 绿色免安装，解压即用。
- **ARM64 原生版本（骁龙 / Windows on ARM 设备）**
  - [安装包 Lertaro-Setup-arm64.exe](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup-arm64.exe) —— ARM 设备推荐，原生高效运行。
  - [便携版 Lertaro-Portable-arm64.zip](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Portable-arm64.zip) —— ARM 原生免安装便携包。

## 从源码构建

环境要求：Windows 10/11、.NET 10 SDK、Visual Studio 2022 或 JetBrains Rider；如需生成安装包还需要 [Inno Setup](https://jrsoftware.org/isinfo.php)。

- `build_and_run.bat` —— 重新编译 App/Core/Service/插件并在本地重新启动，适合日常开发调试。
- `make.bat` —— 生成 Release 构建，产出 `dist/` 目录下的 x64 与 ARM64 安装包及便携包。

完整架构设计与插件 SDK 详见[开发手册](https://lertaro.github.io/zh-CN/dev-guide/)。

## 🎁 捐赠与支持

如果 Lertaro 对你有帮助，非常感谢你考虑捐赠支持！

- **USDT (TRC20)**：`TNDh3husX1trDW2ZPm4ZZYdoCoCRCZQXn5`

## 许可证

本项目基于 MIT License 开源。
