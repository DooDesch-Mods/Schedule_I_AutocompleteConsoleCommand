using System;
using System.Collections.Generic;
using System.Reflection;
using ConsoleAutocomplete.Util;
#if IL2CPP
using GameConsole = Il2CppScheduleOne.Console;
#else
using GameConsole = ScheduleOne.Console;
#endif

namespace ConsoleAutocomplete.Autocomplete
{
    /// <summary>
    /// Indexes commands from the live game Console.Commands list.
    /// Mod-added items/vehicles appear through Registry / VehicleManager arg providers,
    /// not via a special S1API scan.
    /// </summary>
    public static class CommandIndex
    {
        private static readonly List<CommandEntry> _commands = new List<CommandEntry>();
        private static readonly Dictionary<string, CommandEntry> _byWord =
            new Dictionary<string, CommandEntry>(StringComparer.OrdinalIgnoreCase);

        private static bool _dirty = true;

        public static IReadOnlyList<CommandEntry> Commands => _commands;

        public static void MarkDirty() => _dirty = true;

        public static void EnsureBuilt()
        {
            if (!_dirty)
                return;

            Rebuild();
        }

        public static void Rebuild()
        {
            _commands.Clear();
            _byWord.Clear();
            ModAttribution.Invalidate();

            try
            {
                IndexGameCommands();
            }
            catch (Exception ex)
            {
                ModLog.Warning("Failed to index console commands: " + ex.Message);
            }

            _commands.Sort((a, b) => string.Compare(a.Word, b.Word, StringComparison.OrdinalIgnoreCase));
            _dirty = false;
            ModLog.Debug("Command index rebuilt: " + _commands.Count + " commands.");
        }

        public static bool TryGet(string word, out CommandEntry entry)
        {
            EnsureBuilt();
            if (string.IsNullOrWhiteSpace(word))
            {
                entry = null;
                return false;
            }

            return _byWord.TryGetValue(word.Trim(), out entry);
        }

        public static IEnumerable<CommandEntry> FindPrefix(string prefix)
        {
            EnsureBuilt();
            string p = prefix ?? string.Empty;
            for (int i = 0; i < _commands.Count; i++)
            {
                CommandEntry cmd = _commands[i];
                if (cmd.Word.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                    yield return cmd;
            }
        }

        private static void IndexGameCommands()
        {
            object commandsObj = typeof(GameConsole).GetField(
                    "Commands",
                    BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null);

            if (commandsObj == null)
                return;

            foreach (object raw in GameLists.Enumerate<object>(commandsObj))
            {
                if (raw == null)
                    continue;

                TryAddFromGameCommand(raw);
            }
        }

        private static void TryAddFromGameCommand(object raw)
        {
            Type type = raw.GetType();
            string word = ReadStringProp(raw, "CommandWord");
            if (string.IsNullOrWhiteSpace(word))
                return;

            string description = ReadStringProp(raw, "CommandDescription") ?? string.Empty;
            string example = ReadStringProp(raw, "ExampleUsage") ?? string.Empty;

            bool nestedInConsole = type.DeclaringType == typeof(GameConsole)
                                   || (type.Namespace != null
                                       && type.Namespace.StartsWith(
                                           typeof(GameConsole).Namespace ?? "ScheduleOne",
                                           StringComparison.Ordinal));

            bool isVanilla = nestedInConsole || ModAttribution.IsGameAssembly(type.Assembly);
            string source = isVanilla
                ? ModAttribution.VanillaLabel
                : ModAttribution.LabelForType(type);

            Add(new CommandEntry
            {
                Word = word.Trim().ToLowerInvariant(),
                Description = description,
                ExampleUsage = example,
                StructureHeader = ExampleUsageNormalizer.Normalize(word, example),
                SourceLabel = source,
                IsVanilla = isVanilla
            });
        }

        private static void Add(CommandEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Word))
                return;

            if (_byWord.ContainsKey(entry.Word))
                return;

            _byWord[entry.Word] = entry;
            _commands.Add(entry);
        }

        private static string ReadStringProp(object target, string name)
        {
            if (target == null)
                return null;

            PropertyInfo prop = target.GetType().GetProperty(
                name,
                BindingFlags.Public | BindingFlags.Instance);
            if (prop == null)
                return null;

            try
            {
                return prop.GetValue(target, null)?.ToString();
            }
            catch
            {
                return null;
            }
        }
    }
}
