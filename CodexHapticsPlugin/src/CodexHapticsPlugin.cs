namespace Loupedeck.CodexHapticsPlugin
{
    using System;
    using System.IO;
    using System.Threading;

    // This class contains the plugin-level logic of the Loupedeck plugin.

    public class CodexHapticsPlugin : Plugin
    {
        private static readonly String StatusPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "Logi", "LogiPluginService", "CodexHaptics.status");
        private StopEventReader _reader;
        private Timer _pollTimer;
        private readonly Object _pollLock = new();
        // Gets a value indicating whether this is an API-only plugin.
        public override Boolean UsesApplicationApiOnly => true;

        // Gets a value indicating whether this is a Universal plugin or an Application plugin.
        public override Boolean HasNoApplication => true;

        // Initializes a new instance of the plugin class.
        public CodexHapticsPlugin()
        {
            // Initialize the plugin log.
            PluginLog.Init(this.Log);

            // Initialize the plugin resources.
            PluginResources.Init(this.Assembly);
        }

        // This method is called when the plugin is loaded.
        public override void Load()
        {
            try
            {
                WriteStatus("loading");
                // Logitech can load assemblies from memory, leaving Assembly.Location empty.
                // The development link points to CodexHapticsPlugin/bin/Debug/.
                var linkPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support", "Logi", "LogiPluginService", "Plugins", "CodexHapticsPlugin.link");
                var pluginDir = File.ReadAllText(linkPath).Trim();
                var logPath = Path.GetFullPath(Path.Combine(pluginDir, "..", "..", "..", ".local", "events.jsonl"));
                this._reader = new StopEventReader(logPath);
                this._pollTimer = new Timer(this.Poll, null, 250, 250);
                WriteStatus("watching");
                PluginLog.Info("Codex Stop observer started.");
            }
            catch (Exception error)
            {
                WriteStatus("load_error: " + error.GetType().Name + ": " + error.Message);
                PluginLog.Warning(error, "Could not start Codex Stop observer.");
            }
        }

        // This method is called when the plugin is unloaded.
        public override void Unload()
        {
            this._pollTimer?.Dispose();
            this._pollTimer = null;
        }

        private void Poll(Object state)
        {
            if (!Monitor.TryEnter(this._pollLock)) return;
            try { this.RaiseStops(this._reader.ReadNewStops()); }
            catch (Exception error) { this.ObserverError(error); }
            finally { Monitor.Exit(this._pollLock); }
        }

        private void RaiseStops(Int32 count)
        {
            for (; count > 0; count--)
            {
                this.PluginEvents.RaiseEvent("TestPulse");
                WriteStatus("event_raised");
            }
        }

        private void ObserverError(Exception error)
        {
            WriteStatus("observer_error: " + error.GetType().Name + ": " + error.Message);
            PluginLog.Warning(error, "Could not observe Codex event log.");
        }

        private static void WriteStatus(String status)
        {
            try { File.WriteAllText(StatusPath, $"{DateTimeOffset.UtcNow:O} {status}\n"); }
            catch (Exception) { /* Diagnostics must not affect plugin operation. */ }
        }
    }
}
