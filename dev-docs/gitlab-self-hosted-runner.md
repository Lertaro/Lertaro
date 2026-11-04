# 自建 GitLab Windows runner 与 release 链路

## 适用范围与核对基线

按 `main` = `19cadbbb`（v5.12.6）核对，尚未提交的那一组改动覆盖 `make.bat`、`.gitlab-ci.yml`、`.github/workflows/release.yml` 与本文（统一后的 key 契约）。runner 版本 19.5.2，注册在项目 `gitlab.com/Mikachu2333/lertaro`，安装目录 `C:\gitlab-runner`（目录名任意，`--working-directory` 决定 builds 落点）。核对日期 2026-11-04。

本文只讲"在哪建、怎么起、谁删旧包"。产物文件名与更新器的约定在 `pitfalls.md:229`，`make.bat` 的构建步骤不在本文范围。

## 为什么在开发机上建，而不是别的办法

- ** installer 只能在 Windows 出**：`make.bat` 用 Inno Setup 7 的 `ISCC.exe` 编安装器，Inno Setup 没有跨平台编译器（Linux 上只能靠 wine）。`EnableWindowsTargeting` 之类的交叉构建能出 portable zip，出不了 `Setup.exe`。
- **GitLab.com 免费档其实有 hosted Windows runner**：tag `saas-windows-medium-amd64`，Tier 标 Free/Premium/Ultimate，状态 beta，2 vCPU / 7.5 GB。没走它的原因是配额——Free 每月 400 compute minutes，而一次 x64 + arm64 双 publish 加两遍 Inno Setup 编译就要占掉可观的一块；hosted 镜像里 .NET 具体版本和 winget 可用性都没实测（见"未验证"）。
- **release 页面不能手动上传文件不是免费版的限制**：那条通道走 attachment/project uploads，gitlab.com 历史上限 10 MB，并且官方已把它指向 CI 路径。本仓库 `.gitlab-ci.yml` 的形状就是官方推荐形状：build 出 `dist/` → 用 `CI_JOB_TOKEN` PUT 进 generic package registry → release 只带 `assets.links`（`direct_asset_path` 让下载链接长得像站内直链）。更新器要 `<zip url>.sig`，所以每个 zip 的签名必须以 `<zip>.sig` 同名同版本并排发布。

## 部署与启动

1. 网页建 runner：项目 Settings → CI/CD → Runners → New project runner，**Tags 填 `windows`**，拿到一次性 `glrt-` token。
2. 注册：`gitlab-runner register --url https://gitlab.com --token glrt-… --executor shell --shell pwsh`。token 以明文写进 `config.toml`，`verify -c config.toml` 是检查它是否还有效的最快的办法。
3. 起：`gitlab-runner.exe run --working-directory C:\gitlab-runner --config C:\gitlab-runner\config.toml`。这是用户态前台进程，**终端关掉就没人领 job**，之后的 pipeline 会一直 pending 到人手动取消——最初那次 v5.12.5 的失败（`release_guard` canceled、`build`/`release` skipped）就是这个机制：pipeline 建于注册之前，`tags: [windows]` 没有任何 runner 匹配。v5.12.6 由同一个 runner 领走并一路跑到 `make.bat`，tag 匹配与 CI 变量下发（含 Protected 状态下）都已证实。
4. tag 只存在服务端，`config.toml` 不存 tag，要改得回网页；job 侧和 runner 侧的 `windows` 必须一致。
5. 不要勾 "Run untagged jobs"：shell executor 跑不了带 `image:` 的 job，接了只会失败。这个 runner 是项目级，别的仓库（例如用 alpine 镜像的 genshin_words）本来也不归它管。

## 为什么没装成 Windows 服务

- Inno Setup 7 是**用户级**安装，落在 `%LOCALAPPDATA%\Programs\Inno Setup 7\ISCC.exe`；`make.bat` 只探测这一处和 `C:\Program Files\Inno Setup 7`。
- build job 的兜底 `winget install JRSoftware.InnoSetup.7` 在 LocalSystem 会话里不可用（winget 属于用户级的 App Installer）。
- 其余工具链反而是机器级的，换谁都能解析：PATH 里有 `C:\Program Files\dotnet`（SDK 10.0.403）、`C:\Program Files\Git\cmd`、`C:\Program Files\PowerShell\7\`；签名用的 `openssl` 走 `make.bat` 的绝对路径探测（`C:\Program Files\Git\usr\bin\openssl.exe`）。
- 真要常驻服务，两条路：用 `/ALLUSERS=1` 把 Inno Setup 重装到 Program Files，或者 `gitlab-runner install -p` 以 `Linked` 身份跑（代价是密码进服务配置，改期即停）。当前选择是登录后手动前台跑。

## 发布链路需要的 CI/CD 变量

两条形状规则是实跑踩出来的，先记住它们，原因在下面的条目里：

1. GitLab 上 `LERTARO_SIGNING_KEY` 的类型必须是 **Variable**，不是 **File**：File 型交给 job 的是文件路径，而契约要的是内容。
2. 开发机上的 `LERTARO_SIGNING_KEY` 必须是**系统（机器）级**环境变量，不是用户级；设完要新开一个终端再启动 runner。

- `LERTARO_SIGNING_KEY`：**普通 Variable，不是 File 型**，值是**整个 key 文件压成的一行 base64**（`openssl base64 -A -in lertaro-release.key`）。这是三处共用的唯一契约——本机、GitLab job、GitHub workflow 都只认这一个形态，PEM 文件路径不再是任何一方的输入。
  - 谁解码：只有 `make.bat`。它在两个架构构建之前把 base64 解到 `%TEMP%\lertaro-release-<random>.key`，签完两个 zip 后在清理步骤删除；进入时先扫掉 `%TEMP%\lertaro-release-*.key`，所以上一次崩溃留下的副本不会被复用，而中途 `exit /b 1` 漏下的也在这一步收走。GitLab 的 build job 只做形状校验（能解码、解出来有 `-----BEGIN`）就放行构建——解码不做第二遍，但形状错了要在二十分钟构建之前发现而不是之后。
  - File 型为什么不行：File 变量交给进程的是路径而不是内容，把路径当 base64 解必然失败，所以报错文案点名了这一点。
  - 没有 key 是错误不是警告：无签名的 portable 包会被更新器拒装，`make.bat` 在变量缺失时直接 `exit /b 1`；GitHub 那侧同样改成 `::error` + 退出，原来"缺 key 就写一个 unsigned 占位 sig"的分支已删。
  - 本机一次性设置：`setx /M LERTARO_SIGNING_KEY "<那一行 base64>"`，需要管理员 shell。**必须是系统（机器）级变量，不是用户级**——实测机器级有值（312 字符，开头 `LS0tLS1CRUdJ`，即 `-----BEGIN` 的 base64）、用户级没有，job 才读得到。另外任何已启动的进程用的都是自己启动那一刻的环境快照，`setx` 不会追认：所以设完变量要**新开一个终端**再启动 runner，从旧终端里起的 runner 依然看不到它。旧的 `<Documents>\lertaro-release.key` 默认路径已删除，不设变量的本机打包直接失败，这是有意的。
  - 兜底签名的静默失败（2026-11-04 实测）：没有 openssl 时 `make.bat` 用 pwsh 的 ECDsa 签。原写法里 .NET 调用失败是**非终止错误**——pwsh 打印异常、继续跑 `WriteAllBytes`、退出码 0，于是落出一个 139 字节、DER 结构合法但对任何公钥都验不过的 blob；`if errorlevel 1`、`if not exist` 和 CI 里的 `-lt 8` 三道检查全部放行。现在那条命令以 `$ErrorActionPreference='Stop'` 开头，并在写文件前用同一把 key 做一次 `VerifyData` 自检。实测：坏 key → `[Error] Signing ... failed`、exit 1、不产生 `.sig`；好 key → 70 字节签名、`Verified OK`。
  - GitHub 侧：`secrets.SIGNING_KEY` 继续存 PEM 原文，workflow 取 `UTF8.GetBytes` 后 base64 编码再导出，与本机那份逐字节相同（实测）。
  - 其余实测数字：Git 自带的 openssl 3.5.7 与 `C:\Program Files\OpenSSL-Win64` 的 4.1.0 都能签 LF 和 CRLF 两种 PEM；runner 里 `Get-Command openssl` 先命中 `C:\Program Files\Git\mingw64\bin\openssl.exe`；ECDSA 的 DER 签名长度在 70–72 字节之间浮动属正常，别按固定长度判存在（release job 用的是 `-lt 8`）。
- `PACKAGE_TOKEN`（可选）：旧包清理默认用 `CI_JOB_TOKEN` 调 `DELETE /projects/:id/packages/:package_id`。文档没把 delete 写进 job token 的权限表，所以第一次很可能 403；那时建一个 Project Access Token（scope `api`，Maintainer）存成这个变量，release job 会自动改用它。不设它不影响发布，只是旧包一直堆着。

## 存储与保留数字

一次 release 是 6 个文件、约 29 MB（2026-11-04 实测 `dist/`：`Lertaro-Setup.exe` 7.29 MB、`Lertaro-Setup-arm64.exe` 6.37 MB、`Lertaro-Portable.zip` 7.89 MB、`Lertaro-Portable-arm64.zip` 7.54 MB、两个 `.sig` 各 71 B）。generic package 永不过期，`artifacts` 那份反倒有 `expire_in: 1 week`。

`KEEP_PACKAGE_VERSIONS: "3"` 的窗口语义，三条是不可省的理由：

- 清理排在 release 创建之后，此时当前版本已经进了包列表，才会把旧版本顶出窗口；反过来先删后传会留出"删了好的、传 Failed"的空档。
- 手动重跑老 tag 时，刚 PUT 的版本未必落在 `created_at` 首位，所以跳过窗口之后还要再过一道 `-ne $tag`，保证当前版本永不进删除列表。
- 只看最新 100 个包（6 个一批约 16 个版本）。因为每次发布都清，积压不会超过一页；这条上限写在 YAML 的 `ponytail:` 注释里。

## 首次成功发布的实测

pipeline #5（tag `v5.12.6` = `a8b87124`，2026-11-04 13:25 UTC）三段全绿：`release_guard` 12 s、`build` 80 s、`release` 37 s，整条约 1 分 39 秒。之前按 hosted runner 的规格估的"一次 20–40 分钟"是错的——本机有热 NuGet 缓存，两架构 publish + 两遍 Inno Setup + 签名只要 80 秒，Free 档 400 compute minutes/月因此完全不是瓶颈（一个月发 40 次也用不完）。

发布物本身按更新器走的路径复验过：

| 检查 | 结果 |
|---|---|
| 匿名 `GET` generic package（zip 与 `<zip>.sig`） | 200，zip 7 894 081 / 7 538 120 字节，两个 `.sig` 各 71 字节 |
| 用 `UpdatePackage.cs` 内公钥验签（x64 与 arm64） | 两个都 `Verified OK` |
| 下载文件 sha256 对 release notes | 6 项全一致 |

匿名可读这一点是契约的一部分：更新器和用户都不需要任何 token，`<zip url>.sig` 并排解析也成立。

## 未验证

- hosted `saas-windows-medium-amd64` 镜像里的 .NET SDK 版本与 winget 可用性；真要用它，`build` 里 Inno Setup 的兜底安装得换成直接下载静默安装包。
- `CI_JOB_TOKEN` 能否删包仍未证实：PUT 上传与创建 release 已由首次成功的 pipeline 证明可用，但 `DELETE /projects/:id/packages/:package_id` 的结果只在 job trace 里，而 trace 匿名访问是 401。看日志里那行是 `Pruned N old package file(s)` 还是 `Prune of package ... refused`；若是后者，建一个 Project Access Token（scope `api`）存成 `PACKAGE_TOKEN`，release job 会自动改用它。
- 发布前没有任何一处拿 `UpdatePackage.cs` 里的公钥验签：build job 只判 `.sig` 是否 `-lt 8`，`make.bat` 只用自己的私钥自检。也就是说"用错了一把格式正确的 key"仍然能发出去，用户侧才第一次失败。要收口就在 build job 里用源码抽出的公钥对两个 zip 各验一次。
- project uploads 通道的现行大小上限（10 MB 来自 GitLab 2017 年的 issue，未按现行 gitlab.com 核实）。
