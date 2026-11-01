namespace Loupedeck.CodexHapticsPlugin
{
    using System;

    public class TestHapticCommand : PluginDynamicCommand
    {
        public TestHapticCommand() : base("Probar vibración", "Emite una señal háptica de prueba", "Codex") { }

        protected override Boolean OnLoad()
        {
            this.Plugin.PluginEvents.AddEvent("TestPulse", "Prueba háptica", "Vibración de prueba");
            return true;
        }

        protected override void RunCommand(String actionParameter) =>
            this.Plugin.PluginEvents.RaiseEvent("TestPulse");
    }
}
