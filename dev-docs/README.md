# dev-docs

面向维护者的现行约束、可复用经验和仍有证据的边界。用户操作说明放在 `Site/`，历史方案和执行过程查 Git 历史。

2026-11-10 按 `5f735e70` 核对本文档集的相关实现。本次整理没有重新运行性能实验或真机验证；历史测量保留原日期与适用范围。

## 索引

| 文件 | 内容 |
| --- | --- |
| [pitfalls.md](pitfalls.md) | 取证、验证、构建与维护经验 |
| [search-grammar.md](search-grammar.md) | 当前语法边界与解析层职责 |
| [search-ranking.md](search-ranking.md) | V3 排序、候选合并与历史反馈 |
| [search-performance.md](search-performance.md) | 11 月 10 日测量结论、复现入口与剩余限制 |
| [pinyin-matching.md](pinyin-matching.md) | 拼音规则、别名精度与匹配证据复用 |
| [entry-categories.md](entry-categories.md) | 统一分类、筛选下推与协议边界 |
| [index-rules.md](index-rules.md) | 路径继承、置顶、迁移与排除范围 |
| [window-rendering.md](window-rendering.md) | 字体、首帧、DPI 与窗口复用 |
| [windows-integration.md](windows-integration.md) | Hook 恢复、跨进程对话框与宿主适配 |
| [settings-permissions.md](settings-permissions.md) | 权限诊断、数据目录与启动失败处理 |
| [gitlab-self-hosted-runner.md](gitlab-self-hosted-runner.md) | runner、签名与发布保留策略 |

性能原始样本保留在 [第一轮证据](search-performance-results.json) 与 [第二轮证据](search-performance-checklist-results.json)。第二轮基线是第一轮修复后的产物，两组前后对照不能混用。

## 维护规则

- 一个主题只保留一份现行说明；跨主题用链接，不复制规则。
- 保留原因、可复现的反例、适用边界和源码入口；删除会话过程、完成清单、主观评分和失效命令。
- 新结论替换旧结论。只有仍能解释取舍的失败实验才保留，且标明历史条件。
- 测量要区分组件实验与真实用户路径；源码核对不能写成真机验证。
- 未解决项必须有当前证据和验证条件；没有依据的猜测、已取消计划不继续滚存。
- 环境权限、机器性能和外部平台配额不写成通用事实。
