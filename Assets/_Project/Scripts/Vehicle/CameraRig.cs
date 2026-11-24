using UnityEngine;

namespace TNLorry.Vehicle
{
    public enum CameraView { Chase = 0, Cab = 1, Hood = 2, Top = 3, Trailer = 4 }

    /// <summary>
    /// Camera views (section K core tech: cab, chase, top, trailer). One Camera, repositioned,
    /// rather than several cameras — extra enabled cameras cost a full culling pass each on
    /// mobile, and mirrors already cost us one.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        [System.Serializable]
        public class ViewSetup
        {
            public CameraView view;
            [Tooltip("Anchor on the truck this view is relative to. Falls back to the truck root.")]
            public Transform anchor;
            public Vector3 localOffset;
            public Vector3 localEuler;
            public float fieldOfView = 60f;
            [Tooltip("0 = rigid, higher = lazier follow. Cab views should be rigid.")]
            [Range(0f, 1f)] public float followLag;
            [Tooltip("Look ahead into the turn, in degrees per unit of steering input.")]
            public float steerLookDegrees;
        }

        [SerializeField] Camera targetCamera;
        [SerializeField] TruckController truck;
        [SerializeField] ViewSetup[] views = new ViewSetup[0];
        [SerializeField] CameraView startingView = CameraView.Chase;

        [Header("Speed feel")]
        [Tooltip("Extra FOV at top speed. Subtle - too much makes players nauseous on a phone.")]
        [SerializeField] float speedFovBoost = 6f;
        [SerializeField] float fovSmoothing = 3f;

        public CameraView CurrentView { get; private set; }

        int _index;
        float _currentFov;
        Vector3 _smoothedPosition;
        Quaternion _smoothedRotation;

        void Awake()
        {
            if (targetCamera == null) targetCamera = GetComponentInChildren<Camera>();
            if (truck == null) truck = GetComponentInParent<TruckController>();

            if (views.Length == 0)
            {
                Debug.LogError("[CameraRig] No views configured.", this);
                enabled = false;
                return;
            }

            _index = Mathf.Max(0, System.Array.FindIndex(views, v => v.view == startingView));
            CurrentView = views[_index].view;
            _currentFov = views[_index].fieldOfView;

            SnapToView();
        }

        public void CycleView()
        {
            _index = (_index + 1) % views.Length;
            CurrentView = views[_index].view;
            SnapToView();
        }

        public void SetView(CameraView view)
        {
            int found = System.Array.FindIndex(views, v => v.view == view);
            if (found < 0) return;
            _index = found;
            CurrentView = view;
            SnapToView();
        }

        void SnapToView()
        {
            var setup = views[_index];
            var anchor = setup.anchor != null ? setup.anchor : transform;

            _smoothedPosition = anchor.TransformPoint(setup.localOffset);
            _smoothedRotation = anchor.rotation * Quaternion.Euler(setup.localEuler);

            targetCamera.transform.SetPositionAndRotation(_smoothedPosition, _smoothedRotation);
        }

        /// <summary>
        /// LateUpdate, not Update: the truck's Rigidbody has already been interpolated by then,
        /// so the camera does not lag a frame behind the vehicle it is bolted to.
        /// </summary>
        void LateUpdate()
        {
            var setup  = views[_index];
            var anchor = setup.anchor != null ? setup.anchor : transform;

            var desiredPosition = anchor.TransformPoint(setup.localOffset);
            var desiredRotation = anchor.rotation * Quaternion.Euler(setup.localEuler);

            if (setup.steerLookDegrees != 0f && truck != null)
                desiredRotation *= Quaternion.Euler(0f, truck.Steer * setup.steerLookDegrees, 0f);

            if (setup.followLag <= 0f)
            {
                _smoothedPosition = desiredPosition;
                _smoothedRotation = desiredRotation;
            }
            else
            {
                // Exponential smoothing, framerate-independent so it feels the same at 30 and 60 fps.
                float t = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.001f, setup.followLag * 0.5f));
                _smoothedPosition = Vector3.Lerp(_smoothedPosition, desiredPosition, t);
                _smoothedRotation = Quaternion.Slerp(_smoothedRotation, desiredRotation, t);
            }

            targetCamera.transform.SetPositionAndRotation(_smoothedPosition, _smoothedRotation);

            float speed01    = truck != null ? Mathf.Clamp01(truck.SpeedKmh / 90f) : 0f;
            float desiredFov = setup.fieldOfView + speed01 * speedFovBoost;
            _currentFov = Mathf.Lerp(_currentFov, desiredFov, fovSmoothing * Time.deltaTime);
            targetCamera.fieldOfView = _currentFov;
        }
    }
}
