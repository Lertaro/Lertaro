# Lertaro.Benchmarks

排序模型的基准。只用来回答一个问题：改了排序之后，快速窗口和完整窗口的排序有没有变慢、多花了多少分配。

它**不在 `Lertaro.slnx` 里，也不进 CI**，只引用 `Core`：这里测的是内核与比较器，不牵扯 WPF、窗口或索引文件，跑一次也不需要真实数据。

## 运行

```powershell
dotnet build benchmarks/Lertaro.Benchmarks/Lertaro.Benchmarks.csproj -c Release
dotnet run -c Release --project benchmarks/Lertaro.Benchmarks/Lertaro.Benchmarks.csproj -- --filter '*'
```

只跑其中一组：把 `--filter '*'` 换成 `--filter *QuickRank*`。三组加起来在几分钟内跑完（每组 `SimpleJob`：暖机 2 次、计测 5 次），要更稳的数字就加大 `-j` 的迭代数，别改数据规模——规模是和真实场景对得上才可比。

跑完会在**当前目录**生成 `BenchmarkDotNet.Artifacts/`（日志与 `results/`）。它已由 `.gitignore` 里的 `BenchmarkDotNet.Artifacts/` 忽略，不会被误提交；看完要清掉可直接删。要留在别处就加 `--artifacts <路径>`。

## 三组基准

| 基准 | 测的是哪段 |
| --- | --- |
| `SearchCandidateComparerBenchmarks` | 引擎截断（每盘缓冲 → 排序 → `Take`）。`CandidateComparer` 是基线，`LegacyRankComparer` 是它换掉的旧比较器，`Ratio` 一列直接说两者是否等价 |
| `QuickRankBenchmarks` | 快速窗口：全量 `RankPolicy.BuildKey` + `Array.Sort(keys, signals, RankKeyComparer.Instance)`，204 与 4000 条 |
| `RankedSearchResultComparerBenchmarks` | 完整窗口终局排序：10 万行已装饰好，只测 `Array.Sort`（装饰在 `GlobalSetup` 里做一次，符合真实的一次性成本） |

数据由 `Synthetic.cs` 里的固定 xorshift 生成，每次跑都是同一批行（含优先级、强弱、历史分、目录深度的混合），所以两次跑的数字可以直接比。

## 结果怎么看

`Mean` 之外主要看 `Allocated`：排序在快速窗口是每次重绘都跑的，每行几十字节的临时分配在 4000 条时就会显形。`Ratio` 以标了 `Baseline = true` 的那一列为 1.00，其余列相对它。只跑 5 次迭代，别拿单次 `StdDev` 下结论；四千行以下还会吃到 BDN 的 `MinIterationTime` 警告，那一格按数量级读。

每个类同时带 `[SimpleJob]`（进程外默认 toolchain）和 `[InProcess]`，所以汇总表里每个方法有两行。这台机器上进程外那一行永远是 `Mean = NA`：BDN 生成 boilerplate 工程时从仓库根递归找 csproj，撞上权限拒绝的 `debug/Data/Machine/indexes`，抛 `UnauthorizedAccessException`。读表只看 `Job = InProcess` 的行。换一台没有这个目录权限问题的机器，`[InProcess]` 可以去掉，`[SimpleJob]` 那行才是标准姿势。

## 改排序之后

动了分值、分段、键序或比较器就要重跑本目录，并把新数字更新到 `dev-docs/search-ranking-redesign.md` §7.3；`dev-docs/search-ranking.md` 是现行行为的描述，不放基准数字。
