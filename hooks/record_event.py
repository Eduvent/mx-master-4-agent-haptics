#!/usr/bin/env python3
"""Record selected Codex hook metadata without changing agent behavior."""

import datetime
import json
import os
from pathlib import Path
import sys

EVENTS = {"UserPromptSubmit", "PermissionRequest", "Stop", "Interrupt", "PreToolUse", "PostToolUse"}


def main():
    try:
        payload = json.load(sys.stdin)
        event = payload.get("hook_event_name")
        if event not in EVENTS:
            raise ValueError("Unsupported hook event")
        record = {"timestamp": datetime.datetime.now(datetime.timezone.utc).isoformat(),
                  "event": event}
        for key in ("session_id", "turn_id"):
            if isinstance(payload.get(key), str):
                record[key] = payload[key]
        for key in ("tool_name", "tool_use_id", "permission_mode"):
            if isinstance(payload.get(key), str):
                record[key] = payload[key]
        if event == "Stop" and isinstance(payload.get("last_assistant_message"), str):
            record["message_has_question_mark"] = "?" in payload["last_assistant_message"] or "¿" in payload["last_assistant_message"]
        path = Path(os.environ.get("AGENT_HAPTICS_LOG", Path(__file__).resolve().parent.parent / ".local" / "events.jsonl"))
        path.parent.mkdir(parents=True, exist_ok=True)
        with path.open("a", encoding="utf-8") as log:
            log.write(json.dumps(record, ensure_ascii=False) + "\n")
    except (ValueError, OSError, TypeError, AttributeError) as error:
        print(f"agent-haptics: could not record event: {error}", file=sys.stderr)
    # Stop requires JSON; this response does not change decisions or approvals.
    print("{}")


if __name__ == "__main__":
    main()
