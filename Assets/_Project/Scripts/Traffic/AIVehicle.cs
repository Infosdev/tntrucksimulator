using TNLorry.Core;
using UnityEngine;

namespace TNLorry.Traffic
{
    public enum AIVehicleClass
    {
        TwoWheeler, Auto, ShareAuto, Car, GovtBus, OmniBus, MiniLorry, Lorry, Tractor, BullockCart, Cattle
    }

    /// <summary>
    /// One AI road user, driven kinematically along a lane with IDM car-following (section M.2).
    ///
    /// Kinematic, not physics: 40 Rigidbody vehicles with WheelColliders would eat the whole
    /// frame budget on a 3 GB phone. A vehicle only switches to physics when the player gets
    /// close enough that a collision is plausible — see SetPhysicsActive.
    /// </summary>
    public class AIVehicle : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] AIVehicleClass vehicleClass = AIVehicleClass.Car;
        [Tooltip("Bumper-to-bumper length in metres; used for gap maths.")]
        [SerializeField] float bodyLength = 4.2f;

        [Header("IDM parameters (section M.2)")]
        [Tooltip("Comfortable acceleration, m/s^2.")]
        [SerializeField] float maxAcceleration = 1.2f;
        [Tooltip("Comfortable deceleration, m/s^2.")]
        [SerializeField] float comfortDeceleration = 2.0f;
        [Tooltip("Minimum standstill gap, metres.")]
        [SerializeField] float minGap = 3f;
        [Tooltip("Desired time headway, seconds. Lower = more aggressive, more Indian.")]
        [SerializeField] float timeHeadway = 1.5f;
        [Tooltip("Emergency braking limit, m/s^2. Used when a gap closes faster than comfort allows.")]
        [SerializeField] float emergencyDeceleration = 7f;

        [Header("Behaviour")]
        [Tooltip("Fraction of the lane speed limit this driver aims for. 1.1 = habitual speeder.")]
        [SerializeField] float speedPreference = 1f;
        [Tooltip("Lateral steering response toward the lane centre line.")]
        [SerializeField] float steerSharpness = 3.5f;
        [Tooltip("Chance per second of honking when blocked. Set high for autos.")]
        [SerializeField] float honkChancePerSecond = 0.15f;

        [Header("Overtaking (section M.2 — two-lane roads)")]
        [SerializeField] bool canOvertake = true;
        [Tooltip("Seconds stuck behind a slow leader before pulling out. Low = impatient.")]
        [SerializeField] float patienceSeconds = 4f;
        [Tooltip("Leader must be slower than this fraction of our desired speed to be worth passing.")]
        [Range(0.3f, 1f)] [SerializeField] float overtakeSpeedRatio = 0.8f;
        [Tooltip("Lateral offset while passing, in metres. Roughly one lane width.")]
        [SerializeField] float overtakeOffset = 3.2f;
        [Tooltip("Speed multiplier while committed to a pass.")]
        [SerializeField] float overtakeSpeedBonus = 1.25f;
        [Tooltip("Seconds of closing headroom required on the oncoming lane before pulling out.")]
        [SerializeField] float oncomingClearSeconds = 7f;
        [Tooltip("Assumed oncoming closing speed when sizing the gap, in km/h.")]
        [SerializeField] float assumedOncomingKmh = 60f;
        [Tooltip("How fast the lateral offset is taken up and given back, in metres/second.")]
        [SerializeField] float lateralRate = 1.4f;
        [Tooltip("Hard cap on how long a pass may take before it is abandoned, in seconds.")]
        [SerializeField] float overtakeTimeout = 12f;

        [Header("Physics handover")]
        [Tooltip("Distance from the player at which this vehicle becomes a real Rigidbody.")]
        [SerializeField] float physicsActivationDistance = 45f;
        [SerializeField] Rigidbody physicsBody;

        [Header("Refs")]
        [SerializeField] Transform[] wheelMeshes = new Transform[0];
        [SerializeField] AudioSource hornSource;

        public enum OvertakeState { None, PullingOut, Alongside, PullingIn }

        public AIVehicleClass VehicleClass => vehicleClass;
        public float BodyLength => bodyLength;
        public float SpeedMps { get; private set; }
        public OvertakeState Overtaking { get; private set; } = OvertakeState.None;
        /// <summary>Metres travelled along the current lane — the ordering key for leader lookup.</summary>
        public float DistanceAlongLane { get; private set; }
        public int CurrentLaneId { get; private set; } = -1;
        public bool PhysicsActive { get; private set; }

        LaneGraph _graph;
        LaneGraph.Lane _lane;
        int _waypointIndex;
        float _desiredSpeedMps;
        Transform _player;
        float _weatherSpeedFactor = 1f;
        float _honkCooldown;

        // Overtaking state
        float _blockedSeconds;
        float _lateralOffset;
        float _overtakeElapsed;
        AIVehicle _overtakeTarget;

        void Awake()
        {
            if (physicsBody == null) physicsBody = GetComponent<Rigidbody>();
            if (physicsBody != null) physicsBody.isKinematic = true;
        }

        void OnEnable()  => EventBus.Subscribe<WeatherChangedEvent>(OnWeatherChanged);
        void OnDisable()
        {
            EventBus.Unsubscribe<WeatherChangedEvent>(OnWeatherChanged);
            if (_graph != null && CurrentLaneId >= 0) _graph.Unregister(this, CurrentLaneId);
        }

        void OnWeatherChanged(WeatherChangedEvent e)
        {
            // AI slows in rain and fog, which is both realistic and makes bad weather feel
            // different to drive in rather than just look different.
            _weatherSpeedFactor = e.Type switch
            {
                WeatherType.HeavyRain => Mathf.Lerp(1f, 0.65f, e.Intensity),
                WeatherType.Rain      => Mathf.Lerp(1f, 0.8f, e.Intensity),
                WeatherType.Fog       => Mathf.Lerp(1f, 0.6f, e.Intensity),
                _                     => 1f
            };
        }

        /// <summary>Called by TrafficManager when spawning out of the pool.</summary>
        public void Initialise(LaneGraph graph, LaneGraph.Lane lane, float distanceAlongLane, Transform player)
        {
            _graph  = graph;
            _player = player;

            EnterLane(lane, distanceAlongLane);

            SpeedMps = _desiredSpeedMps * Random.Range(0.8f, 1f);
            SetPhysicsActive(false);
        }

        void EnterLane(LaneGraph.Lane lane, float distanceAlongLane)
        {
            if (_graph != null && CurrentLaneId >= 0) _graph.Unregister(this, CurrentLaneId);

            _lane = lane;
            CurrentLaneId = lane != null ? lane.id : -1;
            DistanceAlongLane = distanceAlongLane;
            _waypointIndex = 1;

            if (lane != null)
            {
                _desiredSpeedMps = lane.speedLimitKmh * speedPreference / 3.6f;
                _graph?.Register(this, lane.id);
                SnapToLanePosition();
            }
        }

        void SnapToLanePosition()
        {
            // Walk the waypoint list to the authored distance so a spawn lands on the road
            // rather than at the lane's first point.
            float travelled = 0f;
            for (int i = 1; i < _lane.WaypointCount; i++)
            {
                float segment = Vector3.Distance(_lane.PointAt(i - 1), _lane.PointAt(i));
                if (travelled + segment >= DistanceAlongLane)
                {
                    float t = segment <= 0.01f ? 0f : (DistanceAlongLane - travelled) / segment;
                    transform.position = Vector3.Lerp(_lane.PointAt(i - 1), _lane.PointAt(i), t);
                    transform.rotation = Quaternion.LookRotation(
                        (_lane.PointAt(i) - _lane.PointAt(i - 1)).normalized, Vector3.up);
                    _waypointIndex = i;
                    return;
                }
                travelled += segment;
            }
            _waypointIndex = Mathf.Max(1, _lane.WaypointCount - 1);
            transform.position = _lane.PointAt(_lane.WaypointCount - 1);
        }

        void Update()
        {
            if (_lane == null) return;

            float dt = Time.deltaTime;

            UpdatePhysicsHandover();
            if (PhysicsActive) return;      // physics drives it; AI stops steering

            UpdateOvertake(dt);

            float acceleration = ComputeAcceleration(dt);
            SpeedMps = Mathf.Max(0f, SpeedMps + acceleration * dt);

            Advance(dt);
            SpinWheels(dt);
        }

        float ComputeAcceleration(float dt)
        {
            float desired = _desiredSpeedMps * _weatherSpeedFactor;
            if (Overtaking != OvertakeState.None) desired *= overtakeSpeedBonus;

            // Signal and junction handling takes priority over car-following: a red light is
            // an infinitely slow leader at the stop line.
            float stopDistance = DistanceToStopLine();
            float gap = float.MaxValue;
            float leaderSpeed = desired;

            if (_graph.TryGetLeader(this, CurrentLaneId, out var leader, out float leaderGap))
            {
                // Once we are substantially out of the lane we are passing the leader, not
                // following it — holding its gap here would brake us alongside it forever.
                bool ignoringLeader = Overtaking == OvertakeState.Alongside && leader == _overtakeTarget;

                if (!ignoringLeader)
                {
                    gap = leaderGap;
                    leaderSpeed = leader.SpeedMps;
                }
            }

            if (stopDistance < gap)
            {
                gap = stopDistance;
                leaderSpeed = 0f;
            }

            if (gap == float.MaxValue) return IdmAcceleration(SpeedMps, desired, desired, float.MaxValue);

            float accel = IdmAcceleration(SpeedMps, leaderSpeed, desired, gap);

            // IDM can ask for more braking than is comfortable when a gap closes suddenly
            // (the player cutting in). Allow emergency braking, but no more.
            accel = Mathf.Max(accel, -emergencyDeceleration);

            if (accel < -comfortDeceleration * 0.9f) TryHonk(dt);

            return accel;
        }

        // ── Overtaking ──────────────────────────────────────────────────────

        /// <summary>
        /// Overtaking on two-lane roads — the manoeuvre that makes Indian highway traffic feel
        /// like Indian highway traffic (section M.2).
        ///
        /// Modelled as a lateral offset rather than an actual lane swap: the vehicle stays
        /// registered on its own lane, so leader lookup and signal logic keep working, while
        /// visually and physically it is out in the oncoming lane. A real lane handover would
        /// need the oncoming lane's distance axis reversed and buys nothing.
        /// </summary>
        void UpdateOvertake(float dt)
        {
            switch (Overtaking)
            {
                case OvertakeState.None:
                    ConsiderPullingOut(dt);
                    break;

                case OvertakeState.PullingOut:
                    _overtakeElapsed += dt;
                    if (!IsOncomingStillClear() || _overtakeElapsed > overtakeTimeout) { AbortOvertake(); break; }
                    if (_lateralOffset >= overtakeOffset - 0.15f) Overtaking = OvertakeState.Alongside;
                    break;

                case OvertakeState.Alongside:
                    _overtakeElapsed += dt;
                    if (!IsOncomingStillClear() || _overtakeElapsed > overtakeTimeout) { AbortOvertake(); break; }
                    if (IsPastTarget()) Overtaking = OvertakeState.PullingIn;
                    break;

                case OvertakeState.PullingIn:
                    if (_lateralOffset <= 0.15f) FinishOvertake();
                    break;
            }

            // The offset itself is always eased, so an abort is a smooth tuck back in rather
            // than a teleport across the centre line.
            float target = Overtaking == OvertakeState.PullingOut || Overtaking == OvertakeState.Alongside
                ? overtakeOffset
                : 0f;

            _lateralOffset = Mathf.MoveTowards(_lateralOffset, target, lateralRate * dt);
        }

        void ConsiderPullingOut(float dt)
        {
            if (!canOvertake || _lane == null) { _blockedSeconds = 0f; return; }

            // Never start a pass approaching a junction or a signal.
            if (_lane.signal != null && DistanceToStopLine() < 120f) { _blockedSeconds = 0f; return; }

            if (!_graph.TryGetLeader(this, CurrentLaneId, out var leader, out float gap))
            {
                _blockedSeconds = 0f;
                return;
            }

            float desired = _desiredSpeedMps * _weatherSpeedFactor;
            bool blocked = leader.SpeedMps < desired * overtakeSpeedRatio &&
                           gap < Mathf.Max(minGap * 4f, SpeedMps * timeHeadway * 2f);

            if (!blocked)
            {
                _blockedSeconds = 0f;
                return;
            }

            // Patience, so a queue does not all pull out on the same frame. Autos and
            // two-wheelers are set impatient in their prefabs.
            _blockedSeconds += dt;
            if (_blockedSeconds < patienceSeconds) return;

            if (_lane.oncomingLaneId < 0) return;              // divided highway: nowhere to go
            if (!IsOncomingStillClear()) return;

            Overtaking = OvertakeState.PullingOut;
            _overtakeTarget = leader;
            _overtakeElapsed = 0f;
            _blockedSeconds = 0f;
        }

        /// <summary>
        /// Required clearance is a closing distance, not a fixed one: at 80 km/h against an
        /// oncoming 60 km/h you need about 270 m, and at 30 km/h you need far less.
        /// </summary>
        bool IsOncomingStillClear()
        {
            if (_lane == null || _lane.oncomingLaneId < 0) return false;

            float closingMps = SpeedMps + assumedOncomingKmh / 3.6f;
            float required = closingMps * oncomingClearSeconds;

            return _graph.IsLaneClearAhead(_lane.oncomingLaneId, transform.position, transform.forward, required);
        }

        /// <summary>Clear of the vehicle being passed, with its full length plus a margin.</summary>
        bool IsPastTarget()
        {
            if (_overtakeTarget == null) return true;

            var toTarget = _overtakeTarget.transform.position - transform.position;
            float ahead = Vector3.Dot(toTarget, transform.forward);
            float clearance = (bodyLength + _overtakeTarget.BodyLength) * 0.5f + minGap;

            return ahead < -clearance;
        }

        void AbortOvertake()
        {
            Overtaking = OvertakeState.PullingIn;
            _overtakeTarget = null;
            TryHonk(1f);               // the frustrated honk of an abandoned pass
        }

        void FinishOvertake()
        {
            Overtaking = OvertakeState.None;
            _overtakeTarget = null;
            _overtakeElapsed = 0f;
            _lateralOffset = 0f;
        }

        /// <summary>
        /// Intelligent Driver Model (section M.2). Produces smooth, human-looking
        /// follow-and-brake behaviour from four parameters and no state machine.
        /// </summary>
        float IdmAcceleration(float v, float vLead, float vDesired, float gap)
        {
            const int delta = 4;

            float freeRoad = 1f - Mathf.Pow(v / Mathf.Max(0.1f, vDesired), delta);
            if (gap == float.MaxValue) return maxAcceleration * freeRoad;

            float dv = v - vLead;
            float sStar = minGap + Mathf.Max(0f,
                v * timeHeadway + v * dv / (2f * Mathf.Sqrt(maxAcceleration * comfortDeceleration)));

            float interaction = Mathf.Pow(sStar / Mathf.Max(gap, 0.1f), 2);
            return maxAcceleration * (freeRoad - interaction);
        }

        float DistanceToStopLine()
        {
            if (_lane.signal == null) return float.MaxValue;
            if (_lane.signal.IsGreenFor(CurrentLaneId)) return float.MaxValue;

            float remaining = _lane.Length - DistanceAlongLane;
            return Mathf.Max(0f, remaining);
        }

        void Advance(float dt)
        {
            DistanceAlongLane += SpeedMps * dt;

            var target = AimPoint(_waypointIndex);
            var toTarget = target - transform.position;
            toTarget.y = 0f;

            if (toTarget.magnitude < 2f)
            {
                _waypointIndex++;
                if (_waypointIndex >= _lane.WaypointCount)
                {
                    var next = _graph.PickNextLane(_lane);
                    if (next == null)
                    {
                        // Dead end: hand back to the pool rather than piling up at the edge.
                        TrafficManager.Instance?.Despawn(this);
                        return;
                    }
                    EnterLane(next, 0f);
                    return;
                }
                target = AimPoint(_waypointIndex);
                toTarget = target - transform.position;
                toTarget.y = 0f;
            }

            if (toTarget.sqrMagnitude > 0.01f)
            {
                var desiredRotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, steerSharpness * dt);
            }

            transform.position += transform.forward * (SpeedMps * dt);
        }

        /// <summary>
        /// The waypoint to steer at, pushed sideways by the current overtaking offset.
        /// Offsetting the aim point rather than the vehicle's position means the manoeuvre
        /// comes out as a steered lane change, not a sideways slide.
        /// </summary>
        Vector3 AimPoint(int waypointIndex)
        {
            var point = _lane.PointAt(waypointIndex);
            if (_lateralOffset <= 0.01f) return point;

            return point + LaneGraph.SegmentRight(_lane, waypointIndex) * _lateralOffset;
        }

        void SpinWheels(float dt)
        {
            if (wheelMeshes.Length == 0) return;

            // Approximate: one wheel radius for the whole class, which is invisible at the
            // distance AI vehicles are actually seen from.
            float degrees = SpeedMps / (0.35f * 2f * Mathf.PI) * 360f * dt;
            foreach (var wheel in wheelMeshes)
                if (wheel != null) wheel.Rotate(Vector3.right, degrees, Space.Self);
        }

        void UpdatePhysicsHandover()
        {
            if (_player == null) return;

            float distance = Vector3.Distance(transform.position, _player.position);
            bool shouldBePhysical = distance < physicsActivationDistance;

            if (shouldBePhysical != PhysicsActive) SetPhysicsActive(shouldBePhysical);
        }

        void SetPhysicsActive(bool active)
        {
            PhysicsActive = active;
            if (physicsBody == null) return;

            physicsBody.isKinematic = !active;

            if (active)
            {
                // Hand the current kinematic velocity to PhysX so the switch is not a lurch.
                physicsBody.linearVelocity = transform.forward * SpeedMps;
            }
            else
            {
                // Coming back out of physics: adopt whatever speed the collision left us with,
                // so a vehicle the player shunted does not snap back to its old speed.
                SpeedMps = Mathf.Max(0f, Vector3.Dot(physicsBody.linearVelocity, transform.forward));
            }
        }

        void TryHonk(float dt)
        {
            _honkCooldown -= dt;
            if (_honkCooldown > 0f) return;
            if (hornSource == null || hornSource.isPlaying) return;

            float chance = honkChancePerSecond * dt;
            if (vehicleClass == AIVehicleClass.Auto || vehicleClass == AIVehicleClass.ShareAuto) chance *= 2f;

            if (Random.value < chance)
            {
                hornSource.Play();
                _honkCooldown = 2.5f;
            }
        }

        /// <summary>Called by TrafficManager when returning this vehicle to the pool.</summary>
        public void OnDespawn()
        {
            if (_graph != null && CurrentLaneId >= 0) _graph.Unregister(this, CurrentLaneId);
            _lane = null;
            CurrentLaneId = -1;
            SpeedMps = 0f;

            // Pooled objects must come back clean, or a recycled vehicle spawns mid-overtake
            // three metres off its lane.
            Overtaking = OvertakeState.None;
            _overtakeTarget = null;
            _lateralOffset = 0f;
            _overtakeElapsed = 0f;
            _blockedSeconds = 0f;

            SetPhysicsActive(false);
        }
    }
}
