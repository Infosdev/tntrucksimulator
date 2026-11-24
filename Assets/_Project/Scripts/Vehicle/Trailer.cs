using System.Collections.Generic;
using TNLorry.Data;
using UnityEngine;

namespace TNLorry.Vehicle
{
    /// <summary>
    /// A trailer: own Rigidbody, own wheels, own brakes (section K.1). Registers itself in a
    /// static list so TrailerCoupling can find the nearest pin without a physics query every
    /// frame — there are rarely more than a dozen trailers in a loaded depot.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Trailer : MonoBehaviour
    {
        static readonly List<Trailer> All = new List<Trailer>(16);

        [Header("Identity")]
        [SerializeField] string trailerId = "trailer_flatbed";
        [SerializeField] TrailerType type = TrailerType.Flatbed;

        [Header("Physical")]
        [SerializeField] float emptyMassKg = 6500f;
        [SerializeField] Transform kingpin;
        [SerializeField] WheelCollider[] wheels = new WheelCollider[0];
        [Tooltip("Where the cargo visual is parented.")]
        [SerializeField] Transform cargoMount;

        [Header("Brakes")]
        [Tooltip("Trailer service brake torque. Trailer braking is what stops a jackknife.")]
        [SerializeField] float maxBrakeTorque = 14000f;

        public string TrailerId => trailerId;
        public TrailerType Type => type;
        public Rigidbody Body { get; private set; }
        public bool IsCoupled => _coupling != null;
        public Vector3 KingpinPosition => kingpin != null ? kingpin.position : transform.position;
        public float CargoMassKg { get; private set; }
        public float TotalMassKg => emptyMassKg + CargoMassKg;

        TrailerCoupling _coupling;
        GameObject _cargoVisual;

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Body.mass = emptyMassKg;
            Body.interpolation = RigidbodyInterpolation.Interpolate;

            if (kingpin == null)
                Debug.LogError("[Trailer] " + trailerId + " has no kingpin transform assigned.", this);
        }

        // No origin-shift handler needed: FloatingOrigin moves the root transform (which syncs
        // the Rigidbody), and the coupling joint is defined in local anchors, so it survives
        // a shift untouched.
        void OnEnable()  => All.Add(this);
        void OnDisable() => All.Remove(this);

        void FixedUpdate()
        {
            // Trailer brakes follow the tractor's. Applied here rather than from the tractor so
            // an uncoupled trailer still holds still on a slope.
            float brake = IsCoupled && _coupling.TryGetComponent<TruckController>(out var truck)
                ? truck.Brake * maxBrakeTorque
                : maxBrakeTorque;              // parking brake when uncoupled

            foreach (var wheel in wheels)
                if (wheel != null) wheel.brakeTorque = brake;
        }

        public void OnCoupled(TrailerCoupling coupling) => _coupling = coupling;
        public void OnDecoupled() => _coupling = null;

        /// <summary>Loads cargo: adds mass and spawns the visual. Called by the loading bay.</summary>
        public void LoadCargo(CargoData cargo)
        {
            UnloadCargo();
            if (cargo == null) return;

            if (cargo.requiredTrailer != type)
            {
                Debug.LogWarning("[Trailer] " + cargo.id + " needs a " + cargo.requiredTrailer +
                                 " but this is a " + type + ".");
                return;
            }

            CargoMassKg = cargo.massKg;
            Body.mass = TotalMassKg;

            if (cargo.visualPrefab != null && cargoMount != null)
                _cargoVisual = Instantiate(cargo.visualPrefab, cargoMount);

            if (_coupling != null && _coupling.TryGetComponent<TruckController>(out var truck))
                truck.SetCargoMass(TotalMassKg);
        }

        public void UnloadCargo()
        {
            CargoMassKg = 0f;
            Body.mass = emptyMassKg;

            if (_cargoVisual != null)
            {
                Destroy(_cargoVisual);
                _cargoVisual = null;
            }

            if (_coupling != null && _coupling.TryGetComponent<TruckController>(out var truck))
                truck.SetCargoMass(TotalMassKg);
        }

        public static Trailer FindNearest(Vector3 position, float maxDistance)
        {
            Trailer best = null;
            float bestSqr = maxDistance * maxDistance;

            foreach (var trailer in All)
            {
                if (trailer == null || trailer.IsCoupled) continue;
                float sqr = (trailer.KingpinPosition - position).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = trailer; }
            }
            return best;
        }
    }
}
