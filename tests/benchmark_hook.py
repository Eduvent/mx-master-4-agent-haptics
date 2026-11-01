#!/usr/bin/env python3
"""Compare hook commands on temporary logs; never activate the mouse."""
import argparse
import datetime
import json
import math
import os
from pathlib import Path
import random
import resource
import statistics
import subprocess
import sys
import tempfile
import time

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "hooks"))
from configure_hooks import hook_command

LEGACY = '/usr/bin/python3 "$(git rev-parse --show-toplevel)/hooks/record_event.py"'


def benchmark(samples=120, modes=None):
    commands = {
        "legacy_python": ["/bin/sh", "-c", LEGACY],
        "python_static": ["/bin/sh", "-c", hook_command(ROOT, "python")],
        "go_static": ["/bin/sh", "-c", hook_command(ROOT, "go")],
    }
    modes = modes or list(commands)
    measured = {mode: {"elapsed": [], "cpu": [], "to_metadata": []} for mode in modes}
    payload = b'{"hook_event_name":"Stop","session_id":"benchmark"}'
    with tempfile.TemporaryDirectory(prefix="haptics-hook-") as directory:
        log = Path(directory) / "events.jsonl"
        env = dict(os.environ, AGENT_HAPTICS_LOG=str(log))
        order = random.Random(42)
        for sample in range(samples + 5):
            sequence = list(modes)
            order.shuffle(sequence)
            for mode in sequence:
                usage = resource.getrusage(resource.RUSAGE_CHILDREN)
                start_wall = time.time_ns()
                start = time.perf_counter_ns()
                process = subprocess.run(commands[mode], input=payload, capture_output=True,
                                         check=True, cwd=ROOT, env=env)
                elapsed = (time.perf_counter_ns() - start) / 1e6
                after = resource.getrusage(resource.RUSAGE_CHILDREN)
                if process.stdout.strip() != b'{}' or process.stderr:
                    raise RuntimeError("Unexpected hook response: " + mode)
                record = json.loads(log.read_text().splitlines()[-1])
                if record.get("event") != "Stop" or record.get("session_id") != "benchmark":
                    raise RuntimeError("Invalid hook record: " + mode)
                if sample < 5:
                    continue
                timestamp = datetime.datetime.fromisoformat(record["timestamp"]).timestamp()
                measured[mode]["elapsed"].append(elapsed)
                measured[mode]["cpu"].append(((after.ru_utime + after.ru_stime) - (usage.ru_utime + usage.ru_stime)) * 1000)
                measured[mode]["to_metadata"].append((timestamp * 1e9 - start_wall) / 1e6)
    results = []
    for mode in modes:
        values = measured[mode]
        elapsed = sorted(values["elapsed"])
        results.append(dict(mode=mode, samples=samples,
                            median_ms=statistics.median(elapsed), p95_ms=elapsed[math.ceil(samples*.95)-1],
                            max_ms=max(elapsed), child_cpu_median_ms=statistics.median(values["cpu"]),
                            to_metadata_median_ms=statistics.median(values["to_metadata"])))
    return {"samples_per_mode": samples, "warmups_per_mode": 5, "order": "interleaved, shuffled with seed 42",
            "boundary": "spawn configured shell through process exit; timestamp metadata precedes file write",
            "results": results}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--samples", type=int, default=120)
    parser.add_argument("--modes", nargs="+", choices=["legacy_python", "python_static", "go_static"])
    args = parser.parse_args()
    if args.samples < 1:
        parser.error("samples must be positive")
    print(json.dumps(benchmark(args.samples, args.modes), indent=2))
