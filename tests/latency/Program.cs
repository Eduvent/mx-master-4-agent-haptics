using System.Diagnostics;
using System.Text.Json;
using Loupedeck.CodexHapticsPlugin;

// Isolated file: never sends events to Logitech or modifies the live log.
if (args.SequenceEqual(new[] { "--help" }))
{
    Console.WriteLine("Usage: Latency [positive-samples=60] [poll250 poll25 watcher hook250 hookwatcher hook_python_static hook_go_static hook_go_configured ...]");
    Console.WriteLine("Latency --self-test: deterministic isolated configuration-routing checks; no SDK calls.");
    Console.WriteLine("hook_go_configured: Go shell spawn to TestPulse selection, configuration parsed once at startup.");
    return;
}
if (args.SequenceEqual(new[] { "--self-test" }))
{
    RunConfiguredSelfTest();
    return;
}
var samples = args.Length > 0 ? int.Parse(args[0]) : 60;
if (samples < 1) throw new ArgumentException("Sample count must be positive");
var modes = args.Length > 1 ? args.Skip(1).ToArray() : new[] { "poll250", "poll25", "watcher" };
if (modes.Any(mode => !new[] { "poll250", "poll25", "watcher", "hook250", "hookwatcher", "hook_python_static", "hook_go_static", "hook_go_configured" }.Contains(mode)))
    throw new ArgumentException("Unknown benchmark mode");
var results = new List<object>();
foreach (var mode in modes)
{
    var directory = Path.Combine(Path.GetTempPath(), "haptics-bench-" + Guid.NewGuid());
    Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, "events.jsonl");
    File.WriteAllText(path, "");
    var reader = new StopEventReader(path);
    var gate = new object();
    TaskCompletionSource<long> pending = null;
    long callbacks = 0;
    void Read()
    {
        lock (gate)
        {
            callbacks++;
            if (reader.ReadNewStops() > 0) pending?.TrySetResult(Stopwatch.GetTimestamp());
        }
    }
    using var watcher = new FileSystemWatcher(directory, "events.jsonl") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
    watcher.Changed += (_, _) => Read();
    watcher.Created += (_, _) => Read();
    watcher.Renamed += (_, _) => Read();
    var fullHook = mode.StartsWith("hook");
    watcher.EnableRaisingEvents = mode == "watcher";
    HapticsConfigurationStore store = null;
    if (mode == "hook_go_configured")
    {
        var configPath = Path.Combine(directory, "haptics.json");
        File.WriteAllText(configPath, "{\"schema_version\":1,\"enabled\":true,\"events\":{\"turn_finished\":{\"enabled\":true,\"profile\":\"completed\"}}}");
        store = new HapticsConfigurationStore(configPath);
        if (store.Diagnostic != null) throw new Exception(store.Diagnostic);
    }
    void Complete() { lock (gate) pending?.TrySetResult(Stopwatch.GetTimestamp()); }
    using var monitor = mode == "hook_go_configured"
        ? StopEventMonitor.ForRecords(path,
            records => DispatchConfigured(store, records, _ => Complete()),
            error => Console.Error.WriteLine(error))
        : fullHook ? new StopEventMonitor(path,
            _ => Complete(), error => Console.Error.WriteLine(error),
            mode == "hook250" ? 250 : 1000, mode != "hook250") : null;
    using var timer = new Timer(_ => Read(), null, (mode == "watcher" || fullHook) ? Timeout.Infinite : mode == "poll25" ? 25 : 250,
        (mode == "watcher" || fullHook) ? Timeout.Infinite : mode == "poll25" ? 25 : 250);
    await Task.Delay(200);
    var process = Process.GetCurrentProcess();
    var cpuStart = process.TotalProcessorTime;
    var allocStart = GC.GetTotalAllocatedBytes();
    var callsStart = Interlocked.Read(ref callbacks);
    var idle = Stopwatch.StartNew();
    await Task.Delay(8000);
    var idleSeconds = idle.Elapsed.TotalSeconds;
    var idleCpu = (process.TotalProcessorTime - cpuStart).TotalMilliseconds;
    var idleAlloc = GC.GetTotalAllocatedBytes() - allocStart;
    var idleCalls = Interlocked.Read(ref callbacks) - callsStart;
    var latencies = new List<double>();
    var random = new Random(42);
    for (var i = 0; i < samples; i++)
    {
        await Task.Delay(random.Next(17, 171));
        var completion = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate) pending = completion;
        var start = Stopwatch.GetTimestamp();
        Process hook = null;
        if (fullHook)
        {
            var info = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true };
            info.ArgumentList.Add("-c");
            var command = mode switch
            {
                "hook_python_static" => "/usr/bin/python3 " + ShellQuote(Path.GetFullPath("hooks/record_event.py")),
                "hook_go_static" or "hook_go_configured" => ShellQuote(Path.GetFullPath(".local/bin/record-event")),
                _ => "/usr/bin/python3 \"$(git rev-parse --show-toplevel)/hooks/record_event.py\""
            };
            info.ArgumentList.Add(command);
            info.Environment["AGENT_HAPTICS_LOG"] = path;
            hook = Process.Start(info);
            hook.StandardInput.WriteLine("{\"hook_event_name\":\"Stop\"}");
            hook.StandardInput.Close();
        }
        else File.AppendAllText(path, "{\"event\":\"Stop\"}\n");
        var end = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (hook != null)
        {
            await hook.WaitForExitAsync();
            if (hook.ExitCode != 0) throw new Exception("Hook failed");
            hook.Dispose();
        }
        latencies.Add(Stopwatch.GetElapsedTime(start, end).TotalMilliseconds);
    }
    watcher.EnableRaisingEvents = false;
    await timer.DisposeAsync();
    latencies.Sort();
    results.Add(new { mode, samples, median_ms = latencies[(samples - 1) / 2], p95_ms = latencies[(int)Math.Ceiling(samples * .95) - 1], max_ms = latencies[^1], idle_seconds = idleSeconds, idle_cpu_ms = idleCpu, idle_alloc_bytes = idleAlloc, idle_callbacks = fullHook ? (long?)null : idleCalls, rss_bytes = process.WorkingSet64 });
    monitor?.Dispose();
    Directory.Delete(directory, true);
    Console.Error.WriteLine($"Completed {mode}");
}
Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));

static string ShellQuote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";

static void DispatchConfigured(HapticsConfigurationStore store,
    IReadOnlyList<HapticsEventRecord> records, Action<string> dispatch)
{
    var snapshot = store.Snapshot;
    foreach (var record in records)
    {
        var backendEvent = HapticsEventRouter.BackendEvent(snapshot, record);
        if (backendEvent != null) dispatch(backendEvent);
    }
}

static void RunConfiguredSelfTest()
{
    var directory = Path.Combine(Path.GetTempPath(), "haptics-bench-test-" + Guid.NewGuid());
    Directory.CreateDirectory(directory);
    try
    {
        var path = Path.Combine(directory, "events.jsonl");
        var configPath = Path.Combine(directory, "haptics.json");
        foreach (var (json, expected) in new[]
        {
            ("{\"schema_version\":1,\"events\":{\"turn_finished\":{\"profile\":\"completed\"}}}", 1),
            ("{\"schema_version\":1,\"enabled\":false}", 0),
            ("{\"schema_version\":1,\"events\":{\"turn_finished\":{\"enabled\":false}}}", 0)
        })
        {
            File.WriteAllText(configPath, json);
            var store = new HapticsConfigurationStore(configPath);
            if (store.Diagnostic != null) throw new Exception(store.Diagnostic);
            var snapshot = store.Snapshot;
            File.WriteAllText(configPath, "invalid after startup");
            File.WriteAllText(path, "{\"event\":\"Stop\"}\n"); // History must not dispatch.
            var reader = new StopEventReader(path);
            File.AppendAllText(path, "{\"event\":\"PermissionRequest\"}\n{\"event\":\"Stop\",\"session_id\":\"test\"}\n");
            var outputs = new List<string>();
            DispatchConfigured(store, reader.ReadPendingRecords(), outputs.Add);
            DispatchConfigured(store, reader.ReadPendingRecords(), outputs.Add); // No replay.
            if (outputs.Count != expected || outputs.Any(value => value != "TestPulse") ||
                !ReferenceEquals(snapshot, store.Snapshot) || store.Diagnostic != null)
                throw new Exception("Configured dispatch must use the startup snapshot and honor gates/history.");
        }
        File.WriteAllText(path, "");
        var countReader = new StopEventReader(path);
        File.AppendAllText(path, "{\"event\":\"Stop\"}\n{\"event\":\"PermissionRequest\"}\n");
        if (countReader.ReadNewStops() != 1 || countReader.ReadNewStops() != 0)
            throw new Exception("Legacy count mode must still consume Stop once.");
        Console.WriteLine("PASS configured dispatch: parsed startup snapshot, TestPulse, both gates, unrelated event, history, no replay");
        Console.WriteLine("PASS preserved legacy count consumption");
    }
    finally { Directory.Delete(directory, true); }
}
