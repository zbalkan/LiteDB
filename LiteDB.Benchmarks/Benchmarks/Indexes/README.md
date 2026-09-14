# Scalar Index Benchmark Protocol

This directory contains the common benchmark suite for evaluating alternative persistent scalar-index data structures in LiteDB.

## Baseline

The benchmark branch was created from `dev` commit:

`a50661a9d1a25b5713586d0096c6325d76bf5dfe`

This branch must not change scalar-index behavior. Experimental data-structure branches should fork from the commit that introduces this benchmark suite so that the benchmark code and workload definitions remain identical.

## Initial workloads

The first benchmark set isolates four costs that are expected to differ between the current skip list and the selected alternatives:

- indexed point lookup, including hits and misses;
- bounded indexed range traversal;
- indexed insertion, separating right-edge sequential insertion from random insertion between existing keys;
- `EnsureIndex` construction over an existing unindexed collection.

Read and build workloads are run for `Int32`, short string, and longer string keys. String keys are zero padded so their lexical order matches their generated ordinal order.

Dataset sizes are currently 10,000 and 100,000 documents. Range widths are 10, 100, and 1,000 keys. Indexed insertion adds 256 keys per measured invocation.

## Reproducibility

All generated data is deterministic. The benchmark seed is `3131`.

Read benchmarks use a dedicated temporary database and create the secondary index before inserting the dataset. Mutation benchmarks create a pristine template database during global setup, copy it before every measured iteration, and open the copy for the benchmark. Dataset generation and template copying therefore remain outside the measured operation.

Each benchmark uses `ConnectionType.Direct` without encryption. Mutation benchmarks checkpoint inside the measured operation so the comparison includes the persistence work caused by the index change.

Run the index suite with BenchmarkDotNet in Release configuration, for example:

```text
dotnet run -c Release --project LiteDB.Benchmarks -- --filter *Indexes*
```

When comparing branches, use the same machine, runtime, storage device, power policy, benchmark arguments, and benchmark-suite commit lineage. Keep the raw BenchmarkDotNet artifacts for every run.

## Interpretation

BenchmarkDotNet supplies latency, throughput-derived timing, allocation, and GC measurements. These are necessary but not sufficient for the data-structure decision.

Structural metrics such as distinct index pages touched, pointer or block hops, dirty-page count, bytes dirtied, page occupancy, and total index size should be added through common instrumentation only after the baseline workload suite is stable. They must be implemented identically across experimental branches and should not be inferred from elapsed time alone.

The first comparison branches are:

- `research/index-bskiplist`
- `research/index-cssl`
- `research/index-write-optimized`

Do not merge optimizations between these branches before the first independent benchmark comparison. A hybrid should be created only if the independent results justify it.
