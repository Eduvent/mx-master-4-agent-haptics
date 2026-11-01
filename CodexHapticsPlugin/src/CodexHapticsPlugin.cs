namespace Loupedeck.CodexHapticsPlugin
{
    public class CodexHapticsPlugin : Plugin
    {
        public override System.Boolean UsesApplicationApiOnly => true;
        public override System.Boolean HasNoApplication => true;

        public CodexHapticsPlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);
        }

        public override void Load() { }
        public override void Unload() { }
    }
}
