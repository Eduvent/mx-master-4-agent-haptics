#!/usr/bin/env python3
"""Experimental, bounded observer of new assistant messages in one rollout."""
import argparse
import json
import time
from pathlib import Path


def metadata(line):
    try:
        record = json.loads(line)
        payload = record.get("payload", {})
        if (record.get("type") != "response_item" or payload.get("type") != "message"
                or payload.get("role") != "assistant"):
            return None
        phase = payload.get("phase")
        if phase not in ("commentary", "final_answer"):
            return None
        texts = [part.get("text", "") for part in payload.get("content", [])
                 if isinstance(part, dict) and part.get("type") == "output_text"
                 and isinstance(part.get("text"), str)]
        return {"timestamp": record.get("timestamp"), "phase": phase,
                "has_question_mark": any("?" in text or "¿" in text for text in texts)}
    except (ValueError, TypeError, AttributeError):
        return None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("rollout", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--seconds", type=float, default=120)
    args = parser.parse_args()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    deadline = time.monotonic() + max(0, args.seconds)
    with args.rollout.open("r", encoding="utf-8") as source, args.output.open("a", encoding="utf-8") as output:
        source.seek(0, 2)
        while time.monotonic() < deadline:
            position = source.tell()
            line = source.readline()
            if not line.endswith("\n"):
                source.seek(position)
                time.sleep(0.1)
                continue
            event = metadata(line)
            if event is not None:
                output.write(json.dumps(event, ensure_ascii=False) + "\n")
                output.flush()


if __name__ == "__main__":
    main()
