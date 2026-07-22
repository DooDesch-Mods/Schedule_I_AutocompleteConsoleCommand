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
            public List<SuggestionItem> Suggestions { get; set; } = new List<SuggestionItem>();
            public int SelectedIndex { get; set; }
            public string CurrentToken { get; set; } = string.Empty;
            public int TokenStart { get; set; }
            public int TokenEnd { get; set; }
            public bool HasSuggestions => Suggestions != null && Suggestions.Count > 0;

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

            if (argIndex <= 0)
            {
                // Completing command word.
                string prefix = token;
                var suggestions = new List<SuggestionItem>();
                foreach (CommandEntry entry in CommandIndex.FindPrefix(prefix))
                {
                    suggestions.Add(new SuggestionItem
                    {
                        Value = entry.Word,
                        DisplayLeft = entry.Word,
                        SourceLabel = entry.SourceLabel,
                        StructureHeader = entry.StructureHeader
                    });
                }

                result.Suggestions = UsageStats.RankCommands(suggestions);
                if (result.Suggestions.Count > 0)
                {
                    SuggestionItem selectedPreview = result.Suggestions[
                        ClampIndex(selectedIndex, result.Suggestions.Count)];
                    result.StructureHeader = selectedPreview.StructureHeader
                                            ?? selectedPreview.Value;
                    // Stash description via StructureHeader path — engine fills DescriptionHelper below.
                    if (CommandIndex.TryGet(selectedPreview.Value, out CommandEntry selectedCmd))
                        result.DescriptionHelper = selectedCmd.Description ?? string.Empty;
                    else
                        result.DescriptionHelper = string.Empty;
                }

                result.SelectedIndex = ClampIndex(selectedIndex, result.Suggestions.Count);
                // Keep header in sync with final selection.
                if (result.Selected != null)
                {
                    result.StructureHeader = result.Selected.StructureHeader ?? result.Selected.Value;
                    if (CommandIndex.TryGet(result.Selected.Value, out CommandEntry selectedCmd2))
                        result.DescriptionHelper = selectedCmd2.Description ?? string.Empty;
                }

                return result;
            }

            string commandWord = allTokens.Length > 0 ? allTokens[0] : string.Empty;
            CommandIndex.TryGet(commandWord, out CommandEntry command);
            result.StructureHeader = command?.StructureHeader
                                     ?? ExampleUsageNormalizer.Normalize(commandWord, command?.ExampleUsage);
            result.DescriptionHelper = command?.Description ?? string.Empty;

            string argPrefix = token;
            var argSuggestions = new List<SuggestionItem>();

            if (ArgProviderRegistry.TryGetCandidates(commandWord, argIndex - 1, out List<ArgCandidate> candidates))
            {
                foreach (ArgCandidate candidate in candidates)
                {
                    if (!candidate.Value.StartsWith(argPrefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    argSuggestions.Add(new SuggestionItem
                    {
                        Value = candidate.Value,
                        DisplayLeft = candidate.Value,
                        SourceLabel = candidate.SourceLabel,
                        StructureHeader = result.StructureHeader
                    });
                }
            }
            else if (command != null && !string.IsNullOrWhiteSpace(command.ExampleUsage))
            {
                foreach (string sample in ExtractExampleTokens(command.ExampleUsage, argIndex))
                {
                    if (!sample.StartsWith(argPrefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    argSuggestions.Add(new SuggestionItem
                    {
                        Value = sample,
                        DisplayLeft = sample,
                        SourceLabel = command.SourceLabel,
                        StructureHeader = result.StructureHeader
                    });
                }
            }

            result.Suggestions = UsageStats.RankArgs(commandWord, argSuggestions);
            result.SelectedIndex = ClampIndex(selectedIndex, result.Suggestions.Count);
            return result;
        }

        public static string ApplySelection(string input, Result result)
        {
            if (result?.Selected == null)
                return input ?? string.Empty;

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
