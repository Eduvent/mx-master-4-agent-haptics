namespace Loupedeck.CodexHapticsPlugin
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    public class CodexHapticsPlugin : Plugin
    {
        private readonly Object _lifecycleGate = new();
        private readonly Object _gate = new();
        private readonly String _statusPath;
        private readonly String _linkPath;
        private StopEventMonitor _monitor;
        private HapticsConfigurationStore _configuration;
        private Boolean _active;

        public override Boolean UsesApplicationApiOnly => true;
        public override Boolean HasNoApplication => true;

        public CodexHapticsPlugin() : this(
            Path.Combine(ServiceDirectory, "Plugins", "CodexHapticsPlugin.link"),
            Path.Combine(ServiceDirectory, "CodexHaptics.status")) { }

        // Path seam for isolated SDK-double tests; production always uses the development link.
        internal CodexHapticsPlugin(String linkPath, String statusPath)
        {
            this._linkPath = linkPath;
            this._statusPath = statusPath;
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);
        }

        private static String ServiceDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "Logi", "LogiPluginService");

        public override void Load()
        {
            lock (this._lifecycleGate)
            lock (this._gate)
            {
                if (this._active) return;
                try
                {
                    this.WriteStatus("loading");
                    // Assembly.Location can be empty when Logitech loads assemblies from memory.
                    var pluginDir = File.ReadAllText(this._linkPath).Trim();
                    var logPath = Path.GetFullPath(Path.Combine(pluginDir, "..", "..", "..", ".local", "events.jsonl"));
                    this._configuration = new HapticsConfigurationStore(
                        Path.Combine(Path.GetDirectoryName(logPath), "haptics.json"));
                    this.ReportConfigurationDiagnostic();
                    this._active = true;
                    this._monitor = StopEventMonitor.ForRecords(logPath, this.RaiseRecords, this.ObserverError);
                    this.WriteStatus("watching_notifications_with_1000ms_reconciliation");
                    PluginLog.Info("Codex typed event observer started.");
                }
                catch (Exception)
                {
                    this._active = false;
                    this.WriteStatus("load_error");
                    PluginLog.Warning("Could not start Codex event observer.");
                }
            }
        }

        public override void Unload()
        {
            lock (this._lifecycleGate)
            {
                StopEventMonitor monitor;
                lock (this._gate)
                {
                    this._active = false;
                    monitor = this._monitor;
                    this._monitor = null;
                    this.WriteStatus("unloaded");
                }
                // Dispose waits for the monitor callback, which may be waiting for _gate.
                // Never hold _gate while joining that callback.
                monitor?.Dispose();
            }
        }

        public Boolean ReloadHapticsConfiguration()
        {
            lock (this._gate)
            {
                if (!this._active) return false;
                var applied = this._configuration.Reload();
                this.ReportConfigurationDiagnostic();
                this.WriteStatus(applied ? "configuration_reloaded" : "configuration_reload_failed");
                return applied;
            }
        }

        private void RaiseRecords(IReadOnlyList<HapticsEventRecord> records)
        {
            lock (this._gate)
            {
                if (!this._active) return;
                var snapshot = this._configuration.Snapshot;
                var raised = false;
                foreach (var record in records)
                {
                    var backendEvent = HapticsEventRouter.BackendEvent(snapshot, record);
                    if (backendEvent == null) continue;
                    this.PluginEvents.RaiseEvent(backendEvent);
                    raised = true;
                }
                this.WriteStatus(raised ? "event_raised" : "events_processed_without_output");
            }
        }

        private void ObserverError(Exception error)
        {
            lock (this._gate)
            {
                if (!this._active) return;
                this.WriteStatus("observer_error");
                PluginLog.Warning("Could not observe Codex event log.");
            }
        }

        private void ReportConfigurationDiagnostic()
        {
            if (this._configuration.Diagnostic is String diagnostic)
                PluginLog.Warning("configuration_error: " + diagnostic);
        }

        // Fixed diagnostics never include JSON, event IDs, paths or exception messages.
        // Keep a configuration failure visible even after startup or event status updates.
        private void WriteStatus(String status)
        {
            var diagnostic = this._configuration?.Diagnostic;
            if (diagnostic != null) status += " configuration_error: " + diagnostic;
            try { File.WriteAllText(this._statusPath, $"{DateTimeOffset.UtcNow:O} {status}\n"); }
            catch (Exception) { /* Diagnostics must not affect plugin operation. */ }
        }
    }
}
