using System;
using System.IO;
using System.Reflection;
using MelonLoader;

[assembly: MelonInfo(
    typeof(ConsoleAutocomplete.Loader.ConsoleAutocompleteLoader),
    "ConsoleAutocomplete.Loader",
    "0.1.0",
    "Hiemdallh")]

namespace ConsoleAutocomplete.Loader
{
    /// <summary>
    /// MelonPlugin that enables ConsoleAutocomplete.dll or ConsoleAutocomplete.IL2CPP.dll
    /// for the current game backend (same pattern as S1API / LegalProduce loaders).
    /// </summary>
    public sealed class ConsoleAutocompleteLoader : MelonPlugin
    {
        private const string MonoModFileName = "ConsoleAutocomplete.dll";
        private const string Il2CppModFileName = "ConsoleAutocomplete.IL2CPP.dll";

        public override void OnApplicationEarlyStart()
        {
            string pluginsFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrWhiteSpace(pluginsFolder))
            {
                MelonLogger.Error("[ConsoleAutocomplete.Loader] Could not resolve Plugins folder.");
                return;
            }

            string modsFolder = Path.GetFullPath(Path.Combine(pluginsFolder, "..", "Mods"));
            if (!Directory.Exists(modsFolder))
            {
                MelonLogger.Warning("[ConsoleAutocomplete.Loader] Mods folder not found: " + modsFolder);
                return;
            }

            bool isIl2Cpp = MelonUtils.IsGameIl2Cpp();
            MelonLogger.Msg(
                "[ConsoleAutocomplete.Loader] Enabling Console Autocomplete for "
                + (isIl2Cpp ? "IL2CPP" : "Mono")
                + ".");

            SetEnabled(Path.Combine(modsFolder, Il2CppModFileName), isIl2Cpp);
            SetEnabled(Path.Combine(modsFolder, MonoModFileName), !isIl2Cpp);
        }

        private static void SetEnabled(string dllPath, bool enabled)
        {
            string disabledPath = dllPath + ".disabled";
            bool dllExists = File.Exists(dllPath);
            bool disabledExists = File.Exists(disabledPath);

            if (!dllExists && !disabledExists)
                return;

            try
            {
                if (enabled)
                {
                    if (dllExists)
                    {
                        if (disabledExists)
                            SafeDelete(disabledPath);
                        return;
                    }

                    File.Move(disabledPath, dllPath);
                    return;
                }

                if (!dllExists && disabledExists)
                    return;

                if (dllExists)
                {
                    SafeDelete(disabledPath);
                    File.Move(dllPath, disabledPath);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning(
                    "[ConsoleAutocomplete.Loader] Failed to "
                    + (enabled ? "enable" : "disable")
                    + " '"
                    + Path.GetFileName(dllPath)
                    + "': "
                    + ex.Message);
            }
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning(
                    "[ConsoleAutocomplete.Loader] Failed to delete '" + path + "': " + ex.Message);
            }
        }
    }
}
