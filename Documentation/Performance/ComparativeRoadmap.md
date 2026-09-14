# LiteDB Comparative Performance Roadmap

## Baseline

All comparative performance branches are forked from one frozen `perf/baseline` commit. During a benchmark round, experiment branches are not rebased and do not merge changes from `dev` or from one another.

The original Round 0 source baseline is `dev` commit `a50661a9d1a25b5713586d0096c6325d76bf5dfe`.

Round 0 may change benchmark and measurement infrastructure, but must not intentionally optimize LiteDB engine behavior. The final Round 0 commit is the common parent for every Round 1 experiment branch.

## Round 1 branches

- `perf/voron` — dirty-page/WAL amplification, commit critical sections, journal behavior.
- `perf/faster` — materialization/copy reduction and hot-path mutation behavior.
- `perf/garnet` — storage/buffer plumbing and positioned I/O experiments.
- `perf/tsavorite-cache` — allocation, temporary buffers, BSON/API materialization.
- `perf/dbreeze` — read amplification, scan pollution, cache admission, lazy reads.
- `perf/btdb` — encoded-to-encoded transformations, secondary-index serialization, bounded state reuse, structural transaction work.
- `perf/synthesis-r1` — only independently validated winning commits, cherry-picked onto the frozen baseline.

Branches represent competing hypotheses. Individual commits represent individual experiments.

## Common benchmark workloads

`ComparativeReadBenchmarks` provides a fixed 20,000-document data set with:

- random point lookups;
- localized point lookups;
- narrow indexed range queries;
- full scans;
- concurrent random reads with 1, 4 and 16 workers;
- point reads after reopening the database.

`ComparativeTransactionBenchmarks` provides fixed transaction workloads with transaction sizes 1, 10, 100 and 1,000:

- non-indexed updates in one transaction;
- indexed updates in one transaction;
- the same indexed updates committed one operation at a time.

Every measured transaction iteration starts from a newly seeded database to avoid cumulative WAL growth or previous mutations biasing later measurements.

Existing `MemoryManagementBenchmarks` remains part of the full comparison suite and covers cache-size profiles, encryption, vector search, full scans, bulk writes/updates, concurrent reads, transaction-end trim, and index rebuilds.

## Benchmark runtime

The benchmark project targets `net10.0` and Round 0 pins BenchmarkDotNet 0.15.8. The benchmark runner explicitly uses .NET 10 RyuJIT.

Run the comparative workloads in Release mode. Example filters:

```sh
dotnet run -c Release --project LiteDB.Benchmarks/LiteDB.Benchmarks.csproj -- --filter '*ComparativeReadBenchmarks*'
dotnet run -c Release --project LiteDB.Benchmarks/LiteDB.Benchmarks.csproj -- --filter '*ComparativeTransactionBenchmarks*'
dotnet run -c Release --project LiteDB.Benchmarks/LiteDB.Benchmarks.csproj -- --filter '*MemoryManagementBenchmarks*'
```

Run baseline and candidate measurements on the same host, runtime, storage device and power configuration. Do not compare warm results on one branch with reopened/cold results on another.

## Result classification

Do not collapse the benchmark set into one composite score. Classify each experiment per relevant workload as:

- **WIN** — repeatable relevant improvement with no important regression;
- **NEUTRAL** — differences are operationally insignificant;
- **TRADE-OFF** — meaningful gain in one dimension with a meaningful cost in another;
- **REGRESSION** — relevant workload worsens without adequate compensation;
- **INCONCLUSIVE** — noise or environment prevents a reliable conclusion.

Initial screening thresholds are approximately 5% for CPU/throughput, 15% for managed allocation, and 10% for read/write amplification. These are decision aids rather than universal acceptance rules.

## Synthesis

Do not merge complete experiment branches into `perf/synthesis-r1`. Cherry-pick individual winning commits from the frozen baseline and rerun the complete suite. Where two changes affect the same subsystem, benchmark `baseline`, `A`, `B`, and `A+B` explicitly because optimization effects are not assumed to be additive.

Persistent file-format changes are excluded from Round 1. If later measurements justify them, create isolated `perf-format/*` branches from the current synthesis baseline.
