using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ConsoleAutocomplete.Autocomplete
{
    /// <summary>
    /// Turns freeform ExampleUsage strings into a &lt;required&gt; / [optional] structure header.
    /// </summary>
    public static class ExampleUsageNormalizer
    {
        private static readonly Regex MultiExampleSplit =
            new Regex(@"\s*,\s*(?=\S+\s)", RegexOptions.Compiled);

        private static readonly HashSet<string> KnownOptionalSeconds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "give", "triggerlightning"
            };

        public static string Normalize(string commandWord, string exampleUsage)
        {
            if (string.IsNullOrWhiteSpace(commandWord))
                commandWord = string.Empty;

            string word = commandWord.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(exampleUsage))
                return word;

            string example = exampleUsage.Trim();

            // Prefer templates that already use angle/square brackets.
            if (example.IndexOf('<') >= 0 || example.IndexOf('[') >= 0)
            {
                string firstTemplate = TakeFirstExample(example);
                return CollapseSpaces(firstTemplate.ToLowerInvariant());
            }

            // Built-in overrides for clearer schemas than raw examples.
            switch (word)
            {
                case "packageproduct":
                    return "packageproduct <packaging>";
                case "setdiscovered":
                    return "setdiscovered <product>";
                case "give":
                    return "give <item> [quantity]";
                case "teleport":
                    return "teleport <location|property|npc>";
                case "spawnvehicle":
                    return "spawnvehicle <vehicle>";
                case "setowned":
                    return "setowned <property|business>";
                case "setunlocked":
                    return "setunlocked <npc>";
                case "setrelationship":
                    return "setrelationship <npc> <value>";
                case "addemployee":
                    return "addemployee <type> <property>";
                case "setquality":
                    return "setquality <quality>";
                case "setregionunlocked":
                    return "setregionunlocked <region>";
                case "setqueststate":
                    return "setqueststate <quest> <state>";
                case "setquestentrystate":
                    return "setquestentrystate <quest> <entry> <state>";
                case "setvar":
                    return "setvar <variable> <value>";
                case "bind":
                    return "bind <key> <command>";
                case "unbind":
                    return "unbind <key>";
                case "setpoliceignoreplayers":
                    return "setpoliceignoreplayers <true|false>";
                case "setweather":
                    return "setweather <weather>";
                case "triggerlightning":
                    return "triggerlightning [npc|player]";
            }

            string first = TakeFirstExample(example);
            string[] tokens = first.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                return word;

            var sb = new StringBuilder();
            sb.Append(tokens[0].ToLowerInvariant());

            for (int i = 1; i < tokens.Length; i++)
            {
                string token = tokens[i];
                bool optional = i == tokens.Length - 1
                                && KnownOptionalSeconds.Contains(word)
                                && tokens.Length >= 3;

                // Heuristic: trailing numeric / bool-looking tokens are often optional for give-like cmds.
                if (i == tokens.Length - 1
                    && tokens.Length >= 3
                    && (int.TryParse(token, out _) || float.TryParse(token, out _)
                        || token.Equals("true", StringComparison.OrdinalIgnoreCase)
                        || token.Equals("false", StringComparison.OrdinalIgnoreCase)))
                    optional = true;

                string name = InferParamName(word, i, token);
                sb.Append(' ');
                if (optional)
                    sb.Append('[').Append(name).Append(']');
                else
                    sb.Append('<').Append(name).Append('>');
            }

            return sb.ToString();
        }

        private static string TakeFirstExample(string example)
        {
            string[] parts = MultiExampleSplit.Split(example);
            return parts.Length > 0 ? parts[0].Trim() : example.Trim();
        }

        private static string InferParamName(string commandWord, int index, string sampleToken)
        {
            if (int.TryParse(sampleToken, out _) || float.TryParse(sampleToken, out _))
                return index == 1 ? "value" : "amount";

            if (sampleToken.Equals("true", StringComparison.OrdinalIgnoreCase)
                || sampleToken.Equals("false", StringComparison.OrdinalIgnoreCase))
                return "true|false";

            switch (commandWord)
            {
                case "give":
                case "setdiscovered":
                case "packageproduct":
                    return index == 1 ? "item" : "quantity";
                case "teleport":
                case "setowned":
                    return "location";
                case "spawnvehicle":
                    return "vehicle";
                default:
                    return "arg" + index.ToString();
            }
        }

        private static string CollapseSpaces(string value)
        {
            var sb = new StringBuilder(value.Length);
            bool prevSpace = false;
            foreach (char c in value)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!prevSpace)
                        sb.Append(' ');
                    prevSpace = true;
                }
                else
                {
                    sb.Append(c);
                    prevSpace = false;
                }
            }

            return sb.ToString().Trim();
        }
    }
}
