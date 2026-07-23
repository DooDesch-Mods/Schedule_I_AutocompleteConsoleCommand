using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace ConsoleAutocomplete.Util
{
    /// <summary>IL2CPP-safe enumeration of game list-like collections.</summary>
    public static class GameLists
    {
        public static IEnumerable<T> Enumerate<T>(object list) where T : class
        {
            foreach (object entry in EnumerateObjects(list))
            {
                if (entry is T typed)
                    yield return typed;
            }
        }

        /// <summary>
        /// Enumerate any list-like object without requiring a matching generic T
        /// (critical on IL2CPP where Commands is List&lt;ConsoleCommand&gt;, not List&lt;object&gt;).
        /// </summary>
        public static IEnumerable<object> EnumerateObjects(object list)
        {
            if (list == null)
                yield break;

            // Prefer Count + Item reflection — works for both Mono List and Il2Cpp List`1.
            if (TryGetCountAndIndexer(list, out int count, out PropertyInfo itemProp))
            {
                for (int i = 0; i < count; i++)
                {
                    object item = null;
                    try
                    {
                        item = itemProp.GetValue(list, new object[] { i });
                    }
                    catch
                    {
                        // ignored
                    }

                    if (item != null)
                        yield return item;
                }

                yield break;
            }

#if IL2CPP
            if (list is Il2CppSystem.Collections.Generic.List<Il2CppSystem.Object> il2ObjList)
            {
                for (int i = 0; i < il2ObjList.Count; i++)
                {
                    Il2CppSystem.Object item = il2ObjList[i];
                    if (item != null)
                        yield return item;
                }

                yield break;
            }
#endif
            if (list is IList managedIList)
            {
                for (int i = 0; i < managedIList.Count; i++)
                {
                    object item = managedIList[i];
                    if (item != null)
                        yield return item;
                }

                yield break;
            }

            if (list is IEnumerable enumerable)
            {
                foreach (object entry in enumerable)
                {
                    if (entry != null)
                        yield return entry;
                }
            }
        }

        private static bool TryGetCountAndIndexer(
            object list,
            out int count,
            out PropertyInfo itemProp)
        {
            count = 0;
            itemProp = null;
            if (list == null)
                return false;

            Type type = list.GetType();
            PropertyInfo countProp = type.GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
            itemProp = type.GetProperty("Item", BindingFlags.Public | BindingFlags.Instance);
            if (countProp == null || itemProp == null)
                return false;

            try
            {
                object raw = countProp.GetValue(list, null);
                if (raw is int i)
                {
                    count = i;
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }
    }
}
