namespace Loupedeck.CodexHapticsPlugin
{
    using System;
    using System.IO;
    using System.Text;
    using System.Text.Json;

    // Only consume complete records appended after the plugin starts.
    internal sealed class StopEventReader
    {
        private readonly String _path;
        private Int64 _offset;

        public StopEventReader(String path)
        {
            this._path = path;
            this._offset = File.Exists(path) ? new FileInfo(path).Length : 0;
        }

        public Int32 ReadNewStops()
        {
            if (!File.Exists(this._path)) return 0;
            using var stream = new FileStream(this._path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < this._offset) this._offset = 0;
            stream.Position = this._offset;
            using var line = new MemoryStream();
            var count = 0;
            Int32 value;
            while ((value = stream.ReadByte()) != -1)
            {
                if (value != '\n') { line.WriteByte((Byte)value); continue; }
                this._offset = stream.Position;
                try
                {
                    using var json = JsonDocument.Parse(line.ToArray());
                    if (json.RootElement.ValueKind == JsonValueKind.Object &&
                        json.RootElement.TryGetProperty("event", out var name) &&
                        name.ValueKind == JsonValueKind.String && name.GetString() == "Stop") count++;
                }
                catch (JsonException) { /* Ignore malformed records; never block Codex. */ }
                line.SetLength(0);
            }
            return count;
        }
    }
}
