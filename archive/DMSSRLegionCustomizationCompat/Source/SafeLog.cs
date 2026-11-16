using System;

namespace WhiteGiver.DMSSRLegionCustomizationCompat
{
    /// <summary>
    /// Logging must never be the reason a Harmony patch fails to install. The validation
    /// harness reads patch state through public flags instead of scraping this log.
    /// </summary>
    internal static class SafeLog
    {
        private const string Prefix = "[DMS Legion Customization Compat] ";

        internal static void Message(string text)
        {
            try
            {
                Verse.Log.Message(Prefix + text);
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
                Verse.Log.Warning(Prefix + text);
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
                Verse.Log.Error(Prefix + text + (exception == null ? string.Empty : "\n" + exception));
            }
            catch
            {
                // Never let logging break patching.
            }
        }
    }
}
