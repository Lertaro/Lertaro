<p align="center">
  <img src="../App/logo.png" alt="Lertaro logo" width="120">
</p>

# ⚡ Lertaro

[English](../README.md) | 简体中文 | [繁體中文（香港）](zh-HK.md) | [繁體中文（台灣）](zh-TW.md) | [日本語](ja-JP.md) | [한국어](ko-KR.md) | [Español](es-ES.md)

> [!CAUTION]
> **安全警告：请仅从官方来源下载 Lertaro。** 仓库 `github.com/adelmagical742/Lertaro` 和网站 `adelmagical742.github.io` 正在冒充 Lertaro 并传播恶意下载。请勿下载或运行来自这些地址的任何文件。唯一官方仓库是 [Lertaro/Lertaro](https://github.com/Lertaro/Lertaro)，唯一官方网站是 [lertaro.github.io](https://lertaro.github.io/)，官方程序仅通过 [GitHub Releases](https://github.com/Lertaro/Lertaro/releases) 发布。即使文件名或内容发生变化，也请始终将上述假冒来源视为不可信。

Lertaro 是一款基于 **.NET 10 (WPF)** 打造的超轻量、极速、高度可扩展的 Windows 全局搜索与效率启动工具，是 **Everything** 和 **Listary** 的现代化开源替代——通过读取 NTFS **USN 日志** 与 MFT 直接索引本地磁盘，实现毫秒级、低资源占用的检索体验。

📖 [完整文档、用户手册与开发手册](https://lertaro.github.io/zh-CN/)

## 核心特性

- **毫秒级索引** —— 直接读取 NTFS USN 日志/MFT，而不是递归扫描目录；低占用后台服务实时保持索引同步。
- **FZF 风格模糊搜索** —— 支持多关键词模糊匹配及前缀/后缀/精确/排除操作符，中文文件名支持拼音别名匹配。
- **三种搜索方式** —— 快速弹窗、完整主窗口，以及直接贴靠嵌入文件资源管理器/原生文件对话框的内联搜索栏。
- **QuickLook 预览**、类右键菜单的动作菜单，热键全部可自定义重新绑定。
- **开放插件 SDK** —— 可扩展搜索源、别名、右键菜单动作、结果列、文件预览与主题。
- **进程隔离** —— SYSTEM 级后台索引服务与用户态界面进程彻底分离。

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
