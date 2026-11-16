using System;

namespace WhiteGiver.DMSSRNeuroglueFix
{
    internal static class SafeLog
    {
        internal static void Message(string text)
        {
            try
            {
                Verse.Log.Message("[DMSSR NeuroglueFix] " + text);
            }
            catch
            {
                // Never let logging break patching or offline harnesses.
            }
        }

        internal static void Warning(string text)
        {
            try
            {
                Verse.Log.Warning("[DMSSR NeuroglueFix] " + text);
            }
            catch
            {
                // Never let logging break patching or offline harnesses.
            }
        }

        internal static void Error(string text, Exception? exception = null)
        {
            try
            {
                Verse.Log.Error("[DMSSR NeuroglueFix] " + text + (exception == null ? "" : "\n" + exception));
            }
            catch
            {
                // Never let logging break patching or offline harnesses.
            }
        }
    }
}
