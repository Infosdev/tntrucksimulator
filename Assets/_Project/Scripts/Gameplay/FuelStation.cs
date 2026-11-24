using TNLorry.Core;
using TNLorry.Services;
using TNLorry.Vehicle;
using UnityEngine;

namespace TNLorry.Gameplay
{
    /// <summary>
    /// A petrol bunk (section K.5). Drive into the bay, stop, and the pump becomes available;
    /// the UI calls FillUp or FillPartial, and this charges for exactly the litres delivered.
    ///
    /// Fictional brands only (Appendix 1) — the brand is a prefab/material concern, not a
    /// code one, but the reminder belongs where someone will read it.
    ///
    /// Put this on the bay root with a trigger collider covering the pump island.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class FuelStation : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] string stationId = "bunk_";
        [Tooltip("Price multiplier. Highway bunks a little dearer than city ones.")]
        [Range(0.8f, 1.3f)] [SerializeField] float priceMultiplier = 1f;

        [Header("Rules")]
        [Tooltip("Speed below which the truck counts as stopped at the pump, in km/h.")]
        [SerializeField] float stopSpeedKmh = 1.5f;
        [Tooltip("Hazardous loads cannot refuel here (no-smoking zone rules, section E.1).")]
        [SerializeField] bool refuseHazardousLoads = true;

        /// <summary>True while a stopped player truck is at the pump — the UI prompt listens to this.</summary>
        public bool PumpAvailable { get; private set; }
        public float PricePerLitre => _economy != null ? _economy.DieselPricePerLitre * priceMultiplier : 0f;

        /// <summary>Raised when availability changes, so the HUD can show or hide the prompt.</summary>
        public event System.Action<bool> OnAvailabilityChanged;

        EconomyService _economy;
        TruckController _truck;
        FuelSystem _fuel;
        DamageSystem _damage;

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
            _fuel = truck.GetComponent<FuelSystem>();
            _damage = truck.GetComponent<DamageSystem>();

            if (_fuel == null)
                Debug.LogError("[FuelStation] The player truck has no FuelSystem; refuelling is impossible.", truck);
        }

        void OnTriggerExit(Collider other)
        {
            if (other.GetComponentInParent<TruckController>() != _truck) return;

            _truck = null;
            _fuel = null;
            _damage = null;
            SetAvailable(false);
        }

        void Update()
        {
            SetAvailable(_truck != null && _fuel != null &&
                         _truck.SpeedKmh <= stopSpeedKmh &&
                         _fuel.LitresToFill > 0.5f &&
                         !IsCarryingHazardousLoad());
        }

        bool IsCarryingHazardousLoad()
            => refuseHazardousLoads && _damage != null &&
               (_damage.ActiveCargoFlags & Data.CargoFlags.Hazardous) != 0;

        void SetAvailable(bool available)
        {
            if (available == PumpAvailable) return;
            PumpAvailable = available;
            OnAvailabilityChanged?.Invoke(available);
        }

        /// <summary>Cost of filling the tank from where it is now.</summary>
        public int QuoteFullTank()
            => _fuel == null ? 0 : Mathf.RoundToInt(_fuel.LitresToFill * PricePerLitre);

        public int QuoteLitres(float litres)
            => Mathf.RoundToInt(Mathf.Max(0f, litres) * PricePerLitre);

        /// <summary>
        /// Fills as much as the player can pay for. Returns the litres delivered.
        ///
        /// Partial fills matter: running out of diesel 40 km short with ₹300 in the account is
        /// a situation the player must be able to drive out of, not a dead end.
        /// </summary>
        public float FillUp()
        {
            if (!PumpAvailable) return 0f;

            float wanted = _fuel.LitresToFill;
            float affordable = PricePerLitre <= 0f ? wanted : _economy.Balance / PricePerLitre;
            float litres = Mathf.Min(wanted, Mathf.Max(0f, affordable));

            return Deliver(litres);
        }

        public float FillPartial(float litres) => PumpAvailable ? Deliver(litres) : 0f;

        float Deliver(float requestedLitres)
        {
            if (requestedLitres <= 0.01f) return 0f;

            // Charge for what the tank actually took, not what was asked for.
            float delivered = _fuel.Refuel(requestedLitres);
            if (delivered <= 0.01f) return 0f;

            int cost = Mathf.RoundToInt(delivered * PricePerLitre);
            if (!_economy.TryDebit(cost, "fuel:" + stationId))
            {
                // Should not happen — FillUp sizes to the balance — but if a price changed
                // mid-transaction, the fuel must not be given away.
                Debug.LogWarning("[FuelStation] Could not charge for fuel; reversing the fill.");
                _fuel.Restore(Mathf.Max(0f, _fuel.Litres - delivered));
                return 0f;
            }

            EventBus.Publish(new RefuelledEvent
            {
                StationId = stationId,
                Litres = delivered,
                Cost = cost
            });

            SetAvailable(false);
            return delivered;
        }

        /// <summary>Rewarded-ad option: a free top-up when the player is stranded (section U.1).</summary>
        public void OfferRewardedTopUp(float litres, System.Action<bool> onComplete)
        {
            var ads = ServiceLocator.Get<IAdService>();
            if (ads == null) { onComplete?.Invoke(false); return; }

            ads.ShowRewarded(RewardedPlacement.FreeTow, earned =>
            {
                if (earned && _fuel != null)
                {
                    float delivered = _fuel.Refuel(litres);
                    EventBus.Publish(new RefuelledEvent { StationId = stationId, Litres = delivered, Cost = 0 });
                }
                onComplete?.Invoke(earned);
            });
        }
    }
}
