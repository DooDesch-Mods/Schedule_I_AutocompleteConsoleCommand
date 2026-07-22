using System;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;

namespace ConsoleAutocomplete.Autocomplete
{
    public static class ModAttribution
    {
        public const string VanillaLabel = "Vanilla";
        public const string UnknownLabel = "Unknown";

        private static readonly Dictionary<Assembly, string> _assemblyLabels =
            new Dictionary<Assembly, string>();

        public static void Invalidate() => _assemblyLabels.Clear();

        public static string LabelForAssembly(Assembly assembly)
        {
            if (assembly == null)
                return UnknownLabel;

            if (_assemblyLabels.TryGetValue(assembly, out string cached))
                return cached;

            string label = ResolveLabel(assembly);
            _assemblyLabels[assembly] = label;
            return label;
        }

        public static string LabelForType(Type type) =>
            type == null ? UnknownLabel : LabelForAssembly(type.Assembly);

        public static bool IsGameAssembly(Assembly assembly)
        {
            if (assembly == null)
                return false;

            string name = assembly.GetName().Name ?? string.Empty;
            return name.Equals("Assembly-CSharp", StringComparison.OrdinalIgnoreCase)
                   || name.Equals("Assembly-CSharp-firstpass", StringComparison.OrdinalIgnoreCase)
                   || name.Equals("ScheduleOne.Core", StringComparison.OrdinalIgnoreCase)
                   || name.Equals("Il2CppScheduleOne.Core", StringComparison.OrdinalIgnoreCase);
        }

        public static string LabelFromStackTrace(int skipFrames = 2)
        {
            try
            {
                var trace = new System.Diagnostics.StackTrace(skipFrames, false);
                for (int i = 0; i < trace.FrameCount; i++)
                {
                    MethodBase method = trace.GetFrame(i)?.GetMethod();
                    Type declaring = method?.DeclaringType;
                    if (declaring == null)
                        continue;

                    Assembly assembly = declaring.Assembly;
                    if (IsGameOrFrameworkAssembly(assembly))
                        continue;

                    string label = LabelForAssembly(assembly);
                    if (!string.IsNullOrEmpty(label) && label != UnknownLabel)
                        return label;
                }
            }
            catch
            {
                // Stack traces can be incomplete under IL2CPP.
            }

            return UnknownLabel;
        }

        private static string ResolveLabel(Assembly assembly)
        {
            if (IsGameAssembly(assembly))
                return VanillaLabel;

            foreach (MelonBase melon in MelonBase.RegisteredMelons)
            {
                if (melon?.MelonAssembly?.Assembly == assembly && melon.Info != null)
                    return FormatMelon(melon.Info.Name, melon.Info.Version);
            }

            try
            {
                object[] attrs = assembly.GetCustomAttributes(typeof(MelonInfoAttribute), false);
                if (attrs != null && attrs.Length > 0 && attrs[0] is MelonInfoAttribute info)
                    return FormatMelon(info.Name, info.Version);
            }
            catch
            {
                // ignored
            }

            string name = assembly.GetName().Name;
            if (string.IsNullOrWhiteSpace(name))
                return UnknownLabel;

            if (name.StartsWith("Assembly-CSharp", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Il2Cpp", StringComparison.OrdinalIgnoreCase))
                return VanillaLabel;

            return name;
        }

        private static string FormatMelon(string name, string version)
        {
            if (string.IsNullOrWhiteSpace(name))
                return UnknownLabel;

            if (string.IsNullOrWhiteSpace(version))
                return name.Trim();

            return name.Trim() + " v" + version.Trim();
        }

        private static bool IsGameOrFrameworkAssembly(Assembly assembly)
        {
            if (assembly == null)
                return true;

            if (IsGameAssembly(assembly))
                return true;

            string name = assembly.GetName().Name ?? string.Empty;
            if (name.StartsWith("System", StringComparison.Ordinal)
                || name.StartsWith("Unity", StringComparison.Ordinal)
                || name.StartsWith("Il2CppSystem", StringComparison.Ordinal)
                || name.StartsWith("Il2CppInterop", StringComparison.Ordinal)
                || name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase)
                || name.Equals("netstandard", StringComparison.OrdinalIgnoreCase)
                || name.Equals("0Harmony", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Harmony", StringComparison.OrdinalIgnoreCase)
                || name.Equals("MelonLoader", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("ConsoleAutocomplete", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }
    }
}
