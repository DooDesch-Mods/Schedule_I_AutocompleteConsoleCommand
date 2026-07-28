namespace ConsoleAutocomplete.Autocomplete
{
    public sealed class CommandEntry
    {
        public string Word { get; set; }
        public string Description { get; set; }
        public string ExampleUsage { get; set; }
        public string StructureHeader { get; set; }
        public string SourceLabel { get; set; }
        public bool IsVanilla { get; set; }
    }

    public sealed class SuggestionItem
    {
        public string Value { get; set; }
        public string SourceLabel { get; set; }
        public string DisplayLeft { get; set; }
        public string StructureHeader { get; set; }
        public int UsageCount { get; set; }

        /// <summary>How closely this hit answers what was typed; ranked above usage stats.</summary>
        public MatchKind MatchKind { get; set; } = MatchKind.Prefix;

        /// <summary>Index of the first matched character, breaks ties inside a match kind.</summary>
        public int MatchOffset { get; set; }
    }

    public sealed class ArgCandidate
    {
        public string Value { get; set; }
        public string SourceLabel { get; set; }
    }

    /// <summary>A command from the index plus how well it matched what was typed.</summary>
    public struct CommandMatch
    {
        public CommandEntry Entry { get; set; }
        public MatchResult Match { get; set; }
    }
}
