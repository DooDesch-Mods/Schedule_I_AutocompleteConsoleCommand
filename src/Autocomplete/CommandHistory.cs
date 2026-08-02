using System;
using System.Collections.Generic;

namespace ConsoleAutocomplete.Autocomplete
{
    /// <summary>
    /// The lines the player has already run this session, most recent first.
    ///
    /// KEPT HERE RATHER THAN READ FROM THE GAME. Vanilla has its own list in a private static field on ConsoleUI and
    /// walks it with the arrow keys, which is the behaviour this replaces: the history now sits at the bottom of the
    /// suggestion list, so one list and one pair of arrow keys cover both. Reaching into that field would mean
    /// interop reflection for something the mod can simply record itself - the SubmitCommand prefix that feeds the
    /// usage stats sees every line anyway.
    ///
    /// Held most recent first, which is what makes recording cheap. The suggestion list wants the opposite order and
    /// reverses it on the way in - see SuggestionEngine.AppendHistory for why the most recent has to be last.
    /// </summary>
    public static class CommandHistory
    {
        /// <summary>Enough for a working session, few enough that the list stays worth stepping through.</summary>
        private const int Keep = 25;

        /// <summary>What the overlay prints in the source column for these rows.</summary>
        public const string Label = "History";

        private static readonly List<string> _lines = new List<string>();

        public static IReadOnlyList<string> Lines => _lines;

        /// <summary>
        /// Records a line that was just submitted.
        ///
        /// A repeat moves to the front instead of being added again: running the same command five times in a row is
        /// normal, and five identical rows would push everything else out of a 25-entry list.
        /// </summary>
        public static void Record(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            string trimmed = line.Trim();
            for (int i = 0; i < _lines.Count; i++)
            {
                if (!string.Equals(_lines[i], trimmed, StringComparison.OrdinalIgnoreCase))
                    continue;

                _lines.RemoveAt(i);
                break;
            }

            _lines.Insert(0, trimmed);
            while (_lines.Count > Keep)
                _lines.RemoveAt(_lines.Count - 1);
        }

        public static void Clear() => _lines.Clear();
    }
}
