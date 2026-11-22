# Windows runner 与发布链路

入口：[.gitlab-ci.yml](../.gitlab-ci.yml)、[make.bat](../make.bat)、[GitHub workflow](../.github/workflows/release.yml)、[verify-release.ps1](../verify-release.ps1)、[generate-release-notes.ps1](../generate-release-notes.ps1)。历史附件取证记录于 2026-11-19。

## runner 环境

GitLab job 使用 `windows` 标签和 PowerShell shell，服务端标签须匹配；本地 config.toml 不保存标签。前台 runner 退出后无人领取该执行器的任务，pending 时先检查进程与匹配条件。

打包依赖 Windows、.NET、PowerShell 7、Git 和 Inno Setup 7。改用服务账户前，在该账户核对工具路径和权限，尤其是用户级 Inno Setup。shell executor 直接执行脚本，job 需适配宿主环境。

## 签名契约

- 两条流水线均使用 `LERTARO_SIGNING_KEY`，内容为完整私钥 PEM 的 base64。GitLab 使用普通 Variable；File 类型传入路径。GitHub 使用同名 secret。
- 支持 SEC1 EC PRIVATE KEY 与 PKCS8 PRIVATE KEY，兼容 PEM 的 LF/CRLF、末尾换行和外层 base64 换行。外层建议一行，便于 masked 变量。文件路径、直接编码的 DER、字面量反斜杠 `\n / \r\n` 均拒绝。
- 变量修改后重新启动读取它的进程，GitLab 另核对 Protected 变量与 tag 可见性；诊断只显示检查结果。
- 缺密钥时停止发布。Portable ZIP 与同名 `<zip>.sig` 并排，构成更新器的下载契约。
- PowerShell 签名使用 `$ErrorActionPreference='Stop'`，写入前 VerifyData 自检。历史非终止异常曾以退出码 0 留下 DER 外形正确的坏签名，文件存在性与长度检查未发现。

`make.bat` 构建前调用 `verify-release.ps1 -CheckSigningKey`，比较私钥导出的 SPKI 字节与更新器公钥，并签名固定挑战验证密钥。打包后，无论 OpenSSL 或 PowerShell 签名，都用更新器公钥验签两个 ZIP，失败停止发布。

预检分别报告 `Base64 decoding failed` 和 `ECDSA private-key PEM import failed`，并附底层异常类型及 HRESULT。前者发生在外层 Base64 解码，后者表示解码后无法导入密钥；两者都在公钥匹配之前。日志只记录异常元数据，避免异常消息包含私钥输入。

Windows 配置当前进程变量：

```powershell
$env:LERTARO_SIGNING_KEY = [Convert]::ToBase64String([IO.File]::ReadAllBytes('C:\keys\lertaro-release.key'))
pwsh -NoProfile -File ./verify-release.ps1 -CheckSigningKey
```

Linux shell 可按同一契约配置变量；WPF/Inno 打包仍在 Windows：

```bash
export LERTARO_SIGNING_KEY="$(openssl base64 -A -in /path/to/lertaro-release.key)"
pwsh -NoProfile -File ./verify-release.ps1 -CheckSigningKey
```

[ReleaseVerificationScriptTests](../Tests/Core/Services/Update/ReleaseVerificationScriptTests.cs) 在 Windows PowerShell 7 覆盖 LF/CRLF、SEC1/PKCS8、base64 换行和字面量反斜杠拒绝。

## #548 签名密钥错配

2026-11-03 的 `a1ead1e0` 更换了客户端公钥，GitHub workflow 仍读取旧 SIGNING_KEY，未使用当天创建的 LERTARO_SIGNING_KEY。`d91cfd3a` 改为新 secret 并增加客户端公钥关卡，避免私钥自签自验漏掉信任错配。

2026-11-19 用 .NET 与 OpenSSL 交叉验证附件：

| 附件来源 | 旧公钥 | 当前公钥 |
| --- | --- | --- |
| GitHub v5.12.7 | 通过 | 失败 |
| GitHub bot 上传的 v5.13.2 | 通过 | 失败 |
| 维护者上传的 v5.13.3 | 失败 | 通过 |
| GitLab 替换后的 v5.13.4 | 失败 | 通过 |

v5.13.2 的 Core DLL 内置新公钥，包仍由旧私钥签名。v5.13.3 Actions run `43734166973` 显示成功，但构建 job 被 36 小时限制跳过，可用附件由维护者上传；核对发布时必须查看实际执行的 job 与附件来源。

v5.13.4 原附件已替换且未留存，表中为替换后产物。当时原 ZIP 的 SHA-256 与说明一致却验签失败，原文件已无法直接复验。

两把公钥的 SPKI SHA-256：

- 旧：`4d89e6c857f102b044636cc7bbffbafa4a73b4020b42e1cab548cc0f3c55508d`
- 当前：`985a63db5ae42c076fa9cac27fe3616df06bca02672d60db2de808e193dae50d`

哈希用于核对文件，更新器公钥决定签名信任。轮换密钥时须安排旧客户端的升级路径。

## 本地 release notes

GitLab build 在 make.bat 后生成 `dist/release-notes.md`，release job 直接读取它。说明包含 tag、完整提交号、上个 tag 以来的提交、六个附件 SHA-256，以及 ZIP 验签命令和更新器公钥；上传失败后本地仍保留配套 Markdown。

脚本要求 HEAD 对应 tag、六个文件齐全且两个 ZIP 验签通过，失败删除旧 Markdown。输出为无 BOM 的 UTF-8 与 LF，相同输入重复生成一致。

在 tag 对应的 checkout 手动生成：

```powershell
pwsh -NoProfile -File ./generate-release-notes.ps1 -Tag v5.13.4
```

可用 `-RepositoryPath` 与 `-ArtifactDirectory` 指定其他 checkout 和产物，目标 HEAD 仍须对应 tag。

本地 shell runner 的产物通常位于 `builds/<runner-id>/<slot>/<namespace>/<project>/dist`。目录由 runner 分配，后续 job 可能清理或重用；发布存档将六个附件与 Markdown 一起复制。手动上传使用存档原文件，保持说明中的哈希对应。

[ReleaseNotesScriptTests](../Tests/Core/Services/Update/ReleaseNotesScriptTests.cs) 覆盖 tag、文件完整性、哈希、编码、重复生成和失败清理。

## 两个平台的产物一致性

当前两条流水线独立构建，完整发布目录的逐字节复现尚未实现。Deterministic 编译的“相同输入”仍需固定各层条件：

| 层次 | 需固定的输入 |
| --- | --- |
| DLL/PDB | SDK、依赖、参数、源码换行、路径映射、Source Link、WPF 资源 |
| 发布目录/安装器 | 原生 apphost、运行时、Inno Setup 与打包元数据 |
| ZIP | 文件顺序、时间戳、压缩器和参数 |
| .sig | 签名算法的确定性；当前 ECDSA 对同一数据签名五次得到五个不同但有效签名 |

跨平台分发优先采用一次构建、一次签名的同一组附件。独立重建需单独固定环境并持续定位差异；ZIP 时间戳实验与 ECDSA 重签实验已说明差异不只来自 DLL。

技术依据为 Microsoft Learn 的 C# Deterministic 编译选项与 RFC 6979；后者描述确定性 ECDSA，本项目保留当前签名实现。

## 发布与保留

产物上传 generic package registry，release 通过 assets links 引用；一周过期的 job artifacts 用于短期取回。发布下载地址须允许客户端取得 ZIP 和相邻签名。

`KEEP_PACKAGE_VERSIONS=3`。上传并创建 release 后才清理，另保留本次 tag。当前只读取最新 100 个 package，依赖定期成功清理；出现积压时需分页。

清理使用 CI_JOB_TOKEN 或 PACKAGE_TOKEN，删除权限与上传权限分别检查，清理结果以 API 响应和 job 日志为准。
