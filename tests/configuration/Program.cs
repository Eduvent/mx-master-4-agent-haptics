using System.Text.Json;
using Loupedeck.CodexHapticsPlugin;

var invalid = new (string Json, string Diagnostic)[]
{
    ("", "JSON de configuración inválido."),
    ("{", "JSON de configuración inválido."),
    ("{\"schema_version\":1,}", "JSON de configuración inválido."),
    ("{/* comentario */\"schema_version\":1}", "JSON de configuración inválido."),
    ("null", "Objeto inválido en configuración."),
    ("[]", "Objeto inválido en configuración."),
    ("{}", "schema_version debe ser el entero 1."),
    ("{\"schema_version\":2}", "schema_version debe ser el entero 1."),
    ("{\"schema_version\":0}", "schema_version debe ser el entero 1."),
    ("{\"schema_version\":\"1\"}", "schema_version debe ser el entero 1."),
    ("{\"schema_version\":true}", "schema_version debe ser el entero 1."),
    ("{\"schema_version\":null}", "schema_version debe ser el entero 1."),
    ("{\"schema_version\":1,\"extra\":\"private-value\"}", "Propiedad desconocida o duplicada en configuración."),
    ("{\"schema_version\":1,\"enabled\":1}", "configuración.enabled debe ser booleano."),
    ("{\"schema_version\":1,\"enabled\":null}", "configuración.enabled debe ser booleano."),
    ("{\"schema_version\":1,\"events\":[]}", "Objeto inválido en events."),
    ("{\"schema_version\":1,\"events\":{\"Stop\":{}}}", "Propiedad desconocida o duplicada en events."),
    ("{\"schema_version\":1,\"events\":{\"turn_finished\":false}}", "Objeto inválido en events.turn_finished."),
    ("{\"schema_version\":1,\"events\":{\"turn_finished\":{\"enabled\":\"false\"}}}", "events.turn_finished.enabled debe ser booleano."),
    ("{\"schema_version\":1,\"events\":{\"turn_finished\":{\"profile\":\"double_tap\"}}}", "events.turn_finished.profile solo admite completed."),
    ("{\"schema_version\":1,\"events\":{\"turn_finished\":{\"profile\":\"Completed\"}}}", "events.turn_finished.profile solo admite completed."),
    ("{\"schema_version\":1,\"events\":{\"turn_finished\":{\"profile\":null}}}", "events.turn_finished.profile solo admite completed."),
    ("{\"schema_version\":1,\"events\":{\"turn_finished\":{\"profile\":7}}}", "events.turn_finished.profile solo admite completed."),
    ("{\"schema_version\":1,\"events\":{\"turn_finished\":{\"enabled\":false,\"profile\":\"unsupported\"}}}", "events.turn_finished.profile solo admite completed."),
    ("{\"schema_version\":1,\"events\":{\"turn_finished\":{\"unknown\":false}}}", "Propiedad desconocida o duplicada en events.turn_finished."),
    ("{\"schema_version\":1,\"events\":{\"approval_required\":{\"enabled\":true}}}", "events.approval_required está reservado y debe permanecer desactivado."),
    ("{\"schema_version\":1,\"events\":{\"input_required\":{\"enabled\":true}}}", "events.input_required está reservado y debe permanecer desactivado."),
    ("{\"schema_version\":1,\"events\":{\"approval_required\":{\"enabled\":0}}}", "events.approval_required.enabled debe ser booleano."),
    ("{\"schema_version\":1,\"events\":{\"input_required\":null}}", "Objeto inválido en events.input_required."),
    ("{\"schema_version\":1,\"events\":{\"approval_required\":{\"profile\":\"completed\"}}}", "Propiedad desconocida o duplicada en events.approval_required.")
};
var duplicates = new (string Json, string Diagnostic)[]
{
    ("{\"schema_version\":1,\"schema_version\":1}", "Propiedad desconocida o duplicada en configuración."),
    ("{\"schema_version\":1,\"events\":{\"turn_finished\":{},\"turn_finished\":{}}}", "Propiedad desconocida o duplicada en events."),
    ("{\"schema_version\":1,\"events\":{\"turn_finished\":{\"enabled\":true,\"enabled\":false}}}", "Propiedad desconocida o duplicada en events.turn_finished.")
};
var valid = new[]
{
    "{\"schema_version\":1}",
    "{\"schema_version\":1,\"enabled\":false}",
    "{\"schema_version\":1,\"events\":{}}",
    "{\"schema_version\":1,\"events\":{\"turn_finished\":{}}}",
    "{\"schema_version\":1,\"events\":{\"turn_finished\":{\"enabled\":false,\"profile\":\"completed\"}}}",
    "{\"schema_version\":1,\"events\":{\"approval_required\":{},\"input_required\":{\"enabled\":false}}}"
};
var passed = 0;
Run("defaults preserve Stop -> completed and reserve attention", () => Defaults(HapticsConfiguration.Default));
Run("optional settings use safe defaults", () => Defaults(Parse(valid[0])));
Run("global and event switches remain independent", () =>
{
    var globalOff = Parse(valid[1]);
    Check(!globalOff.Enabled && globalOff.TurnFinished.Enabled, "Global disable changed the event setting");
    var eventOff = Parse(valid[4]);
    Check(eventOff.Enabled && !eventOff.TurnFinished.Enabled && eventOff.TurnFinished.Profile == "completed", "Event disable failed");
});
Run("valid documents and disabled reservations", () =>
{
    foreach (var json in valid) Parse(json);
    var snapshot = Parse(valid[^1]);
    Check(!snapshot.ApprovalRequired.Enabled && !snapshot.InputRequired.Enabled, "Attention enabled");
    Check(snapshot.ApprovalRequired.Profile == null && snapshot.InputRequired.Profile == null, "Attention has a profile");
});
Run("strict parsing and last-valid retention for every invalid case", () =>
{
    WithFile(path =>
    {
        foreach (var item in invalid.Concat(duplicates))
        {
            Check(!HapticsConfiguration.TryParse(item.Json, out var rejected, out var diagnostic), "Accepted: " + item.Json);
            Check(rejected == null && diagnostic == item.Diagnostic, "Wrong parse diagnostic: " + diagnostic);
            File.WriteAllText(path, item.Json);
            var initial = new HapticsConfigurationStore(path);
            Defaults(initial.Snapshot);
            Check(initial.Diagnostic == item.Diagnostic, "Invalid startup lacked diagnostic");
            File.WriteAllText(path, valid[4]);
            var store = new HapticsConfigurationStore(path);
            var before = store.Snapshot;
            File.WriteAllText(path, item.Json);
            Check(!store.Reload(), "Invalid reload succeeded");
            Check(ReferenceEquals(before, store.Snapshot), "Invalid reload replaced snapshot");
            Check(store.Diagnostic == item.Diagnostic, "Invalid reload lacked diagnostic");
            Check(File.ReadAllText(path) == item.Json, "Loader changed the source file");
        }
    });
});
Run("malformed UTF-16 strings and property names are rejected without changing configuration files", () =>
{
    const string expectedDiagnostic = "JSON de configuración inválido.";
    var malformed = new[]
    {
        "{\"schema_version\":1,\"events\":{\"turn_finished\":{\"profile\":\"\\uD800\"}}}",
        "{\"schema_version\":1,\"events\":{\"turn_finished\":{\"profile\":\"\\uDC00\"}}}",
        "{\"schema_version\":1,\"\\uD800\":false}",
        "{\"schema_version\":1,\"events\":{\"\\uD800\":{}}}",
        "{\"schema_version\":1,\"events\":{\"turn_finished\":{\"\\uD800\":false}}}"
    };
    WithFile(path =>
    {
        foreach (var json in malformed)
        {
            Check(!HapticsConfiguration.TryParse(json, out var rejected, out var diagnostic), "Accepted malformed UTF-16");
            Check(rejected == null && diagnostic == expectedDiagnostic, "Malformed UTF-16 diagnostic changed");
            File.WriteAllText(path, json);
            var hash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path));
            var startup = new HapticsConfigurationStore(path);
            Defaults(startup.Snapshot);
            Check(startup.Diagnostic == expectedDiagnostic, "Malformed startup lacked fixed diagnostic");
            Check(hash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))), "Startup changed malformed file");
            File.WriteAllText(path, valid[1]);
            var store = new HapticsConfigurationStore(path);
            var before = store.Snapshot;
            File.WriteAllText(path, json);
            Check(!store.Reload(), "Malformed reload succeeded");
            Check(ReferenceEquals(before, store.Snapshot) && !store.Snapshot.Enabled, "Malformed reload replaced disabled snapshot");
            Check(store.Diagnostic == expectedDiagnostic, "Malformed reload lacked fixed diagnostic");
            Check(hash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))), "Reload changed malformed file");
        }
    });
    Defaults(Parse("{\"schema_version\":1,\"events\":{\"turn_finished\":{\"profile\":\"\\u0063ompleted\"}}}"));
});
Run("missing startup defaults, missing reload retains state, recovery clears diagnostic", () =>
{
    WithFile(path =>
    {
        var store = new HapticsConfigurationStore(path);
        Defaults(store.Snapshot);
        Check(store.Diagnostic == null && !File.Exists(path), "Missing startup should be silent and read-only");
        var before = store.Snapshot;
        Check(!store.Reload() && ReferenceEquals(before, store.Snapshot), "Missing reload changed state");
        Check(store.Diagnostic == "No se encontró la configuración al recargar; se conserva la última válida.", "Missing reload diagnostic");
        File.WriteAllText(path, valid[1]);
        Check(store.Reload() && !store.Snapshot.Enabled && store.Diagnostic == null, "Recovery failed");
        before = store.Snapshot;
        File.Delete(path);
        Check(!store.Reload() && ReferenceEquals(before, store.Snapshot), "Missing reload lost nondefault snapshot");
    });
});
Run("file changes apply only on explicit reload, old snapshots remain immutable", () =>
{
    WithFile(path =>
    {
        File.WriteAllText(path, valid[0]);
        var store = new HapticsConfigurationStore(path);
        var before = store.Snapshot;
        File.WriteAllText(path, valid[4]);
        Check(ReferenceEquals(before, store.Snapshot) && before.TurnFinished.Enabled, "Implicit reload occurred");
        Check(store.Reload() && !store.Snapshot.TurnFinished.Enabled, "Explicit reload failed");
        Defaults(before);
        Defaults(HapticsConfiguration.Default);
        foreach (var type in new[] { typeof(HapticsConfiguration), typeof(HapticsEventConfiguration) })
            Check(type.IsSealed && type.GetProperties().All(p => !p.CanWrite), "Snapshot is mutable");
    });
});
Run("unreadable path is diagnosed without replacing a snapshot", () =>
{
    WithFile(path =>
    {
        File.WriteAllText(path, valid[4]);
        var store = new HapticsConfigurationStore(path);
        var before = store.Snapshot;
        File.Delete(path);
        Directory.CreateDirectory(path);
        Check(!store.Reload() && ReferenceEquals(before, store.Snapshot), "I/O error replaced snapshot");
        Check(store.Diagnostic == "No se pudo leer la configuración; se conserva la última válida.", "I/O diagnostic");
        var initial = new HapticsConfigurationStore(path);
        Defaults(initial.Snapshot);
        Check(initial.Diagnostic == store.Diagnostic, "I/O startup diagnostic");
    });
});
Run("concurrent readers see complete immutable snapshots during serialized reloads", () =>
{
    WithFile(path =>
    {
        File.WriteAllText(path, valid[1]); // global false, event true
        var store = new HapticsConfigurationStore(path);
        Parallel.Invoke(
            () =>
            {
                for (var i = 0; i < 200; i++)
                {
                    File.WriteAllText(path, valid[i % 2 == 0 ? 4 : 1]);
                    Check(store.Reload(), "Valid reload failed under concurrency");
                }
            },
            () => Parallel.For(0, 10000, _ =>
            {
                var snapshot = store.Snapshot;
                Check(snapshot.Enabled != snapshot.TurnFinished.Enabled && snapshot.TurnFinished.Profile == "completed", "Torn snapshot");
            }));
        Parallel.For(0, 50, _ => Check(store.Reload(), "Concurrent reload failed"));
    });
});
Run("public example and schema agree with the parser corpus", () =>
{
    var example = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "haptics.example.json"));
    Defaults(Parse(example));
    using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "haptics.schema.json")));
    foreach (var json in valid.Append(example))
    {
        using var document = JsonDocument.Parse(json);
        Check(Matches(schema.RootElement, document.RootElement, schema.RootElement), "Schema rejected valid example");
    }
    foreach (var item in invalid)
    {
        try
        {
            using var document = JsonDocument.Parse(item.Json);
            Check(!Matches(schema.RootElement, document.RootElement, schema.RootElement), "Schema accepted: " + item.Json);
        }
        catch (JsonException) { /* Invalid JSON is rejected before schema validation. */ }
    }
});
Console.WriteLine($"Configuration tests passed ({passed} groups; {invalid.Length + duplicates.Length} invalid cases)");

void Run(string name, Action test)
{
    var stdout = Console.Out;
    var stderr = Console.Error;
    using var capturedOut = new StringWriter();
    using var capturedError = new StringWriter();
    try
    {
        Console.SetOut(capturedOut);
        Console.SetError(capturedError);
        test();
        Check(capturedOut.ToString() == "" && capturedError.ToString() == "", "Library wrote to stdout/stderr");
    }
    finally { Console.SetOut(stdout); Console.SetError(stderr); }
    passed++;
    Console.WriteLine("PASS " + name);
}
static HapticsConfiguration Parse(string json)
{
    Check(HapticsConfiguration.TryParse(json, out var snapshot, out var diagnostic), "Valid parse failed: " + diagnostic);
    Check(diagnostic == null && snapshot != null, "Successful parse returned diagnostic or no snapshot");
    return snapshot;
}
static void Defaults(HapticsConfiguration snapshot)
{
    Check(snapshot.SchemaVersion == 1 && snapshot.Enabled, "Wrong global defaults");
    Check(snapshot.TurnFinished.Enabled && snapshot.TurnFinished.Profile == "completed", "Stop compatibility lost");
    Check(!snapshot.ApprovalRequired.Enabled && !snapshot.InputRequired.Enabled, "Attention enabled by default");
    Check(snapshot.ApprovalRequired.Profile == null && snapshot.InputRequired.Profile == null, "Reserved profile exposed");
}
static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static void WithFile(Action<string> test)
{
    var directory = Directory.CreateTempSubdirectory("codex-configuration-").FullName;
    try { test(Path.Combine(directory, "haptics.json")); }
    finally { Directory.Delete(directory, true); }
}
// Small evaluator for the keywords used by our public schema; no external package or network access.
static bool Matches(JsonElement schema, JsonElement value, JsonElement root)
{
    if (schema.TryGetProperty("$ref", out var reference))
        return Matches(root.GetProperty("$defs").GetProperty(reference.GetString().Split('/')[^1]), value, root);
    if (schema.TryGetProperty("type", out var type))
    {
        var correct = type.GetString() switch
        {
            "object" => value.ValueKind == JsonValueKind.Object,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
            "string" => value.ValueKind == JsonValueKind.String,
            _ => throw new Exception("Untested schema keyword type")
        };
        if (!correct) return false;
    }
    if (schema.TryGetProperty("const", out var constant) && value.GetRawText() != constant.GetRawText()) return false;
    if (value.ValueKind != JsonValueKind.Object) return true;
    if (schema.TryGetProperty("required", out var required))
        foreach (var name in required.EnumerateArray())
            if (!value.TryGetProperty(name.GetString(), out _)) return false;
    var properties = schema.GetProperty("properties");
    foreach (var property in value.EnumerateObject())
    {
        if (properties.TryGetProperty(property.Name, out var child))
        {
            if (!Matches(child, property.Value, root)) return false;
        }
        else if (schema.GetProperty("additionalProperties").ValueKind == JsonValueKind.False) return false;
    }
    return true;
}
