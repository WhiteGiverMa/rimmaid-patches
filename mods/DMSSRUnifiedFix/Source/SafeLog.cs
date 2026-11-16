using System;

namespace WhiteGiver.DMSSRUnifiedFix;

internal static class SafeLog
{
    internal static void Message(string text)
    {
        try { Verse.Log.Message("[DMSSR Unified Fix] " + text); }
        catch { /* Logging must not interrupt another subsystem's installation. */ }
    }

    internal static void Warning(string text)
    {
        try { Verse.Log.Warning("[DMSSR Unified Fix] " + text); }
        catch { /* Logging must not interrupt another subsystem's installation. */ }
    }

    internal static void Error(string text, Exception? exception = null)
        => Warning(text + (exception == null ? "" : "\n" + exception));
}
