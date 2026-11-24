using UnityEngine;

namespace TNLorry.Vehicle
{
    public enum HeadlightState { Off = 0, Low = 1, High = 2 }

    /// <summary>
    /// Lights, indicators and the lorry's decorative LED work (sections K.7, F.3).
    ///
    /// Real lights are expensive on mobile, so only the headlights are actual Light components
    /// and they are disabled entirely on the Low tier — everything else is emissive material
    /// swapping, which costs nothing.
    /// </summary>
    public class TruckLights : MonoBehaviour
    {
        [Header("Headlights (real lights)")]
        [SerializeField] Light[] headlights = new Light[0];
        [SerializeField] float lowBeamIntensity = 2.2f;
        [SerializeField] float highBeamIntensity = 4.5f;
        [SerializeField] float lowBeamAngle = 70f;
        [SerializeField] float highBeamAngle = 45f;

        [Header("Emissive renderers")]
        [SerializeField] Renderer[] headlightGlow = new Renderer[0];
        [SerializeField] Renderer[] tailLights = new Renderer[0];
        [SerializeField] Renderer[] brakeLights = new Renderer[0];
        [SerializeField] Renderer[] reverseLights = new Renderer[0];
        [SerializeField] Renderer[] leftIndicators = new Renderer[0];
        [SerializeField] Renderer[] rightIndicators = new Renderer[0];
        [Tooltip("Decorative top/side marker LEDs - the strips every TN lorry runs.")]
        [SerializeField] Renderer[] decorativeLights = new Renderer[0];

        [Header("Timing")]
        [SerializeField] float indicatorHz = 1.5f;

        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        public HeadlightState Headlights { get; private set; } = HeadlightState.Off;
        public bool HazardOn { get; private set; }
        public bool LeftIndicatorOn  { get; private set; }
        public bool RightIndicatorOn { get; private set; }
        /// <summary>True when any forward lighting is on — the no-headlights fine checks this.</summary>
        public bool AnyHeadlightOn => Headlights != HeadlightState.Off;

        TruckController _truck;
        float _blinkTimer;
        bool  _blinkOn;
        bool  _decorativeOn = true;

        void Awake()
        {
            _truck = GetComponentInParent<TruckController>();
            ApplyHeadlights();
            SetEmissive(decorativeLights, _decorativeOn, new Color(1f, 0.85f, 0.4f));
        }

        void Update()
        {
            float dt = Time.deltaTime;

            // Indicator blink
            if (HazardOn || LeftIndicatorOn || RightIndicatorOn)
            {
                _blinkTimer += dt * indicatorHz * 2f;
                if (_blinkTimer >= 1f)
                {
                    _blinkTimer = 0f;
                    _blinkOn = !_blinkOn;
                }
            }
            else
            {
                _blinkOn = false;
                _blinkTimer = 0f;
            }

            var amber = new Color(1f, 0.55f, 0f);
            SetEmissive(leftIndicators,  _blinkOn && (HazardOn || LeftIndicatorOn),  amber);
            SetEmissive(rightIndicators, _blinkOn && (HazardOn || RightIndicatorOn), amber);

            if (_truck == null) return;

            bool braking   = _truck.Brake > 0.05f || _truck.HandBrake;
            bool reversing = _truck.Gearbox.CurrentGear == Gearbox.Reverse && _truck.ForwardSpeedKmh < -0.5f;

            SetEmissive(brakeLights,   braking,   new Color(1f, 0.05f, 0.05f));
            SetEmissive(reverseLights, reversing, Color.white);
            SetEmissive(tailLights,    AnyHeadlightOn, new Color(0.6f, 0.03f, 0.03f));
        }

        public void CycleHeadlights()
        {
            Headlights = Headlights switch
            {
                HeadlightState.Off  => HeadlightState.Low,
                HeadlightState.Low  => HeadlightState.High,
                _                   => HeadlightState.Off
            };
            ApplyHeadlights();
        }

        public void SetHeadlights(HeadlightState state)
        {
            Headlights = state;
            ApplyHeadlights();
        }

        void ApplyHeadlights()
        {
            bool on = Headlights != HeadlightState.Off;
            bool high = Headlights == HeadlightState.High;

            // Real-time lights are the single biggest night-time cost on low-end GPUs.
            // The Low tier runs on the emissive glow and the baked road brightening instead.
            bool allowRealLights = !IsLowTier();

            foreach (var light in headlights)
            {
                if (light == null) continue;
                light.enabled   = on && allowRealLights;
                light.intensity = high ? highBeamIntensity : lowBeamIntensity;
                light.spotAngle = high ? highBeamAngle : lowBeamAngle;
            }

            SetEmissive(headlightGlow, on, high ? Color.white : new Color(0.9f, 0.9f, 0.75f));
        }

        static bool IsLowTier()
        {
            return Core.ServiceLocator.TryGet<Services.QualityService>(out var quality) &&
                   quality.ActiveTier == Services.DeviceTierLevel.Low;
        }

        public void ToggleIndicator(bool left)
        {
            if (left)
            {
                LeftIndicatorOn = !LeftIndicatorOn;
                if (LeftIndicatorOn) RightIndicatorOn = false;
            }
            else
            {
                RightIndicatorOn = !RightIndicatorOn;
                if (RightIndicatorOn) LeftIndicatorOn = false;
            }
        }

        public void ToggleHazard() => HazardOn = !HazardOn;

        public void ToggleDecorative()
        {
            _decorativeOn = !_decorativeOn;
            SetEmissive(decorativeLights, _decorativeOn, new Color(1f, 0.85f, 0.4f));
        }

        /// <summary>Cancels the indicator after a turn is completed, like a real self-cancelling stalk.</summary>
        public void AutoCancelIndicators() => LeftIndicatorOn = RightIndicatorOn = false;

        static void SetEmissive(Renderer[] renderers, bool on, Color color)
        {
            if (renderers == null) return;
            var value = on ? color : Color.black;

            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                // Instanced property, so toggling one truck's lights does not touch the shared material.
                renderer.material.SetColor(EmissionColorId, value);
            }
        }
    }
}
