using TNLorry.Core;
using TNLorry.Data;
using TNLorry.Vehicle;
using UnityEngine;

namespace TNLorry.Gameplay
{
    /// <summary>
    /// The delivery dock and the parking mini-game (sections C, G). Completing a job means
    /// getting the trailer inside the bay volume, square enough, and stopped.
    ///
    /// Alignment is scored rather than pass/fail, and the tolerance is generous — reverse
    /// parking a trailer on a phone screen is hard enough without millimetre judging.
    /// </summary>
    public class DeliveryBay : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Must match the destination CityData.id for a job to be completable here.")]
        [SerializeField] string cityId = "";
        [Tooltip("Leave empty to accept any cargo; otherwise restrict to these cargo ids.")]
        [SerializeField] string[] acceptedCargoIds = new string[0];

        [Header("Parking tolerance")]
        [Tooltip("Degrees of yaw error still counted as parked.")]
        [SerializeField] float maxYawErrorDegrees = 20f;
        [Tooltip("Metres of lateral offset from the bay centre still counted as parked.")]
        [SerializeField] float maxLateralOffset = 1.8f;
        [Tooltip("Speed below which the truck counts as stopped, in km/h.")]
        [SerializeField] float stopSpeedKmh = 1.5f;
        [Tooltip("Seconds the truck must hold position before the delivery resolves.")]
        [SerializeField] float holdSeconds = 1.2f;

        [Header("Scene refs")]
        [Tooltip("Trigger volume the trailer (or rigid body bed) must be inside.")]
        [SerializeField] Collider bayVolume;
        [Tooltip("Marker pointing down the bay; its forward is the correct parking direction.")]
        [SerializeField] Transform bayCentre;

        /// <summary>0..1 parking quality, surfaced on the result screen.</summary>
        public float LastParkingScore { get; private set; }
        public bool  TruckInBay { get; private set; }
        public bool  IsAligned  { get; private set; }

        /// <summary>Raised when the delivery resolves, with the itemised payout.</summary>
        public event System.Action<EconomyService.PayoutBreakdown> OnDelivered;

        JobBoardService _jobs;
        ProgressionService _progression;
        DailyService _daily;
        GameClock _clock;

        TruckController _truck;
        FuelSystem _fuel;
        DamageSystem _damage;
        TrafficRuleMonitor _rules;
        TrailerCoupling _coupling;

        float _holdTimer;
        bool  _resolved;

        void Awake()
        {
            _jobs        = ServiceLocator.Get<JobBoardService>();
            _progression = ServiceLocator.Get<ProgressionService>();
            _clock       = FindFirstObjectByType<GameClock>();

            // Optional: Quick Job and the tutorial run without daily missions registered.
            ServiceLocator.TryGet(out _daily);

            if (bayCentre == null) bayCentre = transform;
            if (bayVolume == null)
            {
                Debug.LogError("[DeliveryBay] " + name + " has no bay volume collider.", this);
                enabled = false;
                return;
            }
            bayVolume.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            var truck = other.GetComponentInParent<TruckController>();
            if (truck == null) return;

            _truck     = truck;
            _fuel      = truck.GetComponent<FuelSystem>();
            _damage    = truck.GetComponent<DamageSystem>();
            _rules     = truck.GetComponentInChildren<TrafficRuleMonitor>();
            _coupling  = truck.GetComponent<TrailerCoupling>();
            TruckInBay = true;
            _holdTimer = 0f;
        }

        void OnTriggerExit(Collider other)
        {
            if (other.GetComponentInParent<TruckController>() != _truck) return;
            TruckInBay = false;
            _holdTimer = 0f;
            IsAligned  = false;
        }

        void Update()
        {
            if (!TruckInBay || _truck == null || _resolved) return;

            var job = _jobs.ActiveJob;
            if (!IsEligible(job)) return;

            // Score the trailer when there is one; a rigid lorry is judged on its own body.
            var parked = _coupling != null && _coupling.IsCoupled
                ? _coupling.AttachedTrailer.transform
                : _truck.transform;

            float yawError = Mathf.Abs(Vector3.SignedAngle(bayCentre.forward, parked.forward, Vector3.up));
            // Mirror-symmetric: backing in and driving in both count.
            yawError = Mathf.Min(yawError, 180f - yawError);

            var offset = parked.position - bayCentre.position;
            float lateral = Mathf.Abs(Vector3.Dot(offset, bayCentre.right));

            IsAligned = yawError <= maxYawErrorDegrees && lateral <= maxLateralOffset;

            bool stopped = _truck.SpeedKmh <= stopSpeedKmh;

            if (IsAligned && stopped)
            {
                _holdTimer += Time.deltaTime;
                if (_holdTimer >= holdSeconds)
                {
                    LastParkingScore = ScoreParking(yawError, lateral);
                    Resolve(job);
                }
            }
            else
            {
                _holdTimer = 0f;
            }
        }

        bool IsEligible(Job job)
        {
            if (job == null || !job.CargoLoaded) return false;
            if (job.DestinationCity == null || job.DestinationCity.id != cityId) return false;

            if (acceptedCargoIds.Length > 0 &&
                System.Array.IndexOf(acceptedCargoIds, job.Cargo.id) < 0) return false;

            return true;
        }

        float ScoreParking(float yawError, float lateral)
        {
            float yawScore     = 1f - Mathf.Clamp01(yawError / Mathf.Max(1f, maxYawErrorDegrees));
            float lateralScore = 1f - Mathf.Clamp01(lateral / Mathf.Max(0.1f, maxLateralOffset));
            return (yawScore + lateralScore) * 0.5f;
        }

        void Resolve(Job job)
        {
            _resolved = true;

            var report = new DeliveryReport
            {
                ElapsedMinutes   = _clock != null ? _clock.JobElapsedMinutes : job.ElapsedMinutes,
                CargoDamage01    = _damage != null ? _damage.CargoDamage : job.CargoDamage01,
                FuelEfficient    = _fuel != null && _fuel.WasEfficient(),
                ViolationCount   = _rules != null ? _rules.ViolationCount : 0,
                FinesIncurred    = _rules != null ? _rules.TotalFinesThisJob : 0,
                DistanceDrivenKm = _fuel != null ? _fuel.KmDrivenThisJob : job.DistanceKm
            };

            var breakdown = _jobs.Complete(report, _progression, _daily);

            // Unload and reset the per-job counters so the next job starts clean.
            if (_coupling != null && _coupling.IsCoupled) _coupling.AttachedTrailer.UnloadCargo();
            _damage?.ResetCargoDamage(CargoFlags.None);
            _fuel?.ResetJobCounters();
            _rules?.ResetJobCounters();
            _clock?.ResetJobTimer();

            OnDelivered?.Invoke(breakdown);
            _resolved = false;
        }

        void OnDrawGizmosSelected()
        {
            var centre = bayCentre != null ? bayCentre : transform;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(centre.position, centre.position + centre.forward * 12f);
            Gizmos.DrawWireSphere(centre.position, maxLateralOffset);
        }
    }
}
