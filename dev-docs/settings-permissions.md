# 设置读写的权限变迁与 Low 完整性下的实际故障

## 适用范围与核对基线

覆盖 Lertaro 启动器（WPF App 加 LocalSystem 后台服务）的用户设置读写、机器设置读写、启动门与完整性标签。

调查区间从 `d883625f`（2026-10-03 13:44 -0700）到 `37ea0ba5`（2026-10-13 02:15），区间 122 个提交，处置提交 `cfa8302f`（2026-10-13 12:46）。依据只有 `git show` 的实际 diff 与一次性探针的实测，先前文档里的结论不采信。

核对基线 `main` = `9a6b9769`。文中 `文件.cs:行号` 按这个基线复核过。崩溃日志里的行号属于产生它的那个构建，拿来对现行源码会错。基线之后落地的 `ef1fb63f` 与 `334d7980` 只动 installer 侧（`Installer/installer.iss` 与安装脚本），不在这条设置读写路径上。

环境侧的标签会变，跑命令前先确认当前状态。`9a6b9769` 复核时：`%LOCALAPPDATA%\Lertaro` 已无 Mandatory Label ACE（回到 Medium 默认），`<repo>` 根仍带 `Mandatory Label\Low Mandatory Level:(OI)(CI)(NW)`，`LertaroService` 的登录账户仍是 LocalSystem（当时 State=Running，复核时 Stopped / StartMode=Auto）。矩阵里 Low 那一列要先自己给目录打标签才复现得出来。

本机才有的标识（用户 SID、计算机名、沙箱配置组名、进程 PID）只保留"存在"这一事实，不记录具体值。`S-1-16-4096` / `S-1-16-8192` 是 Low 与 Medium 完整性的平台常量，保留。

Low 这一族问题只落在带标签的树上。`<repo>` 的可继承 Low 标签（沙箱层）落到 exe 镜像上，与程序代码无关；仓库外的拷贝目录和 `%ProgramFiles%\Lertaro` 这样的安装目录没有标签，始终 Medium，碰不到这些。

## 结论速览

1. 程序跑成 Low 与设置代码无关。`<repo>` 自身带可继承的 Low 标签（`icacls`：`Mandatory Label\Low Mandatory Level:(OI)(CI)(NW)`），构建产物继承为 `(I)(NW)`。Windows 不会让进程高于镜像标签启动，从 Medium 的 shell 派生它仍然是 Low。当时实测：运行中的 App 进程自读令牌 Low `S-1-16-4096`，同一份探针二进制复制到仓库外的无标签目录后是 Medium `S-1-16-8192`。变量是文件标签，换启动方式换不出 Medium。
2. 用户设置文件的读与写，在 Low 下已全部通畅。探针在 Low 进程内实测：读 `user-settings.json` OK；`Verify` 的建-写-删探针与 Owner 校验都 OK；`AtomicFileStore.Write` 的 temp 加 `File.Replace` OK；`.bak.N` 轮转也 OK。"设置写不进去"不是当前的故障。`58e7ae14` 之后这套语义第一次可以被实测验证，读有回退链，写走原子替换，失败返回 false 并回退缓存。
3. 当前真正坏掉的是三件事，都不是用户设置目录的 DACL。
   - `LocalSendCertificate` 在 Low 下抛 `CryptographicException`，崩在 `App.OnStartup`（现行源码的调用点在 `App/App.xaml.cs:331`），而 `DispatcherExceptionHandler` 当时把 `args.Handled = true`，进程于是不会退出，永远卡在 5 秒一次的 `LaunchHook` 重试环里。双击就出问题指的是这个。两处修订：①"没有窗口"当时从 `MainWindowTitle` 为空推出来，该判据不成立（见"更正过的说法与坑"）；②直接修法 `EphemeralKeySet` 经实测否决，它把 LocalSend 的 TLS 握手段掉。
   - 服务管道 `LertaroPipe`：Low 客户端读得到、写不进（`PipeDirection.In` OK，`InOut` 被拒）。App 所有需要服务的动作（LaunchHook、Status、`SaveMachineSettings`，以及未提交的 `SetDataFolderIntegrity`）走的都是 `InOut`，全部失败。
   - HKCU 注册表写入在 Low 下 `LSTATUS=5`（启动项、`Software\Classes\lertaro`）。
4. `37ea0ba5` 的进程内打标签是死代码。它用 `SetFileSecurityW(..., SACL=0x8, "S:(ML;;NW;;;LW)")`，实测在 Low 和 Medium 都返回 win32=1314（`ERROR_PRIVILEGE_NOT_HELD`），按路径写 SACL 需要 `SeSecurityPrivilege`，普通令牌没有。同一时间工作区里未提交的 `DirectoryLockNativeMethods.LowerIntegrityLabelToLow`（句柄 + `WRITE_OWNER` + `LABEL_SECURITY_INFORMATION=0x10` + `SetKernelObjectSecurity`）实测两条上下文都能写标签：Low 进程对已 Low 的对象成功，Medium 进程把一个 Medium 目录降到了 Low（icacls 复核为 `Mandatory Label\Low Mandatory Level:(NW)`）。两条路线、线协议 id 23 与它的服务端处理已在 `cfa8302f` 全部删除，理由见"已处置"。
5. 未提交方案里"必须交给 LocalSystem 服务"的前提被实测推翻了一半。正常的 Medium 启动（所有真实用户）里 App 自己就能打标签，不需要服务往返。Low 启动里服务那条路恰好不可达，因为 App 写不进管道。现在机器上数据目录的 Low 标签不可能是 HEAD 的代码打的（HEAD 只会 1314），能打上它的只有 Medium 上下文的工作区构建（句柄路线）、服务（SYSTEM）、提权修复或手工 `icacls`。9a6b9769 复核两处：`%ProgramData%\Lertaro` 现在 `icacls` 读得到（`SYSTEM`/`Administrators:(OI)(CI)(F)`、`BUILTIN\Users:(OI)(CI)(RX)`），"对本 shell 无法取证"那句作废，`logs` 子目录本身仍然被拒；数据目录根的标签是不带 `(OI)(CI)` 的 plain `(NW)`，而句柄路线写的是 `S:(ML;OICI;NW;;;LW)`，形状不同，当前这个标签出自手工 `icacls`，不是代码。

## 时间线

只列改变设置读写语义或启动判定的提交，逐条按 diff 核对。

| 提交 | 日期 | 这一步对设置读写做了什么 | 后果 |
| --- | --- | --- | --- |
| `d883625f` 之前 | — | `AtomicFileStore.Write` 已在用，`UserSettingsBackupStore.Rotate` 的失败只记一条 `LogLevel.Warn` | 没有权限门，保存失败会被吞 |
| `d883625f` | 10-03 | 新建 `InstallDirectoryLock`(221)、`PortableDirectoryLock`(92)、`DirectoryLockNativeMethods`(112)、`CallerVisibility`。范围是 `%ProgramData%` 共享目录和便携版 exe 同级目录，没碰 `%LOCALAPPDATA%\Lertaro`；`Core/Settings/`、`UserDataAccess`、启动门都没动 | 机器设置改为只能由服务写（`App/ViewModels/Settings/SettingsViewModel.cs:259` 先要求 `IsUserAdmin && IsServiceReady`，`:269` 走 `SaveMachineSettingsAsync`）；自己构造的裸 ACE 漏了 `SYNCHRONIZE` |
| `6e041f56` | 10-04 | 裸 ACE 补上 `FileSystemRights.Synchronize`；另加 `HasZone` 探测与 `PortableDirectoryLock.IsCurrent`，服务每次 `--service` 启动重新上锁 | 对设置读取的第一次真实伤害与修复（issue #316，App 读不了 `machine-settings.json`） |
| `193327c7` | 10-05 | `HasZone` → `GrantsAtLeast` | 手工加过的授权不再导致每次启动重走整棵树。不改读写语义 |
| `f1909502` | 10-06 | 提交信息写 "restore portable user data permissions"，diff 里一处 `SetSecurity`/ACL 写入都没有，改的是 `IsCurrent` 的判定范围（纳入 `Data\Users` 与按 SID 的目录名，同时哈希 `Hash(sid)` 与 `Hash(Hash(sid))` 两种拼法） | 按"信息不可信、以 diff 为准"的口径，这条不算权限写入的变迁 |
| `58e7ae14` | 10-06 | 第一次把 ACL 写到用户设置目录上，同时立起启动门：新建 `UserDataAccess`，`Verify` 建目录时置受保护 DACL；写路径从静默变响亮；提权修复 `RepairProfileUserDirectory` 沿 profile → AppData → Local → Lertaro 重写 ACL | 设置保存失败不再被吞掉，代价是启动阶段多了一道能拒绝启动的门，用户设置目录的 DACL 从此可能被外部重写 |
| `7727f9fb`、`1683d0d2` | 10-06、10-07 | 新增 `Core/Settings/Backup/*` 与 `OpenForBackup`（拒 reparse 与 hardlink，不写 DACL）；`OnStartup` 里加设置会话租约；`PluginSettingsStore.Save` 提到主保存之前；租约放进 `%LOCALAPPDATA%\Lertaro\RuntimeLocks` | sidecar 写失败 ⇒ 整次保存失败；租约冲突对所有成因共用同一句话（"another instance is transferring settings"），误报由 `c75e51dd` 收掉 |
| `61cfcc1c` | 10-10 | `CallerVisibility` 取消对每个索引路径的模拟 `CreateFileW`，改为词法判定 `IsIndexedPathVisible` | 不涉及设置读写。一次为了速度把权限判定从内核侧收回用户侧的取舍 |
| `b8928931`、`9a726380`、`c75e51dd`、`d7d93d4c`、`52ecedb3` | 10-12 | `DataDirectoryResolver.ResolveFinalTarget` 沿路径自卷根向下解链（最多 8 轮，只接受本地固定卷）；`plugin-settings.json` 回归可选；新增 `PermissionRepairReport`、`DescribeRepairFailure`、`UserDataAccess.TryGetOwner` 回读校验，`InstallerTimeoutMs 30000→120000`；`DescribeRelocationRefusal` → `SettingsDataRootUnsupportedException` → MessageBox + `Shutdown(1)` | 不写 ACL，不碰完整性标签。修的是"数据目录挪到别的盘却被当成设置传输失败"与 sidecar 缺失炸读取；整条提权修复链从此可诊断；启动前拒绝的那条针对不受支持的卷，与权限判定无关 |
| `3aae4792` | 10-12 21:20 | 提交信息只有 "1"。新增 `ProcessIntegrityLevel.Current()`，任何低于 Medium 的令牌在服务与提权修复被提供之前就拒绝启动；同批 `OpenWithoutFollowing(..., pin: true)` 与 `RepairTargets` | 区间内唯一一处按令牌长相而非目录可用性判定的启动阻断 |
| `1eb78154` | 10-13 00:25 | 提交信息只有 "2"，`git show --stat` 是 `App/logo.ico` 同尺寸二进制替换，0 insertions | 与权限、设置完全无关 |
| `37ea0ba5` | 10-13 02:15 | 删除 `ProcessIntegrityLevel.cs` 与 `RepairCannotHelpReason`；新增 `UserDataAccess.SetLowIntegrityLabel`（路径式 SACL），外面包一层 `catch { }` | 移除阻断的方向正确。打标签实测 1314，在"让降权启动能写设置"上的实际效果是 0 |
| 工作区（当时未提交） | 10-13 11:16 前 | `UserDataAccess` 删掉 `SetLowIntegrityLabel`，`Verify` 只做探针；新增 `SearchRequestId.SetDataFolderIntegrity = 23`（无载荷）与服务端 `SetDataFolderIntegrity(pipe)`，用调用者令牌推导目录后降标签；`LocalSendCertificate.LoadFlags` 加 `EphemeralKeySet` | 服务那一跳在 Low 下不可达；`EphemeralKeySet` 修掉了崩溃却打断 TLS。全部由 `cfa8302f` 处置，本行只作过程记录 |
| `cfa8302f` | 10-13 12:46 | 删除整套标签机制（连同 `37ea0ba5` 的无效打标签）；`EphemeralKeySet` 回退为 `Exportable` | `UserDataAccess.Verify` 的写探针回到唯一判据，目录确实不可写时照旧弹框并拒绝启动；启动期异常改为报告后退出；`HookIpcClient` 重试倍增封顶 60 秒 |

`App/app.manifest` 在 `d883625f..9a6b9769` 区间无改动（`git log` 为空），当前 `App/app.manifest:8` 是 `level="asInvoker" uiAccess="false"`。程序既不要求提权，也不自我降权，Low 完全来自文件标签。

### d883625f 埋下的那颗雷

`InstallDirectoryLock` 手写 ACE，绕过了 `FileSystemAccessRule` 会自动补 `Synchronize` 那一层，而 `FileStream` 和目录枚举请求的 `GENERIC_READ` 含 `SYNCHRONIZE`，`ReadAndExecute` 于是整条被拒。锁的本意是把共享目录留给 Users 读，效果是 Users 连读都读不了。`Core/Services/Installation/InstallDirectoryLock.cs:264-270` 的注释记录了这个原因和 issue #316。同批的另一半后果要几轮之后才暴露：机器设置的唯一写入通道收进了服务。实测过 Low 和 Medium 都读得到 `%ProgramData%\Lertaro\machine-settings.json`，两者都写不进该目录。

### 58e7ae14，整条链的转折点

四件事同时发生。

1. 新建 `Core/Services/Installation/UserDataAccess.cs`(+39)。`Verify` 的形状与今天一致：目录不存在就用 `InstallDirectoryLock.Describe(PortableDirectoryLock.OwnedBy(user), isZoneRoot: true, ...)` 创建，`Describe`（`Core/Services/Installation/InstallDirectoryLock.cs:228-232`）在 `isZoneRoot` 时置 `ControlFlags.DiscretionaryAclProtected`，用户设置目录从此变成受保护 DACL；然后是拒链接、拒 Owner 非本人、真实建-写-删探针。今天该文件 61 行，写探针在 `UserDataAccess.cs:29-33`。
2. 新建启动门 `AppStartupServiceBootstrapper.cs`(+31)，当时在 `App.xaml.cs:80` 调用，现在位于 `App/App.xaml.cs:72`：

   ```csharp
   if (!await AppStartupServiceBootstrapper.PrepareUserSettingsAsync()) { Shutdown(1); return; }
   Logger.Initialize("app.log", overwrite: true);
   ```

   `overwrite: true` 意味着每次启动截断 app.log（现行 `App/App.xaml.cs:78`），这是后续取证困难的根源之一，当时本机 app.log 只剩一个会话。
3. 写路径从静默变响亮：`AtomicFileStore.Write` 改为每次写唯一的 `{path}.{guid}.tmp` + `FileMode.CreateNew` + `FlushToDisk`；`UserSettingsBackupStore.Rotate` 去掉原来的 `catch → LogLevel.Warn`，轮转失败向上抛（今天这个方法里已经没有 catch）；`Save` 里删掉 `Directory.CreateDirectory(Logger.UserDataDir)`；即使内容未变也持久化；失败时 `_cachedSettings = null`（`Core/Settings/UserSettingsPersistence.cs:119`、`:135`），保证 `Load()` 不会谎报已保存；`LoadFromDisk` 新增备份全坏就抛 `InvalidDataException`（`:74`）。
4. 提权修复 `RepairProfileUserDirectory`（`Core/Services/Installation/PortableDirectoryLock.cs:98`）沿 profile → AppData → Local → Lertaro 走 `Lock()`，也就是 SYSTEM 或管理员重写上用户设置目录的 ACL。issue #342 那类"报告成功、下次启动仍被拒"的场景由此而来，`c75e51dd` 的 Owner 回读校验是为它补的。

对读写语义的净效果：失败不再被吞掉，代价是一道能拒绝启动的门，加上用户设置目录的 DACL 从此可能被外部重写。

### 3aae4792，唯一一次按令牌长相阻断启动

```csharp
// AppStartupServiceBootstrapper.cs @ 3aae4792:53-54
if (Core.Services.Installation.UserDataAccess.RepairCannotHelpReason(Logger.UserDataDir) is { } reduced)
    throw new UnauthorizedAccessException(reduced);
```

语义是任何低于 Medium 的令牌，在服务和提权修复被提供之前就拒绝启动。同批还有两件真实修复：`OpenWithoutFollowing(..., pin: true)` 让 pinned 祖先不再请求 `DELETE`，修掉提权修复在 `%USERPROFILE%` 上撞 `ERROR_SHARING_VIOLATION`；`RepairTargets` 让便携版要修的是 exe 旁边的实际数据根，而不是只有 profile 默认目录。提交信息是 "1"，这些全靠 diff 才能归类。

现行 `App/Services/ServiceManagement/AppStartupServiceBootstrapper.cs:50-53` 的注释把这个方向写明了：一次失败的检查从不单独拒绝启动，启动由目录之后能不能用来决定，与这个进程的令牌长什么样无关。

### 37ea0ba5，移除门，同时加了一段死代码

```csharp
// git show 37ea0ba5:Core/Services/Installation/UserDataAccess.cs:82-83
if (!ConvertStringSecurityDescriptorToSecurityDescriptorW("S:(ML;;NW;;;LW)", SddlRevision1, out descriptor, out _))
    return;
SetFileSecurityW(directory.FullName, SaclSecurityInformation /* 0x00000008 */, descriptor);
```

实测 Low 和 Medium 都失败于 1314，这一提交真正生效的部分是删掉 3aae4792 的启动阻断。

## Low 完整性实测矩阵

探针是一份一次性小程序，逐行复刻 `Core/AtomicFileStore.cs`、`UserDataAccess.Verify`、`UserSettingsBackupStore.Rotate` 与 `LocalSendCertificate` 的调用形态，而不调用工程代码，避免把被测对象和测试工具混在一起。两种上下文各跑一遍。

| 位置 | 镜像标签 | 进程完整性（自读令牌） |
| --- | --- | --- |
| `<repo>\...`（仓库内） | `Low:(I)(NW)` | Low `S-1-16-4096` |
| 仓库外的同一二进制副本 | 无 | Medium `S-1-16-8192` |

| 被测动作 | Low（`S-1-16-4096`） | Medium（`S-1-16-8192`） |
| --- | --- | --- |
| 读 `user-settings.json` | OK（12055 字节） | OK |
| `Verify` 建-写-删探针（数据目录） | **OK** | OK |
| `Verify` Owner 校验 | OK | OK |
| `AtomicFileStore.Write`（temp + `File.Replace`） | **OK** | OK |
| `.bak.1/.bak.2` 轮转（同一写法） | **OK** | OK |
| 写 `%LOCALAPPDATA%\Lertaro` 内任意文件 | OK | OK |
| 写 `%LOCALAPPDATA%`（Medium 目录） | **FAIL 5 / 拒绝访问** | OK |
| 在 `%TEMP%` 建目录 | **FAIL 拒绝访问** | OK |
| 读 `%ProgramData%\Lertaro\machine-settings.json` | OK | OK |
| 写 `%ProgramData%\Lertaro` | FAIL | **FAIL（同样写不进）** |
| HKCU `...\CurrentVersion\Run` 写值 | **FAIL LSTATUS=5** | OK |
| HKCU `Software\Classes` 建键写值 | **FAIL LSTATUS=5** | OK |
| PFX 载入 `Exportable`（HEAD `LoadFlags`） | **FAIL `CryptographicException`** | OK |
| PFX 载入 `EphemeralKeySet\|Exportable`（工作区） | **OK** | OK |
| 自签 + 导入，`Exportable`（HEAD `CreateEphemeral`） | **FAIL** | OK |
| 环回 TLS 握手（`LocalSendHttpClientFactoryTests`，5 例，测试主机在 Medium） | 未在 Low 复现 | `Exportable` **0 失败** / `EphemeralKeySet\|Exportable` **2 失败** / `EphemeralKeySet` **4 失败** |
| 连 `LertaroPipe` `PipeDirection.InOut`（App 的写法） | **FAIL 拒绝访问** | OK |
| 连 `LertaroPipe` `PipeDirection.In` | **OK（读得到）** | OK |
| 打标签：路径式 `SetFileSecurityW(SACL=0x8)` | **FAIL 1314** | **FAIL 1314** |
| 打标签：句柄 `WRITE_OWNER`+`LABEL(0x10)` | **OK**（对象已 Low） | **OK**（把 Medium 目录降为 Low，icacls 复核） |

表中没有任何一行说明用户设置目录写不进去。失败集中在注册表、密钥库持久化、管道写和比自身等级更高的对象，全是 MIC no-write-up 的典型形状，DACL 都是正确的。

`PFX 载入 OK` 只证明 `LoadPkcs12` 没抛异常，不证明 LocalSend 可用；能用不能用要看握手那一行。

## 因果链

### LocalSend 私钥持久化撞 Medium 密钥库，随后被吞成僵尸

app.log（当时的会话，唯一一个）：

```
2   [10:48:10.115][Warn] [StartupManager] Failed to update startup setting: Requested registry access is not allowed.
5   [10:48:10.117][Info] [App] Running as Administrator: False
97  [10:48:11.495][Error] CRITICAL CRASH (DispatcherUnhandledException):
98  System.Security.Cryptography.CryptographicException: 拒绝访问。
104   ... LocalSendCertificate.CreateEphemeral() in LocalSendCertificate.cs:line 62
105   ... LocalSendCertificate.LoadOrCreate(String path) in LocalSendCertificate.cs:line 46
107   ... LocalSendServiceManager.ApplySettings ... LocalSendServiceManager.cs:line 46
108   ... LocalSendAppEventHandler.Initialize ... LocalSendAppEventHandler.cs:line 32
109   ... Lertaro.App.App.OnStartup(StartupEventArgs e) in App/App.xaml.cs:line 328
115 [10:48:12.910][Info] [AppStartupServiceBootstrapper] Existing service started without elevation.
118 [10:50:12.301][Warn] [HookIpcClient] Service declined hook launch: Access to the path is denied.
...  (每 5 秒一条，到调查结束时仍未停止)
```

- `OnStartup` 在这个异常处中断，进程此后只剩重试环：日志每 5 秒刷一条 `LaunchHook` 被拒，到最后一次读取时（11:35:27）仍在刷。"窗口从未创建"这句只是推断，没有观测依据，其唯一凭据是 `MainWindowTitle` 为空，而 11:51 那次不含崩溃的会话（日志走到 `Starting normal WPF GUI client mode.` 与 `InlineSearchManager started.`）标题同样为空。这个启动器平时就没有带标题的主窗口，标题当不了判据。堆栈里的 `App.xaml.cs:line 328` 属于那个构建；现行源码里 `MainWindow.Show()` 在 `App/App.xaml.cs:313`，LocalSend 的初始化在 `:331`。
- `DispatcherExceptionHandler.Handle` 的 `args.Handled = true` 把启动期致命异常降级成"记一条就继续跑"。
- 这条路径上文件权限是好的。`localsend.pfx` 读得到，失败发生在把私钥写进当前用户的密钥库，那是 Medium 对象。"`EphemeralKeySet` 一项就把两条路径都救活"这句当时写进了报告，实测后撤回：载入确实不抛了，同一改动把 TLS 握手打断（`LocalSendHttpClientFactoryTests` 里 `Exportable` 0 失败、`EphemeralKeySet|Exportable` 2 失败、`EphemeralKeySet` 单用 4 失败）。密钥不入库与能握手，在这个代码路径上不能同时成立。
- 版本归属核对过：崩溃日志来自不含修复的二进制（构建于 10:48:02，修复写于 11:11:36）。崩溃点是 `CreateEphemeral()`，它在 `LoadOrCreate` 的 `try` 之外（现行 `Core/Services/LocalSend/LocalSendCertificate.cs:46`，`try` 覆盖到 `:40`），所以 `catch (CryptographicException)` 那条静默重建并覆盖 pfx 的路径本次未触发，`localsend.pfx` 时间戳仍是 2026-09-02，未被改写。

### 一切需要服务的设置写入在 Low 下不可达

`Core/Services/Search/ServicePipe.cs:44` 用 `PipeDirection.InOut` 连接 `LertaroPipe`，管道对象由 LocalSystem 服务创建，等级高于 Low。实测 Low 下 `In` 能连、`InOut` 被拒。`PipeSecurityFactory` 给 Everyone（WorldSid）与 Authenticated Users 的都是 `PipeAccessRights.ReadWrite`（`Core/Services/Pipe/PipeSecurityFactory.cs:52-66`），权限位给全了，被拒的是对象标签。

后果是 `Status`、`LaunchHook`、`SaveMachineSettingsAsync`（机器设置唯一写入通道），以及未提交方案里 App 请求服务打标签的 `SetDataFolderIntegrity`，在降权启动里全部走不通。app.log 从 10:50 起持续刷 `SendPipeCommand failed for LaunchHook/Status: Access to the path is denied.`。

### 标签这一半，HEAD 打不上，打得上的那半不需要服务

- 数据目录当时的形状（实测 `icacls`）：`%LOCALAPPDATA%\Lertaro` = `SYSTEM`/`Administrators`/当前用户 `(OI)(CI)(F)`，加 `Mandatory Label\Low Mandatory Level:(OI)(CI)(NW)`；`user-settings.json` = `(NW)`，子目录 `logs` 与 `ContentIndex` 同样带 `OICI NW`。DACL 未受保护，仍保留继承 ACE，与同级目录形状一致。9a6b9769 复核时这个目录已经没有任何 Mandatory Label ACE。
- HEAD 的代码打不上这个标签（1314），机器上这个标签不是 HEAD 打的。
- 能打的路线只有句柄 + `WRITE_OWNER` + `LABEL(0x10)`：Medium 进程可以把 Medium 目录降到 Low，Low 进程对已经 Low 的对象也能写，两条都实测过。正常用户的 Medium 启动里 App 自己就能完成，服务往返是不必要的一跳。已经处于 Low 的进程对 Medium 目录既建不了文件也解不开句柄（实测），这一步确实需要更高等级的主体，而服务那一跳此时又因上一节不可达。
- 两处注释当时都不准确，随代码一并消失：`LowerIntegrityLabelToLow` 的注释说 App 不是执行者，工作区 `UserDataAccess.cs:29-33` 的注释说实测 `ERROR_ACCESS_DENIED`（即使请求了 `WRITE_DAC` 和 `ACCESS_SYSTEM_SECURITY`）。实测的失败码是 1314（权限未启用）而不是 5，且它只属于路径式 SACL 写法，换句柄写法在同等级对象上是成功的。现行的 `UserDataAccess.cs:29-33` 是写探针的注释，与那两句无关。

### 设置文件时间戳不能当失败证据

`user-settings.json` 与 `search-history.json` 停在 10-12 14:49，`plugin-settings.json` 停在 10-09 08:44，而 10:48 起有一个会话在跑。表面像"写不进去"，实际是这个会话在第一条因果链里崩在 `OnStartup`，之后没有任何一次保存被触发。能证明目录可写的是 `logs\app.log`（同一个 Low 进程一直写到 11:35）和探针在数据目录里的建-写-删。判据只能是探针，不能是 mtime。

同一条因果上有过一个未决观测，现已定因：`content_index.db` 在 10:49 与 11:52 两次报 `SQLite Error 14: 'unable to open database file'`。DB 文件本身带 `Low:(NW)` 且当前用户 `(I)(F)`，写入探针也通，所以不是这个文件的标签，真正被拒的是 SQLite 的临时目录。

- 全扫的清理分支会 `CREATE TEMP TABLE IF NOT EXISTS to_delete`（`Plugins/ContentSearch/Storage/DatabaseWriterHelper.cs:191`，插入在 `:198`），普通写入路径 `InsertOrUpdateBatch` 不建临时表。这解释了为什么只有 "Full scan failed" 出现，而索引本身照常。
- 临时库落在进程 `GetTempPath()`，即 `%LOCALAPPDATA%\Temp`，该目录没有任何 Mandatory Label ACE（`icacls` 复核，9a6b9769 复核时仍然如此），默认按 Medium 处理，Low 进程 no-write-up 拒建。这与矩阵里"在 `%TEMP%` 建目录：Low FAIL / Medium OK"是同一件事，创建失败对外就表现为 `unable to open database file`。
- 不是路径选错。插件用 `GetUserDataDirectory()`（`Plugins/ContentSearch/ContentSearchPlugin.cs:53-57`）拿到 `%LOCALAPPDATA%\Lertaro\ContentIndex`，从未调用 `GetSharedDataDirectory()`，`%ProgramData%\Lertaro` 那条只读锁与它无关。
- 插件启动时的 `Initialize()` 打开成功（app.log:86-89 之后无异常），DB mtime 在 10:46:49、目录 mtime 11:56 说明 `-wal`/`-shm` 建过又被干净删除，这是一条只在"有行要清理"时走到的窄路径。
- 未核对（截至 9a6b9769）：App 自己看到的 `%TEMP%` 是否就是这个值，低完整性下环境变量可被改动。
- 修法与代价见"已处置"的 SQLite 一节，`080a70bf` 已落地。

## 已处置

### 删除整套标签机制（`cfa8302f`）

删掉的是 `DataFolderIntegrity` 与其测试、线协议 id 23 与服务端处理体、`PipeClientIdentity.TryGetClientUser`、`SetDataFolderIntegrityAsync`、启动流程里的那一跳、提权修复里的降权调用、HEAD 的 `UserDataAccess.SetLowIntegrityLabel`，以及 `DirectoryLockNativeMethods` 的四个标签原语（`SetSecurity`/`SetKernelObjectSecurity` 保留，`InstallDirectoryLock` 在用）。只为这一跳抽出来的单调用点辅助（`OpenAt`、`VerifyAfterServiceAsync`）退回原状，`PipeClientIdentity`、`SearchRequestMessage`、`SearchServiceManagementExtensions`、`Service/Program.cs`、`UsnServicePipeRequestProcessor`、`DirectoryLockNativeMethods` 与当时的 HEAD 逐字一致。

三条理由。

1. 它在要救的场景里不可达。Low 打不开 `InOut` 管道，请求发不出去，Medium 不需要它。
2. 它会伤到真实用户。那句降权在提权修复里执行，把用户设置根永久降到 Low；此后任何 Low 进程都能改写 `user-settings.json`、`plugin-settings.json` 和 `localsend.pfx`，而 Medium 的 App 读取时不校验完整性。拿一条 MIC 保证换一个配置注入口。
3. 它唯一的收益是"让降权启动能写设置"，而结论速览第 2 条已经证明设置本来就能写。

净结果是 `UserDataAccess.Verify` 的写探针仍是唯一判据，目录确实不可写时照旧弹框并拒绝启动。9a6b9769 复核过，"标签与降权"那一组符号（`SetDataFolderIntegrity`、`DataFolderIntegrity`、`LowerIntegrityLabelToLow`、`OpenForRelabel`、`LowIntegrityLabel`、`ProcessIntegrityLevel`、`RepairCannotHelpReason`、`EphemeralKeySet`）在 `*.cs` 里 grep 为空。被删的两个未跟踪文件当时留档在一次性目录，工件已清理，本文不再记录路径。

### 否决 `EphemeralKeySet`

`LocalSendHttpClientFactoryTests`（5 例，测试主机在 Medium）三组对照：

| `LoadFlags` | 结果 |
| --- | --- |
| `Exportable`（HEAD） | 0 失败；整套 Core 1968/1968 通过 |
| `EphemeralKeySet \| Exportable` | 2 失败：`Create_WithClientCertificateAndMatchingPin_CompletesHttpsRequest` 握手不通、abort 用例超时 |
| `EphemeralKeySet` | 4 失败 |

已复跑确认稳定，不是环境抖动。私钥不入库与该路径能握手不能同时成立，因此 LocalSend 在 Low 下无解，工作区那版回退为 HEAD 的 `Exportable`。现行值见 `Core/Services/LocalSend/LocalSendCertificate.cs:10`。

顺带记一条测试自身的抖动：单独按类跑时 `PrepareUpload_ClientAbortsHttpsRequest_CancelsPendingSession` 曾失败一次、单跑三连过、整套里也过，是同类内的端口/会话串扰，与本报告结论无关。

### 落地的两处修复

- `DispatcherExceptionHandler`：新增 `StartupCompleted`（`App/Helpers/App/DispatcherExceptionHandler.cs:17`），窗口出现前的异常报告后 `Shutdown(1)`（`:31`）；`App/App.xaml.cs:336` 在 `MainWindow.Show()`（`:313`）之后置位。早退路径（单一实例移交、设置传输）都先调过 `Shutdown`，不受影响。`App.xaml.cs:334-335` 的注释保留了理由：LocalSend 的 identity 在 Show() 之后建立，那正是 reduced token 死掉的地方，光有窗口不足以说明启动走完了。
- `HookIpcClient`：launch 失败的重试由固定 5 秒改为倍增、封顶 60 秒（`Core/Hook/Ipc/HookIpcClient.cs:20` 的 `LaunchRetryDelayFirstMs = 5_000` / `LaunchRetryDelayMaxMs = 60_000`，`:221-225` 倍增并在成功后复位），`HookLaunchBroker` 与 `HookLaunchRequestHandler` 里"每 5 秒重试"的注释同步更正。

验证（`cfa8302f` 那次运行）：`dotnet build Lertaro.slnx` 0 警告 0 错误，Core 1968 通过、App 2062 通过，均 0 失败。

### SQLite 全扫不再依赖临时目录（`080a70bf`）

`PRAGMA temp_store = MEMORY;` 现在在 `Plugins/ContentSearch/Storage/ContentSearchDatabase.cs:68` 的 `OpenConnection` 里，按连接执行，所以不在 `InitializeSchema`（同文件 `:61-63` 的注释记了这个理由）。同处的 `ponytail:` 注释（`:64-65`）留了一条判断：代价是一笔等于删除候选列表的内存，真不够用时该在主库里加一张带索引的表，而不是把 `temp_store` 撑大。验证方式是移除法，不带 pragma 时 `PRAGMA temp_store` 读回来是 0，随包的 e_sqlite3 默认确实落在文件上（`Tests/Plugins/ContentSearch/Storage/ContentSearchDatabaseTests.cs:299` 读回它）。同批把 `ContentIndexScanCoordinator` 的那条 catch 改成记录整个 `ex`，下一次失败会说清楚断在哪。

环境侧的收尾也在这一项里：`%LOCALAPPDATA%\Lertaro` 现在已无 Mandatory Label ACE，数据目录回到 Medium 默认；`<repo>` 根仍带 `Low:(OI)(CI)(NW)`、沙箱配置组的继承授权和一条 `Everyone:(CI)(DENY)(DC)`，属于沙箱配置层，剥离与否是安全决定，不由代码负责。

## 更正过的说法与坑

以下都能一次性证伪，留结论和当初判断错在哪。

1. "构建产物由启动它的 shell 决定完整性，换个方式启动就是 Medium"，不成立。镜像标签才是上限，Medium 的 shell 派生同一个 exe 得 Low，复制到仓库外同一个 exe 得 Medium（实测）。
2. "现在的版本被拦在启动前，v5.9.3 能跑"，不成立。区间内唯一的令牌门（3aae4792）已在 HEAD 移除，而当前失败点（LocalSend）在 v5.9.3 同样存在，那次崩溃与门无关，是密钥库写入。
3. "数据目录写不进去是 DACL 问题，需要修 ACL"，不成立。DACL 正确，Low 下设置读写实测全通。唯一与完整性无关的写不进是 `%ProgramData%`，那是 `d883625f` 有意的锁，已改由服务代写。
4. "`37ea0ba5` 让 App 给自己目录打了 Low 标签"，不成立（1314）。生效的是同一提交删掉的启动阻断。把这条读反会以为代码还在降标签。
5. "标签只能由 SYSTEM 或提权侧写"，只对请求者是 Low 而目标是 Medium 的情形成立。Medium 的 App 自己就能完成（实测），服务那一跳在正常路径上是多余的。
6. "`EphemeralKeySet` 是这次崩溃的修复"，不成立。它让 `LoadPkcs12` 不再抛，同一标志把 LocalSend 的 TLS 握手段掉（0/2/4 三组对照）。载入不抛不等于功能可用，这是以后判断此类修复是否成立的检查点。
7. "`MainWindowTitle` 为空等于窗口没建"，不成立。正常会话里标题也是空的，无崩溃的那次同样为空。启动是否走完只能看 app.log 的 `Starting normal WPF GUI client mode.` 与 `InlineSearchManager started.`。
8. "数据目录的 Low 标签是代码打的"，不成立。目录根是 plain `Low:(NW)`，句柄路线写的是带 `OICI` 的描述符，形状不匹配，来源是手工 `icacls`。
9. "`%ProgramData%\Lertaro` 本机无法取证"，作废。`icacls` 读到了 `SYSTEM`/`Administrators:(OI)(CI)(F)` 与 `BUILTIN\Users:(OI)(CI)(RX)`；`logs` 子目录本身仍被拒，两件事别混。
10. "设置文件的 mtime 不动就是写不进去"，不成立，见第四条因果链。
11. 工作区那一版（服务打标签一跳加 `EphemeralKeySet`）在报告里曾被当作待落地的方案，`cfa8302f` 全部处置。以后读到"未提交工作区"的描述，先确认它有没有被后续提交取代。
12. 租约冲突在 `7727f9fb` 里对所有成因共用同一句话，会把权限问题报成"another instance is transferring settings"，误报由 `c75e51dd` 收掉。看到这句文案先确认成因。
13. `f1909502` 的提交信息（"restore portable user data permissions"）强于 diff，里面没有一处 ACL 写入。按信息归类变迁会把这条算进来，按 diff 才不会。

### Windows 语义与本机取证局限

1. "只有 SYSTEM 或提权能降标签"这个结论对、理由错。Low 进程调 `SetKernelObjectSecurity(LABEL_SECURITY_INFORMATION)` 得到 Win32 5 是 MIC no-write-up（对象是 Medium），不是缺特权，掩码带上 `WRITE_OWNER` 后 SYSTEM/提权不需要任何特权就能改。修复当年仍要走服务，只因为 App 进不去那道门，而 `cfa8302f` 已经把这整条路删了。
2. `Directory.Delete(path, recursive: true)` 会跟随目录 junction（本机 .NET 实测）：连目标内容一起删，最后在已悬空的链接上抛 `UnauthorizedAccessException` 并留下残树。测试清理必须先 `Directory.Delete(link)`（不递归，只删链接）再递归删根。产品的删除走句柄（`InstallDirectoryLock.cs:196` 调 `DirectoryLockNativeMethods.Delete(handle)`），不受影响。
3. .NET 把强制标签 ACE 解析成 `CustomAce`，没有 `SecurityIdentifier`、没有 `AccessMask`，`GetSddlForm(Audit)` 会把它丢掉（返回 `"S:"`）。要断言标签形状只能读 `GetBinaryForm` 的字节：`[0]=0x11`、`[1]=AceFlags`、`[4..8)=AccessMask`（NW=0x1）、`[8..)=SID`（`S-1-16-4096`）；`AceType` 枚举里没有 `SystemMandatoryLabel`，只能用数值 0x11。
4. Low 标签会传染给副本：从 Low 目录复制出来的 exe/dll 仍带 Low 标签，"拷到别处就好了"不成立，工作区构建产物天然 Low 也是同一个原因。
5. `.git` 在本沙箱只读，`git worktree` 与 `git stash` 都写不进 `.git`（`Permission denied`）。"用 worktree 取基线"在这类机器上不可用，基线只能用推理加定向探针证明。
6. 本机跑 ACL 测试会假红。沙箱配置组在工作区只有 Modify，没有 `WRITE_DAC`/`WRITE_OWNER`，根上还有一条 `Everyone:(CI)(DENY)(DC)`。凡是要保护 DACL 或改所有者的用例（`InstallDirectoryLock*`、`PortableDirectoryLock*`）在这个账户下必失败，要么以真实用户运行，要么接受假红。跑 settings/ACL 套件前把 `$env:TEMP`/`$env:TMP` 指到工作区，`SettingsBackupPaths` 的盘根遍历不再撞 `%USERPROFILE%`，失败从 71 降到 25，剩下的才是真环境限制。这只是让用例可跑，不是产品行为。
7. 单测覆盖不到 Low 行为。测试主机进程没有 Low 标签，`dotnet test` 跑在 Medium，修复前 `LocalSendCertificateTests` 是 4/4 绿的。"跑过单测就等于验证了 Low 权限"是错的，必须另起 Low 进程并看端到端日志。
8. 同一根因会持续冒头，任何"往 Medium 对象里写"的路径在 Low 下都拒绝访问。当时观察到四处：数据目录（崩）、用户密钥库（崩）、`HKCU\Software\Classes\lertaro`（`UrlProtocolManager` Warn 不崩）、每 5 秒一条的 `HookIpcClient LaunchHook: Access to the path is denied`（Warn 循环，已改成倍增封顶 60 秒）。只修数据目录不够，得逐条评估。
9. 一个没有对上的观察，如实记在这里。当时的反馈是"给目录 `/grant Everyone:F` 之后程序能往下走、至少能写 log 了"。对照实验是全新 Medium 目录先 `/grant Everyone:(OI)(CI)F`，Low 写入仍然被拒；同一目录降标签，写入成功。no-write-up 与 DACL 无关，那次大概率是同一时刻提权修复已经把标签降了（或目录里还有别的变化）。别把这条读成"DACL 修好了就够了"。

## 遗留待办

### LocalSend 在 Low 下的产品选择，唯一开放项

带原因只降级这一个功能后继续启动，还是维持"报错并退出"。`EphemeralKeySet` 已经否决，密钥不入库与该路径能握手不能同时成立。9a6b9769 核对现状：`LoadFlags` 仍是 `Exportable`（`Core/Services/LocalSend/LocalSendCertificate.cs:10`），`LocalSendAppEventHandler.Initialize` 仍在 `App/App.xaml.cs:331`，外面没有降级分支。未实现。

### `Core/Logger.cs` 吞掉初始化失败

`Initialize()` 的异常整个吞掉（`Core/Logger.cs:164-169` 的空 catch，注释给的理由是回退到 exe 旁边可能通过一个 Users 可读的文件泄漏私有服务路径）。权限问题最需要日志的时候恰好没日志：写不进，被吞，用户只看到"无法启动"。风险评级高。建议的兜底顺序是 EventLog、Debug Output、临时目录，至少保留一条启动诊断记录。`Plugins/ContentSearch/Indexing/ContentIndexScanCoordinator.cs:111` 已经改成记录整个 `ex` 而不是 `ex.Message`，那是同一类问题的局部处理，不是这条的替代。

### `UpdateRelaunchMarker` 的账户假设

`Core/Services/Update/UpdateRelaunchMarker.cs:108-112` 只接受 LocalSystem 与 BuiltinAdministrators 两个 well-known SID。这是对的，防止普通用户伪造重启指令。如果服务账户将来改成 LocalService、NetworkService 或自定义 service account，兼容性会断裂，建议按服务 SID 白名单判定。风险评级低。9a6b9769 核对：判定形状未变。

### ACL 子系统缺一份自己的架构文档

`InstallDirectoryLock` 与 `PortableDirectoryLock` 覆盖了 Owner、用户目录、Portable 多用户隔离、`%ProgramData%` 和安装目录锁定，复杂度已经高出"读一段代码就懂"的门槛。这与下面"不重构"是同一条决定的两面，形状本身不改，改的是把它讲清楚。

### 两条未复核的观察

- App 自己看到的 `%TEMP%` 在低完整性下是否还是 `%LOCALAPPDATA%\Temp`（环境变量可被改动）。未核对（截至 9a6b9769）。
- 从工作区目录直接跑 `debug\Lertaro.App.exe` 曾观察到 `STATUS_DLL_NOT_FOUND`（exe 是 Low，运行时在 `%ProgramFiles%\dotnet`）。若成立，`build_and_run.bat` 那种"直接 Start-Process 构建产物"在本机不能作为端到端验证手段。未复核（截至 9a6b9769）。

### 不重构的裁定

`PortableDirectoryLock` 与 `InstallDirectoryLock` 明确不做重构，理由是 AGENTS.md 的懒惰原则：现有形状的每一处奇怪都有具体事故来源（缺 `Synchronize` 位、issue #342 的 Owner 回读、提权前的锁等待），重写会连这些一起丢掉，而本机没有任何测试能接住回归。`DescribeRelocationRefusal` 保持"再观察"状态。行数口径按现树：`InstallDirectoryLock` 299 行、`PortableDirectoryLock` 226 行、`UserDataAccess` 61 行（旧文档里的 266/204/51→118 已失真，9a6b9769 复核为上述三个值）。

### 环境侧，不由代码负责

一次性探针的工件已清理：数据目录内无 `sacl-probe/`、无 `.access-*.tmp`、无 `probe-*`，`%TEMP%\lertaro-sacl-*` 与仓库外那份探针副本已删。

## 复现与取证命令

`<repo>` 是仓库根。探针是一次性构建，二进制路径自行替换。

```bash
# 标签与令牌
icacls '<repo>'                        # Low:(OI)(CI)(NW)，可继承
icacls '<repo>\debug\Lertaro.App.exe'  # 当时继承为 Low:(I)(NW)；9a6b9769 复核该文件读到 Medium:(I)(NW)，以当次构建为准
icacls '%LOCALAPPDATA%\Lertaro'        # 当时 Low:(OI)(CI)(NW) + DACL 正常；复核时已无 ML ACE
setprobe.exe                           # 放在带 Low 标签的目录里，自读令牌 = Low
setprobe.exe <pid>                     # 对运行中的 App 读它的令牌
# 把探针输出目录整体复制一份到仓库外的无标签目录再跑一次，同一二进制 = Medium

# 服务
Get-CimInstance Win32_Service -Filter "Name LIKE '%Lertaro%'"   # StartName = LocalSystem

# 会话证据（app.log 每次启动被截断，只剩最后一个会话）
sed -n '1,5p;96,118p' "$LOCALAPPDATA/Lertaro/logs/app.log"

# 关键几步的 diff
git show 3aae4792:App/Services/ServiceManagement/AppStartupServiceBootstrapper.cs | sed -n '49,60p'
git show 37ea0ba5:Core/Services/Installation/UserDataAccess.cs | sed -n '70,105p'
git show 58e7ae14:Core/Services/Installation/UserDataAccess.cs
git show 6e041f56 -- Core/Services/Installation/InstallDirectoryLock.cs

# 标签机制删除后应无残留（9a6b9769 复核：为空）
git grep -nE "SetDataFolderIntegrity|DataFolderIntegrity|LowerIntegrityLabelToLow|OpenForRelabel|LowIntegrityLabel" -- '*.cs'

# EphemeralKeySet 的代价：同一个测试类三组对照（Exportable 0 失败 / 组合 2 / 单用 4）
dotnet test Tests/Core/Core.csproj -c Debug --filter "FullyQualifiedName~LocalSendHttpClientFactoryTests"

# SQLite 全扫失败的真实依赖：临时表 + 默认 Medium 的临时目录
git grep -n "CREATE TEMP TABLE" -- Plugins/ContentSearch/Storage/DatabaseWriterHelper.cs
icacls '%LOCALAPPDATA%\Temp'    # 没有任何 ML ACE
icacls 'C:\ProgramData\Lertaro' # SYSTEM/Admin (OI)(CI)(F) + Users (OI)(CI)(RX)

# 全套
dotnet build Lertaro.slnx -c Debug && dotnet test Tests/Core/Core.csproj -c Debug --no-build \
  && dotnet test Tests/App/App.csproj -c Debug --no-build
```
