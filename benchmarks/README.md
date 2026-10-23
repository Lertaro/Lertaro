# Lertaro.Benchmarks

排序热路径的 BenchmarkDotNet 基准。项目只引用 `Core`，不加入 `Lertaro.slnx` 或 CI，也不读取用户数据。

## 运行

```powershell
dotnet build benchmarks/Lertaro.Benchmarks/Lertaro.Benchmarks.csproj -c Release
dotnet run -c Release --project benchmarks/Lertaro.Benchmarks/Lertaro.Benchmarks.csproj -- --filter '*'
```

单独运行一组时，把 filter 改成 `*QuickRank*`、`*SearchCandidateComparer*` 或 `*RankedSearchResultComparer*`。输出写入已忽略的 `BenchmarkDotNet.Artifacts/`。

## 基准

| 基准 | 范围 |
| --- | --- |
| `SearchCandidateComparerBenchmarks` | 引擎截断：排序后 `Take`；保留 legacy 对照 |
| `QuickRankBenchmarks` | 快速窗口：构建 `RankKey` 并排序 204/4000 行 |
| `RankedSearchResultComparerBenchmarks` | 完整窗口：排序 100000 个已装饰行 |

`Synthetic.cs` 使用固定 xorshift 数据，适合同机前后对比。主要观察 `Mean` 与 `Allocated`；不要用不同机器的绝对值判断回归，也不要从少量迭代的 `StdDev` 推断稳定性。

改动分值、分段、键序或比较器后，重跑相关基准，并把同机前后结果更新到 `dev-docs/search-ranking.md`。窗口端到端延迟仍需单独测量 p50/p95。
