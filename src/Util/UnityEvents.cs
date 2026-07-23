using System;
using UnityEngine.Events;
#if IL2CPP
using Il2CppInterop.Runtime;
#endif

namespace ConsoleAutocomplete.Util
{
    /// <summary>IL2CPP-safe UnityEvent listener wrapping.</summary>
    public static class UnityEvents
    {
        public static UnityAction Action(Action handler)
        {
#if IL2CPP
            UnityAction converted = DelegateSupport.ConvertDelegate<UnityAction>(handler);
            if (converted != null)
                return converted;
            return (UnityAction)(() => handler());
#else
            return new UnityAction(handler);
#endif
        }

        public static UnityAction<T> Action<T>(Action<T> handler)
        {
#if IL2CPP
            UnityAction<T> converted = DelegateSupport.ConvertDelegate<UnityAction<T>>(handler);
            if (converted != null)
                return converted;
            return (UnityAction<T>)(value => handler(value));
#else
            return new UnityAction<T>(handler);
#endif
        }
    }
}
