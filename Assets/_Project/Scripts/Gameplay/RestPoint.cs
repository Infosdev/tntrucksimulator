using TNLorry.Core;
using TNLorry.Vehicle;
using UnityEngine;

namespace TNLorry.Gameplay
{
    public enum RestPointKind { TeaKadai, Dhaba, Lodge, TransportNagar }

    /// <summary>
    /// A tea kadai, dhaba or lodge (section K.5, D.3). Pull in, stop, sleep — fatigue clears,
    /// the clock advances, and the game saves.
    ///
    /// This doubles as the game's save point, which is why it writes synchronously: the whole
    /// value of a rest stop is that the player can put the phone down there.
    ///
    /// Kinds differ only in how long you can stay and what it costs: a tea kadai is a free
    /// 20-minute breather, a lodge is a paid full night.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class RestPoint : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] string pointId = "rest_";
        [SerializeField] RestPointKind kind = RestPointKind.Dhaba;

        [Header("Rules")]
        [Tooltip("Speed below which the truck counts as parked, in km/h.")]
        [SerializeField] float stopSpeedKmh = 1.5f;
        [Tooltip("Longest stay this place offers, in in-game hours.")]
        [SerializeField] int maxRestHours = 8;
        [Tooltip("Rupees per in-game hour of rest. 0 for a roadside tea stop.")]
        [SerializeField] int costPerHour;

        /// <summary>True while a parked player truck is here — the UI prompt listens to this.</summary>
        public bool Available { get; private set; }
        public RestPointKind Kind => kind;
        public int MaxRestHours => maxRestHours;

        public event System.Action<bool> OnAvailabilityChanged;

        EconomyService _economy;
        FatigueSystem _fatigue;
        TruckController _truck;

        void Awake()
        {
            _economy = ServiceLocator.Get<EconomyService>();
            GetComponent<Collider>().isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            var truck = other.GetComponentInParent<TruckController>();
            if (truck == null) return;

            _truck = truck;

            // FatigueSystem is a scene-level component pointed at the truck, not a child of it.
            _fatigue = FindFirstObjectByType<FatigueSystem>();
            if (_fatigue == null)
                Debug.LogError("[RestPoint] No FatigueSystem in the scene; resting will do nothing.", this);
        }

        void OnTriggerExit(Collider other)
        {
            if (other.GetComponentInParent<TruckController>() != _truck) return;

            _truck = null;
            SetAvailable(false);
        }

        void Update()
        {
            SetAvailable(_truck != null && _fatigue != null && _truck.SpeedKmh <= stopSpeedKmh);
        }

        void SetAvailable(bool available)
        {
            if (available == Available) return;
            Available = available;
            OnAvailabilityChanged?.Invoke(available);
        }

        public int QuoteRest(int hours) => costPerHour * Mathf.Clamp(hours, 1, maxRestHours);

        /// <summary>
        /// Sleeps for the requested hours. Returns false when the player cannot pay, so the
        /// prompt can say why rather than silently doing nothing.
        /// </summary>
        public bool Rest(int hours)
        {
            if (!Available) return false;

            hours = Mathf.Clamp(hours, 1, maxRestHours);
            int cost = QuoteRest(hours);

            if (cost > 0 && !_economy.TryDebit(cost, "rest:" + pointId)) return false;

            // FatigueSystem.Rest advances the clock, clears fatigue and saves immediately.
            _fatigue.Rest(hours);
            return true;
        }
    }
}
