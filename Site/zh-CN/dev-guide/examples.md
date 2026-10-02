# 官方插件范例

为了帮助开发者深入理解 `Lertaro.PluginSdk` 的各模块协同机制，本章节选取了 Lertaro 官方仓库自带的四个典型开源插件进行深度案例剖析。

## 1. CoreExtensions —— 动作、Shell 菜单与快速面板

`CoreExtensions` 插件是 Lertaro 最核心的功能扩展包，同时实现了 `IPlugin`、`IActionProvider`、`IConfigurable` 以及多个子提供者接口。

### 核心实现要点

- **静态结果动作（`IActionProvider.GetActions()`）**：注册了一组常用的基础文件动作，包括打开与定位项目、复制路径和文件、添加到收藏夹、重命名、终端命令、删除以及管理员身份运行等。
- **原生 Shell 菜单集成（`IDynamicActionProvider`）**：通过 `ShellMenuActionProvider` 与 Windows Shell COM 接口交互，将完整的 Windows 右键级联菜单（如“发送到”、7-Zip、VS Code 打开等）无缝渲染至 Lertaro 的 `Ctrl+O` 动作菜单中。
- **模式驱动的配置表单（`IConfigurable`）**：展示了如何定义包含嵌套分组（`Group`）、多行字符串列表（`StringList`）与快捷键录制（`Hotkey`）的复杂配置表单，无需手写任何 XAML 即可在设置中心中自动生成。
- **多样化的快速面板标签（`IQuickPanelTabProvider`）**：
  - `FavoritesTabProvider` / `HistoryTabProvider`：直接使用宿主已经加载好的收藏与历史，而不是再从磁盘重新读取；历史查询会通过 `Task.Run` 派发离开 UI 线程执行，因此呼出面板时不会等待它。
  - `WindowsRecentTabProvider`：在后台任务中遍历系统 `Recent` 目录并通过 COM 解析快捷方式目标，预先截断并填充 `Metadata.Modified` 时间戳以实现“最新在前”。
  - `LastDirectoryTabProvider` / `RecentFilesTabProvider`：直接调用宿主公开的 [`ExplorerPathService`](./sdk/services) 与 `RecentFilesService` 查询宿主已有状态。

## 2. PinyinAlias —— 非 ASCII 别名转写引擎

`PinyinAlias` 插件专门为中文文件名提供拼音全拼与首字母缩写检索支持，同时实现了 `IAliasProvider` 与 `ITranslationProvider` 两个接口。

### 核心实现要点

- **输入/输出字母表边界（`InputRanges` / `OutputRanges`）**：输入边界只声明一段 CJK 表意文字区间（U+3007–U+9FFD），输出范围则是小写 `a`–`z`。宿主利用该边界智能将“大cj”等混合查询切分为字面匹配与拼音别名匹配。
- **快速预检过滤（`CanHandle(text)`）**：在生成别名前先扫描文本中是否存在中文字符，对于纯英文字符串直接返回 `false`，完全跳过后续开销。
- **多音字组合与别名构建（`GetAliases(text)`）**：先构建字符级音节表，对于含多音字的文件名（如“重”、“长”），自动生成各常见读音组合并使用 `|` 管道符连接（上限 32 种组合以防爆炸），供搜索引擎作为候选集并行匹配。
- **内嵌多语言与线程安全缓存**：通过 `ITranslationProvider` 提供插件显示名称与描述的多语言本地化，并在内部使用带 `lock` 保护的字典缓存解析后的 JSON 翻译，避免每次查询重复解析。

## 3. FlowLauncherBridge —— 跨生态桥接与隔离运行时

`FlowLauncherBridge` 插件展示了如何构建一个大型复合型桥接系统，将外部开源社区生态无缝吸纳进 Lertaro 体系。

### 核心实现要点

- **多语言跨进程桥接**：兼容 C# (.NET)、Python 3.12、Node.js v20 LTS 及独立 `.exe` 形式的 Flow Launcher 插件。
- **纯净自包含环境**：把相互隔离的 Python 与 Node.js 运行时部署在 Lertaro 的**共享数据目录**下（`SharedDataDirectory\FlowData\PythonEmbeded-{arch}`），而非按用户划分的数据目录。每个插件通过**被重定向的标准输入/输出**与自己的解释器通信，其上承载 JSON-RPC——不存在命名管道，也完全不触碰系统 PATH。
- **动态配置与富文本预览**：解析外部插件的 `SettingsTemplate.yaml`/`.json` 并动态映射为 `PluginConfigSchema`；在 QuickLook 预览面板中利用 WebView2 渲染外部插件返回的富文本卡片（如词典释义、实时天气等）。

## 4. FileUnlocker —— 解除文件占用动作

`FileUnlocker` 展示了一个专注于单一动作的插件如何通过 Windows Restart Manager API 查询文件占用并请求释放。

### 核心实现要点

- **单选约束**：仅对一个已存在的文件提供动作，避免对文件夹或多个结果发起含义不明确的请求。
- **进程信息展示**：显示占用进程名称、PID 和可执行文件路径，并提供刷新操作以应对文件状态变化。
- **请求式释放**：请求占用进程释放文件；未检测到进程或操作进行中时，释放按钮会禁用。
- **宿主窗口框架**：将 WPF 视图放入 SDK 提供的主题化 `PluginWindow` 对话框，插件无需重复实现主题、DPI、任务栏和 Alt+Tab 处理。
