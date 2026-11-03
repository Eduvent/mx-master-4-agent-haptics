namespace Loupedeck.CodexHapticsPlugin
{
    using System;

    internal static class HapticsEventRouter
    {
        // Allowlist only the verified catalog entry. Attention sources remain unmapped.
        public static String BackendEvent(HapticsConfiguration configuration, HapticsEventRecord record)
        {
            if (!configuration.Enabled || record.NormalizedEvent != "turn_finished" ||
                !configuration.TurnFinished.Enabled) return null;
            return configuration.TurnFinished.Profile == "completed" ? "TestPulse" : null;
        }
    }
}
