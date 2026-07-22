using System.Collections.Generic;

namespace ConsoleAutocomplete.Util
{
    /// <summary>IL2CPP-safe enumeration of game list-like collections.</summary>
    public static class GameLists
    {
        public static IEnumerable<T> Enumerate<T>(object list) where T : class
        {
            if (list == null)
                yield break;

#if IL2CPP
            if (list is Il2CppSystem.Collections.Generic.List<T> il2List)
            {
                for (int i = 0; i < il2List.Count; i++)
                {
                    T item = il2List[i];
                    if (item != null)
                        yield return item;
                }

                yield break;
            }
#endif
            if (list is List<T> managed)
            {
                for (int i = 0; i < managed.Count; i++)
                {
                    T item = managed[i];
                    if (item != null)
                        yield return item;
                }

                yield break;
            }

            if (list is System.Collections.IEnumerable enumerable)
            {
                foreach (object entry in enumerable)
                {
                    if (entry is T typed)
                        yield return typed;
                }
            }
        }
    }
}
