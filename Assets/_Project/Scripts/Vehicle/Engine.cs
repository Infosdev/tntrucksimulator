using TNLorry.Data;
using UnityEngine;

namespace TNLorry.Vehicle
{
    /// <summary>
    /// Diesel engine model (section K.1). Plain C# class, not a MonoBehaviour — it is ticked
    /// from TruckController.FixedUpdate so the whole drivetrain advances in one place.
    /// </summary>
    public class Engine
    {
        readonly TruckData _data;

        public float Rpm        { get; private set; }
        public float Torque     { get; private set; }
        public bool  IsRunning  { get; private set; }
        public bool  AtRedline  => Rpm >= _data.redlineRpm - 20f;
        public float IdleRpm    => _data.idleRpm;
        public float MaxRpm     => _data.maxRpm;
        public float Rpm01      => Mathf.Clamp01((Rpm - _data.idleRpm) / Mathf.Max(1f, _data.maxRpm - _data.idleRpm));

        /// <summary>0..1 engine damage. Scales peak torque down — a blown engine still limps.</summary>
        public float Damage01 { get; set; }

        public Engine(TruckData data)
        {
            _data = data;
            Rpm = 0f;
            IsRunning = false;
        }

        public void Start()
        {
            IsRunning = true;
            Rpm = _data.idleRpm;
        }

        public void Stop()
        {
            IsRunning = false;
            Torque = 0f;
        }

        /// <summary>
        /// targetRpm comes from the gearbox (wheel speed x ratio) when a gear is engaged, or is
        /// free-revving when in neutral / mid-shift. Throttle is 0..1.
        /// </summary>
        public void Tick(float targetRpm, float throttle, float dt, bool clutchEngaged)
        {
            if (!IsRunning)
            {
                Rpm = Mathf.MoveTowards(Rpm, 0f, 1200f * dt);
                Torque = 0f;
                return;
            }

            throttle = Mathf.Clamp01(throttle);

            // With the clutch in, rpm is dictated by the wheels. With it out, the engine
            // follows throttle demand and falls back to idle when the pedal is released.
            float demandRpm = clutchEngaged
                ? Mathf.Max(targetRpm, _data.idleRpm)
                : Mathf.Lerp(_data.idleRpm, _data.maxRpm, throttle);

            Rpm = Mathf.Lerp(Rpm, demandRpm, 1f - Mathf.Exp(-_data.rpmResponse * dt));
            Rpm = Mathf.Clamp(Rpm, 0f, _data.maxRpm);

            // Rev limiter: cut fuel above redline so the engine cannot be run past it.
            float available = Rpm >= _data.redlineRpm ? 0f : _data.TorqueAt(Rpm);

            // Damage costs power (section K.6) but never all of it, so the player can limp home.
            float healthFactor = Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(Damage01));

            Torque = available * throttle * healthFactor;
        }

        /// <summary>
        /// Engine braking at the wheels when off-throttle — the thing that makes a loaded truck
        /// feel heavy, and what you actually use descending the Kallatti ghat.
        /// </summary>
        public float EngineBrakeTorque(float throttle)
        {
            if (!IsRunning) return 0f;
            float offThrottle = 1f - Mathf.Clamp01(throttle);
            return _data.engineBrakeTorque * offThrottle * Rpm01;
        }

        /// <summary>Normalised load for the audio layer's RPM/load crossfade (section R).</summary>
        public float AudioLoad01(float throttle)
            => Mathf.Clamp01(throttle * 0.7f + Rpm01 * 0.3f);
    }
}
