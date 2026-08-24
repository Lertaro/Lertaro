---
layout: home
hero:
  name: Lertaro
  text: 高性能、可扩展的 Windows 本地检索系统
  tagline: 基于 USN 日志与列式内存索引，毫秒级快速定位文件与启动应用，兼具深度对话框挂载与开放插件生态。
  image:
    src: /logo.png
    alt: Lertaro Logo
securityWarning:
  title: "安全警告：仅信任官方来源"
  details: "仓库 github.com/adelmagical742/Lertaro 和网站 adelmagical742.github.io 正在冒充 Lertaro 并传播恶意下载。请勿下载或运行来自这些地址的任何文件，并仅使用下方官方链接。"
features:
  - icon: 💡
    title: Listary 的开源替代
    details: 现代化高颜值且高度可扩展的开源文件检索与启动工具，完美替代并拓展传统商业级桌面检索工作流。
  - icon: ⚡
    title: USN 与 MFT 底层索引
    details: 基于 Windows NTFS / ReFS 底层 USN Journal 与 $MFT 机制快速构建索引，支持 FAT32 / exFAT 变动监听与网络驱动器缓存。
  - icon: 🎯
    title: fzf 模糊匹配与拼音别名
    details: 支持字符跳跃模糊命中与路径定向操作符，内置拼音别名引擎，支持中文文件名首字母与全拼极速检索。
  - icon: 🖱️
    title: 原生文件对话框深度挂载
    details: 自动挂载于 Windows 原生“打开 / 另存为”对话框及 Explorer、Total Commander，双向同步选中状态与当前工作路径。
  - icon: 🎬
    title: 动作菜单与 QuickLook 预览
    details: 选中条目按 Ctrl+O 呼出完整动作菜单与原生 Shell 右键，按下 Alt+P 即可通过 QuickLook 即时预览文档与影音。
  - icon: 📊
    title: 即时磁盘空间透视分析
    details: 基于已有内存索引直接生成矩形树（Treemap）空间占用图，免去漫长重新扫描磁盘的过程，支持快速定位大文件。
  - icon: 🧩
    title: 开放插件 SDK 与生态兼容
    details: 提供基于 .NET 10 的官方 C# 插件开发接口，并支持桥接运行 Flow Launcher 社区插件与自定义工作流。
  - icon: 🛡️
    title: 三进程架构与离线隐私
    details: SYSTEM 索引服务、用户态 App 与独立 Hook 进程安全隔离；纯本地运行，不收集任何用户隐私数据。
---
