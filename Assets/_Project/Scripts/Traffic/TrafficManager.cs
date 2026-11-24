using System.Collections.Generic;
using TNLorry.Core;
using TNLorry.Gameplay;
using TNLorry.Services;
using UnityEngine;

namespace TNLorry.Traffic
{
    /// <summary>
    /// Spawns, pools and culls AI traffic (section M.2). Density scales with device tier, time
    /// of day and area, because a 3 GB phone cannot afford Chennai rush hour.
    ///
    /// Pooled per vehicle class: instantiating a bus prefab mid-drive causes a visible hitch,
    /// and the mix needs to stay Tamil Nadu-shaped (lots of two-wheelers) rather than drifting
    /// toward whatever happened to be spawned last.
    /// </summary>
    public class TrafficManager : MonoBehaviour
    {
        public static TrafficManager Instance { get; private set; }

        [System.Serializable]
        public class VehicleEntry
        {
            public AIVehicleClass vehicleClass;
            public AIVehicle prefab;
            [Tooltip("Relative share of traffic. Two-wheelers should dominate (section M.1).")]
            [Range(0f, 20f)] public float weight = 1f;
            [Tooltip("Pool size. Should be at least weight/totalWeight * maxActive, plus slack.")]
            public int poolSize = 6;
            [Tooltip("Rural-only vehicles: bullock carts, cattle. Suppressed inside cities.")]
            public bool ruralOnly;
            [Tooltip("City-only: share autos, city buses.")]
            public bool urbanOnly;
        }

        [Header("Mix (section M.1)")]
        [SerializeField] VehicleEntry[] vehicles = new VehicleEntry[0];

        [Header("Density")]
        [Tooltip("Active AI vehicles on the High tier. Low/Mid are scaled down automatically.")]
        [SerializeField] int maxActiveHighTier = 60;
        [Tooltip("Spawn ring radius from the player, in metres. Spawn outside the view.")]
        [SerializeField] float spawnRadius = 120f;
        [Tooltip("Despawn beyond this distance.")]
        [SerializeField] float despawnRadius = 180f;
        [Tooltip("Seconds between spawn attempts.")]
        [SerializeField] float spawnInterval = 0.4f;

        [Header("Time-of-day multipliers")]
        [Tooltip("Density multiplier at night (22:00-05:00). Highways stay busy with lorries.")]
        [SerializeField] float nightMultiplier = 0.45f;
        [Tooltip("Density multiplier during the morning and evening peaks.")]
        [SerializeField] float peakMultiplier = 1.3f;

        [Header("Refs")]
        [SerializeField] Transform player;
        [SerializeField] LaneGraph laneGraph;

        public int ActiveCount => _active.Count;
        public int MaxActive { get; private set; }

        readonly Dictionary<AIVehicleClass, Queue<AIVehicle>> _pools =
            new Dictionary<AIVehicleClass, Queue<AIVehicle>>();
        readonly List<AIVehicle> _active = new List<AIVehicle>(64);
        readonly List<AIVehicle> _despawnBuffer = new List<AIVehicle>(16);

        GameClock _clock;
        float _spawnTimer;
        float _totalWeight;

        void Awake()
        {
            Instance = this;
            _clock = FindFirstObjectByType<GameClock>();

            if (player == null || laneGraph == null)
            {
                Debug.LogError("[TrafficManager] Player and LaneGraph must be assigned; disabling.", this);
                enabled = false;
                return;
            }

            MaxActive = ResolveMaxActive();
            BuildPools();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        int ResolveMaxActive()
        {
            if (!ServiceLocator.TryGet<QualityService>(out var quality)) return maxActiveHighTier / 2;

            return quality.ActiveTier switch
            {
                DeviceTierLevel.Low => Mathf.RoundToInt(maxActiveHighTier * 0.33f),   // ~20
                DeviceTierLevel.Mid => Mathf.RoundToInt(maxActiveHighTier * 0.6f),    // ~36
                _                   => maxActiveHighTier
            };
        }

        void BuildPools()
        {
            _totalWeight = 0f;

            foreach (var entry in vehicles)
            {
                if (entry?.prefab == null)
                {
                    Debug.LogError("[TrafficManager] A vehicle entry has no prefab assigned.", this);
                    continue;
                }

                _totalWeight += entry.weight;

                // Pool sizes scale with the tier too, or a low-end device pays the memory for
                // vehicles it will never be allowed to show.
                int size = Mathf.Max(1, Mathf.RoundToInt(entry.poolSize * MaxActive / (float)maxActiveHighTier));
                var queue = new Queue<AIVehicle>(size);

                for (int i = 0; i < size; i++)
                {
                    var instance = Instantiate(entry.prefab, transform);
                    instance.gameObject.SetActive(false);
                    queue.Enqueue(instance);
                }
                _pools[entry.vehicleClass] = queue;
            }

            if (_totalWeight <= 0f)
                Debug.LogError("[TrafficManager] Total vehicle weight is zero; no traffic will spawn.", this);
        }

        void Update()
        {
            CullDistant();

            _spawnTimer += Time.deltaTime;
            if (_spawnTimer < spawnInterval) return;
            _spawnTimer = 0f;

            int target = Mathf.RoundToInt(MaxActive * DensityMultiplier());
            if (_active.Count < target) TrySpawnOne();
        }

        float DensityMultiplier()
        {
            if (_clock == null) return 1f;

            float hour = _clock.HourOfDay;

            if (hour >= 22f || hour < 5f) return nightMultiplier;
            if ((hour >= 8f && hour < 11f) || (hour >= 17f && hour < 20f)) return peakMultiplier;
            return 1f;
        }

        void TrySpawnOne()
        {
            var lane = PickSpawnLane();
            if (lane == null) return;

            var entry = PickVehicleEntry();
            if (entry == null) return;

            if (!_pools.TryGetValue(entry.vehicleClass, out var pool) || pool.Count == 0) return;

            // Spawn at a point on the lane that is inside the ring but outside the near field,
            // so vehicles never pop into existence in front of the player.
            float distanceAlong = Random.Range(0f, Mathf.Max(1f, lane.Length));
            var candidatePosition = SampleLanePosition(lane, distanceAlong);
            float distanceToPlayer = Vector3.Distance(candidatePosition, player.position);

            if (distanceToPlayer < spawnRadius * 0.6f || distanceToPlayer > spawnRadius) return;
            if (IsPositionOccupied(candidatePosition, entry.prefab.BodyLength * 1.5f)) return;

            var vehicle = pool.Dequeue();
            vehicle.gameObject.SetActive(true);
            vehicle.Initialise(laneGraph, lane, distanceAlong, player);
            _active.Add(vehicle);
        }

        LaneGraph.Lane PickSpawnLane()
        {
            var lanes = laneGraph.AllLanes;
            if (lanes.Count == 0) return null;

            // A few random tries rather than scoring every lane: lane counts get large once
            // several tiles are streamed in, and this runs multiple times a second.
            for (int attempt = 0; attempt < 6; attempt++)
            {
                var lane = lanes[Random.Range(0, lanes.Count)];
                if (lane == null || lane.WaypointCount < 2) continue;

                float distance = Vector3.Distance(lane.PointAt(0), player.position);
                if (distance <= spawnRadius * 1.5f) return lane;
            }
            return null;
        }

        VehicleEntry PickVehicleEntry()
        {
            if (_totalWeight <= 0f) return null;

            bool urban = IsPlayerInUrbanArea();

            float roll = Random.value * _totalWeight;
            float cumulative = 0f;

            foreach (var entry in vehicles)
            {
                if (entry?.prefab == null) continue;
                if (entry.ruralOnly && urban) continue;
                if (entry.urbanOnly && !urban) continue;

                cumulative += entry.weight;
                if (roll <= cumulative) return entry;
            }

            // Weighted roll can fall through when entries were filtered out; take any legal one.
            foreach (var entry in vehicles)
            {
                if (entry?.prefab == null) continue;
                if (entry.ruralOnly && urban) continue;
                if (entry.urbanOnly && !urban) continue;
                return entry;
            }
            return null;
        }

        bool IsPlayerInUrbanArea()
        {
            // Urban zones are tagged trigger volumes placed around each depot city; absent one,
            // assume highway, which is the common case.
            return Physics.CheckSphere(player.position, 1f, LayerMask.GetMask("UrbanZone"),
                                       QueryTriggerInteraction.Collide);
        }

        static Vector3 SampleLanePosition(LaneGraph.Lane lane, float distanceAlong)
        {
            float travelled = 0f;
            for (int i = 1; i < lane.WaypointCount; i++)
            {
                float segment = Vector3.Distance(lane.PointAt(i - 1), lane.PointAt(i));
                if (travelled + segment >= distanceAlong)
                {
                    float t = segment <= 0.01f ? 0f : (distanceAlong - travelled) / segment;
                    return Vector3.Lerp(lane.PointAt(i - 1), lane.PointAt(i), t);
                }
                travelled += segment;
            }
            return lane.PointAt(lane.WaypointCount - 1);
        }

        bool IsPositionOccupied(Vector3 position, float radius)
        {
            float sqrRadius = radius * radius;
            foreach (var vehicle in _active)
                if (vehicle != null && (vehicle.transform.position - position).sqrMagnitude < sqrRadius)
                    return true;
            return false;
        }

        void CullDistant()
        {
            _despawnBuffer.Clear();
            float sqrDespawn = despawnRadius * despawnRadius;

            foreach (var vehicle in _active)
            {
                if (vehicle == null) { _despawnBuffer.Add(vehicle); continue; }

                // Only despawn behind the player, never ahead — a vehicle vanishing from the
                // windscreen is the most noticeable possible bug.
                var toVehicle = vehicle.transform.position - player.position;
                bool behind = Vector3.Dot(toVehicle, player.forward) < 0f;

                if (toVehicle.sqrMagnitude > sqrDespawn && behind) _despawnBuffer.Add(vehicle);
            }

            foreach (var vehicle in _despawnBuffer) Despawn(vehicle);
        }

        public void Despawn(AIVehicle vehicle)
        {
            if (vehicle == null)
            {
                _active.RemoveAll(v => v == null);
                return;
            }

            _active.Remove(vehicle);
            vehicle.OnDespawn();
            vehicle.gameObject.SetActive(false);

            if (_pools.TryGetValue(vehicle.VehicleClass, out var pool)) pool.Enqueue(vehicle);
            else Destroy(vehicle.gameObject);     // prefab set changed at runtime; do not leak
        }

        /// <summary>Clears all traffic, e.g. before a cutscene or when fast-travelling.</summary>
        public void DespawnAll()
        {
            for (int i = _active.Count - 1; i >= 0; i--) Despawn(_active[i]);
        }
    }
}
