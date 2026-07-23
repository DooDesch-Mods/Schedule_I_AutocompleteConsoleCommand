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
    /// Mod-added items/vehicles appear through Registry / VehicleManager arg providers.
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
                ModLog.Warning("Failed to index console commands: " + ex);
            }

            _commands.Sort((a, b) => string.Compare(a.Word, b.Word, StringComparison.OrdinalIgnoreCase));
            _dirty = false;
            ModLog.Debug("Command index rebuilt: " + _commands.Count + " commands.");
            if (_commands.Count == 0)
                ModLog.Warning("Command index is empty — autocomplete will have no command suggestions.");
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
            object commandsObj = GetCommandsCollection();
            if (commandsObj == null)
            {
                ModLog.Debug("Console.Commands collection is null (Console may not be Awake yet).");
                return;
            }

            ModLog.Debug(
                "Indexing Console.Commands from "
                + commandsObj.GetType().FullName
                + ".");

            int seen = 0;
            foreach (object raw in GameLists.EnumerateObjects(commandsObj))
            {
                seen++;
                if (raw == null)
                    continue;

                TryAddFromGameCommand(raw);
            }

            ModLog.Debug("Raw command entries seen=" + seen + ", indexed=" + _commands.Count + ".");
        }

        /// <summary>
        /// IL2CPP exposes Commands as a property; Mono dump uses a public static field.
        /// </summary>
        private static object GetCommandsCollection()
        {
            Type type = typeof(GameConsole);

            PropertyInfo prop = type.GetProperty(
                "Commands",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (prop != null)
            {
                try
                {
                    return prop.GetValue(null, null);
                }
                catch (Exception ex)
                {
                    ModLog.Debug("Console.Commands property get failed: " + ex.Message);
                }
            }

            FieldInfo field = type.GetField(
                "Commands",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (field != null)
            {
                try
                {
                    return field.GetValue(null);
                }
                catch (Exception ex)
                {
                    ModLog.Debug("Console.Commands field get failed: " + ex.Message);
                }
            }

            ModLog.Warning("Could not resolve Console.Commands via property or field.");
            return null;
        }

        private static void TryAddFromGameCommand(object raw)
        {
            Type type = raw.GetType();
            string word = ReadStringProp(raw, "CommandWord");
            if (string.IsNullOrWhiteSpace(word))
            {
                ModLog.Debug("Skipping command entry with empty CommandWord (" + type.FullName + ").");
                return;
            }

            string description = ReadStringProp(raw, "CommandDescription") ?? string.Empty;
            string example = ReadStringProp(raw, "ExampleUsage") ?? string.Empty;

            bool nestedInConsole = type.DeclaringType == typeof(GameConsole)
                                   || (type.Namespace != null
                                       && type.Namespace.IndexOf(
                                           "ScheduleOne",
                                           StringComparison.OrdinalIgnoreCase) >= 0);

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

            try
            {
                PropertyInfo prop = target.GetType().GetProperty(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null)
                    return prop.GetValue(target, null)?.ToString();

                // Il2Cpp sometimes exposes get_CommandWord methods without a PropertyInfo.
                MethodInfo getter = target.GetType().GetMethod(
                    "get_" + name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (getter != null && getter.GetParameters().Length == 0)
                    return getter.Invoke(target, null)?.ToString();
            }
            catch (Exception ex)
            {
                ModLog.Debug("ReadStringProp(" + name + ") failed: " + ex.Message);
            }

            return null;
        }
    }
}
