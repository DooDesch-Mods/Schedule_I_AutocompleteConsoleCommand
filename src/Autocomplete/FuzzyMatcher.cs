using System;

namespace ConsoleAutocomplete.Autocomplete
{
    /// <summary>
    /// How well a candidate answers what the player typed. Higher wins, and the suggestion list is
    /// ordered by this before usage stats, so the closer hits always sit on top.
    /// </summary>
    public enum MatchKind
    {
        None = 0,

        /// <summary>Every typed character shows up in order, but not next to each other (`fzr`).</summary>
        Subsequence = 1,

        /// <summary>The typed text appears somewhere inside the candidate.</summary>
        Substring = 2,

        /// <summary>The typed text starts one of the candidate's words (`fertilizer` in `long_life_fertilizer`).</summary>
        WordStart = 3,

        /// <summary>The candidate starts with the typed text.</summary>
        Prefix = 4,

        /// <summary>The candidate is exactly what was typed.</summary>
        Exact = 5
    }

    public struct MatchResult
    {
        public MatchKind Kind;

        /// <summary>Index of the first matched character, used to break ties inside a kind.</summary>
        public int Offset;

        public bool IsMatch => Kind != MatchKind.None;
    }

    /// <summary>
    /// Graded matching for command words and arguments: exact, prefix, word start, substring, and
    /// finally a loose subsequence, so `give fertilizer` also finds `long_life_fertilizer` and
    /// `give fzr` still finds something.
    /// </summary>
    public static class FuzzyMatcher
    {
        /// <summary>Below this length a subsequence match is noise, so it is not offered.</summary>
        private const int MinSubsequenceQuery = 2;

        private static readonly char[] WordSeparators = { '_', '-', '.', ' ', '/', ':' };

        public static MatchResult Match(string candidate, string query)
        {
            var result = new MatchResult { Kind = MatchKind.None, Offset = int.MaxValue };
            if (string.IsNullOrEmpty(candidate))
                return result;

            // Nothing typed yet: everything matches, ordering falls back to usage stats.
            if (string.IsNullOrEmpty(query))
            {
                result.Kind = MatchKind.Prefix;
                result.Offset = 0;
                return result;
            }

            if (candidate.Equals(query, StringComparison.OrdinalIgnoreCase))
            {
                result.Kind = MatchKind.Exact;
                result.Offset = 0;
                return result;
            }

            if (candidate.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            {
                result.Kind = MatchKind.Prefix;
                result.Offset = 0;
                return result;
            }

            int index = candidate.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                result.Kind = IsWordStart(candidate, index) ? MatchKind.WordStart : MatchKind.Substring;
                result.Offset = index;
                return result;
            }

            if (query.Length >= MinSubsequenceQuery
                && TryMatchSubsequence(candidate, query, out int firstIndex))
            {
                result.Kind = MatchKind.Subsequence;
                result.Offset = firstIndex;
            }

            return result;
        }

        public static bool IsMatch(string candidate, string query) => Match(candidate, query).IsMatch;

        private static bool IsWordStart(string candidate, int index)
        {
            if (index <= 0)
                return true;

            char previous = candidate[index - 1];
            for (int i = 0; i < WordSeparators.Length; i++)
            {
                if (previous == WordSeparators[i])
                    return true;
            }

            // camelCase boundary, e.g. `spawnVehicle` matched at `Vehicle`.
            return char.IsLower(previous) && char.IsUpper(candidate[index]);
        }

        private static bool TryMatchSubsequence(string candidate, string query, out int firstIndex)
        {
            firstIndex = -1;
            int q = 0;
            for (int i = 0; i < candidate.Length && q < query.Length; i++)
            {
                if (char.ToLowerInvariant(candidate[i]) != char.ToLowerInvariant(query[q]))
                    continue;

                if (q == 0)
                    firstIndex = i;

                q++;
            }

            if (q < query.Length)
            {
                firstIndex = -1;
                return false;
            }

            return true;
        }
    }
}
