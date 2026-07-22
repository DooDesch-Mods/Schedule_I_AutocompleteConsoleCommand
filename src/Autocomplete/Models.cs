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
    }

    public sealed class ArgCandidate
    {
        public string Value { get; set; }
        public string SourceLabel { get; set; }
    }
}
