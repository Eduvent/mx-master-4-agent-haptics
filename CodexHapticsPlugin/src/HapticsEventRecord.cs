namespace Loupedeck.CodexHapticsPlugin
{
    using System;

    // Only hook metadata belongs here; never retain the source JSON or message content.
    internal sealed class HapticsEventRecord
    {
        public String RawEvent { get; }
        public String NormalizedEvent => this.RawEvent == "Stop" ? "turn_finished" : null;
        public String Timestamp { get; }
        public String SessionId { get; }
        public String TurnId { get; }
        public String ToolUseId { get; }
        public String ToolName { get; }
        public String PermissionMode { get; }
        public Boolean? MessageHasQuestionMark { get; }

        internal HapticsEventRecord(String rawEvent, String timestamp, String sessionId, String turnId,
            String toolUseId, String toolName, String permissionMode, Boolean? messageHasQuestionMark)
        {
            this.RawEvent = rawEvent;
            this.Timestamp = timestamp;
            this.SessionId = sessionId;
            this.TurnId = turnId;
            this.ToolUseId = toolUseId;
            this.ToolName = toolName;
            this.PermissionMode = permissionMode;
            this.MessageHasQuestionMark = messageHasQuestionMark;
        }
    }
}
