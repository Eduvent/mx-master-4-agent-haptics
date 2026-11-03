namespace Loupedeck.CodexHapticsPlugin
{
    using System;
    using System.IO;
    using System.Threading;

    // File I/O happens only at construction and explicit Reload, never in Snapshot.
    public sealed class HapticsConfigurationStore
    {
        private readonly String _path;
        private readonly Object _reloadLock = new Object();
        private State _state = new State(HapticsConfiguration.Default, null);

        public HapticsConfiguration Snapshot => Volatile.Read(ref this._state).Configuration;
        public String Diagnostic => Volatile.Read(ref this._state).Diagnostic;

        public HapticsConfigurationStore(String path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("Se requiere una ruta de configuración.", nameof(path));
            this._path = Path.GetFullPath(path);
            this.Load(true);
        }

        // True only when a complete valid document was read and published.
        public Boolean Reload() => this.Load(false);

        private Boolean Load(Boolean startup)
        {
            lock (this._reloadLock)
            {
                String diagnostic;
                try
                {
                    if (HapticsConfiguration.TryParse(File.ReadAllText(this._path), out var configuration, out diagnostic))
                    {
                        Volatile.Write(ref this._state, new State(configuration, null));
                        return true;
                    }
                }
                catch (FileNotFoundException)
                {
                    diagnostic = MissingDiagnostic(startup);
                }
                catch (DirectoryNotFoundException)
                {
                    diagnostic = MissingDiagnostic(startup);
                }
                catch (IOException)
                {
                    diagnostic = "No se pudo leer la configuración; se conserva la última válida.";
                }
                catch (UnauthorizedAccessException)
                {
                    diagnostic = "No se pudo leer la configuración; se conserva la última válida.";
                }
                // Publishing a diagnostic never replaces the last valid configuration reference.
                Volatile.Write(ref this._state, new State(this._state.Configuration, diagnostic));
                return false;
            }
        }

        private static String MissingDiagnostic(Boolean startup) => startup ? null :
            "No se encontró la configuración al recargar; se conserva la última válida.";

        private sealed class State
        {
            public HapticsConfiguration Configuration { get; }
            public String Diagnostic { get; }

            public State(HapticsConfiguration configuration, String diagnostic)
            {
                this.Configuration = configuration;
                this.Diagnostic = diagnostic;
            }
        }
    }
}
