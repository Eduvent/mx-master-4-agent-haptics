namespace Loupedeck.CodexHapticsPlugin
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;

    // Only consume complete records appended after the plugin starts.
    // Callers serialize reads; StopEventMonitor owns the read/dispose gate.
    internal sealed class StopEventReader
    {
        private readonly String _path;
        private Int64 _offset;

        public StopEventReader(String path)
        {
            this._path = path;
            this._offset = File.Exists(path) ? new FileInfo(path).Length : 0;
        }

        // Both count APIs consume the same cursor as typed reads, including non-Stop records.
        public Int32 ReadNewStops() => this.ReadPendingStops();
        public Int32 ReadPendingStops() => CountStops(this.ReadPendingRecords());

        internal static Int32 CountStops(IReadOnlyList<HapticsEventRecord> records)
        {
            var count = 0;
            foreach (var record in records)
                if (record.RawEvent == "Stop") count++;
            return count;
        }

        public IReadOnlyList<HapticsEventRecord> ReadPendingRecords()
        {
            var records = new List<HapticsEventRecord>();
            if (!File.Exists(this._path)) return records.AsReadOnly();
            using var stream = new FileStream(this._path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < this._offset) this._offset = 0;
            stream.Position = this._offset;
            using var line = new MemoryStream();
            Int32 value;
            while ((value = stream.ReadByte()) != -1)
            {
                if (value != '\n') { line.WriteByte((Byte)value); continue; }
                this._offset = stream.Position;
                try
                {
                    using var json = JsonDocument.Parse(line.ToArray());
                    var root = json.RootElement;
                    if (root.ValueKind == JsonValueKind.Object && ReadString(root, "event") is String name)
                    {
                        records.Add(new HapticsEventRecord(name, ReadString(root, "timestamp"),
                            ReadString(root, "session_id"), ReadString(root, "turn_id"),
                            ReadString(root, "tool_use_id"), ReadString(root, "tool_name"),
                            ReadString(root, "permission_mode"), ReadBoolean(root, "message_has_question_mark")));
                    }
                }
                catch (JsonException) { /* Ignore malformed records; never block Codex. */ }
                catch (InvalidOperationException) { /* Deferred string/name decoding must not discard neighboring records. */ }
                line.SetLength(0);
            }
            return records.AsReadOnly();
        }

        private static String ReadString(JsonElement root, String key)
        {
            try
            {
                return root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() : null;
            }
            catch (InvalidOperationException)
            {
                // Treat malformed UTF-16 like other invalid optional strings; an invalid event is skipped.
                return null;
            }
        }

        private static Boolean? ReadBoolean(JsonElement root, String key) =>
            root.TryGetProperty(key, out var value) &&
            (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                ? value.GetBoolean() : null;
    }
}
