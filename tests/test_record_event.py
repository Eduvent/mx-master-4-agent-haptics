import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parent.parent / "hooks" / "record_event.py"


class RecordEventTest(unittest.TestCase):
    def test_supported_events_are_recorded_without_sensitive_input(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "events.jsonl"
            env = dict(os.environ, AGENT_HAPTICS_LOG=str(path))
            for event in ("UserPromptSubmit", "PermissionRequest", "Stop", "Interrupt"):
                payload = {"hook_event_name": event, "session_id": "s", "turn_id": "t",
                           "tool_input": {"command": "private"}, "prompt": "private"}
                result = subprocess.run([sys.executable, str(SCRIPT)], input=json.dumps(payload),
                                        text=True, capture_output=True, env=env, check=True)
                self.assertEqual(json.loads(result.stdout), {})
            records = [json.loads(line) for line in path.read_text().splitlines()]
            self.assertEqual([r["event"] for r in records],
                             ["UserPromptSubmit", "PermissionRequest", "Stop", "Interrupt"])
            self.assertNotIn("private", path.read_text())
            self.assertTrue(all(r["session_id"] == "s" and r["turn_id"] == "t" for r in records))

    def test_invalid_input_does_not_block_codex(self):
        result = subprocess.run([sys.executable, str(SCRIPT)], input="not json", text=True,
                                capture_output=True, check=True)
        self.assertEqual(json.loads(result.stdout), {})
        self.assertIn("could not record", result.stderr)

    def test_diagnostics_record_metadata_without_messages_or_tool_arguments(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "events.jsonl"
            env = dict(os.environ, AGENT_HAPTICS_LOG=str(path))
            payloads = [
                {"hook_event_name": "PreToolUse", "tool_name": "example", "tool_use_id": "id", "tool_input": {"secret": "private"}},
                {"hook_event_name": "Stop", "last_assistant_message": "¿private?"},
            ]
            for payload in payloads:
                subprocess.run([sys.executable, str(SCRIPT)], input=json.dumps(payload),
                               text=True, capture_output=True, env=env, check=True)
            records = [json.loads(line) for line in path.read_text().splitlines()]
            self.assertEqual(records[0]["tool_name"], "example")
            self.assertEqual(records[0]["tool_use_id"], "id")
            self.assertTrue(records[1]["message_has_question_mark"])
            self.assertNotIn("private", path.read_text())


if __name__ == "__main__":
    unittest.main()
