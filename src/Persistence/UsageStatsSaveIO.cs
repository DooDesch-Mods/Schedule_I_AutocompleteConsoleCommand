using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ConsoleAutocomplete.Util;
using Newtonsoft.Json;

namespace ConsoleAutocomplete.Persistence
{
    internal static class UsageStatsSaveIO
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public static bool TryRead(string filePath, out UsageStatsSaveData data)
        {
            if (TryReadJson(filePath, out data) && IsValid(data))
                return true;

            string backupPath = filePath + ".bak";
            if (TryReadJson(backupPath, out data) && IsValid(data))
            {
                ModLog.Warning("Loaded usage stats from backup.");
                return true;
            }

            data = null;
            return false;
        }

        public static bool TryWrite(string filePath, UsageStatsSaveData data)
        {
            if (string.IsNullOrWhiteSpace(filePath) || data == null)
                return false;

            string directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directory))
                return false;

            string tmpPath = filePath + ".tmp";
            try
            {
                Directory.CreateDirectory(directory);
                string json = JsonConvert.SerializeObject(data, Settings);
                using (var stream = new FileStream(
                    tmpPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(true);
                }

                if (!File.Exists(filePath))
                {
                    File.Move(tmpPath, filePath);
                    return true;
                }

                string backupPath = filePath + ".bak";
                File.Replace(tmpPath, filePath, backupPath, ignoreMetadataErrors: true);
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Warning("Failed to write usage stats: " + ex.Message);
                try
                {
                    if (File.Exists(tmpPath))
                        File.Delete(tmpPath);
                }
                catch
                {
                    // ignored
                }

                return false;
            }
        }

        private static bool IsValid(UsageStatsSaveData data) =>
            data != null
            && data.Version > 0
            && data.Version <= UsageStatsSaveScope.SchemaVersion
            && data.Commands != null
            && data.Args != null;

        private static bool TryReadJson(string filePath, out UsageStatsSaveData data)
        {
            data = null;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return false;

            try
            {
                string json = File.ReadAllText(filePath);
                data = JsonConvert.DeserializeObject<UsageStatsSaveData>(json, Settings);
                if (data == null)
                    return false;

                data.Commands ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                data.Args ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Warning("Failed to read usage stats: " + ex.Message);
                data = null;
                return false;
            }
        }
    }
}
