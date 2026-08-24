# URI 协议（lertaro://）

Lertaro 在首次运行时会自动在 Windows 系统中注册自定义协议 **`lertaro://`**。无论是网页超链接、桌面快捷方式、自动化脚本还是第三方软件，均可通过该协议直接唤起 Lertaro 的特定搜索、直达设置页面或发起跨设备传输。

## 1. 协议机制与实例路由

- **开箱即用**：无需手动配置注册表，Lertaro 启动时会自动完成注册与自愈校验。
- **单实例路由**：若 Lertaro 已经在后台运行，打开 `lertaro://` 链接会直接唤醒当前运行中的前台实例，绝不会重复启动多个进程；若 Lertaro 尚未启动，系统会自动拉起主程序并立即执行链接指定的动作。

## 2. 完整 URI 路由指令表

| URI 指令格式 | 功能说明与交互效果 |
| :--- | :--- |
| `lertaro://` | 激活并显示快速搜索窗口（效果等同于双击 `Ctrl` 全局热键）。 |
| `lertaro://search/[关键词]` | 激活快速搜索窗口，并预先填入指定的 `[关键词]` 并立即过滤。 |
| `lertaro://fullsearch/[关键词]` | 打开大尺寸完整搜索主窗口，并预先填入指定的 `[关键词]`。 |
| `lertaro://settings/page/[分区]` | 打开设置窗口，并直接切换到指定的顶层分区标签页。 |
| `lertaro://settings/entry/[序号]` | 打开设置窗口并精准跳转到某一项具体设置项，同时闪烁高亮该选项。 |
| `lertaro://localsend` | 打开空白的 LocalSend 局域网发送窗口。 |
| `lertaro://localsend/items/[编码后的绝对路径...]` | 打开 LocalSend 并切换至文件模式，自动添加一个或多个目标文件/目录。 |
| `lertaro://localsend/text/[编码后的文本]` | 打开 LocalSend 并切换至文本模式，自动填入指定的待发送文本。 |

### 设置分区参数 `[分区]`

设置分区参数不区分大小写，对应设置界面的侧边栏模块：

```text
Service      - 运行状态
Index        - 索引设置
General      - 通用设置
Appearance   - 外观与主题
Hotkeys      - 热键设置
Plugins      - 插件管理
Favorites    - 收藏夹
History      - 历史记录
QuickPanel   - 快速面板
About        - 关于与更新
```

> [!NOTE]
> `lertaro://settings/entry/[序号]` 中的序号是由内置的[**设置搜索**](./instant-answers#2-关键词触发功能内置插件)功能动态生成的。由于内部序号在版本更新或重启后可能会重新分配，建议在外部脚本中优先使用 `lertaro://settings/page/[分区]`。

## 3. LocalSend 路由与参数编码规范

在使用 LocalSend 相关 URI 时，每个文件路径或文本必须进行标准的 URL 编码（例如将 `:` 转换为 `%3A`，将 `\` 转换为 `%5C`，将空格转换为 `%20`）：

```text
# 预填多个文件路径
lertaro://localsend/items/C%3A%5CUsers%5Ctestuser%5CDesktop%5Cdoc.pdf/D%3A%5CShared%5Cphotos

# 预填待发送文本
lertaro://localsend/text/Hello%20from%20Lertaro%21
```

- **安全约束**：所有传入的文件路径必须为本机已经真实存在的绝对路径；带有预填内容的链接仅会打开 LocalSend 并进入设备选择界面，绝不会自动向任何设备发送数据。

## 4. 外部联动实战示例

### 浏览器与 Markdown 链接

在个人知识库（如 Obsidian、Notion 或 Markdown 文档）中直接插入超链接：

```markdown
点击打开 [Lertaro 外观设置](lertaro://settings/page/Appearance)
点击快速查找 [项目财务报表](lertaro://search/财务报表%202026)
```

### Windows 快捷方式与批处理

在桌面右键新建快捷方式，在对象位置输入：

```cmd
lertaro://fullsearch/D:\Projects\
```

在 PowerShell 脚本中调用：

```powershell
Start-Process "lertaro://settings/page/General"
```

## 5. 安全性与未知路由容错

- **静默容错**：由于任何外部网页或脚本均可尝试触发该协议，Lertaro 对所有传入的 URI 实行严格的白名单校验。若链接格式错误、拼写有误或指向不存在的路由，Lertaro 会直接安全忽略，仅记录调试日志，绝不会产生意外的破坏性操作或崩溃。
