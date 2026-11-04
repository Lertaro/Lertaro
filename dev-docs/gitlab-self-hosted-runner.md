# 自建 GitLab Windows runner 与 release 链路

## 适用范围与核对基线

按 `main` = `fd0a9644`（v5.12.5）核对，`.gitlab-ci.yml` 的改动（`KEEP_PACKAGE_VERSIONS` 与发布后的旧包清理）尚未提交。runner 版本 19.5.2，注册在项目 `gitlab.com/Mikachu2333/lertaro`，安装目录 `C:\gitlab-runner`（目录名任意，`--working-directory` 决定 builds 落点）。核对日期 2026-11-04。

本文只讲"在哪建、怎么起、谁删旧包"。产物文件名与更新器的约定在 `pitfalls.md:229`，`make.bat` 的构建步骤不在本文范围。

## 为什么在开发机上建，而不是别的办法

- ** installer 只能在 Windows 出**：`make.bat` 用 Inno Setup 7 的 `ISCC.exe` 编安装器，Inno Setup 没有跨平台编译器（Linux 上只能靠 wine）。`EnableWindowsTargeting` 之类的交叉构建能出 portable zip，出不了 `Setup.exe`。
- **GitLab.com 免费档其实有 hosted Windows runner**：tag `saas-windows-medium-amd64`，Tier 标 Free/Premium/Ultimate，状态 beta，2 vCPU / 7.5 GB。没走它的原因是配额——Free 每月 400 compute minutes，而一次 x64 + arm64 双 publish 加两遍 Inno Setup 编译就要占掉可观的一块；hosted 镜像里 .NET 具体版本和 winget 可用性都没实测（见"未验证"）。
- **release 页面不能手动上传文件不是免费版的限制**：那条通道走 attachment/project uploads，gitlab.com 历史上限 10 MB，并且官方已把它指向 CI 路径。本仓库 `.gitlab-ci.yml` 的形状就是官方推荐形状：build 出 `dist/` → 用 `CI_JOB_TOKEN` PUT 进 generic package registry → release 只带 `assets.links`（`direct_asset_path` 让下载链接长得像站内直链）。更新器要 `<zip url>.sig`，所以每个 zip 的签名必须以 `<zip>.sig` 同名同版本并排发布。

## 部署与启动

1. 网页建 runner：项目 Settings → CI/CD → Runners → New project runner，**Tags 填 `windows`**，拿到一次性 `glrt-` token。
2. 注册：`gitlab-runner register --url https://gitlab.com --token glrt-… --executor shell --shell pwsh`。token 以明文写进 `config.toml`，`verify -c config.toml` 是检查它是否还有效的最快的办法。
3. 起：`gitlab-runner.exe run --working-directory C:\gitlab-runner --config C:\gitlab-runner\config.toml`。这是用户态前台进程，**终端关掉就没人领 job**，之后的 pipeline 会一直 pending 到人手动取消——最初那次 v5.12.5 的失败（`release_guard` canceled、`build`/`release` skipped）就是这个机制：pipeline 建于注册之前，`tags: [windows]` 没有任何 runner 匹配。
4. tag 只存在服务端，`config.toml` 不存 tag，要改得回网页；job 侧和 runner 侧的 `windows` 必须一致。
5. 不要勾 "Run untagged jobs"：shell executor 跑不了带 `image:` 的 job，接了只会失败。这个 runner 是项目级，别的仓库（例如用 alpine 镜像的 genshin_words）本来也不归它管。

## 为什么没装成 Windows 服务

- Inno Setup 7 是**用户级**安装，落在 `%LOCALAPPDATA%\Programs\Inno Setup 7\ISCC.exe`；`make.bat` 只探测这一处和 `C:\Program Files\Inno Setup 7`。
- build job 的兜底 `winget install JRSoftware.InnoSetup.7` 在 LocalSystem 会话里不可用（winget 属于用户级的 App Installer）。
- 其余工具链反而是机器级的，换谁都能解析：PATH 里有 `C:\Program Files\dotnet`（SDK 10.0.403）、`C:\Program Files\Git\cmd`、`C:\Program Files\PowerShell\7\`；签名用的 `openssl` 走 `make.bat` 的绝对路径探测（`C:\Program Files\Git\usr\bin\openssl.exe`）。
- 真要常驻服务，两条路：用 `/ALLUSERS=1` 把 Inno Setup 重装到 Program Files，或者 `gitlab-runner install -p` 以 `Linked` 身份跑（代价是密码进服务配置，改期即停）。当前选择是登录后手动前台跑。

## 发布链路需要的 CI/CD 变量

- `LERTARO_SIGNING_KEY`，必须是 **File 型**：不设则 build 直接 throw，因为无签名的 portable 包会被更新器拒装。File 型由 GitLab 把 PEM 写成 job 本地文件、变量值是那个路径，恰好就是 `make.bat:197` 读的名字，所以脚本不再做任何解码或转写。原先的 `SIGNING_KEY`（同时接受路径 / 多行 PEM / base64 三形态）已删掉——第一次实跑就是被这个命名差一层挡住的（GitLab 上建的是 `LERTARO_SIGNING_KEY`，脚本查的是 `SIGNING_KEY`）。两个坑：勾了 **Protected** 而发布 tag 不在保护规则里时 GitLab 根本不下发变量，症状与"没设"完全一样；建成普通变量存 PEM 文本则会被第二道"路径必须存在"的检查提前炸掉。GitHub 侧不变，仍是 `secrets.SIGNING_KEY` 由 workflow 自己写文件再导出 `LERTARO_SIGNING_KEY`。
- `PACKAGE_TOKEN`（可选）：旧包清理默认用 `CI_JOB_TOKEN` 调 `DELETE /projects/:id/packages/:package_id`。文档没把 delete 写进 job token 的权限表，所以第一次很可能 403；那时建一个 Project Access Token（scope `api`，Maintainer）存成这个变量，release job 会自动改用它。不设它不影响发布，只是旧包一直堆着。

## 存储与保留数字

一次 release 是 6 个文件、约 29 MB（2026-11-04 实测 `dist/`：`Lertaro-Setup.exe` 7.29 MB、`Lertaro-Setup-arm64.exe` 6.37 MB、`Lertaro-Portable.zip` 7.89 MB、`Lertaro-Portable-arm64.zip` 7.54 MB、两个 `.sig` 各 71 B）。generic package 永不过期，`artifacts` 那份反倒有 `expire_in: 1 week`。

`KEEP_PACKAGE_VERSIONS: "3"` 的窗口语义，三条是不可省的理由：

- 清理排在 release 创建之后，此时当前版本已经进了包列表，才会把旧版本顶出窗口；反过来先删后传会留出"删了好的、传 Failed"的空档。
- 手动重跑老 tag 时，刚 PUT 的版本未必落在 `created_at` 首位，所以跳过窗口之后还要再过一道 `-ne $tag`，保证当前版本永不进删除列表。
- 只看最新 100 个包（6 个一批约 16 个版本）。因为每次发布都清，积压不会超过一页；这条上限写在 YAML 的 `ponytail:` 注释里。

## 未验证

- hosted `saas-windows-medium-amd64` 镜像里的 .NET SDK 版本与 winget 可用性；真要用它，`build` 里 Inno Setup 的兜底安装得换成直接下载静默安装包。
- `CI_JOB_TOKEN` 在现行 gitlab.com 上能否删包（以及 generic package 的 PUT 是否需要额外开关）——都要等第一次实跑的日志。
- 一次完整 release 在这台机器上的实际耗时；`concurrent = 1`，构建期间这台机器会被占住。
- project uploads 通道的现行大小上限（10 MB 来自 GitLab 2017 年的 issue，未按现行 gitlab.com 核实）。
