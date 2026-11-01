#!/usr/bin/env python3
"""Resolve project paths once, leaving event commands independent of Git and cwd."""
import argparse
import json
from pathlib import Path
import shlex
import shutil

ROOT = Path(__file__).resolve().parents[1]


def hook_command(root=ROOT, backend="go"):
    root = Path(root).resolve()
    if backend == "python":
        return shlex.join(["/usr/bin/python3", str(root / "hooks/record_event.py")])
    if backend == "go":
        return shlex.quote(str(root / ".local/bin/record-event"))
    raise ValueError("Unknown hook backend")


def configure(root=ROOT, backend="go"):
    root = Path(root).resolve()
    if backend == "go" and not (root / ".local/bin/record-event").is_file():
        raise ValueError("Build .local/bin/record-event before selecting Go")
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


def configure_global_stop(root=ROOT, backend="go", user_dir=None):
    """Install one user Stop handler and remove only this repo's local copy."""
    root = Path(root).resolve()
    configure(root, backend)
    user_dir = Path(user_dir) if user_dir is not None else Path.home() / ".codex"
    global_path = user_dir / "hooks.json"
    local_path = root / ".codex/hooks.json"
    global_config = json.loads(global_path.read_text()) if global_path.exists() else {"hooks": {}}
    local_config = json.loads(local_path.read_text())
    commands = {hook_command(root, b) for b in ("go", "python")}

    def remove_ours(config):
        groups = []
        for group in config.get("hooks", {}).get("Stop", []):
            handlers = [h for h in group.get("hooks", [])
                        if not (h.get("type") == "command" and h.get("command") in commands)]
            if handlers:
                groups.append(dict(group, hooks=handlers))
        if groups:
            config["hooks"]["Stop"] = groups
        else:
            config.get("hooks", {}).pop("Stop", None)

    remove_ours(global_config)
    global_config.setdefault("hooks", {}).setdefault("Stop", []).append({"hooks": [
        {"type": "command", "command": hook_command(root, backend), "timeout": 3}]})
    remove_ours(local_config)
    for path, config in ((global_path, global_config), (local_path, local_config)):
        content = json.dumps(config, ensure_ascii=False, indent=2) + "\n"
        if path.exists() and json.loads(path.read_text()) == config:
            continue
        path.parent.mkdir(parents=True, exist_ok=True)
        backup = path.with_name(path.name + ".haptics-backup")
        if path.exists() and not backup.exists():
            shutil.copy2(path, backup)
        path.write_text(content)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--backend", choices=["python", "go"], default="go")
    parser.add_argument("--global-stop", action="store_true", help="Enable completion haptics for all local Codex projects")
    args = parser.parse_args()
    if args.global_stop:
        configure_global_stop(backend=args.backend)
        print("Configured global Stop; removed local duplicate ({})".format(args.backend))
    else:
        print("Updated {} hooks ({})".format(configure(backend=args.backend), args.backend))
