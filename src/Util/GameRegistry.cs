using System.Collections.Generic;
using ConsoleAutocomplete.Util;

namespace ConsoleAutocomplete.Util
{
    public static class GameRegistry
    {
        public static IEnumerable<ItemDefinition> EnumerateAllItems()
        {
            Registry registry = Singleton<Registry>.Instance;
            if (registry == null)
                yield break;

            object items = registry.GetAllItems();
            foreach (ItemDefinition item in GameLists.Enumerate<ItemDefinition>(items))
                yield return item;
        }
    }
}
