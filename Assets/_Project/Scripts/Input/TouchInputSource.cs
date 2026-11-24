using TNLorry.Core;
using UnityEngine;

namespace TNLorry.InputSystem
{
    /// <summary>
    /// On-screen controls (section K.2). The UI calls the Set*/On* methods from its button and
    /// drag events; this class only smooths and normalises, so the same source serves all four
    /// steering modes and the HUD prefab decides which widgets are visible.
    ///
    /// Steering is smoothed rather than passed through raw: a finger on a 6-inch screen is
    /// jittery, and raw touch deltas on a 40-tonne vehicle feel broken.
    /// </summary>
    public class TouchInputSource : MonoBehaviour, IVehicleInputSource
    {
        [Header("Mode")]
        [SerializeField] SteeringMode mode = SteeringMode.Wheel;
        [SerializeField] float sensitivity = 1f;

        [Header("Smoothing")]
        [Tooltip("Seconds to reach the commanded steer value. 0 = instant (twitchy).")]
        [SerializeField] float steerSmoothTime = 0.12f;
        [Tooltip("Degrees of on-screen wheel rotation that equal full lock.")]
        [SerializeField] float wheelFullLockDegrees = 160f;
        [Tooltip("How fast steering returns to centre when nothing is held, in units/second.")]
        [SerializeField] float autoCentreRate = 2.5f;

        [Header("Tilt")]
        [SerializeField] float tiltDeadZone = 0.04f;
        [Tooltip("Device tilt in g that equals full lock.")]
        [SerializeField] float tiltFullLock = 0.45f;
        [SerializeField] bool  invertTilt;

        [Header("Pedals")]
        [Tooltip("Seconds for the throttle to reach full from a press. Diesel lag, and it stops jerky starts.")]
        [SerializeField] float throttleRamp = 0.25f;
        [SerializeField] float brakeRamp = 0.15f;

        [Header("Horn")]
        [Tooltip("Seconds of held horn that switches to the air horn.")]
        [SerializeField] float airHornHoldSeconds = 0.45f;

        public bool IsAvailable => true;
        public SteeringMode Mode { get => mode; set => mode = value; }
        public float Sensitivity { get => sensitivity; set => sensitivity = Mathf.Clamp(value, 0.2f, 3f); }
        public bool  InvertTilt  { get => invertTilt;  set => invertTilt = value; }

        // Raw UI state
        float _wheelDegrees;          // on-screen wheel rotation
        float _sliderValue;           // -1..1
        int   _buttonDirection;       // -1, 0, 1
        bool  _throttleHeld, _brakeHeld, _handBrakeHeld, _retarderHeld;
        bool  _hornHeld;
        float _hornHeldTime;

        // Smoothed output
        float _steer, _steerVelocity, _throttle, _brake;

        // Edge-triggered, consumed on read
        bool _shiftUp, _shiftDown, _lights, _wipers, _indLeft, _indRight, _hazard, _camera;

        void Awake() => LoadFromSettings();

        /// <summary>Picks up the player's saved steering preference, if a save is loaded.</summary>
        public void LoadFromSettings()
        {
            if (!ServiceLocator.TryGet<SaveSystem>(out var save) || save.Data == null) return;

            var s = save.Data.settings;
            mode        = (SteeringMode)Mathf.Clamp(s.steeringMode, 0, 3);
            sensitivity = s.steeringSensitivity <= 0f ? 1f : s.steeringSensitivity;
            invertTilt  = s.invertTilt;
        }

        public void SaveToSettings()
        {
            if (!ServiceLocator.TryGet<SaveSystem>(out var save) || save.Data == null) return;

            var s = save.Data.settings;
            s.steeringMode        = (int)mode;
            s.steeringSensitivity = sensitivity;
            s.invertTilt          = invertTilt;
            save.MarkDirty();
        }

        // ── UI entry points ─────────────────────────────────────────────────

        /// <summary>Called by the steering-wheel widget's drag handler, in degrees.</summary>
        public void SetWheelRotation(float degrees) => _wheelDegrees = degrees;

        /// <summary>Called by the slider widget, -1..1.</summary>
        public void SetSliderSteer(float value) => _sliderValue = Mathf.Clamp(value, -1f, 1f);

        public void OnSteerLeftDown()  => _buttonDirection = -1;
        public void OnSteerRightDown() => _buttonDirection = 1;
        public void OnSteerButtonUp()  => _buttonDirection = 0;

        public void SetThrottleHeld(bool held) => _throttleHeld = held;
        public void SetBrakeHeld(bool held)    => _brakeHeld = held;
        public void SetHandBrakeHeld(bool held) => _handBrakeHeld = held;
        public void SetRetarderHeld(bool held)  => _retarderHeld = held;

        public void SetHornHeld(bool held)
        {
            _hornHeld = held;
            if (!held) _hornHeldTime = 0f;
        }

        public void PressShiftUp()    => _shiftUp = true;
        public void PressShiftDown()  => _shiftDown = true;
        public void PressLights()     => _lights = true;
        public void PressWipers()     => _wipers = true;
        public void PressIndicateLeft()  => _indLeft = true;
        public void PressIndicateRight() => _indRight = true;
        public void PressHazard()     => _hazard = true;
        public void PressCamera()     => _camera = true;

        // ── Read ────────────────────────────────────────────────────────────

        public VehicleInputState Read(float dt)
        {
            float targetSteer = ReadSteerTarget(dt);

            _steer = steerSmoothTime <= 0f
                ? targetSteer
                : Mathf.SmoothDamp(_steer, targetSteer, ref _steerVelocity, steerSmoothTime, Mathf.Infinity, dt);

            _throttle = Mathf.MoveTowards(_throttle, _throttleHeld ? 1f : 0f, dt / Mathf.Max(0.01f, throttleRamp));
            _brake    = Mathf.MoveTowards(_brake,    _brakeHeld    ? 1f : 0f, dt / Mathf.Max(0.01f, brakeRamp));

            bool airHorn = false;
            if (_hornHeld)
            {
                _hornHeldTime += dt;
                airHorn = _hornHeldTime >= airHornHoldSeconds;
            }

            var state = new VehicleInputState
            {
                Steer         = Mathf.Clamp(_steer, -1f, 1f),
                Throttle      = _throttle,
                Brake         = _brake,
                HandBrake     = _handBrakeHeld,
                Retarder      = _retarderHeld,
                ShiftUp       = _shiftUp,
                ShiftDown     = _shiftDown,
                Horn          = _hornHeld && !airHorn,
                AirHorn       = airHorn,
                ToggleLights  = _lights,
                ToggleWipers  = _wipers,
                IndicateLeft  = _indLeft,
                IndicateRight = _indRight,
                Hazard        = _hazard,
                CycleCamera   = _camera
            };

            // Edge-triggered flags are consumed, so a single tap is never read twice.
            _shiftUp = _shiftDown = _lights = _wipers = false;
            _indLeft = _indRight = _hazard = _camera = false;

            return state;
        }

        float ReadSteerTarget(float dt)
        {
            switch (mode)
            {
                case SteeringMode.Wheel:
                    return Mathf.Clamp(_wheelDegrees / Mathf.Max(10f, wheelFullLockDegrees) * sensitivity, -1f, 1f);

                case SteeringMode.Slider:
                    return Mathf.Clamp(_sliderValue * sensitivity, -1f, 1f);

                case SteeringMode.Buttons:
                    // Buttons integrate toward lock while held and self-centre when released,
                    // which gives proportional control from two binary inputs.
                    if (_buttonDirection != 0)
                        return Mathf.Clamp(_steer + _buttonDirection * sensitivity * 2f * dt, -1f, 1f);
                    return Mathf.MoveTowards(_steer, 0f, autoCentreRate * dt);

                case SteeringMode.Tilt:
                    float raw = UnityEngine.Input.acceleration.x;
                    if (invertTilt) raw = -raw;
                    if (Mathf.Abs(raw) < tiltDeadZone) return 0f;
                    // Rescale past the dead zone so there is no jump at the threshold.
                    float signed = Mathf.Sign(raw) * (Mathf.Abs(raw) - tiltDeadZone) / Mathf.Max(0.01f, tiltFullLock - tiltDeadZone);
                    return Mathf.Clamp(signed * sensitivity, -1f, 1f);

                default:
                    return 0f;
            }
        }
    }
}
