using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using ConsoleAutocomplete.Util;

namespace ConsoleAutocomplete.Util.Harmony
{
    /// <summary>Applies Harmony patches per class so one bad target cannot disable the mod.</summary>
    public static class HarmonyBootstrap
    {
        private static HarmonyLib.Harmony _harmony;
        private static string _harmonyId = Constants.ModInfo.HarmonyId;
        private static int _appliedCount;
        private static int _failedCount;

        public static void Apply(Assembly assembly)
        {
            if (assembly == null || _harmony != null)
                return;

            _harmony = new HarmonyLib.Harmony(_harmonyId);
            _appliedCount = 0;
            _failedCount = 0;

            foreach (Type patchType in GetPatchTypes(assembly))
            {
                try
                {
                    new PatchClassProcessor(_harmony, patchType).Patch();
                    _appliedCount++;
                }
                catch (Exception ex)
                {
                    _failedCount++;
                    ModLog.ErrorOnce(
                        "harmony-patch:" + patchType.FullName,
                        "Harmony patch failed for " + patchType.FullName + ": " + ex.Message);
                }
            }

            if (_appliedCount == 0)
            {
                ModLog.ErrorOnce("harmony-zero", "Harmony applied zero patch classes.");
                Remove();
            }
            else
            {
                ModLog.Info(
                    "Harmony applied " + _appliedCount + " patch class(es)"
                    + (_failedCount > 0 ? ", failed " + _failedCount : "")
                    + ".");
            }
        }

        public static void Remove()
        {
            if (_harmony == null)
                return;

            try
            {
                _harmony.UnpatchSelf();
            }
            catch (Exception ex)
            {
                ModLog.ErrorOnce("harmony-unpatch", "Failed to remove Harmony patches: " + ex.Message);
            }
            finally
            {
                _harmony = null;
                _appliedCount = 0;
                _failedCount = 0;
            }
        }

        private static IEnumerable<Type> GetPatchTypes(Assembly assembly)
        {
            var results = new List<Type>();
            foreach (Type type in assembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0)
                    results.Add(type);
            }

            return results.OrderBy(type => type.FullName, StringComparer.Ordinal);
        }
    }
}
