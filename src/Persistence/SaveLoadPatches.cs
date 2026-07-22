using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ConsoleAutocomplete.Autocomplete;
using ConsoleAutocomplete.Util;
using HarmonyLib;

namespace ConsoleAutocomplete.Persistence
{
    [HarmonyPatch]
    internal static class SaveManagerPatches
    {
        private static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(typeof(SaveManager), "Save", new Type[] { typeof(string) });
#if IL2CPP
            if (method == null)
                method = AccessTools.Method(typeof(SaveManager), "Save", new Type[] { typeof(Il2CppSystem.String) });
#endif
            return method;
        }

        private static void Postfix(object[] __args)
        {
            try
            {
                string saveFolderPath = null;
                if (__args != null && __args.Length > 0 && __args[0] != null)
                    saveFolderPath = __args[0].ToString();

                if (string.IsNullOrWhiteSpace(saveFolderPath))
                    saveFolderPath = UsageStatsSaveScope.GetLoadedGameFolderPath();

                ApproveModdedPaths();
                UsageStats.WriteToSaveFolder(saveFolderPath);
            }
            catch (Exception ex)
            {
                ModLog.Warning("Usage stats save hook failed: " + ex.Message);
            }
        }

        private static void ApproveModdedPaths()
        {
            if (!Singleton<SaveManager>.InstanceExists || Singleton<SaveManager>.Instance == null)
                return;

            try
            {
                var paths = Singleton<SaveManager>.Instance.ApprovedBaseLevelPaths;
                if (paths == null)
                    return;

                string modded = UsageStatsSaveScope.ModdedFolderName;
                string nested = Path.Combine(
                    UsageStatsSaveScope.ModdedFolderName,
                    UsageStatsSaveScope.FeatureFolderName);

                EnsurePath(paths, modded);
                EnsurePath(paths, nested);
            }
            catch (Exception ex)
            {
                ModLog.Debug("ApprovedBaseLevelPaths update failed: " + ex.Message);
            }
        }

        private static void EnsurePath(object paths, string value)
        {
            if (paths == null || string.IsNullOrEmpty(value) || ContainsPath(paths, value))
                return;

#if IL2CPP
            if (paths is Il2CppSystem.Collections.Generic.List<string> il2List)
            {
                il2List.Add(value);
                return;
            }
#endif
            if (paths is List<string> list)
                list.Add(value);
        }

        private static bool ContainsPath(object paths, string value)
        {
            if (paths == null || string.IsNullOrEmpty(value))
                return false;

#if IL2CPP
            if (paths is Il2CppSystem.Collections.Generic.List<string> il2List)
                return il2List.Contains(value);
#endif
            if (paths is List<string> list)
                return list.Contains(value);

            foreach (object entry in (System.Collections.IEnumerable)paths)
            {
                if (entry != null && string.Equals(entry.ToString(), value, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(LoadManager), "ExitToMenu")]
    internal static class LoadManagerExitPatch
    {
        private static void Prefix()
        {
            try
            {
                // Flush current slot before teardown if a save folder is still known.
                string folder = UsageStatsSaveScope.GetLoadedGameFolderPath();
                if (!string.IsNullOrWhiteSpace(folder))
                    UsageStats.WriteToSaveFolder(folder);

                UsageStats.ResetSession();
            }
            catch (Exception ex)
            {
                ModLog.Debug("ExitToMenu usage stats flush failed: " + ex.Message);
            }
        }
    }

    /// <summary>Host-only gate matching Legal Produce (fail-open when network is unavailable).</summary>
    internal static class SaveHostGate
    {
        public static bool CanWrite()
        {
            try
            {
                if (InstanceFinder.NetworkManager == null)
                    return true;

                return InstanceFinder.IsServer;
            }
            catch
            {
                return true;
            }
        }
    }
}
