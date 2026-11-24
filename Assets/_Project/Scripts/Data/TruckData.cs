using UnityEngine;

namespace TNLorry.Data
{
    public enum DriveLayout { FourByTwo, SixByTwo, SixByFour, TenWheeler, TwelveWheeler }

    /// <summary>
    /// One truck model, data only (section F.2). All brands/models must be FICTIONAL —
    /// see Appendix 1. Do not reference real manufacturer names, logos or badge shapes.
    /// </summary>
    [CreateAssetMenu(menuName = "TN/Truck Data", fileName = "Truck_")]
    public class TruckData : ScriptableObject
    {
        [Header("Identity")]
        public string id = "truck_id";
        [Tooltip("Localization key, e.g. truck.vel_murugan_1612")]
        public string nameKey = "truck.";
        public string fictionalBrand = "VELAN";
        public DriveLayout layout = DriveLayout.SixByFour;
        public Sprite thumbnail;
        public GameObject prefab;              // Addressable in shipping builds

        [Header("Mass & balance")]
        [Tooltip("Kerb mass in kg. Cargo mass is added at runtime by CargoLoad.")]
        public float massKg = 9000f;
        [Tooltip("Keep LOW (negative Y) or the truck rolls over on ghat bends.")]
        public Vector3 centerOfMass = new Vector3(0f, -0.35f, 0.2f);
        public float maxPayloadKg = 16000f;

        [Header("Engine")]
        [Tooltip("X = engine RPM, Y = torque in Nm. Diesel: broad flat plateau 1200-1800 rpm.")]
        public AnimationCurve torqueCurve = new AnimationCurve(
            new Keyframe(600f,  600f),
            new Keyframe(1200f, 1050f),
            new Keyframe(1600f, 1100f),
            new Keyframe(2000f, 950f),
            new Keyframe(2600f, 600f));
        public float idleRpm = 650f;
        public float maxRpm  = 2600f;
        [Tooltip("Rev limiter cut: engine torque is zeroed above this.")]
        public float redlineRpm = 2500f;
        [Tooltip("Nm of retarding torque at the wheels at closed throttle, scaled by rpm.")]
        public float engineBrakeTorque = 900f;
        [Tooltip("How fast rpm follows demand. Higher = snappier, less diesel-like.")]
        public float rpmResponse = 6f;

        [Header("Drivetrain")]
        [Tooltip("Forward gear ratios, lowest gear first. Reverse is derived from gear 1.")]
        public float[] gearRatios = { 9.01f, 5.27f, 3.21f, 2.04f, 1.36f, 1.00f, 0.78f };
        public float finalDrive = 4.1f;
        [Range(0.6f, 1f)] public float drivetrainEfficiency = 0.85f;
        public bool  autoShift = true;
        [Tooltip("Seconds of torque interruption while shifting. Truck feel: 0.4-0.8s.")]
        public float shiftTime = 0.55f;

        [Header("Steering & brakes")]
        public float maxSteerAngle = 35f;
        [Tooltip("Degrees per second the wheels can turn, limits twitchy touch input.")]
        public float steerRate = 90f;
        public float maxBrakeTorque = 22000f;
        [Tooltip("Air-brake build-up delay in seconds. Part of the truck feel.")]
        public float brakeResponseTime = 0.25f;
        [Tooltip("Retarder torque applied to drive wheels when engaged (downhill ghats).")]
        public float retarderTorque = 6000f;

        [Header("Fuel (section K.5)")]
        public float fuelCapacityLitres = 160f;
        [Tooltip("Litres/hour at idle.")]
        public float idleBurnLitresPerHour = 1.6f;
        [Tooltip("Litres/hour at full load, full throttle, max rpm.")]
        public float peakBurnLitresPerHour = 26f;

        [Header("Economy")]
        public int   price = 1200000;
        public int   unlockLevel = 1;
        public float repairCostPerPercent = 900f;

        [Header("Handling aids")]
        [Tooltip("Anti-roll stiffness per axle, Nm per metre of suspension travel difference.")]
        public float antiRollStiffness = 60000f;
        [Tooltip("Small downforce so the truck settles at speed. Keep subtle.")]
        public float downforceCoefficient = 12f;

        // ── Derived helpers ─────────────────────────────────────────────────

        public int GearCount => gearRatios != null ? gearRatios.Length : 0;

        public float TorqueAt(float rpm)
        {
            if (rpm > redlineRpm) return 0f;
            return Mathf.Max(0f, torqueCurve.Evaluate(Mathf.Clamp(rpm, 0f, maxRpm)));
        }

        void OnValidate()
        {
            // A non-monotonic ratio list silently breaks auto-shift, and it is an easy
            // mistake to make when hand-entering numbers, so catch it at author time.
            if (gearRatios == null) return;
            for (int i = 1; i < gearRatios.Length; i++)
            {
                if (gearRatios[i] >= gearRatios[i - 1])
                    Debug.LogWarning(name + ": gearRatios must be strictly descending (lowest gear first). " +
                                     "Index " + i + " breaks the order.", this);
                if (gearRatios[i] <= 0f)
                    Debug.LogWarning(name + ": gearRatios[" + i + "] must be > 0.", this);
            }
            if (redlineRpm > maxRpm) redlineRpm = maxRpm;
            if (idleRpm >= redlineRpm) idleRpm = redlineRpm * 0.25f;
        }
    }
}
