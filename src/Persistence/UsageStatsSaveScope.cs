using System.IO;
using ConsoleAutocomplete.Constants;
using ConsoleAutocomplete.Util;

namespace ConsoleAutocomplete.Persistence
{
    /// <summary>Resolves {save}/Modded/ConsoleAutocomplete/ for per-save usage stats.</summary>
    internal static class UsageStatsSaveScope
    {
        public const string ModdedFolderName = "Modded";
        public const string FeatureFolderName = "ConsoleAutocomplete";
        public const string FileName = "usage_stats.json";
        public const int SchemaVersion = 1;

        public static string GetLoadedGameFolderPath()
        {
            if (!Singleton<LoadManager>.InstanceExists || Singleton<LoadManager>.Instance == null)
                return null;

            string path = Singleton<LoadManager>.Instance.LoadedGameFolderPath;
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }

        public static bool IsGameLoaded()
        {
            return Singleton<LoadManager>.InstanceExists
                   && Singleton<LoadManager>.Instance != null
                   && Singleton<LoadManager>.Instance.IsGameLoaded
                   && !string.IsNullOrWhiteSpace(GetLoadedGameFolderPath());
        }

        public static string GetFeatureFolderPath(string saveFolderPath = null)
        {
            string root = saveFolderPath ?? GetLoadedGameFolderPath();
            if (string.IsNullOrWhiteSpace(root))
                return null;

            return Path.Combine(root, ModdedFolderName, FeatureFolderName);
        }

        public static string GetStatsFilePath(string saveFolderPath = null)
        {
            string folder = GetFeatureFolderPath(saveFolderPath);
            return string.IsNullOrWhiteSpace(folder) ? null : Path.Combine(folder, FileName);
        }

        public static bool TryEnsureFeatureFolder(string saveFolderPath, out string featureFolder)
        {
            featureFolder = GetFeatureFolderPath(saveFolderPath);
            if (string.IsNullOrWhiteSpace(featureFolder))
                return false;

            try
            {
                Directory.CreateDirectory(featureFolder);
                return true;
            }
            catch (System.Exception ex)
            {
                ModLog.Warning("Failed to create usage stats folder: " + ex.Message);
                return false;
            }
        }
    }
}
