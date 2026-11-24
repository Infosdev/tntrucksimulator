using UnityEngine;

namespace TNLorry.InputSystem
{
    /// <summary>
    /// Editor and desktop testing source, and the fallback for a connected Bluetooth
    /// controller's mapped axes (section K.2). Uses the legacy Input class so the project has
    /// no package dependency; swap for InputAction references when the Input System package
    /// is added — only this file changes.
    /// </summary>
    public class KeyboardInputSource : MonoBehaviour, IVehicleInputSource
    {
        [SerializeField] bool enableOnDevice;

        public bool IsAvailable =>
            Application.isEditor || !Application.isMobilePlatform || enableOnDevice;

        public VehicleInputState Read(float dt)
        {
            if (!IsAvailable) return default;

            // GetAxis is already smoothed by Unity's input settings.
            float steer    = UnityEngine.Input.GetAxis("Horizontal");
            float vertical = UnityEngine.Input.GetAxis("Vertical");

            return new VehicleInputState
            {
                Steer         = steer,
                Throttle      = Mathf.Max(0f, vertical),
                Brake         = Mathf.Max(0f, -vertical),
                HandBrake     = UnityEngine.Input.GetKey(KeyCode.Space),
                Retarder      = UnityEngine.Input.GetKey(KeyCode.LeftShift),
                ShiftUp       = UnityEngine.Input.GetKeyDown(KeyCode.E),
                ShiftDown     = UnityEngine.Input.GetKeyDown(KeyCode.Q),
                Horn          = UnityEngine.Input.GetKey(KeyCode.H),
                AirHorn       = UnityEngine.Input.GetKey(KeyCode.H) && UnityEngine.Input.GetKey(KeyCode.LeftControl),
                ToggleLights  = UnityEngine.Input.GetKeyDown(KeyCode.L),
                ToggleWipers  = UnityEngine.Input.GetKeyDown(KeyCode.P),
                IndicateLeft  = UnityEngine.Input.GetKeyDown(KeyCode.Z),
                IndicateRight = UnityEngine.Input.GetKeyDown(KeyCode.X),
                Hazard        = UnityEngine.Input.GetKeyDown(KeyCode.J),
                CycleCamera   = UnityEngine.Input.GetKeyDown(KeyCode.C)
            };
        }
    }
}
