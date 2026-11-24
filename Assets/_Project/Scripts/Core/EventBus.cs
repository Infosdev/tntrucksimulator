using System;
using System.Collections.Generic;

namespace TNLorry.Core
{
    /// <summary>
    /// Zero-allocation-ish typed event bus. Events are structs so publishing does not
    /// allocate; handler lists are cached per event type.
    ///
    /// Decouples systems per architecture section I.2 — the vehicle never references the
    /// HUD, the job system never references the economy UI, etc.
    /// </summary>
    public static class EventBus
    {
        static readonly Dictionary<Type, Delegate> Handlers = new Dictionary<Type, Delegate>(64);

        public static void Subscribe<T>(Action<T> handler) where T : struct
        {
            if (handler == null) return;
            var type = typeof(T);
            Handlers.TryGetValue(type, out var existing);
            Handlers[type] = (Action<T>)existing + handler;
        }

        public static void Unsubscribe<T>(Action<T> handler) where T : struct
        {
            if (handler == null) return;
            var type = typeof(T);
            if (!Handlers.TryGetValue(type, out var existing)) return;

            var remaining = (Action<T>)existing - handler;
            if (remaining == null) Handlers.Remove(type);
            else Handlers[type] = remaining;
        }

        public static void Publish<T>(T evt) where T : struct
        {
            if (Handlers.TryGetValue(typeof(T), out var existing))
                ((Action<T>)existing)?.Invoke(evt);
        }

        /// <summary>Called on scene teardown / returning to menu so stale handlers cannot leak.</summary>
        public static void Clear() => Handlers.Clear();
    }
}
