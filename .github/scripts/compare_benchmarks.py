#!/usr/bin/env python3
"""Compare BenchmarkDotNet FullJSON result sets.

This is a screening tool rather than a statistical significance test. Multiple
--baseline and --candidate roots are supported so order-balanced runs can be
collapsed using the median of independently measured BenchmarkDotNet means.
"""

from __future__ import annotations

import argparse
import csv
import json
from dataclasses import dataclass
from pathlib import Path
from statistics import median
from typing import Iterable


@dataclass(frozen=True)
class Result:
    suite: str
    method: str
    parameters: str
    mean_ns: float | None
    allocated_bytes: float | None
    samples: int

    @property
    def key(self) -> tuple[str, str, str]:
        return self.suite, self.method, self.parameters


def _number(value):
    return float(value) if isinstance(value, (int, float)) else None


def load_root(root: Path) -> dict[tuple[str, str, str], Result]:
    results: dict[tuple[str, str, str], Result] = {}
    for path in sorted(root.rglob("*-report-full.json")):
        data = json.loads(path.read_text(encoding="utf-8-sig"))
        for benchmark in data.get("Benchmarks", []):
            stats = benchmark.get("Statistics") or {}
            memory = benchmark.get("Memory") or {}
            item = Result(
                suite=benchmark.get("Type") or path.stem,
                method=benchmark.get("Method") or benchmark.get("MethodTitle") or "",
                parameters=benchmark.get("Parameters") or "",
                mean_ns=_number(stats.get("Mean")),
                allocated_bytes=_number(memory.get("BytesAllocatedPerOperation")),
                samples=1,
            )
            if item.key in results:
                raise RuntimeError(f"Duplicate benchmark identity in {root}: {item.key}")
            results[item.key] = item
    return results


def aggregate_results(roots: list[Path]) -> dict[tuple[str, str, str], Result]:
    runs = [load_root(root) for root in roots]
    if any(not run for run in runs):
        empty = [str(root) for root, run in zip(roots, runs) if not run]
        raise RuntimeError(f"No BenchmarkDotNet FullJSON results found under: {', '.join(empty)}")

    keys = set().union(*(run.keys() for run in runs))
    aggregated: dict[tuple[str, str, str], Result] = {}
    for key in keys:
        present = [run[key] for run in runs if key in run]
        if len(present) != len(runs):
            raise RuntimeError(f"Benchmark identity missing from one or more repeated runs: {key}")

        means = [item.mean_ns for item in present if item.mean_ns is not None]
        allocations = [item.allocated_bytes for item in present if item.allocated_bytes is not None]
        suite, method, parameters = key
        aggregated[key] = Result(
            suite=suite,
            method=method,
            parameters=parameters,
            mean_ns=median(means) if means else None,
            allocated_bytes=median(allocations) if allocations else None,
            samples=len(present),
        )
    return aggregated


def delta_percent(candidate: float | None, baseline: float | None) -> float | None:
    if candidate is None or baseline is None or baseline == 0:
        return None
    return (candidate / baseline - 1.0) * 100.0


def screen(time_delta: float | None, alloc_delta: float | None) -> str:
    """Conservative hosted-runner screening thresholds.

    The Round 0 null control showed steady-state timing drift up to about 7.5%
    and much larger file-I/O drift. A 10% timing threshold therefore avoids
    classifying ordinary hosted-runner variation as an engine win.
    """
    if time_delta is None:
        return "INCONCLUSIVE"

    time_win = time_delta <= -10.0
    time_loss = time_delta >= 10.0
    alloc_win = alloc_delta is not None and alloc_delta <= -10.0
    alloc_loss = alloc_delta is not None and alloc_delta >= 10.0

    if (time_win and alloc_loss) or (alloc_win and time_delta > 5.0):
        return "TRADE-OFF"
    if time_loss or (alloc_loss and time_delta > 5.0):
        return "REGRESSION"
    if time_win or (alloc_win and time_delta <= 5.0):
        return "WIN"
    return "NEUTRAL"


def fmt_number(value: float | None, digits: int = 2) -> str:
    return "" if value is None else f"{value:.{digits}f}"


def fmt_delta(value: float | None) -> str:
    return "" if value is None else f"{value:+.2f}%"


def rows(
    baseline: dict[tuple[str, str, str], Result],
    candidate: dict[tuple[str, str, str], Result],
) -> Iterable[dict[str, str]]:
    for key in sorted(set(baseline) | set(candidate)):
        base = baseline.get(key)
        cand = candidate.get(key)
        suite, method, parameters = key

        if base is None or cand is None:
            yield {
                "suite": suite,
                "method": method,
                "parameters": parameters,
                "baseline_samples": str(base.samples) if base else "",
                "candidate_samples": str(cand.samples) if cand else "",
                "baseline_mean_ns": fmt_number(base.mean_ns) if base else "",
                "candidate_mean_ns": fmt_number(cand.mean_ns) if cand else "",
                "time_delta_pct": "",
                "baseline_allocated_bytes": fmt_number(base.allocated_bytes) if base else "",
                "candidate_allocated_bytes": fmt_number(cand.allocated_bytes) if cand else "",
                "allocation_delta_pct": "",
                "screen": "INCONCLUSIVE",
            }
            continue

        time_delta = delta_percent(cand.mean_ns, base.mean_ns)
        alloc_delta = delta_percent(cand.allocated_bytes, base.allocated_bytes)
        yield {
            "suite": suite,
            "method": method,
            "parameters": parameters,
            "baseline_samples": str(base.samples),
            "candidate_samples": str(cand.samples),
            "baseline_mean_ns": fmt_number(base.mean_ns),
            "candidate_mean_ns": fmt_number(cand.mean_ns),
            "time_delta_pct": fmt_delta(time_delta),
            "baseline_allocated_bytes": fmt_number(base.allocated_bytes),
            "candidate_allocated_bytes": fmt_number(cand.allocated_bytes),
            "allocation_delta_pct": fmt_delta(alloc_delta),
            "screen": screen(time_delta, alloc_delta),
        }


def write_csv(path: Path, data: list[dict[str, str]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    fields = [
        "suite",
        "method",
        "parameters",
        "baseline_samples",
        "candidate_samples",
        "baseline_mean_ns",
        "candidate_mean_ns",
        "time_delta_pct",
        "baseline_allocated_bytes",
        "candidate_allocated_bytes",
        "allocation_delta_pct",
        "screen",
    ]
    with path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=fields)
        writer.writeheader()
        writer.writerows(data)


def write_markdown(path: Path, data: list[dict[str, str]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    lines = [
        "# LiteDB benchmark comparison",
        "",
        "Observed deltas are candidate relative to baseline. Repeated roots are aggregated by median. This is a screening report, not a statistical significance test.",
        "",
        "| Suite | Method | Parameters | B n | C n | Baseline ns | Candidate ns | Time Δ | Baseline alloc B | Candidate alloc B | Alloc Δ | Screen |",
        "|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|",
    ]
    for row in data:
        values = [
            row["suite"],
            row["method"],
            row["parameters"],
            row["baseline_samples"],
            row["candidate_samples"],
            row["baseline_mean_ns"],
            row["candidate_mean_ns"],
            row["time_delta_pct"],
            row["baseline_allocated_bytes"],
            row["candidate_allocated_bytes"],
            row["allocation_delta_pct"],
            row["screen"],
        ]
        lines.append("| " + " | ".join(value.replace("|", "\\|") for value in values) + " |")
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--baseline", type=Path, action="append", required=True)
    parser.add_argument("--candidate", type=Path, action="append", required=True)
    parser.add_argument("--csv", type=Path, required=True)
    parser.add_argument("--markdown", type=Path, required=True)
    args = parser.parse_args()

    baseline = aggregate_results(args.baseline)
    candidate = aggregate_results(args.candidate)
    data = list(rows(baseline, candidate))
    write_csv(args.csv, data)
    write_markdown(args.markdown, data)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
