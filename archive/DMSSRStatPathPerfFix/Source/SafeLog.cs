using System;

namespace WhiteGiver.DMSSRStatPathPerfFix
{
    /// <summary>
    /// Logging must never be the reason a Harmony patch fails to install, so every call is guarded.
    /// The offline validation harness also reads the patch state through public properties instead of
    /// scraping the log, so a silent console is never mistaken for a silent patch.
    /// </summary>
    internal static class SafeLog
    {
        internal static void Message(string text)
        {
            try
            {
                Verse.Log.Message("[DMSSR StatPathPerfFix] " + text);
            }
            catch
            {
                // Never let logging break patching.
            }
        }

        internal static void Warning(string text)
        {
            try
            {
                Verse.Log.Warning("[DMSSR StatPathPerfFix] " + text);
            }
            catch
            {
                // Never let logging break patching.
            }
        }

        internal static void Error(string text, Exception? exception = null)
        {
            try
            {
                Verse.Log.Error("[DMSSR StatPathPerfFix] " + text + (exception == null ? "" : "\n" + exception));
            }
            catch
            {
                // Never let logging break patching.
            }
        }
    }
}
