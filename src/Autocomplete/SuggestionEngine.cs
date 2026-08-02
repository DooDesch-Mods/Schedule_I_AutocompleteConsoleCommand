using System;
using System.Collections.Generic;
using System.Text;
using ConsoleAutocomplete.Autocomplete.ArgProviders;

namespace ConsoleAutocomplete.Autocomplete
{
    public static class SuggestionEngine
    {
        public const int MaxVisibleSuggestions = 8;

        public sealed class Result
        {
            public string StructureHeader { get; set; } = string.Empty;
            public string DescriptionHelper { get; set; } = string.Empty;
            public string SourceHelper { get; set; } = string.Empty;
            public List<SuggestionItem> Suggestions { get; set; } = new List<SuggestionItem>();
            public int SelectedIndex { get; set; }
            public string CurrentToken { get; set; } = string.Empty;
            public int TokenStart { get; set; }
            public int TokenEnd { get; set; }

            /// <summary>0 while completing the command word, otherwise the argument being typed.</summary>
            public int ArgIndex { get; set; }

            public bool HasSuggestions => Suggestions != null && Suggestions.Count > 0;
            public bool HasHelper =>
                !string.IsNullOrEmpty(StructureHeader)
                || !string.IsNullOrEmpty(DescriptionHelper)
                || HasSuggestions;

            public SuggestionItem Selected =>
                HasSuggestions && SelectedIndex >= 0 && SelectedIndex < Suggestions.Count
                    ? Suggestions[SelectedIndex]
                    : null;
        }

        public static Result Build(string input, int caret, int selectedIndex = 0)
        {
            var result = new Result();
            input = input ?? string.Empty;
            if (caret < 0)
                caret = input.Length;
            if (caret > input.Length)
                caret = input.Length;

            ParseTokenAtCaret(input, caret, out int tokenStart, out int tokenEnd, out string token);
            result.TokenStart = tokenStart;
            result.TokenEnd = tokenEnd;
            result.CurrentToken = token;

            string[] allTokens = Tokenize(input);
            int argIndex = GetArgIndex(input, caret);
            result.ArgIndex = Math.Max(0, argIndex);

            if (argIndex <= 0)
            {
                // Completing command word.
                string prefix = token;
                var suggestions = new List<SuggestionItem>();
                foreach (CommandMatch hit in CommandIndex.FindMatches(prefix))
                {
                    suggestions.Add(new SuggestionItem
                    {
                        Value = hit.Entry.Word,
                        DisplayLeft = hit.Entry.Word,
                        SourceLabel = hit.Entry.SourceLabel,
                        StructureHeader = hit.Entry.StructureHeader,
                        MatchKind = hit.Match.Kind,
                        MatchOffset = hit.Match.Offset
                    });
                }

                result.Suggestions = UsageStats.RankCommands(suggestions, prefix);
                AppendHistory(result.Suggestions, input, caret);
                result.SelectedIndex = ClampIndex(selectedIndex, result.Suggestions.Count);
                if (result.Selected != null)
                {
                    result.StructureHeader = result.Selected.StructureHeader ?? result.Selected.Value;
                    result.SourceHelper = result.Selected.SourceLabel ?? string.Empty;
                    if (CommandIndex.TryGet(result.Selected.Value, out CommandEntry selectedCmd))
                        result.DescriptionHelper = selectedCmd.Description ?? string.Empty;
                }

                return result;
            }

            string commandWord = allTokens.Length > 0 ? allTokens[0] : string.Empty;
            CommandIndex.TryGet(commandWord, out CommandEntry command);
            result.StructureHeader = command?.StructureHeader
                                     ?? ExampleUsageNormalizer.Normalize(commandWord, command?.ExampleUsage);
            result.DescriptionHelper = command?.Description ?? string.Empty;
            result.SourceHelper = command?.SourceLabel ?? string.Empty;

            string argPrefix = token;
            var argSuggestions = new List<SuggestionItem>();

            bool hasProvider = ArgProviderRegistry.TryGetCandidates(
                commandWord,
                argIndex - 1,
                out List<ArgCandidate> candidates);

            if (hasProvider && candidates != null)
            {
                foreach (ArgCandidate candidate in candidates)
                {
                    MatchResult match = FuzzyMatcher.Match(candidate.Value, argPrefix);
                    if (!match.IsMatch)
                        continue;

                    argSuggestions.Add(new SuggestionItem
                    {
                        Value = candidate.Value,
                        DisplayLeft = candidate.Value,
                        SourceLabel = candidate.SourceLabel,
                        StructureHeader = result.StructureHeader,
                        MatchKind = match.Kind,
                        MatchOffset = match.Offset
                    });
                }
            }

            // If the dedicated provider found nothing, still surface ExampleUsage samples
            // (covers IL2CPP type-filter misses like packageproduct packaging ids).
            if (argSuggestions.Count == 0
                && command != null
                && !string.IsNullOrWhiteSpace(command.ExampleUsage))
            {
                foreach (string sample in ExtractExampleTokens(command.ExampleUsage, argIndex))
                {
                    MatchResult match = FuzzyMatcher.Match(sample, argPrefix);
                    if (!match.IsMatch)
                        continue;

                    argSuggestions.Add(new SuggestionItem
                    {
                        Value = sample,
                        DisplayLeft = sample,
                        SourceLabel = command.SourceLabel,
                        StructureHeader = result.StructureHeader,
                        MatchKind = match.Kind,
                        MatchOffset = match.Offset
                    });
                }
            }

            result.Suggestions = UsageStats.RankArgs(commandWord, argSuggestions, argPrefix);
            AppendHistory(result.Suggestions, input, caret);
            result.SelectedIndex = ClampIndex(selectedIndex, result.Suggestions.Count);
            return result;
        }

        /// <summary>
        /// Puts the lines already run at the BOTTOM of the list, under everything the ranker produced.
        ///
        /// This is what replaced walking the history with the arrow keys. Two lists behind one pair of keys needed a
        /// mode to say which one was being walked, and every rule for entering and leaving that mode was wrong for
        /// somebody: going up into the history and changing your mind left you unable to come back down into the
        /// suggestions. One list has no modes.
        ///
        /// OLDEST FIRST, so the most recent command is the LAST entry. The selection wraps, so Up from the top of the
        /// list lands on it - one press for the command just run, which is the press a player reaches for most.
        ///
        /// Appended rather than ranked in, because the position is the feature: ranking would scatter them through
        /// the list by match quality and the bottom would stop meaning "things you have run".
        /// </summary>
        private static void AppendHistory(List<SuggestionItem> into, string input, int caret)
        {
            if (into == null)
                return;

            IReadOnlyList<string> lines = CommandHistory.Lines;
            if (lines.Count == 0)
                return;

            // What has been typed so far, not the token under the caret: a history entry is a whole line, so
            // `give og` has to keep `give ogkushseed 5` and drop `settime 700`.
            string typed = (input ?? string.Empty).Substring(0, Math.Min(caret, (input ?? string.Empty).Length))
                .TrimStart();

            for (int i = lines.Count - 1; i >= 0; i--)
            {
                string line = lines[i];
                if (typed.Length > 0 && !line.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                    continue;

                into.Add(new SuggestionItem
                {
                    Value = line,
                    DisplayLeft = line,
                    SourceLabel = CommandHistory.Label,
                    MatchKind = MatchKind.Substring,
                    IsHistory = true,
                });
            }
        }

        public static string ApplySelection(string input, Result result)
        {
            if (result?.Selected == null)
                return input ?? string.Empty;

            // A history entry IS the prompt, where an ordinary suggestion completes the token under the caret.
            // Replacing just the token would turn `give ogkushseed 5` into `give ogkushseed 5 5`.
            if (result.Selected.IsHistory)
                return result.Selected.Value ?? string.Empty;

            input = input ?? string.Empty;
            string replacement = result.Selected.Value ?? string.Empty;
            var sb = new StringBuilder();
            sb.Append(input, 0, result.TokenStart);
            sb.Append(replacement);

            int after = Math.Min(result.TokenEnd, input.Length);
            bool needsSpace = after >= input.Length || input[after] != ' ';
            // After completing a command word (arg index 0), add trailing space to continue typing.
            int argIndex = GetArgIndex(input, result.TokenStart);
            if (argIndex <= 0)
                needsSpace = true;

            if (needsSpace)
                sb.Append(' ');

            if (after < input.Length)
                sb.Append(input, after, input.Length - after);

            return sb.ToString();
        }

        public static string GhostSuffix(Result result)
        {
            if (result?.Selected == null)
                return string.Empty;

            string selected = result.Selected.Value ?? string.Empty;
            string token = result.CurrentToken ?? string.Empty;
            if (selected.StartsWith(token, StringComparison.OrdinalIgnoreCase)
                && selected.Length > token.Length)
                return selected.Substring(token.Length);

            return string.Empty;
        }

        public static string[] Tokenize(string input)
        {
            if (string.IsNullOrEmpty(input))
                return Array.Empty<string>();

            return input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public static int GetArgIndex(string input, int caret)
        {
            input = input ?? string.Empty;
            if (caret < 0)
                caret = 0;
            if (caret > input.Length)
                caret = input.Length;

            int index = 0;
            bool inToken = false;
            for (int i = 0; i < caret; i++)
            {
                if (char.IsWhiteSpace(input[i]))
                {
                    if (inToken)
                    {
                        index++;
                        inToken = false;
                    }
                }
                else
                {
                    inToken = true;
                }
            }

            return index;
        }

        public static void ParseTokenAtCaret(
            string input,
            int caret,
            out int tokenStart,
            out int tokenEnd,
            out string token)
        {
            input = input ?? string.Empty;
            if (caret < 0)
                caret = 0;
            if (caret > input.Length)
                caret = input.Length;

            tokenStart = caret;
            while (tokenStart > 0 && !char.IsWhiteSpace(input[tokenStart - 1]))
                tokenStart--;

            tokenEnd = caret;
            while (tokenEnd < input.Length && !char.IsWhiteSpace(input[tokenEnd]))
                tokenEnd++;

            token = input.Substring(tokenStart, tokenEnd - tokenStart);
        }

        private static int ClampIndex(int selectedIndex, int count)
        {
            if (count <= 0)
                return 0;
            if (selectedIndex < 0)
                return 0;
            if (selectedIndex >= count)
                return count - 1;
            return selectedIndex;
        }

        private static IEnumerable<string> ExtractExampleTokens(string exampleUsage, int argIndex)
        {
            if (string.IsNullOrWhiteSpace(exampleUsage))
                yield break;

            string[] examples = exampleUsage.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string example in examples)
            {
                string[] tokens = Tokenize(example.Trim());
                if (tokens.Length <= argIndex)
                    continue;

                string value = tokens[argIndex].Trim().ToLowerInvariant();
                if (value.StartsWith("<") || value.StartsWith("["))
                    continue;

                if (seen.Add(value))
                    yield return value;
            }
        }
    }
}
