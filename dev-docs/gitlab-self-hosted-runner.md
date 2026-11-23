# Windows runner 与发布链路

实现入口：[.gitlab-ci.yml](../.gitlab-ci.yml)、[make.bat](../make.bat)、[GitHub release workflow](../.github/workflows/release.yml)、[签名验证](../verify-release.ps1)、[release notes 生成](../generate-release-notes.ps1)。本文签名与发布链路于 2026-11-19 核对；首次成功发布的验证来自 2026-11-04，不代表当前 runner 在线状态。

## runner 的运行环境

GitLab job 使用 `windows` 标签，shell 为 PowerShell。runner 的服务端标签必须匹配；本地 `config.toml` 不保存标签。前台运行的 runner 退出后就没有这个执行器领取任务，pending 应先检查进程与匹配条件。

当前打包依赖 Windows、.NET、PowerShell 7、Git 与 Inno Setup 7。用户级 Inno Setup 与服务账户的工具路径不同；把 runner 改成服务前，应在目标账户验证全部工具，不假定交互终端能运行就代表 LocalSystem 也能运行。shell executor 不执行容器镜像，应只接适配的 job。

构建耗时要用目标 runner 的实际测量，不套用另一机器的估算；外部平台配额、镜像内容和上传限额另行核实，不作为本仓库长期约束。

## 签名契约

- GitHub 和 GitLab 均使用 `LERTARO_SIGNING_KEY`：整个私钥 PEM 文件的 base64。GitLab 使用普通 Variable；File 类型传的是路径，违反这个契约。GitHub 不再读取旧 `SIGNING_KEY` secret。
- PEM 中真实的 LF、CRLF、末尾换行，以及外层 base64 的换行都兼容。推荐外层保存为一行，便于配置 masked CI 变量。支持 SEC1 `EC PRIVATE KEY` 和 PKCS8 `PRIVATE KEY`；不接受文件路径、直接编码的 DER 或把换行写成字面量反斜杠 `\n` / `\r\n` 的字符串。
- 是否能读到变量取决于 runner 进程收到的环境，不是简单规定“必须机器级”。修改变量后重新启动读取它的进程；GitLab 还要核对 Protected 变量与 tag 的可见性。不要输出私钥内容做诊断。
- 缺 key 直接失败，不能发布 unsigned 占位签名。Portable zip 与 `<zip>.sig` 必须同名同版本并排，产物命名是更新器契约。
- PowerShell 签名分支使用 `$ErrorActionPreference='Stop'`，并在写入前 `VerifyData` 自检。过去非终止异常仍以退出码 0 留下合法 DER 外形的坏签名，存在性与长度检查都未发现。

`make.bat` 在构建前调用 `verify-release.ps1 -CheckSigningKey`，将私钥导出的 SubjectPublicKeyInfo 字节与 `Core/Services/Update/UpdatePackage.cs` 的公钥比较，并签名一个固定挑战验证私钥确实可用。它不比较 PEM 或 base64 文本，因此相同密钥的换行差异不会误判。打包后，无论使用 OpenSSL 还是 PowerShell 签名，都用更新器公钥验签两个 ZIP，失败就中止。

预检分别报告 `Base64 decoding failed` 和 `ECDSA private-key PEM import failed`，并附底层异常类型及 HRESULT。前者发生在外层 Base64 解码，后者表示解码后无法导入密钥；两者都在公钥匹配之前。日志只记录异常元数据，避免异常消息包含私钥输入。

在 Windows 上读取文件并设置当前进程变量：

```powershell
$env:LERTARO_SIGNING_KEY = [Convert]::ToBase64String([IO.File]::ReadAllBytes('C:\keys\lertaro-release.key'))
pwsh -NoProfile -File ./verify-release.ps1 -CheckSigningKey
```

在 Linux shell 上生成同一契约的变量（不输出私钥）：

```bash
export LERTARO_SIGNING_KEY="$(openssl base64 -A -in /path/to/lertaro-release.key)"
pwsh -NoProfile -File ./verify-release.ps1 -CheckSigningKey
```

Linux 命令只用于配置变量；WPF/Inno 打包仍在 Windows 上执行。换行兼容性由 [ReleaseVerificationScriptTests](../Tests/Core/Services/Update/ReleaseVerificationScriptTests.cs) 覆盖：LF/CRLF × SEC1/PKCS8 × 单行/换行 base64，以及字面量反斜杠的拒绝场景；测试在 Windows PowerShell 7 上运行，未执行 Linux CI。

## #548：GitHub 构建成功但发布包验签失败

2026-11-03 的 `a1ead1e0` 更换了客户端内置公钥，GitHub workflow 仍读取旧 `SIGNING_KEY`。新 base64 secret `LERTARO_SIGNING_KEY` 当天已创建，却未被该 workflow 使用。旧 secret 的最后更新时间为 2026-08-06。`d91cfd3a` 改为读取新 secret，并加入上述信任公钥关卡；私钥自签自验不足以发现这种配置错配。

2026-11-19 使用 .NET 和 OpenSSL 交叉验证历史附件：

| 附件来源 | 旧公钥 | 当前公钥 |
| --- | --- | --- |
| GitHub v5.12.7 | 通过 | 失败 |
| GitHub bot 上传的 v5.13.2 | 通过 | 失败 |
| 维护者上传的 v5.13.3 | 失败 | 通过 |
| 已用 GitLab 产物替换的 v5.13.4 | 失败 | 通过 |

v5.13.2 的 Core DLL 已内置新公钥，但包仍由旧私钥签名。v5.13.3 的 Actions run `43734166973` 虽整体显示成功，构建 job 实际被 36 小时限制跳过；可用附件由维护者上传，所以“上个版本正常”不能证明 Actions 的签名配置正确。实际执行的 v5.13.2 run 为 `43720157007`。

v5.13.4 run `44323500555` 于 2026-11-18 14:22–14:25 UTC 执行，提交为 `5468fb2f`。其相关签名脚本、SDK 和 runner/action 版本与 v5.13.2 一致。维护者曾核对原 ZIP 与原 release note 的 SHA-256 一致但验签失败；原附件已替换且未保留，因此无法直接重验那一份 v5.13.4。配置错配、v5.13.2 实物和前一版本的上传来源共同解释故障，不能把替换后的附件冒充原始证据。

两把公钥的 SPKI SHA-256（不是 PEM 文本哈希）：

- 旧：`4d89e6c857f102b044636cc7bbffbafa4a73b4020b42e1cab548cc0f3c55508d`
- 当前：`985a63db5ae42c076fa9cac27fe3616df06bca02672d60db2de808e193dae50d`

SHA-256 对应只能证明文件与说明一致，不能证明签名者受客户端信任。今后轮换密钥还须考虑已安装客户端信任旧公钥的升级路径，不能为通过发布验签而临时放宽客户端信任。

## 本地 release notes

GitLab build job 在 `make.bat` 完成后生成 `dist/release-notes.md`；release job 直接读取这份文件作为发布说明。即使后续网络上传失败，本地仍有与构建产物配套的 Markdown。它包括 tag、完整提交号、上一个 tag 以来的提交记录、六个附件的 SHA-256、两个 ZIP 的 OpenSSL 验签命令与更新器公钥。

脚本要求 HEAD 与 tag 对应、六个文件齐全且两个 ZIP 验签通过。失败时删除旧 Markdown，避免把旧哈希用于新文件。输出固定为无 BOM 的 UTF-8 和 LF；相同历史与文件重复生成不会加入变化的当前时间。

在 tag 对应的 checkout 中可手动生成：

```powershell
pwsh -NoProfile -File ./generate-release-notes.ps1 -Tag v5.13.4
```

也可通过 `-RepositoryPath` 与 `-ArtifactDirectory` 指定其他 checkout 和产物目录。脚本仍要求目标 checkout 的 HEAD 对应该 tag，以免使用错误版本的信任公钥。

对使用 `C:\gitlab-runner\start.bat`、工作目录为 `C:\gitlab-runner` 的本地 shell runner，产物通常位于 `builds\<runner-id>\<slot>\<namespace>\<project>\dist`。这些目录段由 runner 分配，不应写死在 CI 中。`dist/` 也作为 job artifact 保存一周；本地 checkout 可能被后续 job 清理或重用，要长期保留就将六个附件和 Markdown 一起复制到发布存档目录。手动上传时使用这组原文件，不要重新压缩或重新签名后沿用旧哈希。

回归入口为 [ReleaseNotesScriptTests](../Tests/Core/Services/Update/ReleaseNotesScriptTests.cs)，覆盖首次发布、相邻 tag、中文日志、文件哈希、输出编码、重复生成，以及缺文件、篡改和 tag 不匹配的失败场景。

## 两个平台的产物完全一致

微软的 Deterministic 编译支持相同输入产生相同程序集字节；当前 SDK 已默认开启。它不等于“任意机器、任意时间打包的整个发布目录都相同”。独立构建还要逐层固定输入：

| 层次 | 需要处理的差异 |
| --- | --- |
| DLL/PDB | 精确 SDK、依赖、参数、源码换行、路径映射、Source Link 的提交和仓库 URL、WPF 生成资源 |
| 发布目录/安装器 | 原生 apphost、运行时、Inno Setup 版本及打包元数据 |
| ZIP | 文件顺序、时间戳、压缩器及参数；实验中只改变 ZIP entry 时间就改变了哈希 |
| `.sig` | 当前 ECDSA 签名可能每次不同；同一 key/数据连续签名五次得到五个不同但有效的签名 |

仅统一 DLL 的难度较低；要保证当前两条独立流水线的 ZIP、安装器和签名逐字节一致，需要专门的环境固定、差异定位和持续验证，超出一次配置修复。RFC 6979 描述了确定性 ECDSA，但本项目未为复现字节更换签名实现。

当前采用的发布建议是一次构建、一次签名，把同一组附件分发到 GitHub 和 GitLab。这样文件哈希与签名文件可以完全一致，无须承担独立重建的维护成本。独立构建的完整产物复现尚未实现。

技术依据：Microsoft Learn 的 `C# Compiler Options that control code generation / Deterministic`（相同输入含编译器、参数、路径、引用和 Source Link 数据），以及 RFC 6979 `Deterministic Usage of DSA and ECDSA`。二者约束的是不同层次，不能用程序集确定性推导 ZIP 或随机化签名的确定性。

## 发布与保留

构建产物上传 generic package registry，release 通过 assets links 引用；短期 artifacts 的 `expire_in: 1 week` 不是长期发布下载地址。发布地址必须允许目标客户端下载 ZIP 及相邻签名，不能仅因 CI job 能下载就认定更新器也有权限。

`KEEP_PACKAGE_VERSIONS` 当前为 3。清理必须在上传与 release 创建之后，且额外排除本次 tag，避免重跑旧 tag 时删除刚发布的版本。只读最新 100 个 package 的实现依赖“每次成功清理、无大量积压”这个前提，失效后需要分页，不能把注释当保证。

清理默认用 `CI_JOB_TOKEN`，也支持 `PACKAGE_TOKEN`。上传成功不能证明删除权限足够；是否清理成功以 job 日志为准，不能因 release 成功就宣称旧包已删除。
