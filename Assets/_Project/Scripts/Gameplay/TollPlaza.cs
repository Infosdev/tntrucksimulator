using TNLorry.Core;
using TNLorry.Vehicle;
using UnityEngine;

namespace TNLorry.Gameplay
{
    public enum TollLane { Fastag, Cash }

    /// <summary>
    /// Toll plaza (section K.8). FASTag lane deducts and lifts the barrier; cash lane needs a
    /// full stop and pays a few rupees more — the discount is the reason to learn the lanes.
    ///
    /// Put this on the plaza root, with one trigger collider per lane approach that calls
    /// EnterLane, and a barrier animator per lane.
    /// </summary>
    public class TollPlaza : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] string plazaId = "plaza_";
        [Tooltip("Chargeable length of highway this plaza covers, in km.")]
        [SerializeField] float coveredKm = 55f;

        [Header("Barriers")]
        [SerializeField] Animator[] fastagBarriers = new Animator[0];
        [SerializeField] Animator[] cashBarriers = new Animator[0];
        [SerializeField] string barrierOpenParam = "Open";

        [Header("Cash lane")]
        [Tooltip("Speed below which the truck counts as stopped, in km/h.")]
        [SerializeField] float stopSpeedKmh = 1.5f;
        [Tooltip("Seconds of payment animation at the cash window.")]
        [SerializeField] float cashPaymentSeconds = 2.5f;

        public bool PlayerHasFastag { get; set; } = true;

        EconomyService _economy;
        TollLane _activeLane;
        TruckController _truck;
        float _cashTimer;
        bool  _barrierOpen;
        bool  _charged;

        void Awake() => _economy = ServiceLocator.Get<EconomyService>();

        /// <summary>Called by a lane's approach trigger.</summary>
        public void EnterLane(TollLane lane, TruckController truck)
        {
            if (truck == null) return;

            _activeLane = lane;
            _truck      = truck;
            _charged    = false;
            _cashTimer  = 0f;
        }

        public void ExitLane()
        {
            _truck = null;
            SetBarriers(_activeLane, false);
            _barrierOpen = false;
        }

        void Update()
        {
            if (_truck == null || _charged) return;

            if (_activeLane == TollLane.Fastag)
            {
                // A truck with no tag in the FASTag lane is the frustrating real-world case:
                // it has to stop and pay cash anyway, with no discount.
                if (!PlayerHasFastag) { TickCashLane(); return; }

                Charge(usedFastag: true);
                return;
            }

            TickCashLane();
        }

        void TickCashLane()
        {
            if (_truck.SpeedKmh > stopSpeedKmh)
            {
                _cashTimer = 0f;
                return;
            }

            _cashTimer += Time.deltaTime;
            if (_cashTimer >= cashPaymentSeconds) Charge(usedFastag: false);
        }

        void Charge(bool usedFastag)
        {
            int amount = _economy.TollCost(coveredKm, usedFastag);

            // Tolls are not optional, so they can take the balance negative.
            _economy.ForceDebit(amount, "toll:" + plazaId);
            _charged = true;

            SetBarriers(_activeLane, true);
            _barrierOpen = true;

            EventBus.Publish(new TollPaidEvent
            {
                PlazaId     = plazaId,
                Amount      = amount,
                UsedFastag  = usedFastag
            });
        }

        void SetBarriers(TollLane lane, bool open)
        {
            var barriers = lane == TollLane.Fastag ? fastagBarriers : cashBarriers;
            foreach (var animator in barriers)
                if (animator != null) animator.SetBool(barrierOpenParam, open);
        }

        public bool BarrierOpen => _barrierOpen;
    }
}
