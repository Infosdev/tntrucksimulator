using TNLorry.Core;
using TNLorry.Vehicle;
using UnityEngine;

namespace TNLorry.Gameplay
{
    /// <summary>
    /// Diesel consumption as a function of rpm, throttle, load and gear (section K.5).
    ///
    /// Modelled rather than a flat per-km drain, because the fuel-efficiency bonus should
    /// reward the thing it claims to reward: short-shifting, staying off the throttle downhill,
    /// and not flooring it up every gradient.
    /// </summary>
    [RequireComponent(typeof(TruckController))]
    public class FuelSystem : MonoBehaviour
    {
        [Header("Burn model")]
        [Tooltip("Extra burn from carried mass: multiplier at full payload.")]
        [SerializeField] float fullLoadBurnMultiplier = 1.45f;
        [Tooltip("Extra burn climbing a gradient, per unit of forward slope (0..1).")]
        [SerializeField] float gradientBurnMultiplier = 1.8f;

        [Header("Warnings")]
        [Tooltip("Fraction of tank at which the low-fuel warning fires.")]
        [SerializeField] float lowFuelFraction = 0.15f;

        public float Litres   { get; private set; }
        public float Capacity => _truck != null ? _truck.Data.fuelCapacityLitres : 0f;
        public float Fraction => Capacity <= 0f ? 0f : Litres / Capacity;
        public bool  IsEmpty  => Litres <= 0.01f;

        /// <summary>Litres burned since the job started — feeds the efficiency bonus.</summary>
        public float LitresBurnedThisJob { get; private set; }
        public float KmDrivenThisJob     { get; private set; }

        TruckController _truck;
        GameClock _clock;
        bool _lowWarningFired;
        float _publishTimer;

        void Awake()
        {
            _truck = GetComponent<TruckController>();
            _clock = FindFirstObjectByType<GameClock>();
            Litres = Capacity;   // overwritten by Restore when a save is loaded
        }

        public void Restore(float litres) => Litres = Mathf.Clamp(litres, 0f, Capacity);

        void Update()
        {
            if (_truck?.Engine == null || !_truck.Engine.IsRunning) return;

            float dt = Time.deltaTime;

            // Burn is quoted per in-game hour, so convert through the clock's time compression
            // rather than real seconds — otherwise a tank lasts a real-world week.
            float gameHours = dt * (_clock != null ? _clock.GameHoursPerRealSecond : 1f / 3600f);

            float burned = Mathf.Min(Litres, BurnLitresPerGameHour() * gameHours);
            Litres -= burned;
            LitresBurnedThisJob += burned;
            KmDrivenThisJob += _truck.SpeedKmh * dt / 3600f;

            if (IsEmpty && _truck.Engine.IsRunning)
            {
                _truck.Engine.Stop();
                EventBus.Publish(new OutOfFuelEvent());
            }

            if (!_lowWarningFired && Fraction <= lowFuelFraction)
            {
                _lowWarningFired = true;
                EventBus.Publish(new FuelChangedEvent { Litres = Litres, CapacityLitres = Capacity });
            }
            if (_lowWarningFired && Fraction > lowFuelFraction * 1.5f) _lowWarningFired = false;

            _publishTimer += dt;
            if (_publishTimer >= 0.5f)
            {
                _publishTimer = 0f;
                EventBus.Publish(new FuelChangedEvent { Litres = Litres, CapacityLitres = Capacity });
            }
        }

        float BurnLitresPerGameHour()
        {
            var data = _truck.Data;
            float idle = data.idleBurnLitresPerHour;
            float peak = data.peakBurnLitresPerHour;

            // Base burn interpolates between idle and peak on rpm and throttle together:
            // high rpm at no throttle (engine braking downhill) burns almost nothing.
            float demand = Mathf.Clamp01(_truck.Engine.Rpm01 * 0.4f + _truck.Throttle * 0.6f);
            float burn = Mathf.Lerp(idle, peak, demand);

            // Payload.
            float loadRatio = data.maxPayloadKg > 0f
                ? Mathf.Clamp01(_truck.CargoMassKg / data.maxPayloadKg) : 0f;
            burn *= Mathf.Lerp(1f, fullLoadBurnMultiplier, loadRatio);

            // Gradient: climbing costs, descending does not refund.
            float slope = Mathf.Max(0f, transform.forward.y);
            burn *= Mathf.Lerp(1f, gradientBurnMultiplier, slope);

            // A damaged engine is a thirsty engine.
            burn *= Mathf.Lerp(1f, 1.25f, _truck.Engine.Damage01);

            return burn;
        }

        /// <summary>Litres per 100 km over the current job, the number the HUD shows.</summary>
        public float LitresPer100Km =>
            KmDrivenThisJob < 0.5f ? 0f : LitresBurnedThisJob / KmDrivenThisJob * 100f;

        /// <summary>
        /// Refuelling at a petrol bunk. Returns litres actually added, so the caller can
        /// charge for exactly that much.
        /// </summary>
        public float Refuel(float requestedLitres)
        {
            float added = Mathf.Min(requestedLitres, Capacity - Litres);
            Litres += added;
            _lowWarningFired = false;

            if (!_truck.Engine.IsRunning && added > 0f) _truck.Engine.Start();

            EventBus.Publish(new FuelChangedEvent { Litres = Litres, CapacityLitres = Capacity });
            return added;
        }

        public float LitresToFill => Mathf.Max(0f, Capacity - Litres);

        public void ResetJobCounters()
        {
            LitresBurnedThisJob = 0f;
            KmDrivenThisJob = 0f;
        }

        /// <summary>
        /// True when this job used less than the benchmark, which earns the efficiency bonus.
        /// Benchmark is deliberately achievable: most careful players should get it.
        /// </summary>
        public bool WasEfficient(float benchmarkLitresPer100Km = 32f)
            => KmDrivenThisJob > 5f && LitresPer100Km > 0f && LitresPer100Km <= benchmarkLitresPer100Km;
    }
}
