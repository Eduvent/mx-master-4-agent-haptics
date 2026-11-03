using Loupedeck.CodexHapticsPlugin;

var path = Path.Combine(Path.GetTempPath(), $"codex-stop-test-{Guid.NewGuid()}.jsonl");
try
{
    File.WriteAllText(path, "{\"event\":\"Stop\"}\n");
    var reader = new StopEventReader(path);
    if (reader.ReadNewStops() != 0) throw new Exception("Replayed historical record");
    File.AppendAllText(path, "{\"event\":\"UserPromptSubmit\"}\n{\"event\":\"Stop\"");
    if (reader.ReadNewStops() != 0) throw new Exception("Read an incomplete line");
    File.AppendAllText(path, "}\ninvalid\n{\"event\":\"Stop\"}\n");
    if (reader.ReadNewStops() != 2 || reader.ReadNewStops() != 0) throw new Exception("Stop count or deduplication failed");
    File.WriteAllText(path, "{\"event\":\"Stop\"}\n");
    if (reader.ReadNewStops() != 1) throw new Exception("Truncation handling failed");
    Console.WriteLine("Stop reader tests passed");
}
finally { File.Delete(path); }

foreach (var notifications in new[] { true, false })
{
    var directory = Path.Combine(Path.GetTempPath(), $"codex-monitor-{Guid.NewGuid()}");
    Directory.CreateDirectory(directory);
    var log = Path.Combine(directory, "events.jsonl");
    File.WriteAllText(log, "{\"event\":\"Stop\"}\n");
    var stops = 0;
    var errors = new List<Exception>();
    using (var monitor = new StopEventMonitor(log, count => Interlocked.Add(ref stops, count),
        error => { lock (errors) errors.Add(error); }, 25, notifications))
    {
        File.AppendAllText(log, "{\"event\":\"Stop\"");
        await Task.Delay(100);
        if (Volatile.Read(ref stops) != 0) throw new Exception("Replayed history or partial line");
        File.AppendAllText(log, "}\ninvalid\n{\"event\":\"Stop\"}\n");
        if (!SpinWait.SpinUntil(() => Volatile.Read(ref stops) == 2, 3000)) throw new Exception("Missed stops");
        await Task.Delay(200);
        if (stops != 2) throw new Exception("Duplicate notifications replayed stops");
        lock (errors) if (errors.Count > 0) throw new AggregateException(errors);
    }
    File.AppendAllText(log, "{\"event\":\"Stop\"}\n");
    await Task.Delay(100);
    if (stops != 2) throw new Exception("Callback after disposal");
    Directory.Delete(directory, true);
}
Console.WriteLine("Monitor notification, reconciliation, deduplication and disposal tests passed");

// With reconciliation far beyond the deadline, creation must arrive by notification.
var creationDirectory = Path.Combine(Path.GetTempPath(), $"codex-create-{Guid.NewGuid()}");
Directory.CreateDirectory(creationDirectory);
var creationPath = Path.Combine(creationDirectory, "events.jsonl");
var createdStops = 0;
using (var monitor = new StopEventMonitor(creationPath,
    count => Interlocked.Add(ref createdStops, count), error => Console.Error.WriteLine(error), 30000))
{
    await Task.Delay(200); // Let the initial reconciliation finish before writing.
    File.WriteAllText(creationPath, "{\"event\":\"Stop\"}\n");
    if (!SpinWait.SpinUntil(() => Volatile.Read(ref createdStops) == 1, 3000))
        throw new Exception("File creation notification was missed");
}
Directory.Delete(creationDirectory, true);
Console.WriteLine("Notification-only file creation test passed");

var failures = 0;
Run("Typed metadata, privacy and Stop-only normalization", () => WithLog(log =>
{
    var reader = new StopEventReader(log);
    File.AppendAllText(log, """
        {"event":"Stop","timestamp":"2026-01-01T00:00:00+00:00","session_id":" sesión ","turn_id":"turn","tool_use_id":"request","tool_name":"example","permission_mode":"default","message_has_question_mark":true,"last_assistant_message":"PRIVATE","tool_input":{"secret":"PRIVATE"},"request_id":"invented"}
        {"event":"PermissionRequest"}
        {"event":"Interrupt"}
        {"event":"UserPromptSubmit"}
        {"event":"PreToolUse"}
        {"event":"PostToolUse"}
        {"event":"stop"}
        {"event":"future_event"}
        """ + "\n");
    var records = reader.ReadPendingRecords();
    Check(records.Count == 8, "Lost raw events");
    var first = records[0];
    Check(first.RawEvent == "Stop" && first.NormalizedEvent == "turn_finished", "Stop mapping");
    Check(first.Timestamp == "2026-01-01T00:00:00+00:00" && first.SessionId == " sesión " &&
        first.TurnId == "turn" && first.ToolUseId == "request" && first.ToolName == "example" &&
        first.PermissionMode == "default" && first.MessageHasQuestionMark == true, "Metadata changed");
    Check(records.Skip(1).Select(record => record.RawEvent).SequenceEqual(new[] {
        "PermissionRequest", "Interrupt", "UserPromptSubmit", "PreToolUse", "PostToolUse", "stop", "future_event" }), "Event ordering");
    Check(records.Skip(1).All(record => record.NormalizedEvent == null && record.Timestamp == null &&
        record.SessionId == null && record.TurnId == null && record.ToolUseId == null &&
        record.ToolName == null && record.PermissionMode == null && record.MessageHasQuestionMark == null),
        "Invented metadata or attention mapping");
    var serialized = System.Text.Json.JsonSerializer.Serialize(first);
    Check(!serialized.Contains("PRIVATE") && !serialized.Contains("invented"), "Retained unallowlisted metadata");
}));
Run("Typed optional metadata rejects wrong types without rejecting event", () => WithLog(log =>
{
    var reader = new StopEventReader(log);
    File.AppendAllText(log, "{\"event\":\"Stop\",\"timestamp\":42,\"session_id\":42,\"turn_id\":null,\"tool_use_id\":{},\"tool_name\":[],\"permission_mode\":false,\"message_has_question_mark\":\"true\"}\n" +
        "{\"event\":\"Stop\",\"session_id\":\"\",\"message_has_question_mark\":false}\n");
    var records = reader.ReadPendingRecords();
    Check(records.Count == 2, "Rejected event with optional invalid metadata");
    var first = records[0];
    Check(first.Timestamp == null && first.SessionId == null && first.TurnId == null &&
        first.ToolUseId == null && first.ToolName == null && first.PermissionMode == null &&
        first.MessageHasQuestionMark == null, "Coerced invalid metadata");
    Check(records[1].SessionId == "" && records[1].MessageHasQuestionMark == false, "Changed empty ID or false flag");
}));
Run("Malformed optional strings preserve all three Stops in order", () => WithLog(log =>
{
    foreach (var key in new[] { "timestamp", "session_id", "turn_id", "tool_use_id", "tool_name", "permission_mode" })
    {
        var reader = new StopEventReader(log);
        File.AppendAllText(log, "{\"event\":\"Stop\",\"session_id\":\"first\"}\n" +
            "{\"event\":\"Stop\",\"" + key + "\":\"\\uD800\"}\n" +
            "{\"event\":\"Stop\",\"session_id\":\"last\"}\n");
        var hash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(log));
        var records = reader.ReadPendingRecords();
        Check(records.Count == 3 && records.All(record => record.RawEvent == "Stop" && record.NormalizedEvent == "turn_finished"), "Malformed optional string lost Stops");
        Check(records.Select(record => record.SessionId).SequenceEqual(new string[] { "first", null, "last" }), "First/last order changed");
        var middle = records[1];
        Check(middle.Timestamp == null && middle.SessionId == null && middle.TurnId == null &&
            middle.ToolUseId == null && middle.ToolName == null && middle.PermissionMode == null &&
            middle.MessageHasQuestionMark == null, "Malformed optional string was not absent");
        Check(reader.ReadPendingRecords().Count == 0, "Malformed optional string replayed records");
        Check(hash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(log))), "Reader changed input file");
    }
}));
Run("Count adapters retain Stops with malformed optional strings and consume once", () => WithLog(log =>
{
    foreach (var pending in new[] { true, false })
    {
        var reader = new StopEventReader(log);
        File.AppendAllText(log, "{\"event\":\"Stop\",\"session_id\":\"first\"}\n" +
            "{\"event\":\"Stop\",\"session_id\":\"\\uD800\"}\n" +
            "{\"event\":\"Stop\",\"session_id\":\"last\"}\n");
        var hash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(log));
        Func<int> read = pending ? reader.ReadPendingStops : reader.ReadNewStops;
        Check(read() == 3, "Count adapter lost Stop with malformed optional string");
        Check(read() == 0 && reader.ReadPendingRecords().Count == 0, "Count adapter replayed records");
        Check(hash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(log))), "Count adapter changed input file");
    }
}));
Run("Malformed required event strings and property names cannot discard neighbors", () => WithLog(log =>
{
    var reader = new StopEventReader(log);
    File.AppendAllText(log, "{\"event\":\"Stop\",\"session_id\":\"first\"}\n" +
        "{\"event\":\"\\uD800\"}\n{\"event\":\"\\uDC00\"}\n{\"\\uD800\":true}\n" +
        "{\"event\":\"Stop\",\"session_id\":\"last\",\"tool_name\":\"\\uD83D\\uDE00\"}\n");
    var hash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(log));
    var records = reader.ReadPendingRecords();
    Check(records.Count == 2 && records.All(record => record.RawEvent == "Stop"), "Malformed required event lost neighbors");
    Check(records.Select(record => record.SessionId).SequenceEqual(new[] { "first", "last" }), "Neighbor order changed");
    Check(records[1].ToolName == "😀", "Valid surrogate pair changed");
    Check(reader.ReadPendingRecords().Count == 0, "Malformed required event replayed records");
    Check(hash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(log))), "Malformed record handling changed input file");
}));
Run("Typed incremental history, partial lines, invalid input and truncation", () => WithLog(log =>
{
    File.WriteAllText(log, "{\"event\":\"Stop\"}\n");
    var reader = new StopEventReader(log);
    Check(reader.ReadPendingRecords().Count == 0, "Replayed history");
    File.AppendAllText(log, "{\"event\":\"Stop\",\"session_id\":\"sesión\"");
    Check(reader.ReadPendingRecords().Count == 0, "Consumed partial record");
    File.AppendAllText(log, "}\ninvalid\nnull\n[]\n{}\n{\"event\":42}\n{\"event\":\"Interrupt\"}\n");
    var records = reader.ReadPendingRecords();
    Check(records.Count == 2 && records[0].SessionId == "sesión" && records[1].RawEvent == "Interrupt", "Parse tolerance or ordering");
    Check(reader.ReadPendingRecords().Count == 0, "Replayed consumed offsets");
    File.WriteAllText(log, "{\"event\":\"Stop\"}\n");
    Check(reader.ReadPendingRecords().Single().RawEvent == "Stop", "Truncation reset");
    Check(reader.ReadPendingRecords().Count == 0, "Replayed after truncation");
}));
Run("Count adapters and typed reads share one cursor", () => WithLog(log =>
{
    var reader = new StopEventReader(log);
    File.AppendAllText(log, "{\"event\":\"Interrupt\"}\n{\"event\":\"Stop\"}\n");
    Check(reader.ReadNewStops() == 1 && reader.ReadPendingRecords().Count == 0, "Legacy adapter cursor");
    File.AppendAllText(log, "{\"event\":\"Stop\"}\n");
    Check(reader.ReadPendingRecords().Count == 1 && reader.ReadPendingStops() == 0, "Typed cursor diverged");
    File.AppendAllText(log, "{\"event\":\"Stop\"}\n{\"event\":\"Interrupt\"}\n");
    Check(reader.ReadPendingStops() == 1 && reader.ReadNewStops() == 0, "Pending count adapter cursor");
}));
Run("Repeated semantic records and distinct requests are not deduplicated", () => WithLog(log =>
{
    var reader = new StopEventReader(log);
    var rows = new[] {
        "{\"event\":\"Stop\",\"session_id\":\"s\",\"turn_id\":\"t\",\"tool_use_id\":\"a\"}\n",
        "{\"event\":\"Stop\",\"session_id\":\"other\",\"turn_id\":\"t\",\"tool_use_id\":\"b\"}\n",
        "{\"event\":\"Stop\",\"session_id\":\"s\",\"turn_id\":\"t\",\"tool_use_id\":\"c\"}\n",
        "{\"event\":\"Stop\"}\n" };
    File.AppendAllText(log, rows[0] + rows[0] + rows[1] + rows[2] + rows[3] + rows[3]);
    var records = reader.ReadPendingRecords();
    Check(records.Count == 6, "Logical deduplication discarded a record");
    Check(records.Select(record => record.ToolUseId).SequenceEqual(new[] { "a", "a", "b", "c", null, null }), "Request identity/order changed");
    Check(reader.ReadPendingRecords().Count == 0, "Duplicate checks replayed records");
}));
Run("Legacy count monitor ignores non-Stop batches", () => WithLog(log =>
{
    var calls = 0;
    var stops = 0;
    var errors = new List<Exception>();
    using var monitor = new StopEventMonitor(log, count =>
    {
        Interlocked.Increment(ref calls);
        Interlocked.Add(ref stops, count);
    }, error => { lock (errors) errors.Add(error); }, 10, false);
    File.AppendAllText(log, "{\"event\":\"PermissionRequest\"}\n");
    Thread.Sleep(100);
    Check(Volatile.Read(ref calls) == 0, "Non-Stop batch invoked count callback");
    File.AppendAllText(log, "{\"event\":\"Stop\"}\n");
    Check(SpinWait.SpinUntil(() => Volatile.Read(ref stops) == 1, 3000), "Lost legacy count");
    Thread.Sleep(100);
    Check(Volatile.Read(ref calls) == 1, "Legacy callback replay");
    lock (errors) Check(errors.Count == 0, "Legacy monitor error");
}));
foreach (var notifications in new[] { true, false })
    Run($"Typed monitor delivery and disposal (notifications={notifications})", () => WithLog(log =>
    {
        File.WriteAllText(log, "{\"event\":\"Stop\"}\n");
        var records = new List<HapticsEventRecord>();
        var errors = new List<Exception>();
        using (var monitor = StopEventMonitor.ForRecords(log,
            batch => { lock (records) records.AddRange(batch); },
            error => { lock (errors) errors.Add(error); }, 10, notifications))
        {
            File.AppendAllText(log, "{\"event\":\"PermissionRequest\"");
            Thread.Sleep(100);
            lock (records) Check(records.Count == 0, "History or partial callback");
            File.AppendAllText(log, "}\ninvalid\n{\"event\":\"Stop\"}\n");
            Check(SpinWait.SpinUntil(() => { lock (records) return records.Count == 2; }, 3000), "Missed typed callbacks");
            Thread.Sleep(100);
            lock (records) Check(records.Select(record => record.RawEvent).SequenceEqual(new[] { "PermissionRequest", "Stop" }), "Replay or callback ordering");
        }
        File.AppendAllText(log, "{\"event\":\"Stop\"}\n");
        Thread.Sleep(100);
        lock (records) Check(records.Count == 2, "Typed callback after disposal");
        lock (errors) Check(errors.Count == 0, "Monitor error");
    }));
Run("Watcher/timer callbacks and disposal use the same serialization boundary", () => WithLog(log =>
{
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    using var disposing = new ManualResetEventSlim();
    var active = 0;
    var overlap = 0;
    var records = new List<HapticsEventRecord>();
    var errors = new List<Exception>();
    var monitor = StopEventMonitor.ForRecords(log, batch =>
    {
        if (Interlocked.Increment(ref active) != 1) Interlocked.Increment(ref overlap);
        try
        {
            lock (records) records.AddRange(batch);
            entered.Set();
            if (!release.Wait(3000)) throw new Exception("Callback release timed out");
        }
        finally { Interlocked.Decrement(ref active); }
    }, error => { lock (errors) errors.Add(error); }, 10, true);
    Task disposal = null;
    try
    {
        File.AppendAllText(log, "{\"event\":\"Stop\",\"turn_id\":\"first\"}\n");
        Check(entered.Wait(3000), "Callback not entered");
        File.AppendAllText(log, "{\"event\":\"Stop\",\"turn_id\":\"second\"}\n");
        Thread.Sleep(100); // Both watcher and reconciliation can contend while the callback holds the gate.
        Check(Volatile.Read(ref overlap) == 0, "Callbacks overlapped");
        disposal = Task.Factory.StartNew(() => { disposing.Set(); monitor.Dispose(); },
            TaskCreationOptions.LongRunning);
        Check(disposing.Wait(3000) && !disposal.Wait(100), "Disposal did not wait for callback");
        release.Set();
        Check(disposal.Wait(3000), "Disposal never completed");
        File.AppendAllText(log, "{\"event\":\"Stop\",\"turn_id\":\"after\"}\n");
        Thread.Sleep(100);
        lock (records)
        {
            // A queued read may win the gate before disposal; either ordered prefix is valid.
            Check(records.Select(record => record.TurnId).SequenceEqual(new[] { "first" }.Concat(
                records.Count == 2 ? new[] { "second" } : Array.Empty<string>())), "Replay, reordered callback or callback after disposal");
        }
        lock (errors) Check(errors.Count == 0, "Concurrent monitor error");
    }
    finally
    {
        release.Set();
        monitor.Dispose();
        disposal?.GetAwaiter().GetResult();
    }
}));
return failures == 0 ? 0 : 1;

void Run(string name, Action test)
{
    try { test(); Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {name}: {error.Message}"); }
}
static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static void WithLog(Action<string> test)
{
    var directory = Path.Combine(Path.GetTempPath(), $"codex-typed-{Guid.NewGuid()}");
    Directory.CreateDirectory(directory);
    try
    {
        var log = Path.Combine(directory, "events.jsonl");
        File.WriteAllText(log, "");
        test(log);
    }
    finally { Directory.Delete(directory, true); }
}
