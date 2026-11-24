using TNLorry.Core;
using UnityEngine;

namespace TNLorry.Vehicle
{
    /// <summary>
    /// Fifth-wheel coupling (section K.1). The trailer is a separate Rigidbody with its own
    /// WheelColliders, joined by a ConfigurableJoint rather than parented — parenting a trailer
    /// gives you a rigid bus, not articulation, and reverse parking stops being a skill.
    ///
    /// Put this on the tractor unit; point it at a kingpin anchor transform.
    /// </summary>
    public class TrailerCoupling : MonoBehaviour
    {
        [Header("Anchors")]
        [Tooltip("Kingpin position on the tractor, at the centre of the fifth wheel.")]
        [SerializeField] Transform fifthWheel;
        [Tooltip("How close a trailer's pin must be to couple, in metres.")]
        [SerializeField] float couplingRadius = 0.6f;
        [Tooltip("Maximum speed at which coupling is allowed, in km/h.")]
        [SerializeField] float maxCouplingSpeedKmh = 4f;

        [Header("Joint")]
        [Tooltip("Yaw limit either side. Real tractors jackknife around 90 degrees.")]
        [SerializeField] float yawLimit = 85f;
        [Tooltip("Pitch/roll articulation, small - the fifth wheel is nearly rigid in these axes.")]
        [SerializeField] float pitchRollLimit = 8f;
        [SerializeField] float jointSpring = 0f;      // 0 = free within limits

        public Trailer AttachedTrailer { get; private set; }
        public bool IsCoupled => AttachedTrailer != null;

        TruckController _truck;
        ConfigurableJoint _joint;

        void Awake()
        {
            _truck = GetComponent<TruckController>();
            if (fifthWheel == null)
            {
                Debug.LogError("[TrailerCoupling] No fifth-wheel anchor assigned.", this);
                enabled = false;
            }
        }

        void Update()
        {
            if (IsCoupled) return;
            if (_truck != null && Mathf.Abs(_truck.SpeedKmh) > maxCouplingSpeedKmh) return;

            // Proximity coupling: back the pin under the trailer and it hooks up, same as
            // a real yard. No button, so the parking mini-game is the whole interaction.
            var candidate = Trailer.FindNearest(fifthWheel.position, couplingRadius);
            if (candidate != null && !candidate.IsCoupled) Couple(candidate);
        }

        public void Couple(Trailer trailer)
        {
            if (trailer == null || IsCoupled) return;

            var trailerBody = trailer.Body;
            if (trailerBody == null) return;

            // Snap the trailer's pin onto the fifth wheel before creating the joint, so the
            // joint does not start life stretched and fling both bodies apart.
            var pinOffset = trailer.transform.position - trailer.KingpinPosition;
            trailerBody.position = fifthWheel.position + pinOffset;
            trailerBody.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

            _joint = gameObject.AddComponent<ConfigurableJoint>();
            _joint.connectedBody = trailerBody;
            _joint.autoConfigureConnectedAnchor = false;
            _joint.anchor = transform.InverseTransformPoint(fifthWheel.position);
            _joint.connectedAnchor = trailerBody.transform.InverseTransformPoint(trailer.KingpinPosition);

            _joint.xMotion = _joint.yMotion = _joint.zMotion = ConfigurableJointMotion.Locked;
            _joint.angularXMotion = ConfigurableJointMotion.Limited;   // pitch
            _joint.angularYMotion = ConfigurableJointMotion.Limited;   // yaw
            _joint.angularZMotion = ConfigurableJointMotion.Limited;   // roll

            _joint.lowAngularXLimit  = new SoftJointLimit { limit = -pitchRollLimit };
            _joint.highAngularXLimit = new SoftJointLimit { limit =  pitchRollLimit };
            _joint.angularYLimit     = new SoftJointLimit { limit =  yawLimit };
            _joint.angularZLimit     = new SoftJointLimit { limit =  pitchRollLimit };

            _joint.angularXLimitSpring = new SoftJointLimitSpring { spring = jointSpring, damper = 50f };
            _joint.angularYZLimitSpring = new SoftJointLimitSpring { spring = jointSpring, damper = 50f };

            _joint.enableCollision = false;   // tractor and trailer bodies overlap at the pin
            _joint.projectionMode = JointProjectionMode.PositionAndRotation;
            _joint.projectionDistance = 0.05f;

            AttachedTrailer = trailer;
            trailer.OnCoupled(this);

            _truck?.SetCargoMass(trailer.TotalMassKg);
            EventBus.Publish(new TrailerCoupledEvent { TrailerId = trailer.TrailerId });
        }

        public void Decouple()
        {
            if (!IsCoupled) return;

            var trailer = AttachedTrailer;
            AttachedTrailer = null;

            if (_joint != null) Destroy(_joint);
            _joint = null;

            trailer.OnDecoupled();
            _truck?.SetCargoMass(0f);
            EventBus.Publish(new TrailerDecoupledEvent { TrailerId = trailer.TrailerId });
        }

        /// <summary>Articulation angle in degrees — the HUD jackknife warning reads this.</summary>
        public float ArticulationAngle
        {
            get
            {
                if (!IsCoupled) return 0f;
                return Vector3.SignedAngle(transform.forward, AttachedTrailer.transform.forward, Vector3.up);
            }
        }

        public bool IsJackknifing => Mathf.Abs(ArticulationAngle) > yawLimit * 0.85f;

        void OnDrawGizmosSelected()
        {
            if (fifthWheel == null) return;
            Gizmos.color = IsCoupled ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(fifthWheel.position, couplingRadius);
        }
    }
}
