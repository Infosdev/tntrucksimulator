using TNLorry.Core;
using TNLorry.Data;
using UnityEngine;

namespace TNLorry.Vehicle
{
    /// <summary>
    /// Component damage from collisions and rough driving (section K.6), plus cargo damage,
    /// which is the one the player actually feels because it comes straight off the payout.
    ///
    /// Collision impulse is split across components by where the hit landed, so clipping a
    /// kerb is not the same as a head-on into a parked lorry.
    /// </summary>
    [RequireComponent(typeof(TruckController))]
    public class DamageSystem : MonoBehaviour
    {
        [Header("Thresholds")]
        [Tooltip("Collision impulse below this is ignored - kerbs, speed breakers, light taps.")]
        [SerializeField] float impulseDeadZone = 2500f;
        [Tooltip("Impulse that equals 100% damage to a single component.")]
        [SerializeField] float impulseForFullDamage = 400000f;

        [Header("Cargo")]
        [Tooltip("Vertical shock in g above which fragile cargo starts taking damage (potholes).")]
        [SerializeField] float cargoShockThresholdG = 2.2f;
        [Tooltip("Cargo damage per g of shock above the threshold, per second.")]
        [SerializeField] float cargoShockDamageRate = 0.02f;
        [Tooltip("Lateral g above which an unsecured load shifts (hard cornering).")]
        [SerializeField] float cargoLateralThresholdG = 0.55f;
        [Tooltip("Cargo damage per second of rain exposure when the tarp is torn.")]
        [SerializeField] float rainDamageRate = 0.01f;

        [Header("Effects")]
        [Tooltip("Engine damage at which power loss becomes obvious. Tuned in Engine.Tick.")]
        [SerializeField] float engineWarnThreshold = 0.5f;

        public float EngineDamage  { get; private set; }
        public float ChassisDamage { get; private set; }
        public float CabinDamage   { get; private set; }
        public float WheelDamage   { get; private set; }
        /// <summary>0..1. Mirrored into the active Job so the payout formula can read it.</summary>
        public float CargoDamage   { get; private set; }

        /// <summary>Set by the loading bay from the cargo's flags.</summary>
        public CargoFlags ActiveCargoFlags { get; set; }
        public bool TarpIntact { get; set; } = true;
        /// <summary>Driven by WeatherSystem; 0..1 rain intensity.</summary>
        public float RainIntensity { get; set; }

        public int CollisionCount { get; private set; }

        TruckController _truck;
        Gameplay.TrafficRuleMonitor _rules;
        Vector3 _previousVelocity;
        float _publishTimer;

        void Awake()
        {
            _truck = GetComponent<TruckController>();

            // Damage and fines are separate concerns, but they share one trigger: the impact.
            // This is the only place that knows a collision happened, so it reports it.
            _rules = GetComponentInChildren<Gameplay.TrafficRuleMonitor>();
        }

        public void Restore(OwnedTruckSave save)
        {
            if (save == null) return;
            EngineDamage  = save.engineDamage;
            ChassisDamage = save.chassisDamage;
            CabinDamage   = save.cabinDamage;
            WheelDamage   = save.wheelDamage;
            ApplyToEngine();
        }

        public void WriteTo(OwnedTruckSave save)
        {
            if (save == null) return;
            save.engineDamage  = EngineDamage;
            save.chassisDamage = ChassisDamage;
            save.cabinDamage   = CabinDamage;
            save.wheelDamage   = WheelDamage;
        }

        void OnCollisionEnter(Collision collision) => HandleImpact(collision);
        void OnCollisionStay(Collision collision)  => HandleImpact(collision);

        void HandleImpact(Collision collision)
        {
            float impulse = collision.impulse.magnitude;
            if (impulse < impulseDeadZone) return;

            CollisionCount++;

            float severity = Mathf.Clamp01(impulse / impulseForFullDamage);

            // Where it hit decides what breaks. Front hits kill the engine and cab,
            // side hits bend the chassis, low hits take out wheels.
            var contact = collision.GetContact(0);
            var local   = transform.InverseTransformPoint(contact.point);

            bool front = local.z > 1.5f;
            bool low   = local.y < 0.6f;

            if (front)
            {
                EngineDamage = Add(EngineDamage, severity * 0.8f);
                CabinDamage  = Add(CabinDamage,  severity * 0.6f);
            }
            if (low)
            {
                WheelDamage = Add(WheelDamage, severity * 0.5f);
            }
            ChassisDamage = Add(ChassisDamage, severity * 0.4f);

            // Cargo takes a share of every real impact, flags or not.
            float cargoShare = HasFlag(Data.CargoFlags.Fragile) ? 1.5f : 0.6f;
            CargoDamage = Add(CargoDamage, severity * cargoShare);

            ApplyToEngine();
            PublishDamage();

            // Hitting a pedestrian or another vehicle is also a traffic offence (section K.7).
            // The monitor applies its own speed threshold and cooldown, so a scrape in a yard
            // damages the truck without issuing a fine.
            _rules?.ReportCollision(collision.gameObject, _truck != null ? _truck.SpeedKmh : 0f);
        }

        void Update()
        {
            float dt = Time.deltaTime;

            TickCargoShock(dt);
            TickRain(dt);

            // Damage events drive HUD warnings; 4 Hz is enough and keeps the bus quiet.
            _publishTimer += dt;
            if (_publishTimer >= 0.25f)
            {
                _publishTimer = 0f;
                PublishDamage();
            }
        }

        void TickCargoShock(float dt)
        {
            if (_truck == null) return;

            // Vertical shock: potholes and speed breakers taken too fast.
            float shock = _truck.VerticalShockG;
            if (shock > cargoShockThresholdG)
            {
                float excess = shock - cargoShockThresholdG;
                float rate   = cargoShockDamageRate * excess;
                if (HasFlag(Data.CargoFlags.Fragile)) rate *= 3f;   // the Namakkal egg run
                CargoDamage = Add(CargoDamage, rate * dt);
            }

            // Lateral shift: cornering hard enough to throw the load against the body.
            var velocity = _truck.Body.linearVelocity;
            float lateralAccel = Vector3.Dot(velocity - _previousVelocity, transform.right) / Mathf.Max(0.0001f, dt);
            _previousVelocity = velocity;

            float lateralG = Mathf.Abs(lateralAccel) / 9.81f;
            if (lateralG > cargoLateralThresholdG)
            {
                float rate = (lateralG - cargoLateralThresholdG) * cargoShockDamageRate;
                if (HasFlag(Data.CargoFlags.Liquid)) rate *= 1.5f;  // slosh
                CargoDamage = Add(CargoDamage, rate * dt);
            }
        }

        void TickRain(float dt)
        {
            if (RainIntensity <= 0.05f) return;
            if (!HasFlag(Data.CargoFlags.RainSensitive)) return;
            if (TarpIntact) return;

            // Turmeric and garments under a torn tarp in the north-east monsoon (section N).
            CargoDamage = Add(CargoDamage, rainDamageRate * RainIntensity * dt);
        }

        void ApplyToEngine()
        {
            if (_truck?.Engine != null) _truck.Engine.Damage01 = EngineDamage;
        }

        void PublishDamage()
        {
            EventBus.Publish(new DamageChangedEvent
            {
                EnginePercent  = EngineDamage * 100f,
                ChassisPercent = ChassisDamage * 100f,
                CargoPercent   = CargoDamage * 100f
            });
        }

        static float Add(float current, float delta) => Mathf.Clamp01(current + delta);

        bool HasFlag(CargoFlags flag) => (ActiveCargoFlags & flag) != 0;

        /// <summary>Called at the loading bay for a new job.</summary>
        public void ResetCargoDamage(CargoFlags flags)
        {
            CargoDamage = 0f;
            ActiveCargoFlags = flags;
            TarpIntact  = true;
            CollisionCount = 0;
        }

        public void RepairAll()
        {
            EngineDamage = ChassisDamage = CabinDamage = WheelDamage = 0f;
            ApplyToEngine();
            PublishDamage();
        }

        /// <summary>Worst single component, for the "needs repair" badge and the repair quote.</summary>
        public float WorstComponentDamage =>
            Mathf.Max(Mathf.Max(EngineDamage, ChassisDamage), Mathf.Max(CabinDamage, WheelDamage));

        public bool NeedsUrgentRepair => EngineDamage >= engineWarnThreshold || WorstComponentDamage >= 0.8f;
    }
}
