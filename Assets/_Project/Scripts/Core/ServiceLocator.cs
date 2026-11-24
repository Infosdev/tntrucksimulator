using System;
using System.Collections.Generic;
using UnityEngine;

namespace TNLorry.Core
{
    /// <summary>
    /// Lightweight service locator (section I.2). Swap for VContainer later if DI is wanted —
    /// call sites only use Get&lt;T&gt;() so the migration is mechanical.
    /// </summary>
    public static class ServiceLocator
    {
        static readonly Dictionary<Type, object> Services = new Dictionary<Type, object>(32);

        public static void Register<T>(T service) where T : class
        {
            var type = typeof(T);
            if (Services.ContainsKey(type))
                Debug.LogWarning($"[ServiceLocator] Replacing already-registered service {type.Name}.");
            Services[type] = service;
        }

        public static T Get<T>() where T : class
        {
            if (Services.TryGetValue(typeof(T), out var service)) return (T)service;
            Debug.LogError($"[ServiceLocator] Service {typeof(T).Name} was never registered.");
            return null;
        }

        public static bool TryGet<T>(out T service) where T : class
        {
            if (Services.TryGetValue(typeof(T), out var found)) { service = (T)found; return true; }
            service = null;
            return false;
        }

        public static void Unregister<T>() where T : class => Services.Remove(typeof(T));

        public static void Clear() => Services.Clear();
    }
}
