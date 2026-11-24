using TNLorry.Core;
using TNLorry.Data;
using UnityEngine;

namespace TNLorry.Vehicle
{
    /// <summary>
    /// Multi-speed gearbox with auto and manual modes (section K.1 / F.2).
    ///
    /// Gear indexing: -1 = reverse, 0 = neutral, 1..N = forward gears, where gear n maps to
    /// data.gearRatios[n - 1]. Keeping neutral at 0 means "is a gear engaged" is just != 0.
    /// </summary>
    public class Gearbox
    {
        public const int Reverse = -1;
        public const int Neutral = 0;

        readonly TruckData _data;

        public int   CurrentGear { get; private set; } = Neutral;
        public bool  IsShifting  => _shiftTimer > 0f;
        public bool  AutoShift   { get; set; }

        float _shiftTimer;
        int   _pendingGear;
        float _shiftCooldown;

        /// <summary>Upshift above this fraction of redline, downshift below the lower bound.</summary>
        const float UpshiftRpmFraction   = 0.82f;
        const float DownshiftRpmFraction = 0.42f;

        /// <summary>Blocks hunting between two gears on rolling ground.</summary>
        const float ShiftCooldownSeconds = 1.2f;

        public Gearbox(TruckData data)
        {
            _data     = data;
            AutoShift = data.autoShift;
        }

        /// <summary>Ratio including final drive. 0 while in neutral or mid-shift (torque interrupted).</summary>
        public float CurrentRatio
        {
            get
            {
                if (IsShifting || CurrentGear == Neutral) return 0f;
                if (CurrentGear == Reverse) return -_data.gearRatios[0] * _data.finalDrive;
                return _data.gearRatios[CurrentGear - 1] * _data.finalDrive;
            }
        }

        public string GearLabel =>
            CurrentGear == Reverse ? "R" :
            CurrentGear == Neutral ? "N" : CurrentGear.ToString();

        // ── Shifting ────────────────────────────────────────────────────────

        public void ShiftTo(int gear)
        {
            gear = Mathf.Clamp(gear, Reverse, _data.GearCount);
            if (gear == CurrentGear || IsShifting) return;

            _pendingGear = gear;
            _shiftTimer  = _data.shiftTime;
        }

        public void ShiftUp()
        {
            if (CurrentGear == Reverse) { ShiftTo(Neutral); return; }
            if (CurrentGear < _data.GearCount) ShiftTo(CurrentGear + 1);
        }

        public void ShiftDown()
        {
            if (CurrentGear > 1) ShiftTo(CurrentGear - 1);
            else if (CurrentGear == 1) ShiftTo(Neutral);
            else if (CurrentGear == Neutral) ShiftTo(Reverse);
        }

        public void Tick(float dt, float engineRpm, float throttle, float speedKmh, float brake)
        {
            if (_shiftCooldown > 0f) _shiftCooldown -= dt;

            if (IsShifting)
            {
                _shiftTimer -= dt;
                if (_shiftTimer <= 0f)
                {
                    _shiftTimer = 0f;
                    CurrentGear = _pendingGear;
                    _shiftCooldown = ShiftCooldownSeconds;
                    EventBus.Publish(new GearChangedEvent { GearIndex = CurrentGear, Label = GearLabel });
                }
                return;
            }

            if (AutoShift) TickAutoShift(engineRpm, throttle, speedKmh, brake);
        }

        void TickAutoShift(float engineRpm, float throttle, float speedKmh, float brake)
        {
            if (_shiftCooldown > 0f) return;

            // Standing still: pick a direction from the pedals rather than leaving it in gear.
            if (speedKmh < 1.5f)
            {
                if (CurrentGear == Neutral && throttle > 0.05f) ShiftTo(1);
                return;
            }

            if (CurrentGear <= Neutral) return;

            float upshiftRpm   = _data.redlineRpm * UpshiftRpmFraction;
            float downshiftRpm = _data.redlineRpm * DownshiftRpmFraction;

            // Hold the lower gear under braking or on a steep descent — this is what keeps the
            // truck from freewheeling down a ghat on its service brakes alone.
            bool holdLowGear = brake > 0.2f;

            if (engineRpm > upshiftRpm && CurrentGear < _data.GearCount && !holdLowGear)
            {
                ShiftUp();
            }
            else if (engineRpm < downshiftRpm && CurrentGear > 1)
            {
                ShiftDown();
            }
        }

        // ── Drivetrain maths ────────────────────────────────────────────────

        /// <summary>Engine rpm implied by wheel rpm through the current ratio, clamped at idle.</summary>
        public float WheelToEngineRpm(float wheelRpm, float idleRpm)
        {
            float ratio = Mathf.Abs(CurrentRatio);
            if (ratio < 0.001f) return idleRpm;
            return Mathf.Max(idleRpm, Mathf.Abs(wheelRpm) * ratio);
        }

        public bool IsClutchEngaged => !IsShifting && CurrentGear != Neutral;

        /// <summary>Road speed at redline in the current gear, used by the HUD shift indicator.</summary>
        public float MaxSpeedKmh(float wheelRadius)
        {
            float ratio = Mathf.Abs(CurrentRatio);
            if (ratio < 0.001f) return 0f;
            float wheelRpm = _data.redlineRpm / ratio;
            return wheelRpm * 60f * 2f * Mathf.PI * wheelRadius / 1000f;
        }

        public void ForceNeutral()
        {
            CurrentGear = Neutral;
            _shiftTimer = 0f;
        }
    }
}
