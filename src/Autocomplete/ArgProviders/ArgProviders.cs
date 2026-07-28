using System;
using System.Collections.Generic;
using ConsoleAutocomplete.Util;
#if IL2CPP
using GameConsole = Il2CppScheduleOne.Console;
#else
using GameConsole = ScheduleOne.Console;
#endif

namespace ConsoleAutocomplete.Autocomplete.ArgProviders
{
    public interface IArgProvider
    {
        string CommandWord { get; }
        int ArgIndex { get; }
        IEnumerable<ArgCandidate> GetCandidates();
    }

    public static class ArgProviderRegistry
    {
        private static readonly List<IArgProvider> _providers = new List<IArgProvider>();
        private static readonly Dictionary<string, string> _itemSources =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static void Initialize()
        {
            _providers.Clear();

            // give → any item id (mod-added items appear via Registry)
            _providers.Add(new ItemArgProvider("give", 0));

            // setdiscovered → product definitions only
            _providers.Add(new TypedItemArgProvider("setdiscovered", 0, ItemKind.Product));

            // packageproduct → packaging definitions only (jar, baggie, …) - not acid/etc.
            _providers.Add(new TypedItemArgProvider("packageproduct", 0, ItemKind.Packaging));

            _providers.Add(new TeleportArgProvider());
            _providers.Add(new PropertyArgProvider("setowned", 0));
            _providers.Add(new NpcArgProvider("setunlocked", 0));
            _providers.Add(new NpcArgProvider("setrelationship", 0));
            _providers.Add(new VehicleArgProvider());
            _providers.Add(new EnumArgProvider(
                "setquality",
                0,
                "ScheduleOne.ItemFramework.EQuality",
                "Il2CppScheduleOne.ItemFramework.EQuality"));
            _providers.Add(new EnumArgProvider(
                "setregionunlocked",
                0,
                "ScheduleOne.Map.EMapRegion",
                "Il2CppScheduleOne.Map.EMapRegion"));
            _providers.Add(new EnumArgProvider(
                "addemployee",
                0,
                "ScheduleOne.Employees.EEmployeeType",
                "Il2CppScheduleOne.Employees.EEmployeeType"));
            _providers.Add(new BoolArgProvider("setpoliceignoreplayers", 0));
            _providers.Add(new WeatherArgProvider());
        }

        public static void RememberItemSource(string itemId, string sourceLabel)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return;

            if (string.IsNullOrWhiteSpace(sourceLabel))
                sourceLabel = ModAttribution.UnknownLabel;

            _itemSources[itemId.Trim()] = sourceLabel;
        }

        public static string GetItemSource(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return ModAttribution.VanillaLabel;

            return _itemSources.TryGetValue(itemId.Trim(), out string label)
                ? label
                : ModAttribution.VanillaLabel;
        }

        public static bool TryGetCandidates(string commandWord, int argIndex, out List<ArgCandidate> candidates)
        {
            candidates = null;
            if (string.IsNullOrWhiteSpace(commandWord) || argIndex < 0)
                return false;

            for (int i = 0; i < _providers.Count; i++)
            {
                IArgProvider provider = _providers[i];
                if (!provider.CommandWord.Equals(commandWord, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (provider.ArgIndex != argIndex)
                    continue;

                candidates = new List<ArgCandidate>();
                foreach (ArgCandidate candidate in provider.GetCandidates())
                {
                    if (candidate != null && !string.IsNullOrWhiteSpace(candidate.Value))
                        candidates.Add(candidate);
                }

                ModLog.Debug(
                    "ArgProvider '"
                    + commandWord
                    + "'["
                    + argIndex
                    + "] → "
                    + candidates.Count
                    + " candidate(s).");

                // Provider owns this slot even if empty (prevents wrong fallback heuristics).
                return true;
            }

            return false;
        }

        internal static bool IsKind(ItemDefinition item, ItemKind kind)
        {
            if (item == null)
                return false;

            switch (kind)
            {
                case ItemKind.Packaging:
                    return IsPackaging(item);
                case ItemKind.Product:
                    return IsProduct(item) && !IsPackaging(item);
                default:
                    return true;
            }
        }

        private static bool IsPackaging(ItemDefinition item)
        {
#if IL2CPP
            try
            {
                if (item.TryCast<Il2CppScheduleOne.Product.Packaging.PackagingDefinition>() != null)
                    return true;
            }
            catch
            {
                // ignored
            }

            // Prefer Il2Cpp runtime type - managed GetType() often reports the List`1 element
            // proxy (ItemDefinition) even when the native object is PackagingDefinition.
            try
            {
                Il2CppSystem.Type il2Type = item.GetIl2CppType();
                if (il2Type != null)
                {
                    string name = il2Type.Name ?? string.Empty;
                    string full = il2Type.FullName ?? string.Empty;
                    if (name.IndexOf("PackagingDefinition", StringComparison.OrdinalIgnoreCase) >= 0
                        || full.IndexOf("PackagingDefinition", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch
            {
                // ignored
            }
#else
            if (item is ScheduleOne.Product.Packaging.PackagingDefinition)
                return true;
#endif
            return TypeNameContains(item.GetType(), "PackagingDefinition");
        }

        private static bool IsProduct(ItemDefinition item)
        {
#if IL2CPP
            try
            {
                if (item.TryCast<Il2CppScheduleOne.Product.ProductDefinition>() != null)
                    return true;
            }
            catch
            {
                // ignored
            }

            try
            {
                Il2CppSystem.Type il2Type = item.GetIl2CppType();
                if (il2Type != null)
                {
                    string name = il2Type.Name ?? string.Empty;
                    string full = il2Type.FullName ?? string.Empty;
                    if (name.IndexOf("ProductDefinition", StringComparison.OrdinalIgnoreCase) >= 0
                        || full.IndexOf("ProductDefinition", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch
            {
                // ignored
            }
#else
            if (item is ScheduleOne.Product.ProductDefinition)
                return true;
#endif
            return TypeNameContains(item.GetType(), "ProductDefinition");
        }

        private static bool TypeNameContains(Type type, string token)
        {
            while (type != null)
            {
                string name = type.Name ?? string.Empty;
                string full = type.FullName ?? string.Empty;
                if (name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0
                    || full.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                type = type.BaseType;
            }

            return false;
        }
    }

    internal enum ItemKind
    {
        Any,
        Packaging,
        Product
    }

    /// <summary>All registry items (except cash) - used by give.</summary>
    internal sealed class ItemArgProvider : IArgProvider
    {
        public ItemArgProvider(string commandWord, int argIndex)
        {
            CommandWord = commandWord;
            ArgIndex = argIndex;
        }

        public string CommandWord { get; }
        public int ArgIndex { get; }

        public IEnumerable<ArgCandidate> GetCandidates()
        {
            foreach (ItemDefinition item in GameRegistry.EnumerateAllItems())
            {
                if (item == null || string.IsNullOrWhiteSpace(item.ID))
                    continue;

                if (item.ID.Equals("cash", StringComparison.OrdinalIgnoreCase))
                    continue;

                yield return new ArgCandidate
                {
                    Value = item.ID.ToLowerInvariant(),
                    SourceLabel = ArgProviderRegistry.GetItemSource(item.ID)
                };
            }
        }
    }

    /// <summary>Registry items filtered to a concrete definition type.</summary>
    internal sealed class TypedItemArgProvider : IArgProvider
    {
        private readonly ItemKind _kind;

        public TypedItemArgProvider(string commandWord, int argIndex, ItemKind kind)
        {
            CommandWord = commandWord;
            ArgIndex = argIndex;
            _kind = kind;
        }

        public string CommandWord { get; }
        public int ArgIndex { get; }

        public IEnumerable<ArgCandidate> GetCandidates()
        {
            int scanned = 0;
            int matched = 0;
            var results = new List<ArgCandidate>();

            foreach (ItemDefinition item in GameRegistry.EnumerateAllItems())
            {
                if (item == null || string.IsNullOrWhiteSpace(item.ID))
                    continue;

                scanned++;

                // One-shot diagnostics for known packaging IDs when filter is broken.
                if (_kind == ItemKind.Packaging
                    && (item.ID.Equals("baggie", StringComparison.OrdinalIgnoreCase)
                        || item.ID.Equals("jar", StringComparison.OrdinalIgnoreCase)))
                {
                    string managedType = item.GetType().FullName ?? item.GetType().Name;
                    string il2Name = "?";
#if IL2CPP
                    try
                    {
                        Il2CppSystem.Type il2 = item.GetIl2CppType();
                        il2Name = il2 != null ? (il2.FullName ?? il2.Name) : "null";
                    }
                    catch (Exception ex)
                    {
                        il2Name = "err:" + ex.GetType().Name;
                    }
#endif
                    ModLog.Debug(
                        "Packaging probe id='"
                        + item.ID
                        + "' managed='"
                        + managedType
                        + "' il2='"
                        + il2Name
                        + "' isKind="
                        + ArgProviderRegistry.IsKind(item, _kind));
                }

                if (!ArgProviderRegistry.IsKind(item, _kind))
                    continue;

                matched++;
                results.Add(new ArgCandidate
                {
                    Value = item.ID.ToLowerInvariant(),
                    SourceLabel = ArgProviderRegistry.GetItemSource(item.ID)
                });
            }

            // IL2CPP GetAllItems sometimes exposes packaging only as base ItemDefinition;
            // seed the known vanilla packaging ids so autocomplete still works.
            if (_kind == ItemKind.Packaging && matched == 0)
            {
                ModLog.Debug(
                    "Packaging filter matched 0 / "
                    + scanned
                    + " items - seeding jar/baggie fallbacks.");

                string[] fallbacks = { "jar", "baggie" };
                for (int i = 0; i < fallbacks.Length; i++)
                {
                    string id = fallbacks[i];
                    results.Add(new ArgCandidate
                    {
                        Value = id,
                        SourceLabel = ArgProviderRegistry.GetItemSource(id)
                    });
                }
            }
            else if (_kind == ItemKind.Packaging)
            {
                ModLog.Debug("Packaging filter matched " + matched + " / " + scanned + " items.");
            }

            return results;
        }
    }

    internal sealed class TeleportArgProvider : IArgProvider
    {
        public string CommandWord => "teleport";
        public int ArgIndex => 0;

        public IEnumerable<ArgCandidate> GetCandidates()
        {
            var results = new List<ArgCandidate>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                GameConsole console = Singleton<GameConsole>.Instance;
                if (console != null && console.TeleportPointsContainer != null)
                {
                    for (int i = 0; i < console.TeleportPointsContainer.childCount; i++)
                    {
                        UnityEngine.Transform child = console.TeleportPointsContainer.GetChild(i);
                        if (child == null || string.IsNullOrWhiteSpace(child.name))
                            continue;

                        string name = child.name.ToLowerInvariant();
                        if (seen.Add(name))
                        {
                            results.Add(new ArgCandidate
                            {
                                Value = name,
                                SourceLabel = ModAttribution.VanillaLabel
                            });
                        }
                    }
                }
            }
            catch
            {
                // ignored
            }

            foreach (Property property in GameLists.Enumerate<Property>(Property.Properties))
            {
                if (property == null || string.IsNullOrWhiteSpace(property.PropertyCode))
                    continue;

                string code = property.PropertyCode.ToLowerInvariant();
                if (!seen.Add(code))
                    continue;

                results.Add(new ArgCandidate
                {
                    Value = code,
                    SourceLabel = ModAttribution.VanillaLabel
                });
            }

            foreach (NPC npc in GameLists.Enumerate<NPC>(NPCManager.NPCRegistry))
            {
                if (npc == null || string.IsNullOrWhiteSpace(npc.ID))
                    continue;

                string id = npc.ID.ToLowerInvariant();
                if (!seen.Add(id))
                    continue;

                results.Add(new ArgCandidate
                {
                    Value = id,
                    SourceLabel = ModAttribution.VanillaLabel
                });
            }

            return results;
        }
    }

    internal sealed class PropertyArgProvider : IArgProvider
    {
        public PropertyArgProvider(string commandWord, int argIndex)
        {
            CommandWord = commandWord;
            ArgIndex = argIndex;
        }

        public string CommandWord { get; }
        public int ArgIndex { get; }

        public IEnumerable<ArgCandidate> GetCandidates()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Property property in GameLists.Enumerate<Property>(Property.Properties))
            {
                if (property == null || string.IsNullOrWhiteSpace(property.PropertyCode))
                    continue;

                string code = property.PropertyCode.ToLowerInvariant();
                if (!seen.Add(code))
                    continue;

                yield return new ArgCandidate
                {
                    Value = code,
                    SourceLabel = ModAttribution.VanillaLabel
                };
            }
        }
    }

    internal sealed class NpcArgProvider : IArgProvider
    {
        public NpcArgProvider(string commandWord, int argIndex)
        {
            CommandWord = commandWord;
            ArgIndex = argIndex;
        }

        public string CommandWord { get; }
        public int ArgIndex { get; }

        public IEnumerable<ArgCandidate> GetCandidates()
        {
            foreach (NPC npc in GameLists.Enumerate<NPC>(NPCManager.NPCRegistry))
            {
                if (npc == null || string.IsNullOrWhiteSpace(npc.ID))
                    continue;

                yield return new ArgCandidate
                {
                    Value = npc.ID.ToLowerInvariant(),
                    SourceLabel = ModAttribution.VanillaLabel
                };
            }
        }
    }

    internal sealed class VehicleArgProvider : IArgProvider
    {
        public string CommandWord => "spawnvehicle";
        public int ArgIndex => 0;

        public IEnumerable<ArgCandidate> GetCandidates()
        {
            VehicleManager manager = NetworkSingleton<VehicleManager>.Instance;
            if (manager == null)
                yield break;

            foreach (LandVehicle vehicle in GameLists.Enumerate<LandVehicle>(manager.VehiclePrefabs))
            {
                if (vehicle == null || string.IsNullOrWhiteSpace(vehicle.VehicleCode))
                    continue;

                yield return new ArgCandidate
                {
                    Value = vehicle.VehicleCode.ToLowerInvariant(),
                    SourceLabel = ModAttribution.VanillaLabel
                };
            }
        }
    }

    internal sealed class EnumArgProvider : IArgProvider
    {
        private readonly string[] _typeNames;

        public EnumArgProvider(string commandWord, int argIndex, params string[] typeNames)
        {
            CommandWord = commandWord;
            ArgIndex = argIndex;
            _typeNames = typeNames;
        }

        public string CommandWord { get; }
        public int ArgIndex { get; }

        public IEnumerable<ArgCandidate> GetCandidates()
        {
            Type enumType = null;
            foreach (string name in _typeNames)
            {
                enumType = FindType(name);
                if (enumType != null)
                    break;
            }

            if (enumType == null || !enumType.IsEnum)
                yield break;

            foreach (string name in Enum.GetNames(enumType))
            {
                yield return new ArgCandidate
                {
                    Value = name.ToLowerInvariant(),
                    SourceLabel = ModAttribution.VanillaLabel
                };
            }
        }

        private static Type FindType(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return null;

            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type type = assembly.GetType(fullName);
                    if (type != null)
                        return type;
                }
                catch
                {
                    // ignored
                }
            }

            return null;
        }
    }

    internal sealed class BoolArgProvider : IArgProvider
    {
        public BoolArgProvider(string commandWord, int argIndex)
        {
            CommandWord = commandWord;
            ArgIndex = argIndex;
        }

        public string CommandWord { get; }
        public int ArgIndex { get; }

        public IEnumerable<ArgCandidate> GetCandidates()
        {
            yield return new ArgCandidate { Value = "true", SourceLabel = ModAttribution.VanillaLabel };
            yield return new ArgCandidate { Value = "false", SourceLabel = ModAttribution.VanillaLabel };
        }
    }

    internal sealed class WeatherArgProvider : IArgProvider
    {
        public string CommandWord => "setweather";
        public int ArgIndex => 0;

        public IEnumerable<ArgCandidate> GetCandidates()
        {
            yield return new ArgCandidate { Value = "clear", SourceLabel = ModAttribution.VanillaLabel };
            yield return new ArgCandidate { Value = "lightrain", SourceLabel = ModAttribution.VanillaLabel };
            yield return new ArgCandidate { Value = "heavyrain", SourceLabel = ModAttribution.VanillaLabel };
        }
    }
}
