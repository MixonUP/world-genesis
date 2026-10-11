"""Small CPU research probe; not a Unity implementation or a performance benchmark.

Run from the workspace: python Tools/Research/procgen_probe.py
Only Python standard library is used. Formulas and primary-source lineage are in
Docs/Research/PROCEDURAL_FORMULAS.md. No third-party source is vendored here.
"""

from collections import deque
from datetime import datetime, timezone, timedelta
import hashlib
import heapq
import json
import math
from pathlib import Path
import random
import sys


def neighbours(index, width, height):
    x, y = index % width, index // width
    for dx, dy in ((-1, 0), (1, 0), (0, -1), (0, 1)):
        nx, ny = x + dx, y + dy
        if 0 <= nx < width and 0 <= ny < height:
            yield ny * width + nx


def boundary(width, height):
    return [i for i in range(width * height)
            if i % width in (0, width - 1) or i // width in (0, height - 1)]


def flood_with_parents(elevation, width, height):
    """Simple heap flood adaptation with stable discovery order and parent forest."""
    filled = list(elevation)
    parent = [-1] * len(elevation)
    seen = set(boundary(width, height))
    order = sorted(seen)
    heap = [(filled[i], serial, i) for serial, i in enumerate(order)]
    heapq.heapify(heap)
    while heap:
        _, _, cell = heapq.heappop(heap)
        for nxt in neighbours(cell, width, height):
            if nxt in seen:
                continue
            seen.add(nxt)
            filled[nxt] = max(elevation[nxt], filled[cell])
            parent[nxt] = cell
            order.append(nxt)
            heapq.heappush(heap, (filled[nxt], len(order) - 1, nxt))
    return filled, parent, order


def reference_spill_levels(elevation, width, height):
    """Independent oracle: sweep heights, BFS all cells connected to boundary.

    This deliberately inefficient small-grid method does not use a priority flood
    or its parent choices. A cell's first reachable level is its minimum spill level.
    """
    result = [None] * len(elevation)
    for level in sorted(set(elevation)):
        seen = {i for i in boundary(width, height) if elevation[i] <= level}
        queue = deque(sorted(seen))
        while queue:
            cell = queue.popleft()
            if result[cell] is None:
                result[cell] = level
            for nxt in neighbours(cell, width, height):
                if nxt not in seen and elevation[nxt] <= level:
                    seen.add(nxt)
                    queue.append(nxt)
    return result


def run():
    checks = []
    def check(name, passed, detail):
        checks.append({"id": name, "passed": bool(passed), "detail": detail})

    rng = random.Random(1206560)  # Reproducible lab samples; not the proposed game PRNG.
    weights = [0.5 ** k for k in range(4)]
    total_weight = sum(weights)
    values = [sum(w * rng.uniform(-1, 1) for w in weights) / total_weight
              for _ in range(5000)]
    check("F02-bounded-weighted-sum", all(-1 <= v <= 1 for v in values),
          {"samples": len(values), "min": min(values), "max": max(values),
           "noise_library_tested": False})
    check("F02-extreme-constant-inputs", sum(weights)/total_weight == 1
          and sum(-w for w in weights)/total_weight == -1,
          "Known extrema: every octave +1 or every octave -1")

    def smooth(t):
        return t*t*t*(t*(6*t-15)+10)
    samples = [smooth(i / 1000) for i in range(1001)]
    check("F04-smooth-endpoints-monotonic", samples[0] == 0 and samples[-1] == 1
          and all(a <= b for a, b in zip(samples, samples[1:])),
          {"sample_count": len(samples)})
    check("F04-smooth-endpoint-derivatives", all(
          30*t*t*(t-1)**2 == 0 and 60*t*(t-1)*(2*t-1) == 0 for t in (0, 1)),
          "Analytic first and second derivatives at 0 and 1")
    edge_results = []
    for width, height in ((2, 2), (3, 7), (128, 64)):
        for i in boundary(width, height):
            u, v = 2*(i % width)/(width-1)-1, 2*(i // width)/(height-1)-1
            t = min(1, max(0, (max(abs(u), abs(v))-0.55)/(1-0.55)))
            mask = smooth(t)
            final_height = (1-mask)*rng.random() + mask*(-0.1)
            edge_results.append(final_height < 0.45)
    check("F04-island-boundaries-below-sea", all(edge_results),
          {"boundary_cells": len(edge_results), "sizes": [[2, 2], [3, 7], [128, 64]]})

    radius, count = 3.0, 50000
    uniform = [rng.random() for _ in range(count)]
    square_radii = [(radius * math.sqrt(1 + 3*u))**2 for u in uniform]
    expected = 2.5 * radius**2
    mean = sum(square_radii)/count
    tolerance = 6 * radius**2 * math.sqrt(0.75/count)
    check("F07-annulus-area-moment", abs(mean-expected) < tolerance,
          {"samples": count, "mean_squared_radius": mean, "expected": expected,
           "six_sigma_tolerance": tolerance, "accepted_poisson_points_tested": False})
    wrong_mean = sum((radius*(1+u))**2 for u in uniform)/count
    check("F07-reject-linear-radius-control", abs(wrong_mean-expected) > tolerance,
          {"biased_control_mean": wrong_mean, "expected_area_uniform": expected})

    cases = [
        ("flat", [2]*25, 5, 5),
        ("bowl", [4 if i in boundary(5, 5) else 0 for i in range(25)], 5, 5),
        ("single-cell", [7], 1, 1),
        ("thin-grid", [3, 1, 2, 0], 1, 4),
    ]
    spill = [4 if i in boundary(5, 5) else 0 for i in range(25)]
    spill[2] = 1
    cases.append(("single-spill", spill, 5, 5))
    for seed in range(24):
        local_rng = random.Random(seed)
        cases.append((f"random-{seed}", [local_rng.randrange(8) for _ in range(42)], 7, 6))
    for name, elevation, width, height in cases:
        filled, parent, order = flood_with_parents(elevation, width, height)
        expected_fill = reference_spill_levels(elevation, width, height)
        border = set(boundary(width, height))
        paths_valid = True
        for start in range(len(elevation)):
            cell, seen = start, set()
            while cell != -1 and cell not in seen:
                seen.add(cell)
                nxt = parent[cell]
                if nxt == -1:
                    paths_valid = paths_valid and cell in border
                else:
                    paths_valid = paths_valid and filled[nxt] <= filled[cell]
                cell = nxt
            paths_valid = paths_valid and cell == -1
        rain = [1 + i % 3 for i in range(len(elevation))]
        flow = list(rain)
        for cell in reversed(order):
            if parent[cell] != -1:
                flow[parent[cell]] += flow[cell]
        outflow = sum(flow[i] for i in border)
        check(f"F05-{name}", filled == expected_fill and paths_valid
              and outflow == sum(rain) and all(a >= b for a, b in zip(filled, elevation)),
              {"cells": len(elevation), "matches_threshold_bfs_oracle": filled == expected_fill,
               "acyclic_boundary_drainage": paths_valid,
               "rain_input": sum(rain), "boundary_output": outflow})

    def logistic(value, capacity, rate, dt):
        return capacity*value/(value+(capacity-value)*math.exp(-rate*dt))
    growth = []
    for value in (0, 1, 25, 100, 140):
        whole = logistic(value, 100, 0.2, 7)
        halves = logistic(logistic(value, 100, 0.2, 3.5), 100, 0.2, 3.5)
        growth.append(whole >= 0 and math.isclose(whole, halves, rel_tol=1e-12, abs_tol=1e-12))
    check("F11-logistic-composition", all(growth)
          and logistic(0, 100, 0.2, 7) == 0 and logistic(100, 100, 0.2, 7) == 100,
          "Constant capacity/rate; includes zero, equilibrium and value above capacity")
    hazard_checks = []
    maximum_error = 0
    for rate in (0, 1e-12, 0.3, 20):
        for dt in (0, 0.1, 10):
            probability = -math.expm1(-rate*dt)
            half = -math.expm1(-rate*dt/2)
            error = abs((1-probability) - (1-half)**2)
            maximum_error = max(maximum_error, error)
            hazard_checks.append(0 <= probability <= 1 and error < 1e-12)
    check("F12-constant-hazard-composition", all(hazard_checks),
          {"cases": len(hazard_checks), "max_survival_error": maximum_error,
           "random_trajectory_equivalence_claimed": False})

    path = Path(__file__).resolve()
    workspace = path.parents[2]
    report_path = workspace / "Evidence/Research/2026-10-12/procgen-probe.json"
    report = {
        "timestamp": datetime.now(timezone(timedelta(hours=3))).isoformat(),
        "scope": "Independent CPU research probes of selected formulas; no Unity/game verification",
        "python": sys.version.split()[0], "lab_seed": 1206560,
        "script_sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        "passed": all(item["passed"] for item in checks), "check_count": len(checks),
        "checks": checks, "unity_import": "not-run", "visual_quality": "not-reviewed",
        "noise_library": "not-installed", "performance_benchmark": False,
    }
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
    failed = [item["id"] for item in checks if not item["passed"]]
    print(json.dumps({"checks": len(checks), "failed": failed, "report": str(report_path)}))
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(run())
