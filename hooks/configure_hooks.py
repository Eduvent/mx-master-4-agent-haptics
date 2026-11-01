#!/usr/bin/env python3
"""Resolve project paths once, leaving event commands independent of Git and cwd."""
import argparse
import json
from pathlib import Path
import shlex

ROOT = Path(__file__).resolve().parents[1]


def hook_command(root=ROOT, backend="python"):
    root = Path(root).resolve()
    if backend == "python":
        return shlex.join(["/usr/bin/python3", str(root / "hooks/record_event.py")])
    raise ValueError("Unknown hook backend")


def configure(root=ROOT, backend="python"):
    root = Path(root).resolve()
    path = root / ".codex/hooks.json"
    source = path if path.exists() else root / ".codex/hooks.example.json"
    config = json.loads(source.read_text())
    command = hook_command(root, backend)
    changed = 0
    for groups in config["hooks"].values():
        for group in groups:
            for handler in group.get("hooks", []):
                old = handler.get("command", "")
                if handler.get("type") == "command" and ("/hooks/record_event.py" in old or "/.local/bin/record-event" in old):
                    handler["command"] = command
                    changed += old != command
    # Do not invalidate hook trust or touch the file for an unchanged definition.
    if changed or not path.exists():
        path.write_text(json.dumps(config, ensure_ascii=False, indent=2) + "\n")
    return changed


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--backend", choices=["python"], default="python")
    args = parser.parse_args()
    print("Updated {} hooks ({})".format(configure(backend=args.backend), args.backend))
