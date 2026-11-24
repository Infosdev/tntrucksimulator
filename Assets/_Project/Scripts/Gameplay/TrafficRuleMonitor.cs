using System.Collections.Generic;
using TNLorry.Core;
using TNLorry.Data;
using TNLorry.Navigation;
using TNLorry.Vehicle;
using UnityEngine;

namespace TNLorry.Gameplay
{
    /// <summary>
    /// Detects violations and issues fines (section K.7).
    ///
    /// Tolerant by design: overspeed is only caught at speed cameras with a margin, not
    /// continuously, and each violation type has a cooldown. A sim that fines you four times
    /// for one mistake reads as broken, and competitor 1-star reviews say exactly that.
    /// </summary>
    public class TrafficRuleMonitor : MonoBehaviour
    {
        /// <summary>Tags the collision check looks for. Declare both in Project Settings → Tags.</summary>
        public const string PedestrianTag = "Pedestrian";
        public const string AIVehicleTag  = "AIVehicle";

        [SerializeField] EconomyConfig config;
        [SerializeField] TruckController truck;
        [SerializeField] TruckLights lights;

        [Header("Tolerances")]
        [Tooltip("km/h over the posted limit before a camera issues a fine.")]
        [SerializeField] float overspeedToleranceKmh = 8f;
        [Tooltip("Seconds before the same violation type can fire again.")]
        [SerializeField] float violationCooldown = 6f;
        [Tooltip("Seconds on the wrong side of the road before it counts.")]
        [SerializeField] float wrongSideGraceSeconds = 2.5f;
        [Tooltip("Minimum speed for a collision to be finable - nudging in a yard is not an offence.")]
        [SerializeField] float collisionMinSpeedKmh = 12f;

        public int ViolationCount { get; private set; }
        public int TotalFinesThisJob { get; private set; }
        /// <summary>Posted limit on the road the truck is currently on, for the HUD sign.</summary>
        public float CurrentSpeedLimitKmh { get; private set; } = 80f;

        EconomyService _economy;
        ProgressionService _progression;
        RoadGraph _roads;
        GameClock _clock;

        readonly Dictionary<ViolationType, float> _lastFined = new Dictionary<ViolationType, float>();
        float _wrongSideTimer;

        void Awake()
        {
            _economy     = ServiceLocator.Get<EconomyService>();
            _progression = ServiceLocator.Get<ProgressionService>();
            _clock       = FindFirstObjectByType<GameClock>();
            ServiceLocator.TryGet<RoadGraph>(out _roads);
        }

        void Update()
        {
            if (truck == null) return;

            UpdateCurrentRoad();
            CheckWrongSide(Time.deltaTime);
            CheckHeadlights();
        }

        void UpdateCurrentRoad()
        {
            if (_roads == null) return;

            if (_roads.TryFindNearestEdge(truck.transform.position, 25f, out int edgeIndex, out _, out var direction))
            {
                var edge = _roads.GetEdge(edgeIndex);
                CurrentSpeedLimitKmh = edge.SpeedLimitKmh;
                _currentRoadDirection = direction;
                _onKnownRoad = true;
            }
            else
            {
                _onKnownRoad = false;
            }
        }

        Vector3 _currentRoadDirection;
        bool _onKnownRoad;

        /// <summary>
        /// Called by a SpeedCamera trigger on the road, not polled — this is what makes
        /// overspeeding a risk at specific places rather than a constant tax.
        /// </summary>
        public void OnSpeedCameraPassed(float postedLimitKmh)
        {
            float limit = postedLimitKmh > 0f ? postedLimitKmh : CurrentSpeedLimitKmh;
            if (truck.SpeedKmh <= limit + overspeedToleranceKmh) return;

            Issue(ViolationType.Overspeed, config.fineOverspeed);
        }

        /// <summary>Called by a TrafficSignal's stop-line trigger while the signal is red.</summary>
        public void OnRedLightCrossed()
        {
            if (truck.SpeedKmh < 3f) return;      // creeping at the line is not running it
            Issue(ViolationType.RedLight, config.fineRedLight);
        }

        void CheckWrongSide(float dt)
        {
            // India drives on the left: the truck's heading should agree with the direction of
            // the nearest directed road edge. Disagreement for more than a couple of seconds
            // means genuinely driving into oncoming traffic, not a lane change or a U-turn.
            if (!_onKnownRoad || truck.SpeedKmh < 15f)
            {
                _wrongSideTimer = 0f;
                return;
            }

            float alignment = Vector3.Dot(truck.transform.forward, _currentRoadDirection);
            if (alignment < -0.5f)
            {
                _wrongSideTimer += dt;
                if (_wrongSideTimer >= wrongSideGraceSeconds)
                {
                    _wrongSideTimer = 0f;
                    Issue(ViolationType.WrongSide, config.fineWrongSide);
                }
            }
            else
            {
                _wrongSideTimer = 0f;
            }
        }

        void CheckHeadlights()
        {
            if (lights == null || _clock == null) return;
            if (truck.SpeedKmh < 10f) return;

            bool requiresLights = _clock.IsNight || _clock.IsDusk || _rainIntensity > 0.4f;
            if (requiresLights && !lights.AnyHeadlightOn)
                Issue(ViolationType.NoHeadlights, config.fineNoHeadlights);
        }

        float _rainIntensity;
        void OnEnable()
        {
            EventBus.Subscribe<WeatherChangedEvent>(OnWeatherChanged);
        }
        void OnDisable()
        {
            EventBus.Unsubscribe<WeatherChangedEvent>(OnWeatherChanged);
        }

        void OnWeatherChanged(WeatherChangedEvent e)
            => _rainIntensity = e.Type == WeatherType.Rain || e.Type == WeatherType.HeavyRain ? e.Intensity : 0f;

        /// <summary>Called from the truck's collision handling (DamageSystem reports the impact).</summary>
        public void ReportCollision(GameObject other, float speedKmh)
        {
            if (speedKmh < collisionMinSpeedKmh) return;

            // String comparison rather than CompareTag: CompareTag throws if the tag has not
            // been declared in the project, and a missing tag must not crash a collision.
            string tag = other.tag;

            if (tag == PedestrianTag)
            {
                Issue(ViolationType.PedestrianCollision, config.finePedestrianCollision,
                      -config.pedestrianReputationHit);
            }
            else if (tag == AIVehicleTag || other.GetComponentInParent<Traffic.AIVehicle>() != null)
            {
                Issue(ViolationType.VehicleCollision, config.fineVehicleCollision, -1);
            }
        }

        /// <summary>Weighbridge check-post result (Phase 2, section K.7).</summary>
        public void ReportOverload(float excessKg)
        {
            if (excessKg <= 0f) return;
            int fine = config.fineOverload + Mathf.RoundToInt(excessKg / 1000f * 500f);
            Issue(ViolationType.Overload, fine);
        }

        void Issue(ViolationType violation, int amount, int reputationDelta = 0)
        {
            if (_lastFined.TryGetValue(violation, out var last) && Time.time - last < violationCooldown) return;
            _lastFined[violation] = Time.time;

            ViolationCount++;
            TotalFinesThisJob += amount;

            _economy.ForceDebit(amount, "fine:" + violation);
            if (reputationDelta != 0) _progression.AdjustReputation(reputationDelta);

            if (ServiceLocator.TryGet<SaveSystem>(out var save))
            {
                save.Data.totalFinesPaid += amount;
                save.MarkDirty();
            }

            EventBus.Publish(new FineIssuedEvent
            {
                Violation       = violation,
                Amount          = amount,
                ReputationDelta = reputationDelta
            });
        }

        public void ResetJobCounters()
        {
            ViolationCount = 0;
            TotalFinesThisJob = 0;
            _lastFined.Clear();
            _wrongSideTimer = 0f;
        }
    }
}
