namespace Loupedeck.CodexHapticsPlugin
{
    using System;

    public class ReloadHapticsConfigurationCommand : PluginDynamicCommand
    {
        public ReloadHapticsConfigurationCommand()
            : base("Recargar configuración háptica", "Aplica .local/haptics.json sin reiniciar ni repetir eventos; conserva la última válida si falla", "Codex") { }

        protected override void RunCommand(String actionParameter) =>
            (this.Plugin as CodexHapticsPlugin)?.ReloadHapticsConfiguration();
    }
}
