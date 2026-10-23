# dev-docs

面向维护者的机制说明、约束和仍在执行的计划。用户操作说明放在 `Site/`。代码与文档冲突时，以代码为准并同步修正文档。

## 索引

| 文件 | 内容 |
| --- | --- |
| `pitfalls.md` | 构建、测试、Windows/.NET 与通用维护约束 |
| `search-ranking.md` | 当前排序管线、分值、截断、诊断和验证 |
| `search-ranking-intuition-plan.md` | 排序 P0–P10 实施计划；P0–P1 已完成 |
| `search-grammar.md` | 搜索语法规格；文首标明尚未合入的部分 |
| `pinyin-vs-winshun.md` | 当前拼音匹配规则、限制和测试矩阵 |
| `index-rules.md` | 索引规则、继承、迁移和冲突处理 |
| `settings-permissions.md` | 设置目录权限与修复流程 |
| `font-clarity.md` | 字体渲染与窗口轮廓约束 |
| `opaque-window-flash.md` | 不透明窗口闪烁的现行约束和未决测量 |
| `autocad-save-dialog.md` | AutoCAD 保存对话框适配计划 |
| `wake-unresponsive.md` | 唤醒恢复路径和未决取证 |
| `quick-switch-reparse-audit.md` | Quick Switch 重解析审计 |

## 维护规则

- 一个主题只留一篇现行说明；过时方案和已替换流程直接删除。
- 开头写核对基线或版本。无法确认时标明未核对，不按印象更新。
- 保留仍影响实现的原因、边界、数字和复现；删除会话过程、重复结论和时间线叙述。
- 路径使用仓库相对路径或 `%LOCALAPPDATA%` 等通用写法。
- 用户可见操作写入 `Site/`，文案同步到所有受支持语言。
