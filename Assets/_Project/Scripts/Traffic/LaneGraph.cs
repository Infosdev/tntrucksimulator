using System.Collections.Generic;
using UnityEngine;

namespace TNLorry.Traffic
{
    /// <summary>
    /// Lane-level waypoint graph for AI traffic (section M.2). Separate from the routing
    /// RoadGraph: that one is coarse (intersection to intersection) and answers "which way to
    /// Madurai"; this one is fine-grained per lane and answers "where exactly do I drive".
    ///
    /// Authored per tile, so a tile can register and unregister its own lanes as it streams.
    /// </summary>
    public class LaneGraph : MonoBehaviour
    {
        public static LaneGraph Instance { get; private set; }

        [System.Serializable]
        public class Lane
        {
            public int id;
            [Tooltip("Ordered waypoints along the lane centre, in the direction of travel.")]
            public Transform[] waypoints = new Transform[0];
            public float speedLimitKmh = 60f;
            [Tooltip("Lanes this one feeds into at its end. Multiple = a junction choice.")]
            public int[] nextLaneIds = new int[0];
            [Tooltip("Lane to the right in the same direction, for overtaking. -1 = none.")]
            public int rightLaneId = -1;
            [Tooltip("Lane to the left in the same direction. -1 = none.")]
            public int leftLaneId = -1;
            [Tooltip("Oncoming lane, used for the overtake-into-oncoming behaviour on 2-lane roads.")]
            public int oncomingLaneId = -1;
            [Tooltip("Signal controlling the end of this lane. Null = uncontrolled.")]
            public TrafficSignal signal;

            public int WaypointCount => waypoints != null ? waypoints.Length : 0;

            public Vector3 PointAt(int index)
            {
                if (waypoints == null || waypoints.Length == 0) return Vector3.zero;
                index = Mathf.Clamp(index, 0, waypoints.Length - 1);
                return waypoints[index] != null ? waypoints[index].position : Vector3.zero;
            }

            /// <summary>Total lane length in metres, computed lazily.</summary>
            public float Length
            {
                get
                {
                    if (_length > 0f) return _length;
                    for (int i = 1; i < WaypointCount; i++)
                        _length += Vector3.Distance(PointAt(i - 1), PointAt(i));
                    return _length;
                }
            }
            float _length;
        }

        [Tooltip("Lanes authored directly on this object. Tiles register theirs via LaneSet instead.")]
        [SerializeField] Lane[] lanes = new Lane[0];

        readonly Dictionary<int, Lane> _byId = new Dictionary<int, Lane>();
        /// <summary>lane id -> vehicles currently on it, ordered front-most last.</summary>
        readonly Dictionary<int, List<AIVehicle>> _occupants = new Dictionary<int, List<AIVehicle>>();
        /// <summary>Flat view of every registered lane, rebuilt on add/remove for cheap iteration.</summary>
        readonly List<Lane> _allLanes = new List<Lane>(64);

        void Awake()
        {
            if (Instance != null && Instance != this)
                Debug.LogWarning("[LaneGraph] A second LaneGraph is being used; the newest wins.");
            Instance = this;

            AddLanes(lanes, this);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>
        /// Registers a tile's lanes as it streams in (section K.10). Called by LaneSet.
        ///
        /// Lane ids must be globally unique across tiles — the map tool allocates them per tile
        /// (e.g. tileIndex * 1000 + localIndex), because an AI vehicle crossing a tile boundary
        /// follows `nextLaneIds` into a lane that belongs to the neighbouring tile.
        /// </summary>
        public void AddLanes(Lane[] toAdd, Object owner)
        {
            if (toAdd == null) return;

            foreach (var lane in toAdd)
            {
                if (lane == null) continue;

                if (lane.WaypointCount < 2)
                {
                    Debug.LogError("[LaneGraph] Lane " + lane.id + " needs at least 2 waypoints.", owner);
                    continue;
                }
                if (_byId.ContainsKey(lane.id))
                {
                    Debug.LogError("[LaneGraph] Duplicate lane id " + lane.id +
                                   " — tile lane ids must be globally unique.", owner);
                    continue;
                }

                _byId[lane.id] = lane;
                _occupants[lane.id] = new List<AIVehicle>(8);
                _allLanes.Add(lane);
            }
        }

        /// <summary>
        /// Unregisters a tile's lanes as it streams out. Any vehicle still on one of them is
        /// despawned rather than left following waypoints that are about to be destroyed —
        /// a null-waypoint lane would otherwise drive it to the world origin.
        /// </summary>
        public void RemoveLanes(Lane[] toRemove)
        {
            if (toRemove == null) return;

            foreach (var lane in toRemove)
            {
                if (lane == null || !_byId.ContainsKey(lane.id)) continue;

                if (_occupants.TryGetValue(lane.id, out var occupants))
                {
                    // Iterate backwards: Despawn unregisters, which mutates this list.
                    for (int i = occupants.Count - 1; i >= 0; i--)
                        if (occupants[i] != null) TrafficManager.Instance?.Despawn(occupants[i]);
                }

                _byId.Remove(lane.id);
                _occupants.Remove(lane.id);
                _allLanes.Remove(lane);
            }
        }

        public Lane GetLane(int id) => _byId.TryGetValue(id, out var lane) ? lane : null;

        public IReadOnlyList<Lane> AllLanes => _allLanes;

        // ── Occupancy, used by IDM to find the vehicle ahead ────────────────

        public void Register(AIVehicle vehicle, int laneId)
        {
            if (!_occupants.TryGetValue(laneId, out var list)) return;
            if (!list.Contains(vehicle)) list.Add(vehicle);
        }

        public void Unregister(AIVehicle vehicle, int laneId)
        {
            if (_occupants.TryGetValue(laneId, out var list)) list.Remove(vehicle);
        }

        /// <summary>
        /// Nearest vehicle ahead on the same lane, and the gap to it in metres.
        /// Linear over the lane's occupants — a lane holds a handful of cars, not hundreds.
        /// </summary>
        public bool TryGetLeader(AIVehicle self, int laneId, out AIVehicle leader, out float gapMetres)
        {
            leader = null;
            gapMetres = float.MaxValue;

            if (!_occupants.TryGetValue(laneId, out var list)) return false;

            foreach (var other in list)
            {
                if (other == null || other == self) continue;

                float ahead = other.DistanceAlongLane - self.DistanceAlongLane;
                if (ahead <= 0f) continue;

                if (ahead < gapMetres)
                {
                    gapMetres = ahead;
                    leader = other;
                }
            }

            // Subtract body lengths so the gap is bumper-to-bumper, not centre-to-centre.
            if (leader != null) gapMetres = Mathf.Max(0.1f, gapMetres - (leader.BodyLength + self.BodyLength) * 0.5f);
            return leader != null;
        }

        /// <summary>
        /// True when nothing on <paramref name="laneId"/> is close enough ahead of
        /// <paramref name="position"/> to matter within <paramref name="requiredMetres"/>.
        ///
        /// Deliberately world-space rather than distance-along-lane: the lane being checked is
        /// usually the *oncoming* one, whose distance axis runs the other way, so comparing its
        /// occupants' DistanceAlongLane against ours would be meaningless.
        /// </summary>
        public bool IsLaneClearAhead(int laneId, Vector3 position, Vector3 forward, float requiredMetres)
        {
            if (laneId < 0) return false;                       // no such lane: never "clear"
            if (!_occupants.TryGetValue(laneId, out var list)) return true;

            float requiredSqr = requiredMetres * requiredMetres;

            foreach (var other in list)
            {
                if (other == null) continue;

                var toOther = other.transform.position - position;
                if (Vector3.Dot(toOther, forward) <= 0f) continue;   // behind us, irrelevant
                if (toOther.sqrMagnitude < requiredSqr) return false;
            }
            return true;
        }

        /// <summary>
        /// Lane-local right vector at a waypoint, for applying an overtaking lateral offset.
        /// On a left-hand-drive road network, overtaking means offsetting to the right.
        /// </summary>
        public static Vector3 SegmentRight(Lane lane, int waypointIndex)
        {
            if (lane == null || lane.WaypointCount < 2) return Vector3.right;

            int to = Mathf.Clamp(waypointIndex, 1, lane.WaypointCount - 1);
            var direction = lane.PointAt(to) - lane.PointAt(to - 1);
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.0001f) return Vector3.right;
            return Vector3.Cross(Vector3.up, direction.normalized);
        }

        /// <summary>Picks the next lane at a junction. Random, weighted toward going straight.</summary>
        public Lane PickNextLane(Lane current)
        {
            if (current?.nextLaneIds == null || current.nextLaneIds.Length == 0) return null;
            if (current.nextLaneIds.Length == 1) return GetLane(current.nextLaneIds[0]);

            var exitDirection = (current.PointAt(current.WaypointCount - 1) -
                                 current.PointAt(current.WaypointCount - 2)).normalized;

            Lane best = null;
            float bestScore = float.MinValue;

            foreach (int id in current.nextLaneIds)
            {
                var candidate = GetLane(id);
                if (candidate == null || candidate.WaypointCount < 2) continue;

                var entryDirection = (candidate.PointAt(1) - candidate.PointAt(0)).normalized;
                // Straighter continuations score higher, plus noise so traffic still turns off.
                float score = Vector3.Dot(exitDirection, entryDirection) + Random.Range(0f, 0.6f);

                if (score > bestScore) { bestScore = score; best = candidate; }
            }
            return best;
        }

        void OnDrawGizmosSelected()
        {
            if (lanes == null) return;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);

            foreach (var lane in lanes)
            {
                if (lane?.waypoints == null) continue;
                for (int i = 1; i < lane.waypoints.Length; i++)
                {
                    if (lane.waypoints[i - 1] == null || lane.waypoints[i] == null) continue;
                    Gizmos.DrawLine(lane.waypoints[i - 1].position, lane.waypoints[i].position);
                }
            }
        }
    }
}
