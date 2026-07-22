using System;
using UnityEngine.Events;

namespace ConsoleAutocomplete.Util
{
    /// <summary>IL2CPP-safe UnityEvent listener wrapping.</summary>
    public static class UnityEvents
    {
        public static UnityAction Action(Action handler)
        {
#if IL2CPP
            return (UnityAction)(() => handler());
#else
            return new UnityAction(handler);
#endif
        }

        public static UnityAction<T> Action<T>(Action<T> handler)
        {
#if IL2CPP
            return (UnityAction<T>)(value => handler(value));
#else
            return new UnityAction<T>(handler);
#endif
        }
    }
}
