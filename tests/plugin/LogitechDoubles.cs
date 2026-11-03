// SDK doubles only: no Logitech assembly, service calls or physical outputs.
namespace Loupedeck
{
    using System.Reflection;
    using System.Collections.Concurrent;

    public abstract class Plugin
    {
        public virtual bool UsesApplicationApiOnly => false;
        public virtual bool HasNoApplication => false;
        public Assembly Assembly => GetType().Assembly;
        public PluginLogFile Log { get; } = new();
        public EventDouble PluginEvents { get; } = new();
        public abstract void Load();
        public abstract void Unload();
    }

    public sealed class EventDouble
    {
        private readonly ConcurrentQueue<string> _raised = new();
        public string[] Raised => _raised.ToArray();
        public Action BeforeRaise { get; set; }
        public void AddEvent(string name, string displayName, string description) { }
        public void RaiseEvent(string name)
        {
            BeforeRaise?.Invoke();
            _raised.Enqueue(name);
        }
    }

    public abstract class PluginDynamicCommand
    {
        public Plugin Plugin { get; private set; }
        public string DisplayName { get; }
        public string Description { get; }
        protected PluginDynamicCommand(string displayName, string description, string groupName)
        {
            DisplayName = displayName;
            Description = description;
        }
        public void Attach(Plugin plugin) { Plugin = plugin; OnLoad(); }
        public void Execute() => RunCommand("");
        protected virtual bool OnLoad() => true;
        protected abstract void RunCommand(string actionParameter);
    }

    public sealed class PluginLogFile
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public void Verbose(string text) => Messages.Enqueue(text);
        public void Verbose(Exception error, string text) => Messages.Enqueue(text + error);
        public void Info(string text) => Messages.Enqueue(text);
        public void Info(Exception error, string text) => Messages.Enqueue(text + error);
        public void Warning(string text) => Messages.Enqueue(text);
        public void Warning(Exception error, string text) => Messages.Enqueue(text + error);
        public void Error(string text) => Messages.Enqueue(text);
        public void Error(Exception error, string text) => Messages.Enqueue(text + error);
    }

    internal static class ArgumentExtensions
    {
        public static void CheckNullArgument(this object value, string name)
        {
            if (value == null) throw new ArgumentNullException(name);
        }
    }
}

namespace Loupedeck.CodexHapticsPlugin
{
    internal static class PluginResources
    {
        public static void Init(System.Reflection.Assembly assembly) { }
    }
}
