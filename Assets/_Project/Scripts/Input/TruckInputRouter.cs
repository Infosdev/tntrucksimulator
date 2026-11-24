using TNLorry.Vehicle;
using UnityEngine;

namespace TNLorry.InputSystem
{
    /// <summary>
    /// Bridges input sources to the truck. Lives on the player truck prefab.
    ///
    /// Why a router rather than reading input inside TruckController: the tutorial, the parking
    /// mini-game and (later) a hired AI driver all need to drive the same truck, and camera /
    /// light / horn toggles are not physics concerns.
    /// </summary>
    [RequireComponent(typeof(TruckController))]
    public class TruckInputRouter : MonoBehaviour
    {
        [Tooltip("Checked in order; the first available source wins. Touch last on mobile, keyboard last in editor.")]
        [SerializeField] MonoBehaviour[] sourceBehaviours = new MonoBehaviour[0];

        [Header("Optional receivers")]
        [SerializeField] TruckLights lights;
        [SerializeField] TruckAudio truckAudio;
        [SerializeField] CameraRig cameraRig;

        [Tooltip("Blocks all driver input, e.g. during a cutscene or the result screen.")]
        public bool InputLocked { get; set; }

        TruckController _truck;
        IVehicleInputSource[] _sources;

        public VehicleInputState LastState { get; private set; }

        void Awake()
        {
            _truck = GetComponent<TruckController>();

            var valid = new System.Collections.Generic.List<IVehicleInputSource>(sourceBehaviours.Length);
            foreach (var behaviour in sourceBehaviours)
            {
                if (behaviour is IVehicleInputSource source) valid.Add(source);
                else if (behaviour != null)
                    Debug.LogError("[TruckInputRouter] " + behaviour.GetType().Name +
                                   " is not an IVehicleInputSource.", behaviour);
            }
            _sources = valid.ToArray();

            if (_sources.Length == 0)
                Debug.LogError("[TruckInputRouter] No input sources assigned; the truck will not respond.", this);
        }

        void Update()
        {
            float dt = Time.deltaTime;

            var state = InputLocked ? default : ReadActiveSource(dt);
            LastState = state;

            _truck.Throttle  = state.Throttle;
            _truck.Brake     = state.Brake;
            _truck.Steer     = state.Steer;
            _truck.HandBrake = state.HandBrake;
            _truck.Retarder  = state.Retarder;

            if (state.ShiftUp)   _truck.Gearbox.ShiftUp();
            if (state.ShiftDown) _truck.Gearbox.ShiftDown();

            if (lights != null)
            {
                if (state.ToggleLights)  lights.CycleHeadlights();
                if (state.Hazard)        lights.ToggleHazard();
                if (state.IndicateLeft)  lights.ToggleIndicator(left: true);
                if (state.IndicateRight) lights.ToggleIndicator(left: false);
            }

            if (truckAudio != null) truckAudio.SetHorn(state.Horn, state.AirHorn);
            if (cameraRig != null && state.CycleCamera) cameraRig.CycleView();
        }

        VehicleInputState ReadActiveSource(float dt)
        {
            // Sum rather than first-wins: on a device with a controller attached, the player may
            // use the on-screen pedals and the stick in the same session. Both stay live.
            var combined = new VehicleInputState();
            foreach (var source in _sources)
            {
                if (source == null || !source.IsAvailable) continue;
                var s = source.Read(dt);

                if (Mathf.Abs(s.Steer) > Mathf.Abs(combined.Steer)) combined.Steer = s.Steer;
                combined.Throttle = Mathf.Max(combined.Throttle, s.Throttle);
                combined.Brake    = Mathf.Max(combined.Brake, s.Brake);

                combined.HandBrake     |= s.HandBrake;
                combined.Retarder      |= s.Retarder;
                combined.ShiftUp       |= s.ShiftUp;
                combined.ShiftDown     |= s.ShiftDown;
                combined.Horn          |= s.Horn;
                combined.AirHorn       |= s.AirHorn;
                combined.ToggleLights  |= s.ToggleLights;
                combined.ToggleWipers  |= s.ToggleWipers;
                combined.IndicateLeft  |= s.IndicateLeft;
                combined.IndicateRight |= s.IndicateRight;
                combined.Hazard        |= s.Hazard;
                combined.CycleCamera   |= s.CycleCamera;
            }
            return combined;
        }
    }
}
