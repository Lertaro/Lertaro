# V2 offline ranking baseline

Fixture: `ranking-replay-cases.v1.json`

Policy: `RankPolicy.Version = 2`

| Case | V2 order | Recall@1 | MRR | nDCG@10 | Pair inversions | Expected contract |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| `pixpin-appdata-shortcut` | folder → exe → Start Menu shortcut | 0.000 | 0.333 | 0.500 | 2 | fail |
| `shortcut-normal-root` | shortcut → exe | 1.000 | 1.000 | 1.000 | 0 | pass |
| `direct-history-overreach` | weaker H5 row → exact app | 0.000 | 0.500 | 0.631 | 1 | fail |
| `learned-alias-overreach` | learned H5 row → direct app | 0.000 | 0.500 | 0.631 | 1 | fail |

The PixPin failure is structural: `%USERPROFILE%\AppData` is Low by default, so the Start Menu shortcut enters the Low section. A normal file or directory remains in Boosted; the Application type bonus cannot cross that section boundary.

The two history failures show the other known exchange-rate problem. H5 scores 34 and therefore ties or beats exact direct evidence before text quality is compared. P3–P8 must separate evidence grade from direct frecency and learned aliases rather than tuning these four examples with larger bonuses.

These are controlled candidate-set measurements, not retrieval measurements. Recall before ranking, provider readiness and end-to-end latency remain separate gates.
