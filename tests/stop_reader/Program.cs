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
