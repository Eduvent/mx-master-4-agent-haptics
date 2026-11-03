# Typed hook records

## Integration APIs

`StopEventReader.ReadPendingRecords()` returns an ordered
`IReadOnlyList<HapticsEventRecord>` of newly completed JSONL records.
`StopEventMonitor.ForRecords(path, onRecords, onError, reconciliationMs = 1000,
enableNotifications = true)` delivers nonempty batches through
`Action<IReadOnlyList<HapticsEventRecord>>`. These types are internal to the plugin
assembly, just like the existing reader and monitor.

The existing `StopEventMonitor(path, Action<int> onStops, onError, ...)`
constructor remains the count adapter. It reports only positive Stop counts;
non-Stop batches do not invoke it. A named typed factory, rather than a constructor
overload, preserves inference for existing callbacks that ignore their argument.
`ReadNewStops()` and `ReadPendingStops()` both count Stops from
`ReadPendingRecords()`. All three methods consume **one cursor**: switching APIs
never replays a record, and count reads also consume non-Stop records.

## Record and normalization policy

- `RawEvent` preserves the exact string in `event`, including unknown event names.
- Only the exact raw event `Stop` maps to `NormalizedEvent = "turn_finished"`.
  Every other name maps to null, including `PermissionRequest` and `Interrupt`.
  Neither `approval_required` nor `input_required` is inferred from hook metadata.
- Optional string properties are `Timestamp`, `SessionId`, `TurnId`, `ToolUseId`,
  `ToolName`, and `PermissionMode`. They correspond only to the existing hook's
  `timestamp`, `session_id`, `turn_id`, `tool_use_id`, `tool_name`, and
  `permission_mode`. Strings are preserved, not parsed, trimmed, or synthesized.
- `MessageHasQuestionMark` accepts only the existing boolean
  `message_has_question_mark`; it does not classify a request for human attention.
- Missing or wrongly typed optional metadata becomes null. Optional strings
  with malformed Unicode (for example, an unpaired `\uD800` escape) also become
  null, without rejecting the event or its neighboring records. Valid surrogate
  pairs and empty strings are preserved. There is no invented request ID;
  `ToolUseId` preserves a tool request identity only when the hook supplied one.
- No source JSON, message content, tool arguments, or unallowlisted properties are
  retained. Malformed JSON, non-object roots, and missing/non-string or malformed
  Unicode `event` values are skipped when their complete newline-terminated record
  is consumed. Deferred string/property decoding errors never abort the batch;
  valid neighboring records remain ordered and are not replayed on the next read.

## Cursor, repetition, and concurrency

Startup history is skipped by starting at the existing byte length. Incomplete
lines wait for a newline; a smaller file length resets the cursor for truncation.
This preserves the existing byte-offset policy, not rotation detection: replacement
or truncate-and-regrow to an equal/larger length between reads is not detected.

**Duplicate notifications** are repeated watcher/timer checks of consumed offsets;
they produce no replay. **Semantic repeated records** are separate appended lines,
even if every field matches. Each is delivered in file order. There is no logical
deduplication, identity cache, session/turn suppression, or global cooldown. Thus
missing IDs, shared session/turn IDs, different tool requests, and simultaneous
sessions never cause one complete record to suppress another.

The standalone reader requires caller serialization. The monitor keeps reading,
ordered batch callbacks, and disposal inside its existing single lock. Watcher
notifications and timer reconciliation cannot overlap record callbacks. Disposal
waits for an active callback and prevents later callbacks; pending unread records
need not be drained on disposal. Callback exceptions go to the existing error
handler after the cursor has advanced, so callbacks are not retried. Existing
watcher error notifications retain their separate diagnostic callback behavior.

Run the isolated console tests with:

```sh
dotnet run --project tests/stop_reader/StopReaderTests.csproj -c Release
```
