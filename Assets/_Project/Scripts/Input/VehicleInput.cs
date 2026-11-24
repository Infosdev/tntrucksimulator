using UnityEngine;

namespace TNLorry.InputSystem
{
    /// <summary>Four steering modes, because players are picky about this (section K.2).</summary>
    public enum SteeringMode { Wheel = 0, Buttons = 1, Tilt = 2, Slider = 3 }

    /// <summary>One frame of driver intent, normalised and device-agnostic.</summary>
    public struct VehicleInputState
    {
        public float Steer;        // -1..1
        public float Throttle;     // 0..1
        public float Brake;        // 0..1
        public bool  HandBrake;
        public bool  Retarder;
        public bool  ShiftUp;      // edge-triggered
        public bool  ShiftDown;    // edge-triggered
        public bool  Horn;
        public bool  AirHorn;      // long-press on horn
        public bool  ToggleLights;
        public bool  ToggleWipers;
        public bool  IndicateLeft;
        public bool  IndicateRight;
        public bool  Hazard;
        public bool  CycleCamera;
    }

    /// <summary>
    /// Anything that can produce driver intent: the on-screen controls, tilt, a gamepad,
    /// the tutorial's scripted playback, or an AI driver taking over the player truck.
    /// </summary>
    public interface IVehicleInputSource
    {
        VehicleInputState Read(float dt);
        bool IsAvailable { get; }
    }
}
