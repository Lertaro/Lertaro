# Windows runner 与发布链路

实现入口：[.gitlab-ci.yml](../.gitlab-ci.yml)、[make.bat](../make.bat)、[GitHub release workflow](../.github/workflows/release.yml)。配置已按目录首页基线核对；首次成功发布的验证来自 2026-11-04，不代表当前 runner 在线状态。

## runner 的运行环境

GitLab job 使用 `windows` 标签，shell 为 PowerShell。runner 的服务端标签必须匹配；本地 `config.toml` 不保存标签。前台运行的 runner 退出后就没有这个执行器领取任务，pending 应先检查进程与匹配条件。

当前打包依赖 Windows、.NET、PowerShell、Git 与 Inno Setup 7。用户级 Inno Setup 与服务账户的工具路径不同；把 runner 改成服务前，应在目标账户验证全部工具，不假定交互终端能运行就代表 LocalSystem 也能运行。shell executor 不执行容器镜像，应只接适配的 job。

构建耗时要用目标 runner 的实际测量，不套用另一机器的估算；外部平台配额、镜像内容和上传限额另行核实，不作为本仓库长期约束。

## 签名契约

- `make.bat` 接收的 `LERTARO_SIGNING_KEY` 是整个 PEM 文件编码成的一行 base64。GitLab 使用普通 Variable，File 类型传的是路径，违反这个契约。
- GitHub 的 `SIGNING_KEY` secret 保存 PEM 原文，由 workflow 编码后传给同一个变量。只有 `make.bat` 解码临时 key 并负责清理。
- 是否能读到变量取决于 runner 进程收到的环境，不是简单规定“必须机器级”。修改变量后重新启动读取它的进程；GitLab 还要核对 Protected 变量与 tag 的可见性。不要输出私钥内容做诊断。
- 缺 key 直接失败，不能发布 unsigned 占位签名。Portable zip 与 `<zip>.sig` 必须同名同版本并排，产物命名是更新器契约。
- PowerShell 签名分支使用 `$ErrorActionPreference='Stop'`，并在写入前 `VerifyData` 自检。过去非终止异常仍以退出码 0 留下合法 DER 外形的坏签名，存在性与长度检查都未发现。

私钥自检只能证明这把 key 能验证自己，不能证明与客户端内置公钥匹配。当前发布前仍没有使用更新器公钥验证两个包的完整关卡；发布验证要分别确认匿名下载、包哈希和客户端公钥验签。2026-11-04 的两个架构包做过这些检查。

## 发布与保留

构建产物上传 generic package registry，release 通过 assets links 引用；短期 artifacts 的 `expire_in: 1 week` 不是长期发布下载地址。更新器匿名读取 zip 及相邻签名，不能给它换成需要 CI token 的地址。

`KEEP_PACKAGE_VERSIONS` 当前为 3。清理必须在上传与 release 创建之后，且额外排除本次 tag，避免重跑旧 tag 时删除刚发布的版本。只读最新 100 个 package 的实现依赖“每次成功清理、无大量积压”这个前提，失效后需要分页，不能把注释当保证。

清理默认用 `CI_JOB_TOKEN`，也支持 `PACKAGE_TOKEN`。上传成功不能证明删除权限足够；是否清理成功以 job 日志为准，不能因 release 成功就宣称旧包已删除。
