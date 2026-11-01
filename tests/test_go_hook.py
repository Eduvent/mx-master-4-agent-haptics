"""Contract parity, default path and concurrent append coverage for the Go hook."""
import concurrent.futures
import datetime
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
BINARY = ROOT / ".local/bin/record-event"
PYTHON = ["/usr/bin/python3", str(ROOT / "hooks/record_event.py")]
sys.path.insert(0, str(ROOT / "hooks"))
from configure_hooks import configure


@unittest.skipUnless(BINARY.is_file(), "Build the Go hook before running parity tests")
class GoHookTest(unittest.TestCase):
    def invoke(self, command, raw, path, cwd=None):
        return subprocess.run(command, input=raw, capture_output=True, check=True, cwd=cwd,
                              env=dict(os.environ, AGENT_HAPTICS_LOG=str(path)))

    def test_contract_matches_python_without_private_content(self):
        payloads = [
            {"hook_event_name": event, "session_id": "s", "turn_id": "t",
             "tool_name": "herramienta", "tool_use_id": "id", "permission_mode": "default",
             "last_assistant_message": "¿private?", "prompt": "private", "tool_input": {"secret": "private"}}
            for event in ("UserPromptSubmit", "PermissionRequest", "Stop", "Interrupt", "PreToolUse", "PostToolUse")
        ] + [{"hook_event_name": "Stop", "session_id": 42, "turn_id": None,
              "tool_name": False, "last_assistant_message": "sin pregunta"},
             {"hook_event_name": "Stop", "last_assistant_message": 42}]
        with tempfile.TemporaryDirectory() as tmp:
            records = []
            for name, command in (("python", PYTHON), ("go", [str(BINARY)])):
                path = Path(tmp) / name / "events.jsonl"
                for payload in payloads:
                    result = self.invoke(command, json.dumps(payload).encode(), path, tmp)
                    self.assertEqual(json.loads(result.stdout), {})
                    self.assertFalse(result.stderr)
                values = [json.loads(line) for line in path.read_text().splitlines()]
                self.assertNotIn("private", path.read_text())
                for value in values:
                    stamp = datetime.datetime.fromisoformat(value.pop("timestamp"))
                    self.assertEqual(stamp.utcoffset(), datetime.timedelta())
                records.append(values)
            self.assertEqual(*records)

    def test_invalid_input_and_write_failure_remain_fail_open(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "absent.jsonl"
            for command in (PYTHON, [str(BINARY)]):
                for raw in (b'not json', b'[]', b'null', b'{}', b'{"hook_event_name":"Unknown"}',
                            b'{"hook_event_name":42}', b'{"hook_event_name":"Stop"} {}'):
                    result = self.invoke(command, raw, path)
                    self.assertEqual(json.loads(result.stdout), {})
                    self.assertIn(b"could not record", result.stderr)
                self.assertFalse(path.exists())
                # A directory cannot be opened as an append-only log file.
                result = self.invoke(command, b'{"hook_event_name":"Stop"}', Path(tmp))
                self.assertEqual(json.loads(result.stdout), {})
                self.assertIn(b"could not record", result.stderr)

    def test_default_path_is_independent_of_cwd(self):
        with tempfile.TemporaryDirectory(prefix="haptics path '") as tmp:
            root = Path(tmp)
            (root / ".local/bin").mkdir(parents=True)
            copied = root / ".local/bin/record-event"
            shutil.copy2(BINARY, copied)
            env = dict(os.environ)
            env.pop("AGENT_HAPTICS_LOG", None)
            result = subprocess.run([str(copied)], input=b'{"hook_event_name":"Stop"}',
                                    capture_output=True, check=True, cwd="/tmp", env=env)
            self.assertEqual(json.loads(result.stdout), {})
            self.assertFalse(result.stderr)
            record = json.loads((root / ".local/events.jsonl").read_text())
            self.assertEqual(record["event"], "Stop")

    def test_concurrent_appends_do_not_lose_or_mix_records(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "events.jsonl"
            def write(index):
                raw = json.dumps({"hook_event_name": "Stop", "turn_id": str(index)}).encode()
                self.assertFalse(self.invoke([str(BINARY)], raw, path).stderr)
            with concurrent.futures.ThreadPoolExecutor(max_workers=8) as workers:
                list(workers.map(write, range(40)))
            rows = [json.loads(line) for line in path.read_text().splitlines()]
            self.assertEqual(len(rows), 40)
            self.assertEqual({row["turn_id"] for row in rows}, {str(i) for i in range(40)})

    def test_go_is_default_for_new_configuration(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / ".codex").mkdir()
            (root / ".local/bin").mkdir(parents=True)
            shutil.copy2(ROOT / ".codex/hooks.example.json", root / ".codex/hooks.example.json")
            shutil.copy2(BINARY, root / ".local/bin/record-event")
            self.assertEqual(configure(root), 6)
            config = json.loads((root / ".codex/hooks.json").read_text())
            command = config["hooks"]["Stop"][0]["hooks"][0]["command"]
            self.assertIn("/.local/bin/record-event", command)
            self.assertNotIn("python", command)
            self.assertEqual(configure(root), 0)
