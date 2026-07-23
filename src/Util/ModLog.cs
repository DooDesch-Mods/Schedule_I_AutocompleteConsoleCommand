using System;
using MelonLoader;

namespace ConsoleAutocomplete.Util
{
    public static class ModLog
    {
        private static string _prefix = "[ConsoleAutocomplete]";
        private static bool _verbose;

        /// <summary>Force verbose logs at runtime (Debug builds default on).</summary>
        public static bool Verbose
        {
            get => _verbose;
            set => _verbose = value;
        }

        public static void SetPrefix(string prefix)
        {
            if (!string.IsNullOrWhiteSpace(prefix))
                _prefix = prefix;
        }

        public static void ConfigureDefaultVerbosity()
        {
#if DEBUG || AUTOCOMPLETE_DEBUG
            _verbose = true;
            MelonLogger.Msg(_prefix + " Verbose/debug logging ENABLED (Debug build).");
#else
            _verbose = false;
#endif
        }

        public static void Info(string message) => MelonLogger.Msg(_prefix + " " + message);

        public static void Warning(string message) => MelonLogger.Warning(_prefix + " " + message);

        public static void Error(string message) => MelonLogger.Error(_prefix + " " + message);

        public static void Debug(string message)
        {
            if (!_verbose)
                return;

            MelonLogger.Msg(_prefix + " [dbg] " + message);
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
