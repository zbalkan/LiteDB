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

## Benchmark gates

The benchmark corpus is split because the Round 0 baseline-vs-baseline control demonstrated materially different noise characteristics between steady-state CPU/allocation work and file-backed I/O.

### Screen

`ComparativeReadBenchmarks` is the automatic branch-push gate. It uses a fixed 20,000-document data set and measures:

- random point lookups;
- localized point lookups;
- narrow indexed range queries;
- full scans;
- concurrent random reads with 1, 4 and 16 workers.

The Round 0 null control kept most steady-state read timing deltas within about 4%, with the largest observed steady-state delta about 7.5%. Hosted-runner timing changes below 10% are therefore treated as screening noise unless independently repeated. Managed-allocation deltas are more stable and remain first-class evidence.

### Reopen

`ComparativeReopenBenchmarks` isolates point reads after database reopen. It is not part of the automatic screening gate because reopen behavior showed larger hosted-runner variance. It is executed explicitly and uses order-balanced ABBA execution.

### Durability

`ComparativeTransactionBenchmarks` provides transaction sizes 1, 10, 100 and 1,000 for:

- non-indexed updates in one transaction;
- indexed updates in one transaction;
- indexed updates committed one operation at a time.

Every measured transaction iteration starts from a newly seeded database. The original null control produced timing swings from roughly -24% to +58% in file-backed transaction cases even though baseline and candidate were identical. A single A/B durability result is therefore not admissible evidence. Explicit durability validation runs baseline, candidate, candidate, baseline (ABBA) and compares the median of the two independent measurements per side.

Existing `MemoryManagementBenchmarks` remains available for broader validation of cache-size profiles, encryption, vector search, full scans, bulk writes/updates, concurrent reads, transaction-end trim, and index rebuilds.

## Benchmark runtime

The benchmark project targets `net10.0` and Round 0 pins BenchmarkDotNet 0.15.8. The runner explicitly uses .NET 10 RyuJIT.

Run comparative workloads in Release mode. Example filters:

```sh
dotnet run -c Release --project LiteDB.Benchmarks/LiteDB.Benchmarks.csproj -- --filter '*ComparativeReadBenchmarks*'
dotnet run -c Release --project LiteDB.Benchmarks/LiteDB.Benchmarks.csproj -- --filter '*ComparativeReopenBenchmarks*'
dotnet run -c Release --project LiteDB.Benchmarks/LiteDB.Benchmarks.csproj -- --filter '*ComparativeTransactionBenchmarks*'
```

The `Performance Comparison` workflow defaults to `screen` for branch pushes. Manual dispatch exposes `screen`, `reopen`, and `durability`. Reopen and durability use ABBA order balancing; the comparison tool aggregates repeated BenchmarkDotNet means by median.

Run baseline and candidate measurements on the same host, runtime, storage device and power configuration. Do not compare warm results on one branch with reopened/cold results on another.

## Result classification

Do not collapse the benchmark set into one composite score. Classify each experiment per relevant workload as:

- **WIN** — repeatable relevant improvement with no important regression;
- **NEUTRAL** — differences are operationally insignificant;
- **TRADE-OFF** — meaningful gain in one dimension with a meaningful cost in another;
- **REGRESSION** — relevant workload worsens without adequate compensation;
- **INCONCLUSIVE** — noise or environment prevents a reliable conclusion.

For GitHub-hosted screening, the comparison tool uses conservative 10% thresholds for timing and managed allocation. A targeted allocation reduction can justify promotion to deeper validation even when timing is below the hosted-runner threshold, provided there is no material timing regression. Reopen and durability timing results require the order-balanced gate and should still be repeated when close to the threshold. Read/write amplification and persistent-layout changes require dedicated instrumentation rather than inference from elapsed time.

## Synthesis

Do not merge complete experiment branches into `perf/synthesis-r1`. Cherry-pick individual winning commits from the frozen baseline and rerun the relevant validation gates. Where two changes affect the same subsystem, benchmark `baseline`, `A`, `B`, and `A+B` explicitly because optimization effects are not assumed to be additive.

Persistent file-format changes are excluded from Round 1. If later measurements justify them, create isolated `perf-format/*` branches from the current synthesis baseline.
