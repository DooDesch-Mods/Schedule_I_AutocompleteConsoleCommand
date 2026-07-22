using System;
using MelonLoader;

namespace ConsoleAutocomplete.Util
{
    public static class ModLog
    {
        private static string _prefix = "[ConsoleAutocomplete]";

        public static void SetPrefix(string prefix)
        {
            if (!string.IsNullOrWhiteSpace(prefix))
                _prefix = prefix;
        }

        public static void Info(string message) => MelonLogger.Msg(_prefix + " " + message);

        public static void Warning(string message) => MelonLogger.Warning(_prefix + " " + message);

        public static void Error(string message) => MelonLogger.Error(_prefix + " " + message);

        public static void Debug(string message)
        {
#if DEBUG
            MelonLogger.Msg(_prefix + " [dbg] " + message);
#else
            _ = message;
#endif
        }

        public static void ErrorOnce(string key, string message)
        {
            if (string.IsNullOrEmpty(key))
            {
                Error(message);
                return;
            }

            if (!_once.Add(key))
                return;

            Error(message);
        }

        private static readonly System.Collections.Generic.HashSet<string> _once =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
    }
}
