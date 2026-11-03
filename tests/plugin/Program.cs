using Loupedeck.CodexHapticsPlugin;

var failures = 0;
Run("missing configuration preserves Stop defaults and manual command", () =>
{
    using var f = new Fixture(null);
    f.Batch("{\"event\":\"Stop\"}\n", 1);
    f.Manual.Execute();
    Check(f.Plugin.PluginEvents.Raised.SequenceEqual(new[] { "TestPulse", "TestPulse" }), "Default/manual catalog changed");
});
foreach (var config in new[] { "{\"schema_version\":1,\"enabled\":false}",
    "{\"schema_version\":1,\"events\":{\"turn_finished\":{\"enabled\":false}}}" })
    Run("automatic gate does not disable manual test: " + config, () =>
    {
        using var f = new Fixture(config);
        f.Batch("{\"event\":\"Stop\"}\n", 0);
        f.Manual.Execute();
        Check(f.Plugin.PluginEvents.Raised.SequenceEqual(new[] { "TestPulse" }), "Manual test was gated");
    });
Run("reload action applies changes without replay, restart or per-event reads", () =>
{
    using var f = new Fixture("{\"schema_version\":1,\"enabled\":false}");
    f.Batch("{\"event\":\"Stop\"}\n", 0);
    File.WriteAllText(f.Config, "{\"schema_version\":1}");
    f.Batch("{\"event\":\"Stop\"}\n", 0); // Editing alone must not apply.
    File.AppendAllText(f.Log, "{\"event\":\"Stop\""); // Reload must preserve a pending partial record too.
    f.Reload.Execute();
    Check(f.Status.Contains("configuration_reloaded"), "Reload action did not apply");
    f.Batch("}\n", 1);
    f.Reload.Execute();
    Thread.Sleep(100);
    Check(f.Plugin.PluginEvents.Raised.Length == 1, "Reload replayed history");
    File.WriteAllText(f.Config, "{\"schema_version\":1,\"enabled\":false}");
    f.Reload.Execute();
    f.Batch("{\"event\":\"Stop\"}\n", 0);
});
Run("failed reload retains disabled snapshot and emits private-safe diagnostics", () =>
{
    using var f = new Fixture("{\"schema_version\":1,\"enabled\":false}");
    foreach (var bad in new[] { "{PRIVATE_JSON", "{\"schema_version\":2}",
        "{\"schema_version\":1,\"events\":{\"turn_finished\":{\"profile\":\"PRIVATE_JSON\"}}}" })
    {
        File.WriteAllText(f.Config, bad);
        f.Reload.Execute();
        Check(f.Status.Contains("configuration_error:"), "Missing reload diagnostic");
        f.Batch("{\"event\":\"Stop\"}\n", 0);
    }
    File.Delete(f.Config);
    f.Reload.Execute();
    Check(f.Status.Contains("No se encontró la configuración al recargar; se conserva la última válida."), "Missing file diagnostic changed");
    f.Batch("{\"event\":\"Stop\"}\n", 0);
    Check(!File.Exists(f.Config), "Reload created a missing configuration file");
    Check(!f.Status.Contains("PRIVATE_JSON") && !string.Join("", f.Plugin.Log.Messages).Contains("PRIVATE_JSON"), "Private JSON leaked");
    File.WriteAllText(f.Config, "{\"schema_version\":1}");
    f.Reload.Execute();
    Check(!f.Status.Contains("configuration_error:"), "Valid reload did not clear diagnostic");
    f.Batch("{\"event\":\"Stop\"}\n", 1);
    File.WriteAllText(f.Config, "{PRIVATE_JSON");
    f.Reload.Execute();
    f.Batch("{\"event\":\"Stop\"}\n", 1); // Retain an enabled snapshot too.
});
Run("invalid startup remains diagnosable while safe defaults operate", () =>
{
    using var f = new Fixture("{PRIVATE_JSON");
    const string diagnostic = "configuration_error: JSON de configuración inválido.";
    Check(f.Status.Contains(diagnostic), "Startup overwrote config diagnostic");
    f.Batch("{\"event\":\"Stop\"}\n", 1);
    Check(f.Status.Contains(diagnostic), "Dispatch overwrote config diagnostic");
    Check(!f.Status.Contains("PRIVATE_JSON"), "Startup leaked JSON");
});
Run("malformed UTF-16 startup uses defaults with configuration_error rather than load_error", () =>
{
    const string json = "{\"schema_version\":1,\"events\":{\"turn_finished\":{\"profile\":\"\\uD800\"}}}";
    const string diagnostic = "configuration_error: JSON de configuración inválido.";
    using var f = new Fixture(json);
    var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json));
    Check(f.Status.Contains(diagnostic) && !f.Status.Contains("load_error"), "Malformed startup did not report configuration_error");
    Check(f.Plugin.Log.Messages.Contains(diagnostic), "Startup log lacked fixed diagnostic");
    f.Batch("{\"event\":\"Stop\"}\n", 1);
    Check(f.Status.Contains(diagnostic), "Dispatch lost malformed startup diagnostic");
    Check(hash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f.Config))), "Startup changed input file");
    Check(!f.Status.Contains("D800") && !string.Join("", f.Plugin.Log.Messages).Contains("D800"), "Malformed input leaked");
});
Run("malformed UTF-16 reload returns false and retains disabled snapshot and file hash", () =>
{
    using var f = new Fixture("{\"schema_version\":1,\"enabled\":false}");
    const string diagnostic = "configuration_error: JSON de configuración inválido.";
    File.WriteAllText(f.Config, "{\"schema_version\":1,\"events\":{\"turn_finished\":{\"profile\":\"\\uD800\"}}}");
    var hash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f.Config));
    Check(!f.Plugin.ReloadHapticsConfiguration(), "Malformed reload returned true");
    Check(f.Status.Contains("configuration_reload_failed " + diagnostic), "Reload lacked fixed diagnostic");
    Check(f.Plugin.Log.Messages.Contains(diagnostic), "Reload log lacked fixed diagnostic");
    f.Batch("{\"event\":\"Stop\"}\n", 0);
    Check(f.Status.Contains(diagnostic), "Dispatch lost malformed reload diagnostic");
    Check(hash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f.Config))), "Reload changed input file");
    Check(!f.Status.Contains("D800") && !string.Join("", f.Plugin.Log.Messages).Contains("D800"), "Malformed input leaked");
});
Run("typed records ignore attention and preserve ordered distinct completions", () =>
{
    using var f = new Fixture(null);
    f.Batch("{\"event\":\"PermissionRequest\"}\n{\"event\":\"Interrupt\"}\n{\"event\":\"input_required\"}\n{\"event\":\"approval_required\"}\n", 0);
    var stop = "{\"event\":\"Stop\",\"session_id\":\"a\",\"turn_id\":\"t\"}\n";
    f.Batch(stop + stop + "{\"event\":\"Stop\",\"session_id\":\"b\",\"turn_id\":\"u\"}\n", 3);
    Check(f.Plugin.PluginEvents.Raised.All(name => name == "TestPulse"), "Unverified catalog output");
    Check(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "events", "extra", "eventMapping.yaml")).Contains("DEFAULT: completed"), "Backend mapping missing");
    Check(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "events", "DefaultEventSource.yaml")).Contains("name: TestPulse"), "Event resource registration missing");
});
Run("reload waits for the current batch and applies atomically to later records", () =>
{
    using var f = new Fixture(null);
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    using var reloading = new ManualResetEventSlim();
    f.Plugin.PluginEvents.BeforeRaise = () => { entered.Set(); Check(release.Wait(5000), "Dispatch release timed out"); };
    File.AppendAllText(f.Log, "{\"event\":\"Stop\"}\n{\"event\":\"Stop\"}\n");
    Check(entered.Wait(3500), "Dispatch never entered");
    File.WriteAllText(f.Config, "{\"schema_version\":1,\"enabled\":false}");
    var reload = Task.Factory.StartNew(() => { reloading.Set(); f.Reload.Execute(); }, TaskCreationOptions.LongRunning);
    try { Check(reloading.Wait(3500) && !reload.Wait(100), "Reload overlapped active dispatch"); }
    finally { release.Set(); }
    Check(reload.Wait(3500), "Reload deadlocked");
    Check(f.Plugin.PluginEvents.Raised.Length == 2, "Reload split or replayed a batch");
    f.Batch("{\"event\":\"Stop\"}\n", 0);
});
Run("unload waits for active dispatch without deadlock and prevents later output", () =>
{
    using var f = new Fixture(null);
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    using var unloading = new ManualResetEventSlim();
    f.Plugin.PluginEvents.BeforeRaise = () => { entered.Set(); Check(release.Wait(5000), "Dispatch release timed out"); };
    File.AppendAllText(f.Log, "{\"event\":\"Stop\"}\n{\"event\":\"Stop\"}\n");
    Check(entered.Wait(3500), "Dispatch never entered");
    Task unload = null;
    Task reload = null;
    try
    {
        unload = Task.Factory.StartNew(() => { unloading.Set(); f.Plugin.Unload(); }, TaskCreationOptions.LongRunning);
        Check(unloading.Wait(3500) && !unload.Wait(100), "Unload did not await dispatch");
        reload = Task.Factory.StartNew(() => f.Reload.Execute(), TaskCreationOptions.LongRunning);
    }
    finally { release.Set(); }
    Check(unload.Wait(3500) && reload.Wait(3500), "Lifecycle/reload/callback deadlock");
    var before = f.Plugin.PluginEvents.Raised.Length;
    File.AppendAllText(f.Log, "{\"event\":\"Stop\"}\n");
    f.Reload.Execute();
    Thread.Sleep(1100);
    Check(f.Plugin.PluginEvents.Raised.Length == before, "Output after unload");
    Check(!f.Plugin.ReloadHapticsConfiguration(), "Reload after unload accepted");
});
Run("repeated load is idempotent and unload/load does not replay history", () =>
{
    using var f = new Fixture(null);
    f.Plugin.Load();
    f.Batch("{\"event\":\"Stop\"}\n", 1);
    f.Plugin.Unload();
    File.AppendAllText(f.Log, "{\"event\":\"Stop\"}\n");
    f.Plugin.Load();
    f.Batch("{\"event\":\"Stop\"}\n", 1);
});
return failures == 0 ? 0 : 1;

void Run(string name, Action test)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + error.Message); }
}
static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

sealed class Fixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "codex-plugin-" + Guid.NewGuid());
    private readonly string _status;
    public string Config { get; }
    public string Log { get; }
    public string Status => File.Exists(_status) ? File.ReadAllText(_status) : "";
    public CodexHapticsPlugin Plugin { get; }
    public TestHapticCommand Manual { get; } = new();
    public ReloadHapticsConfigurationCommand Reload { get; } = new();
    public Fixture(string config)
    {
        Directory.CreateDirectory(Path.Combine(_root, ".local"));
        var build = Path.Combine(_root, "CodexHapticsPlugin", "bin", "Debug");
        Directory.CreateDirectory(build);
        var link = Path.Combine(_root, "CodexHapticsPlugin.link");
        File.WriteAllText(link, build + Path.DirectorySeparatorChar + "\n");
        Log = Path.Combine(_root, ".local", "events.jsonl");
        Config = Path.Combine(_root, ".local", "haptics.json");
        _status = Path.Combine(_root, "CodexHaptics.status");
        File.WriteAllText(Log, "{\"event\":\"Stop\"}\n"); // History must never replay.
        if (config != null) File.WriteAllText(Config, config);
        Plugin = new CodexHapticsPlugin(link, _status);
        Manual.Attach(Plugin);
        Reload.Attach(Plugin);
        Plugin.Load();
    }
    public void Batch(string rows, int expected)
    {
        var before = Plugin.PluginEvents.Raised.Length;
        var status = Status;
        File.AppendAllText(Log, rows);
        if (!SpinWait.SpinUntil(() =>
        {
            var current = Status;
            return current != status && current.EndsWith("\n") &&
                (current.Contains("event_raised") || current.Contains("events_processed_without_output"));
        }, 3500)) throw new Exception("No batch acknowledgement");
        // Status is written after all output from a batch, under the plugin gate.
        if (Plugin.PluginEvents.Raised.Length != before + expected) throw new Exception("Unexpected automatic output count");
    }
    public void Dispose()
    {
        Plugin.Unload();
        Directory.Delete(_root, true);
    }
}
