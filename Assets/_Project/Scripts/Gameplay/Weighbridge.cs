using TNLorry.Core;
using TNLorry.Vehicle;
using UnityEngine;

namespace TNLorry.Gameplay
{
    /// <summary>
    /// RTO check-post weighbridge (sections D.3, K.7). Drive onto the pad, stop, and the load
    /// is weighed; over the permitted limit and the fine scales with the excess.
    ///
    /// The *mini-game* (choosing a lane, paperwork, arguing with the clerk) is Phase 2. What
    /// exists here is the rule itself, so overloading is already a decision with a consequence
    /// rather than a free payout multiplier.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Weighbridge : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] string postId = "checkpost_";

        [Header("Rules")]
        [Tooltip("Speed below which the truck counts as weighed, in km/h.")]
        [SerializeField] float stopSpeedKmh = 1.5f;
        [Tooltip("Tolerance over the truck's rated payload before it counts as overloaded, in kg.")]
        [SerializeField] float toleranceKg = 500f;
        [Tooltip("Seconds on the pad before the reading is taken.")]
        [SerializeField] float weighSeconds = 1.5f;

        /// <summary>Last reading, in kg of cargo. Exposed for the HUD readout.</summary>
        public float LastReadingKg { get; private set; }
        public float LastExcessKg { get; private set; }
        public bool  Weighing { get; private set; }

        public event System.Action<float, float> OnWeighed;   // (cargoKg, excessKg)

        TruckController _truck;
        TrafficRuleMonitor _rules;
        float _onPadTimer;
        bool  _weighedThisVisit;

        void Awake() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerEnter(Collider other)
        {
            var truck = other.GetComponentInParent<TruckController>();
            if (truck == null) return;

            _truck = truck;
            _rules = truck.GetComponentInChildren<TrafficRuleMonitor>();
            _onPadTimer = 0f;
            _weighedThisVisit = false;
            Weighing = true;
        }

        void OnTriggerExit(Collider other)
        {
            if (other.GetComponentInParent<TruckController>() != _truck) return;

            _truck = null;
            _rules = null;
            Weighing = false;
            _onPadTimer = 0f;
        }

        void Update()
        {
            if (_truck == null || _weighedThisVisit) return;

            // Rolling across the pad does not produce a reading — you have to actually stop.
            if (_truck.SpeedKmh > stopSpeedKmh)
            {
                _onPadTimer = 0f;
                return;
            }

            _onPadTimer += Time.deltaTime;
            if (_onPadTimer < weighSeconds) return;

            Weigh();
        }

        void Weigh()
        {
            _weighedThisVisit = true;

            LastReadingKg = _truck.CargoMassKg;
            float permitted = _truck.Data.maxPayloadKg + toleranceKg;
            LastExcessKg = Mathf.Max(0f, LastReadingKg - permitted);

            if (LastExcessKg > 0f) _rules?.ReportOverload(LastExcessKg);

            OnWeighed?.Invoke(LastReadingKg, LastExcessKg);

            EventBus.Publish(new WeighbridgeReadingEvent
            {
                PostId = postId,
                CargoKg = LastReadingKg,
                ExcessKg = LastExcessKg
            });
        }
    }
}
