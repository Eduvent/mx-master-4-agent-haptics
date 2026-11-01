"""Configure fixed hook paths without invoking Git at event time."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "hooks"))
from configure_hooks import configure


class ConfigureHookTest(unittest.TestCase):
    def test_quoting_subdirectory_and_idempotence_preserve_other_hooks(self):
        with tempfile.TemporaryDirectory(prefix="haptics '$() ") as tmp:
            root = Path(tmp)
            (root / "hooks").mkdir()
            (root / ".codex").mkdir()
            (root / "nested").mkdir()
            shutil.copy2(ROOT / "hooks/record_event.py", root / "hooks/record_event.py")
            config = {"hooks": {"Stop": [{"hooks": [
                {"type": "command", "command": '/usr/bin/python3 "$(git rev-parse --show-toplevel)/hooks/record_event.py"', "timeout": 3},
                {"type": "command", "command": "other-command", "timeout": 1}]}]}}
            path = root / ".codex/hooks.json"
            path.write_text(json.dumps(config))
            self.assertEqual(configure(root, "python"), 1)
            timestamp = path.stat().st_mtime_ns
            self.assertEqual(configure(root, "python"), 0)
            self.assertEqual(path.stat().st_mtime_ns, timestamp)
            handlers = json.loads(path.read_text())["hooks"]["Stop"][0]["hooks"]
            self.assertEqual(handlers[1], config["hooks"]["Stop"][0]["hooks"][1])
            self.assertNotIn("git rev-parse", handlers[0]["command"])
            result = subprocess.run(["/bin/sh", "-c", handlers[0]["command"]],
                                    input=b'{"hook_event_name":"Stop"}', capture_output=True,
                                    check=True, cwd=root / "nested", env=dict(os.environ, AGENT_HAPTICS_LOG=str(root / "test.jsonl")))
            self.assertEqual(json.loads(result.stdout), {})
            self.assertFalse(result.stderr)
            self.assertEqual(json.loads((root / "test.jsonl").read_text())["event"], "Stop")


    def test_fresh_checkout_generates_config_from_portable_example(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / ".codex").mkdir()
            shutil.copy2(ROOT / ".codex/hooks.example.json", root / ".codex/hooks.example.json")
            self.assertEqual(configure(root, "python"), 6)
            path = root / ".codex/hooks.json"
            config = json.loads(path.read_text())
            for groups in config["hooks"].values():
                for group in groups:
                    for handler in group["hooks"]:
                        self.assertIn(str(root / "hooks/record_event.py"), handler["command"])
                        self.assertNotIn("git rev-parse", handler["command"])
            self.assertEqual(configure(root, "python"), 0)
