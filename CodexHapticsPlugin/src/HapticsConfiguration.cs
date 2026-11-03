namespace Loupedeck.CodexHapticsPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;

    // Immutable, SDK-independent settings. Only validated documents create snapshots.
    public sealed class HapticsConfiguration
    {
        public static HapticsConfiguration Default { get; } = new HapticsConfiguration(
            true, new HapticsEventConfiguration(true, "completed"),
            new HapticsEventConfiguration(false, null), new HapticsEventConfiguration(false, null));

        public Int32 SchemaVersion => 1;
        public Boolean Enabled { get; }
        public HapticsEventConfiguration TurnFinished { get; }
        public HapticsEventConfiguration ApprovalRequired { get; }
        public HapticsEventConfiguration InputRequired { get; }

        private HapticsConfiguration(Boolean enabled, HapticsEventConfiguration turnFinished,
            HapticsEventConfiguration approvalRequired, HapticsEventConfiguration inputRequired)
        {
            this.Enabled = enabled;
            this.TurnFinished = turnFinished;
            this.ApprovalRequired = approvalRequired;
            this.InputRequired = inputRequired;
        }

        // Diagnostics contain fixed schema paths, never file contents or unknown user values.
        public static Boolean TryParse(String json, out HapticsConfiguration configuration, out String diagnostic)
        {
            configuration = null;
            diagnostic = null;
            if (json == null)
            {
                diagnostic = "JSON de configuración inválido.";
                return false;
            }
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                ValidateObject(root, "configuración", "schema_version", "enabled", "events");
                if (!root.TryGetProperty("schema_version", out var version) ||
                    version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1)
                    throw new FormatException("schema_version debe ser el entero 1.");

                var enabled = ReadEnabled(root, "configuración", true);
                var turnFinished = Default.TurnFinished;
                var approvalRequired = Default.ApprovalRequired;
                var inputRequired = Default.InputRequired;
                if (root.TryGetProperty("events", out var events))
                {
                    ValidateObject(events, "events", "turn_finished", "approval_required", "input_required");
                    if (events.TryGetProperty("turn_finished", out var turn))
                    {
                        const String path = "events.turn_finished";
                        ValidateObject(turn, path, "enabled", "profile");
                        var turnEnabled = ReadEnabled(turn, path, true);
                        if (turn.TryGetProperty("profile", out var profile) &&
                            (profile.ValueKind != JsonValueKind.String || profile.GetString() != "completed"))
                            throw new FormatException(path + ".profile solo admite completed.");
                        turnFinished = new HapticsEventConfiguration(turnEnabled, "completed");
                    }
                    approvalRequired = ReadReserved(events, "approval_required", approvalRequired);
                    inputRequired = ReadReserved(events, "input_required", inputRequired);
                }
                configuration = new HapticsConfiguration(enabled, turnFinished, approvalRequired, inputRequired);
                return true;
            }
            catch (JsonException)
            {
                diagnostic = "JSON de configuración inválido.";
                return false;
            }
            catch (InvalidOperationException)
            {
                // JsonDocument defers malformed UTF-16 string/name decoding until access.
                diagnostic = "JSON de configuración inválido.";
                return false;
            }
            catch (FormatException error)
            {
                diagnostic = error.Message;
                return false;
            }
        }

        private static HapticsEventConfiguration ReadReserved(JsonElement events, String name,
            HapticsEventConfiguration fallback)
        {
            if (!events.TryGetProperty(name, out var value)) return fallback;
            var path = "events." + name;
            ValidateObject(value, path, "enabled");
            if (ReadEnabled(value, path, false))
                throw new FormatException(path + " está reservado y debe permanecer desactivado.");
            return fallback;
        }

        private static Boolean ReadEnabled(JsonElement value, String path, Boolean fallback)
        {
            if (!value.TryGetProperty("enabled", out var enabled)) return fallback;
            if (enabled.ValueKind != JsonValueKind.True && enabled.ValueKind != JsonValueKind.False)
                throw new FormatException(path + ".enabled debe ser booleano.");
            return enabled.GetBoolean();
        }

        private static void ValidateObject(JsonElement value, String path, params String[] allowed)
        {
            if (value.ValueKind != JsonValueKind.Object)
                throw new FormatException("Objeto inválido en " + path + ".");
            var seen = new HashSet<String>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
                if (Array.IndexOf(allowed, property.Name) < 0 || !seen.Add(property.Name))
                    throw new FormatException("Propiedad desconocida o duplicada en " + path + ".");
        }
    }

    public sealed class HapticsEventConfiguration
    {
        public Boolean Enabled { get; }
        // Null for reserved events: no verified output is assigned to them.
        public String Profile { get; }

        internal HapticsEventConfiguration(Boolean enabled, String profile)
        {
            this.Enabled = enabled;
            this.Profile = profile;
        }
    }
}
