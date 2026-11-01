import importlib.util
import json
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("observe_messages", Path(__file__).resolve().parent.parent / "hooks" / "observe_messages.py")
observer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(observer)


class ObserveMessagesTest(unittest.TestCase):
    def test_commentary_question_emits_metadata_without_text(self):
        record = {"timestamp": "time", "type": "response_item", "payload": {
            "type": "message", "role": "assistant", "phase": "commentary",
            "content": [{"type": "output_text", "text": "¿Secreto?"}]}}
        result = observer.metadata(json.dumps(record))
        self.assertEqual(result, {"timestamp": "time", "phase": "commentary", "has_question_mark": True})
        self.assertNotIn("Secreto", json.dumps(result))

    def test_other_records_are_ignored(self):
        self.assertIsNone(observer.metadata("not json"))
        self.assertIsNone(observer.metadata(json.dumps({"type": "response_item", "payload": {"type": "message", "role": "user"}})))


if __name__ == "__main__":
    unittest.main()
