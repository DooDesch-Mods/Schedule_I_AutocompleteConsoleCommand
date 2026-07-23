using System;
using ConsoleAutocomplete.Autocomplete;
using ConsoleAutocomplete.Autocomplete.ArgProviders;
using ConsoleAutocomplete.Constants;
using ConsoleAutocomplete.Util;
using ConsoleAutocomplete.Util.Harmony;
using MelonLoader;

[assembly: MelonInfo(typeof(ConsoleAutocomplete.ModEntry), ModInfo.ModName, ModInfo.ModVersion, ModInfo.ModAuthor)]
[assembly: MelonGame(ModInfo.GameCompany, ModInfo.GameName)]
[assembly: MelonColor(ModInfo.ModConsoleColor.A, ModInfo.ModConsoleColor.R, ModInfo.ModConsoleColor.G, ModInfo.ModConsoleColor.B)]
[assembly: HarmonyDontPatchAll]

namespace ConsoleAutocomplete
{
    public class ModEntry : MelonMod
    {
        private const string GameplayScene = "Main";

        public override void OnInitializeMelon()
        {
            ModLog.SetPrefix("[ConsoleAutocomplete]");
            ModLog.ConfigureDefaultVerbosity();
            ArgProviderRegistry.Initialize();
            HarmonyBootstrap.Apply(typeof(ModEntry).Assembly);
#if DEBUG || AUTOCOMPLETE_DEBUG
            ModLog.Info(
                "Console Autocomplete loaded ("
                + ModInfo.ModVersion
                + ") [DEBUG — watch Melon log for [dbg] lines].");
#else
            ModLog.Info("Console Autocomplete loaded (" + ModInfo.ModVersion + ").");
#endif
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            // LoadManager sets IsGameLoaded during gameplay bootstrap; refresh when entering a save.
            if (IsGameplayScene(sceneName))
                UsageStats.EnsureLoadedForCurrentSave();
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            if (IsGameplayScene(sceneName))
                UsageStats.ResetSession();
        }

        public override void OnApplicationQuit()
        {
            HarmonyBootstrap.Remove();
        }

        public override void OnDeinitializeMelon()
        {
            HarmonyBootstrap.Remove();
        }

        private static bool IsGameplayScene(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                return false;

            // Schedule I uses "Main" for gameplay; accept common aliases defensively.
            return sceneName.Equals("Main", StringComparison.OrdinalIgnoreCase)
                   || sceneName.Equals("Game", StringComparison.OrdinalIgnoreCase)
                   || sceneName.Equals("Gameplay", StringComparison.OrdinalIgnoreCase);
        }
    }
}
