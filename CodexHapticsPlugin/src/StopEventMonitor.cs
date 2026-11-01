namespace Loupedeck.CodexHapticsPlugin
{
    using System;
    using System.IO;
    using System.Threading;

    // Notifications reduce latency; periodic reconciliation recovers missed notifications.
    internal sealed class StopEventMonitor : IDisposable
    {
        private readonly Object _gate = new();
        private readonly StopEventReader _reader;
        private readonly Action<Int32> _onStops;
        private readonly Action<Exception> _onError;
        private readonly Timer _timer;
        private FileSystemWatcher _watcher;
        private Boolean _disposed;

        public StopEventMonitor(String path, Action<Int32> onStops, Action<Exception> onError,
            Int32 reconciliationMs = 1000, Boolean enableNotifications = true)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            this._reader = new StopEventReader(path);
            this._onStops = onStops;
            this._onError = onError;
            try
            {
                if (enableNotifications)
                {
                    this._watcher = new FileSystemWatcher(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFileName(path))
                    { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
                    this._watcher.Changed += (_, _) => this.Read();
                    this._watcher.Created += (_, _) => this.Read();
                    this._watcher.Renamed += (_, _) => this.Read();
                    this._watcher.Error += (_, e) => { this._onError(e.GetException()); this.Read(); };
                    this._watcher.EnableRaisingEvents = true;
                }
            }
            catch (Exception error)
            {
                this._watcher?.Dispose();
                this._watcher = null;
                this._onError(error); // Timer remains available if notifications cannot start.
            }
            this._timer = new Timer(_ => this.Read(), null, 0, reconciliationMs);
        }

        private void Read()
        {
            lock (this._gate)
            {
                if (this._disposed) return;
                try
                {
                    var count = this._reader.ReadNewStops();
                    if (count > 0) this._onStops(count);
                }
                catch (Exception error) { this._onError(error); }
            }
        }

        public void Dispose()
        {
            lock (this._gate)
            {
                this._disposed = true;
                this._timer.Dispose();
                this._watcher?.Dispose();
            }
        }
    }
}
