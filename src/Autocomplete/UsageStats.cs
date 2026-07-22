using System;
using System.Collections.Generic;
using System.Linq;
using ConsoleAutocomplete.Persistence;
using ConsoleAutocomplete.Util;

namespace ConsoleAutocomplete.Autocomplete
{
    /// <summary>
    /// Per-save usage stats: command words and command-scoped first args.
    /// Persisted under {save}/Modded/ConsoleAutocomplete/usage_stats.json on game save.
    /// </summary>
    public static class UsageStats
    {
        private static UsageStatsSaveData _data = NewEmpty();
        private static string _loadedSaveFolder;

        public static void ResetSession()
        {
            _data = NewEmpty();
            _loadedSaveFolder = null;
        }

        /// <summary>Load stats for the currently loaded save slot (no-op if none).</summary>
        public static void EnsureLoadedForCurrentSave()
        {
            if (!UsageStatsSaveScope.IsGameLoaded())
            {
                if (_loadedSaveFolder != null)
                    ResetSession();
                return;
            }

            string folder = UsageStatsSaveScope.GetLoadedGameFolderPath();
            if (string.Equals(_loadedSaveFolder, folder, StringComparison.OrdinalIgnoreCase))
                return;

            LoadFromSaveFolder(folder);
        }

        public static void LoadFromSaveFolder(string saveFolderPath)
        {
            _data = NewEmpty();
            _loadedSaveFolder = saveFolderPath;

            string path = UsageStatsSaveScope.GetStatsFilePath(saveFolderPath);
            if (string.IsNullOrWhiteSpace(path))
                return;

            if (UsageStatsSaveIO.TryRead(path, out UsageStatsSaveData loaded) && loaded != null)
            {
                _data = loaded;
                _data.Commands ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                _data.Args ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                ModLog.Debug(
                    "Loaded usage stats ("
                    + _data.Commands.Count + " commands, "
                    + _data.Args.Count + " args) from save.");
            }
        }

        public static void WriteToSaveFolder(string saveFolderPath)
        {
            if (!SaveHostGate.CanWrite())
                return;

            if (string.IsNullOrWhiteSpace(saveFolderPath))
                saveFolderPath = UsageStatsSaveScope.GetLoadedGameFolderPath();

            if (string.IsNullOrWhiteSpace(saveFolderPath) || _data == null)
                return;

            if (!UsageStatsSaveScope.TryEnsureFeatureFolder(saveFolderPath, out _))
                return;

            string path = UsageStatsSaveScope.GetStatsFilePath(saveFolderPath);
            _data.Version = UsageStatsSaveScope.SchemaVersion;
            UsageStatsSaveIO.TryWrite(path, _data);
        }

        public static int GetCommandCount(string commandWord)
        {
            if (string.IsNullOrWhiteSpace(commandWord) || _data?.Commands == null)
                return 0;

            return _data.Commands.TryGetValue(commandWord.Trim(), out int value) ? value : 0;
        }

        public static int GetArgCount(string commandWord, string arg)
        {
            if (string.IsNullOrWhiteSpace(commandWord) || string.IsNullOrWhiteSpace(arg) || _data?.Args == null)
                return 0;

            string key = ArgKey(commandWord, arg);
            return _data.Args.TryGetValue(key, out int value) ? value : 0;
        }

        public static void RecordCommandLine(IReadOnlyList<string> tokens)
        {
            EnsureLoadedForCurrentSave();
            if (tokens == null || tokens.Count == 0 || _data == null)
                return;

            string command = tokens[0].Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(command))
                return;

            Bump(_data.Commands, command);

            // First positional arg only (e.g. tomato / bruiser). Optional trailing
            // tokens like quantity are ignored for ranking.
            if (tokens.Count > 1)
            {
                string arg = tokens[1].Trim().ToLowerInvariant();
                if (!string.IsNullOrEmpty(arg))
                    Bump(_data.Args, ArgKey(command, arg));
            }
        }

        public static List<SuggestionItem> RankCommands(IEnumerable<SuggestionItem> items)
        {
            EnsureLoadedForCurrentSave();
            List<SuggestionItem> list = items?.ToList() ?? new List<SuggestionItem>();
            foreach (SuggestionItem item in list)
                item.UsageCount = GetCommandCount(item.Value);

            return RankByUsageThenAlpha(list);
        }

        public static List<SuggestionItem> RankArgs(string commandWord, IEnumerable<SuggestionItem> items)
        {
            EnsureLoadedForCurrentSave();
            string cmd = commandWord ?? string.Empty;
            List<SuggestionItem> list = items?.ToList() ?? new List<SuggestionItem>();
            foreach (SuggestionItem item in list)
                item.UsageCount = GetArgCount(cmd, item.Value);

            return RankByUsageThenAlpha(list);
        }

        private static List<SuggestionItem> RankByUsageThenAlpha(List<SuggestionItem> list)
        {
            bool anyStats = list.Any(i => i.UsageCount > 0);
            if (!anyStats)
            {
                return list
                    .OrderBy(i => i.DisplayLeft ?? i.Value, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(i => i.SourceLabel, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            List<SuggestionItem> top = list
                .Where(i => i.UsageCount > 0)
                .OrderByDescending(i => i.UsageCount)
                .ThenBy(i => i.DisplayLeft ?? i.Value, StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();

            var topSet = new HashSet<SuggestionItem>(top);
            List<SuggestionItem> rest = list
                .Where(i => !topSet.Contains(i))
                .OrderBy(i => i.DisplayLeft ?? i.Value, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.SourceLabel, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var result = new List<SuggestionItem>(top.Count + rest.Count);
            result.AddRange(top);
            result.AddRange(rest);
            return result;
        }

        private static void Bump(Dictionary<string, int> map, string key)
        {
            if (map == null || string.IsNullOrWhiteSpace(key))
                return;

            map.TryGetValue(key, out int current);
            map[key] = current + 1;
        }

        private static string ArgKey(string commandWord, string arg) =>
            commandWord.Trim().ToLowerInvariant() + " " + arg.Trim().ToLowerInvariant();

        private static UsageStatsSaveData NewEmpty() =>
            new UsageStatsSaveData
            {
                Version = UsageStatsSaveScope.SchemaVersion,
                Commands = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                Args = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            };
    }
}
