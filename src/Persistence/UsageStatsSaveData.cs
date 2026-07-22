using System;
using System.Collections.Generic;

namespace ConsoleAutocomplete.Persistence
{
    [Serializable]
    public sealed class UsageStatsSaveData
    {
        public int Version = UsageStatsSaveScope.SchemaVersion;

        /// <summary>Command-word usage, e.g. "give" → 12.</summary>
        public Dictionary<string, int> Commands =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Command-scoped first-arg usage, e.g. "give tomato" → 5.</summary>
        public Dictionary<string, int> Args =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }
}
