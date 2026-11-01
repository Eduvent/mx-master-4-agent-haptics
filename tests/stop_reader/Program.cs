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
