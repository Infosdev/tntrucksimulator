using System;
using TNLorry.Core;
using TNLorry.Data;
using UnityEngine;

namespace TNLorry.Vehicle
{
    /// <summary>
    /// Player truck physics (section K.1). Owns the Rigidbody, the wheels, the engine and the
    /// gearbox; reads normalised controls set by the input layer and writes nothing else.
    ///
    /// Axles are declared explicitly rather than as flat wheel arrays, because anti-roll bars
    /// and load distribution are per-axle — and a 6x4 lorry with 3 axles behaves nothing like
    /// a car if you treat its wheels as an undifferentiated list.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class TruckController : MonoBehaviour
    {
        [Serializable]
        public class Axle
        {
            public WheelCollider left;
            public WheelCollider right;
            [Tooltip("Receives engine torque.")]
            public bool isDrive;
            [Tooltip("Follows the steering input.")]
            public bool isSteer;
            [Tooltip("Fraction of the axle's share of brake torque. Rear axles usually < 1.")]
            [Range(0f, 1.5f)] public float brakeBias = 1f;
            [Tooltip("Lift this axle when running empty (tag axle on a 10/12-wheeler).")]
            public bool isLiftable;

            [NonSerialized] public bool  Lifted;
            [NonSerialized] public float LastLeftCompression;
            [NonSerialized] public float LastRightCompression;

            public bool IsValid => left != null && right != null;
        }

        [Header("Data")]
        [SerializeField] TruckData data;

        [Header("Axles (front to rear)")]
        [SerializeField] Axle[] axles = new Axle[0];

        [Header("Visuals")]
        [Tooltip("Wheel meshes, matched to axles in the same left/right order.")]
        [SerializeField] Transform[] wheelMeshes = new Transform[0];

        [Header("Physics tuning")]
        [Tooltip("Substeps for WheelCollider at low speed; higher = stabler, costlier.")]
        [SerializeField] int lowSpeedSubsteps = 12;
        [SerializeField] int highSpeedSubsteps = 6;
        [SerializeField] float substepSpeedThreshold = 8f;

        // ── Controls, written by the input layer ────────────────────────────
        public float Throttle   { get; set; }          // 0..1
        public float Brake      { get; set; }          // 0..1
        public float Steer      { get; set; }          // -1..1
        public bool  HandBrake  { get; set; }
        public bool  Retarder   { get; set; }

        // ── State ───────────────────────────────────────────────────────────
        public TruckData Data      => data;
        public Engine    Engine    { get; private set; }
        public Gearbox   Gearbox   { get; private set; }
        public Rigidbody Body      { get; private set; }

        public float SpeedKmh      => Body.linearVelocity.magnitude * 3.6f;
        /// <summary>Signed: negative when actually rolling backwards, for the reverse beeper.</summary>
        public float ForwardSpeedKmh => Vector3.Dot(Body.linearVelocity, transform.forward) * 3.6f;
        public float CargoMassKg   { get; private set; }
        /// <summary>Driven-wheel radius, read off the colliders at startup. Gear-speed maths uses it.</summary>
        public float WheelRadius   => _wheelRadius;
        public bool  IsGrounded    { get; private set; }
        /// <summary>Peak vertical acceleration last frame, in g. Fragile-cargo damage reads this.</summary>
        public float VerticalShockG { get; private set; }

        float _appliedBrake;          // air-brake lag
        float _currentSteerAngle;
        Vector3 _lastVelocity;
        float   _wheelRadius = 0.5f;

        void Awake()
        {
            Body = GetComponent<Rigidbody>();

            if (data == null)
            {
                Debug.LogError("[TruckController] No TruckData assigned; disabling.", this);
                enabled = false;
                return;
            }

            Body.mass          = data.massKg;
            Body.centerOfMass  = data.centerOfMass;
            Body.interpolation = RigidbodyInterpolation.Interpolate;

            Engine  = new Engine(data);
            Gearbox = new Gearbox(data);

            ConfigureWheels();
            Engine.Start();
        }

        void ConfigureWheels()
        {
            int wheelCount = 0;
            foreach (var axle in axles)
            {
                if (!axle.IsValid)
                {
                    Debug.LogError("[TruckController] An axle is missing a wheel reference.", this);
                    continue;
                }
                wheelCount += 2;
                _wheelRadius = axle.left.radius;

                // Sprung mass must reflect a loaded truck or the suspension bottoms out and the
                // wheels skate. Unity's auto-configure assumes car-like masses.
                foreach (var wheel in new[] { axle.left, axle.right })
                    wheel.ConfigureVehicleSubsteps(substepSpeedThreshold, lowSpeedSubsteps, highSpeedSubsteps);
            }

            if (wheelCount == 0)
                Debug.LogError("[TruckController] No valid axles configured.", this);
        }

        /// <summary>Called by the loading bay. Adds cargo mass and re-derives the balance point.</summary>
        public void SetCargoMass(float massKg)
        {
            CargoMassKg = Mathf.Max(0f, massKg);
            Body.mass   = data.massKg + CargoMassKg;

            // A loaded bed raises the centre of mass, which is exactly why a full lorry
            // rolls over on a hairpin that an empty one takes fine.
            float loadRatio = data.maxPayloadKg > 0f ? Mathf.Clamp01(CargoMassKg / data.maxPayloadKg) : 0f;
            Body.centerOfMass = data.centerOfMass + new Vector3(0f, loadRatio * 0.45f, 0f);

            foreach (var axle in axles)
                if (axle.isLiftable) SetAxleLifted(axle, loadRatio < 0.15f);
        }

        static void SetAxleLifted(Axle axle, bool lifted)
        {
            axle.Lifted = lifted;
            if (!axle.IsValid) return;
            // A lifted tag axle carries no load and applies no torque or braking.
            axle.left.enabled  = !lifted;
            axle.right.enabled = !lifted;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            UpdateDrivetrain(dt);
            UpdateSteering(dt);
            UpdateBrakes(dt);
            ApplyAntiRoll();
            ApplyDownforce();
            TrackShock(dt);
        }

        void UpdateDrivetrain(float dt)
        {
            float wheelRpm  = AverageDriveWheelRpm();
            float engineRpm = Gearbox.WheelToEngineRpm(wheelRpm, Engine.IdleRpm);

            Engine.Tick(engineRpm, Throttle, dt, Gearbox.IsClutchEngaged);
            Gearbox.Tick(dt, Engine.Rpm, Throttle, SpeedKmh, Brake);

            float wheelTorque = Engine.Torque * Gearbox.CurrentRatio * data.drivetrainEfficiency;

            int driveWheels = CountDriveWheels();
            float perWheel  = driveWheels > 0 ? wheelTorque / driveWheels : 0f;

            foreach (var axle in axles)
            {
                if (!axle.IsValid || axle.Lifted) continue;
                float torque = axle.isDrive ? perWheel : 0f;
                axle.left.motorTorque  = torque;
                axle.right.motorTorque = torque;
            }
        }

        void UpdateSteering(float dt)
        {
            // Trucks steer progressively less at speed — without this, a touch-steer flick at
            // 80 km/h flips the trailer.
            float speedFactor = Mathf.Clamp01(SpeedKmh / 90f);
            float maxAngle    = Mathf.Lerp(data.maxSteerAngle, data.maxSteerAngle * 0.25f, speedFactor);
            float target      = Mathf.Clamp(Steer, -1f, 1f) * maxAngle;

            // Rate-limited so the wheels cannot teleport to full lock on a touch input.
            _currentSteerAngle = Mathf.MoveTowards(_currentSteerAngle, target, data.steerRate * dt);

            foreach (var axle in axles)
            {
                if (!axle.IsValid || !axle.isSteer) continue;
                axle.left.steerAngle  = _currentSteerAngle;
                axle.right.steerAngle = _currentSteerAngle;
            }
        }

        void UpdateBrakes(float dt)
        {
            // Air brakes build and release over time. The lag is a feel detail players notice.
            float lag = Mathf.Max(0.01f, data.brakeResponseTime);
            _appliedBrake = Mathf.MoveTowards(_appliedBrake, Mathf.Clamp01(Brake), dt / lag);

            float serviceBrake = _appliedBrake * data.maxBrakeTorque;
            float engineBrake  = Engine.EngineBrakeTorque(Throttle);
            float retarder     = Retarder ? data.retarderTorque : 0f;

            foreach (var axle in axles)
            {
                if (!axle.IsValid || axle.Lifted) continue;

                float torque = serviceBrake * axle.brakeBias;

                // Engine braking and the retarder act through the driveline, so only on drive axles.
                if (axle.isDrive) torque += engineBrake + retarder;

                if (HandBrake && !axle.isSteer) torque = Mathf.Max(torque, data.maxBrakeTorque * 2f);

                axle.left.brakeTorque  = torque;
                axle.right.brakeTorque = torque;
            }
        }

        /// <summary>
        /// Anti-roll bars. Without them a high-CoM lorry on WheelColliders tips over on any
        /// real hairpin; with them it still can, but only when actually driven too fast.
        /// </summary>
        void ApplyAntiRoll()
        {
            IsGrounded = false;

            foreach (var axle in axles)
            {
                if (!axle.IsValid || axle.Lifted) continue;

                float leftTravel  = SuspensionTravel(axle.left,  out bool leftGrounded);
                float rightTravel = SuspensionTravel(axle.right, out bool rightGrounded);

                axle.LastLeftCompression  = leftTravel;
                axle.LastRightCompression = rightTravel;
                if (leftGrounded || rightGrounded) IsGrounded = true;

                float force = (leftTravel - rightTravel) * data.antiRollStiffness;

                if (leftGrounded)
                    Body.AddForceAtPosition(axle.left.transform.up * -force, axle.left.transform.position);
                if (rightGrounded)
                    Body.AddForceAtPosition(axle.right.transform.up * force, axle.right.transform.position);
            }
        }

        static float SuspensionTravel(WheelCollider wheel, out bool grounded)
        {
            grounded = wheel.GetGroundHit(out WheelHit hit);
            if (!grounded) return 1f;   // fully extended

            // 0 = fully compressed, 1 = fully extended.
            return (-wheel.transform.InverseTransformPoint(hit.point).y - wheel.radius)
                   / Mathf.Max(0.01f, wheel.suspensionDistance);
        }

        void ApplyDownforce()
        {
            if (data.downforceCoefficient <= 0f) return;
            float speed = Body.linearVelocity.magnitude;
            Body.AddForce(-transform.up * (data.downforceCoefficient * speed));
        }

        void TrackShock(float dt)
        {
            var velocity = Body.linearVelocity;
            float verticalAccel = (velocity.y - _lastVelocity.y) / Mathf.Max(0.0001f, dt);
            VerticalShockG = Mathf.Abs(verticalAccel) / 9.81f;
            _lastVelocity = velocity;
        }

        void Update() => UpdateWheelMeshes();

        void UpdateWheelMeshes()
        {
            if (wheelMeshes == null || wheelMeshes.Length == 0) return;

            int meshIndex = 0;
            foreach (var axle in axles)
            {
                if (!axle.IsValid) continue;
                meshIndex = ApplyMesh(axle.left,  meshIndex);
                meshIndex = ApplyMesh(axle.right, meshIndex);
            }
        }

        int ApplyMesh(WheelCollider wheel, int meshIndex)
        {
            if (meshIndex >= wheelMeshes.Length) return meshIndex;
            var mesh = wheelMeshes[meshIndex];
            if (mesh != null)
            {
                wheel.GetWorldPose(out var pos, out var rot);
                mesh.SetPositionAndRotation(pos, rot);
            }
            return meshIndex + 1;
        }

        float AverageDriveWheelRpm()
        {
            float sum = 0f;
            int count = 0;
            foreach (var axle in axles)
            {
                if (!axle.IsValid || !axle.isDrive || axle.Lifted) continue;
                sum += axle.left.rpm + axle.right.rpm;
                count += 2;
            }
            return count == 0 ? 0f : sum / count;
        }

        int CountDriveWheels()
        {
            int count = 0;
            foreach (var axle in axles)
                if (axle.IsValid && axle.isDrive && !axle.Lifted) count += 2;
            return count;
        }

        /// <summary>Average slip across drive wheels, for tyre-smoke VFX and traction audio.</summary>
        public float DriveWheelSlip01()
        {
            float worst = 0f;
            foreach (var axle in axles)
            {
                if (!axle.IsValid || !axle.isDrive || axle.Lifted) continue;
                foreach (var wheel in new[] { axle.left, axle.right })
                {
                    if (!wheel.GetGroundHit(out WheelHit hit)) continue;
                    worst = Mathf.Max(worst, Mathf.Abs(hit.forwardSlip));
                }
            }
            return Mathf.Clamp01(worst);
        }

        /// <summary>
        /// Floating origin shifts the world under us (section K.9). FloatingOrigin already moved
        /// this object's root transform, which syncs the Rigidbody — so do NOT move it again here.
        /// What does need clearing is any cached world-space state, or the next frame computes a
        /// 2 km velocity spike and the shock-based cargo damage fires for no reason.
        /// </summary>
        public void OnOriginShift(Vector3 offset)
        {
            _lastVelocity = Body.linearVelocity;
        }

        void OnEnable()  => EventBus.Subscribe<OriginShiftedEvent>(HandleOriginShift);
        void OnDisable() => EventBus.Unsubscribe<OriginShiftedEvent>(HandleOriginShift);

        void HandleOriginShift(OriginShiftedEvent e) => OnOriginShift(e.Offset);
    }
}
