using TNLorry.Core;
using TNLorry.Data;
using TNLorry.Vehicle;
using UnityEngine;

namespace TNLorry.Gameplay
{
    /// <summary>
    /// Driver fatigue (section K.5). After ~10 in-game driving hours the screen starts to
    /// blink and steering gets vague; resting at a lodge or dhaba clears it and saves.
    ///
    /// This is the one system with a real-world message attached, so it nudges rather than
    /// punishes: the effects are visible early and only get dangerous if ignored for hours.
    /// </summary>
    // Runs after TruckInputRouter (default order 0) so the drift it adds to Steer is not
    // overwritten by the input layer in the same frame.
    [DefaultExecutionOrder(100)]
    public class FatigueSystem : MonoBehaviour
    {
        [SerializeField] EconomyConfig config;
        [SerializeField] TruckController truck;

        [Header("Effects")]
        [Tooltip("Steering noise at full fatigue, as a fraction of input.")]
        [SerializeField] float maxSteerDrift = 0.12f;
        [Tooltip("Seconds between involuntary blinks at full fatigue.")]
        [SerializeField] float minBlinkInterval = 4f;

        /// <summary>0..1. Above ~0.5 the player should be looking for a lodge.</summary>
        public float Fatigue01 { get; private set; }
        public bool  IsDrowsy => Fatigue01 >= 0.5f;
        public bool  IsDangerous => Fatigue01 >= 0.85f;

        /// <summary>Raised when the vignette should blink; the HUD listens.</summary>
        public event System.Action<float> OnBlink;

        SaveSystem _save;
        GameClock _clock;
        float _drivingHours;
        float _blinkTimer;
        float _driftSeed;
        float _publishTimer;

        void Awake()
        {
            _save  = ServiceLocator.Get<SaveSystem>();
            _clock = FindFirstObjectByType<GameClock>();
            _driftSeed = Random.value * 100f;

            if (_save?.Data != null)
            {
                Fatigue01 = _save.Data.world.fatigue;
                _drivingHours = Fatigue01 * (config != null ? config.fatigueMaxHours : 14f);
            }
        }

        void Update()
        {
            if (truck == null || _clock == null) return;

            float dt = Time.deltaTime;

            // Only moving counts. Idling at a dhaba with the engine on is not driving.
            if (truck.SpeedKmh > 5f)
                _drivingHours += dt * _clock.GameHoursPerRealSecond;

            float onset = config != null ? config.fatigueOnsetHours : 10f;
            float max   = config != null ? config.fatigueMaxHours : 14f;

            Fatigue01 = Mathf.Clamp01((_drivingHours - onset) / Mathf.Max(0.1f, max - onset));

            if (Fatigue01 > 0.01f) TickEffects(dt);

            _publishTimer += dt;
            if (_publishTimer >= 1f)
            {
                _publishTimer = 0f;
                EventBus.Publish(new FatigueChangedEvent { Normalized = Fatigue01 });
                if (_save?.Data != null)
                {
                    _save.Data.world.fatigue = Fatigue01;
                    _save.MarkDirty();
                }
            }
        }

        void TickEffects(float dt)
        {
            // Involuntary blinks get more frequent as fatigue rises.
            _blinkTimer -= dt;
            if (_blinkTimer <= 0f)
            {
                float interval = Mathf.Lerp(20f, minBlinkInterval, Fatigue01);
                _blinkTimer = interval * Random.Range(0.7f, 1.3f);
                OnBlink?.Invoke(Fatigue01);
            }

            // Steering drift: low-frequency noise added to the player's input, so holding a
            // straight line takes active correction instead of becoming impossible.
            if (Fatigue01 > 0.3f && truck.SpeedKmh > 20f)
            {
                float noise = (Mathf.PerlinNoise(Time.time * 0.35f, _driftSeed) - 0.5f) * 2f;
                truck.Steer = Mathf.Clamp(truck.Steer + noise * maxSteerDrift * Fatigue01, -1f, 1f);
            }
        }

        /// <summary>
        /// Resting at a lodge, dhaba or tea kadai. Advances the clock, clears fatigue
        /// proportionally to hours slept, and is the natural save point (section K.5).
        /// </summary>
        public void Rest(int hours)
        {
            hours = Mathf.Clamp(hours, 1, 12);

            _clock?.AdvanceHours(hours);

            // 8 hours clears everything; a 2-hour nap takes the edge off.
            float recovery = hours / 8f;
            _drivingHours = Mathf.Max(0f, _drivingHours - (config != null ? config.fatigueMaxHours : 14f) * recovery);
            Fatigue01 = Mathf.Clamp01(Fatigue01 - recovery);

            if (_save?.Data != null)
            {
                _save.Data.world.fatigue = Fatigue01;
                _save.MarkDirty();
                _save.SaveImmediate();
            }

            EventBus.Publish(new RestCompletedEvent { HoursSlept = hours });
            EventBus.Publish(new FatigueChangedEvent { Normalized = Fatigue01 });
        }
    }
}
