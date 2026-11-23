**LocalSend 性能修改与测量，2026-11-23**

接收端现在边写入边计算 SHA-256，收到全部数据后比较摘要，取消了原先整文件重新打开和读取。校验失败仍返回 422，保留原有三次尝试和取消行为。接收与发送前校验使用 ArrayPool 缓冲；如果某个输入流不响应取消，缓冲等未完成读取结束后才归还池，避免另一传输复用仍在写入的内存。

会话进度在激活时计算选中文件总大小和编号，后续回调仅替换该文件的字节计数并调整总数；重试先清零该文件上一轮计数。完成状态也改为递减剩余数量，取消每个文件结束时扫描所有状态。最终字节数先发布，再标记完成，保证最后完成事件看到全部并发文件的字节。请求解析拒绝总大小溢出 long 的文件集合。

**基准方法**

使用 BenchmarkDotNet 0.15.8、.NET SDK 10.0.403 / Runtime 10.0.15、Windows 11 x64、Intel i5-10400F。独立项目位于 `scratch/localsend-performance/bench`，未给生产项目增加依赖。基线保存了修改前的接收与校验源代码；当前方法直接编译生产源文件。所有六个用例先经过 Dry 验证。

进度基准以 10 个和 10000 个文件比较旧式 ConcurrentDictionary.Values 汇总及文件大小扫描，与新的会话增量计数。每个文件大小 1000 字节，初始进度 500 字节，两种算法返回值在 GlobalSetup 中核对。

| 一次进度汇总 | 原实现 | 新实现 | 原分配 | 新分配 |
|---|---:|---:|---:|---:|
| 10 个文件 | 431.21 ns | 23.24 ns | 280 B | 0 B |
| 10000 个文件 | 201.16 µs | 25.24 ns | 80200 B | 0 B |

接收基准从 MemoryStream 读取固定的 8 MiB 数据，写入临时文件并验证同一 SHA-256。包含文件打开、写入、关闭、摘要和异步开销，操作结束时验证成功状态。它测量本机接收路径，不测量跨设备网络吞吐。

| 接收与校验 8 MiB | 原实现 | 新实现 |
|---|---:|---:|
| 首轮 Mean | 26.60 ms | 29.91 ms |
| 首轮 StdDev | 0.82 ms | 6.35 ms |
| 无并行构建复测 Mean | 26.47 ms | 24.08 ms |
| 复测 StdDev | 0.91 ms | 0.68 ms |
| 每次托管分配 | 1540.56 KiB | 2.99 KiB |

首轮同时进行了验证构建，新实现测量波动较大，因此进行了不并行构建、5 次预热和20次测量的复测。复测平均耗时降低约9%，两轮均显示约99.8%的分配下降。单文件网络速度仍受网卡、TLS、磁盘及对端处理影响。

基准输出分别保存在 `scratch/localsend-performance/progress`、`receive`、`receive-confirmation`，日志和基线源码保留供复核。

**验证**

- Core 的 LocalSend、索引规则与设置解析合计 251 passed、0 failed、0 skipped。
- 补齐最终字节发布顺序后，服务器、上传授权与进度计数 17 passed、0 failed、0 skipped。
- 新增验证覆盖部分选中文件、并发完成、同一文件校验重试计数、累计大小溢出、多缓冲 SHA-256，以及目的文件在摘要比较阶段被独占时仍能正确校验，证明不再依赖重新读盘。

复现命令：

```powershell
dotnet run --project scratch/localsend-performance/bench/bench.csproj -c Release --no-build -- --filter '*ProgressBenchmarks*' --noOverwrite
dotnet run --project scratch/localsend-performance/bench/bench.csproj -c Release --no-build -- --filter '*ReceiveBenchmarks*' --iterationCount 20 --warmupCount 5 --noOverwrite
```
