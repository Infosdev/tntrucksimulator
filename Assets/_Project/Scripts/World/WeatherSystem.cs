using TNLorry.Core;
using TNLorry.Gameplay;
using TNLorry.Services;
using TNLorry.Vehicle;
using UnityEngine;

namespace TNLorry.World
{
    /// <summary>
    /// Weather and its gameplay consequences (section N). Weather is not decoration here: it
    /// changes tyre grip, AI speed, visibility and rain-sensitive cargo.
    ///
    /// Seasonally weighted — the north-east monsoon (Oct-Dec) is when Tamil Nadu actually gets
    /// its rain, so a December job should feel different from an April one.
    /// </summary>
    public class WeatherSystem : MonoBehaviour
    {
        [Header("Transitions")]
        [Tooltip("In-game hours between weather rolls.")]
        [SerializeField] float rollIntervalHours = 3f;
        [Tooltip("Seconds to blend from one weather state to the next.")]
        [SerializeField] float transitionSeconds = 25f;

        [Header("Grip")]
        [Tooltip("Forward/sideways friction multiplier in heavy rain. Below ~0.7 feels like ice.")]
        [Range(0.5f, 1f)] [SerializeField] float wetGripMultiplier = 0.78f;

        [Header("Visibility")]
        [SerializeField] float clearFogDensity = 0.002f;
        [SerializeField] float rainFogDensity = 0.012f;
        [SerializeField] float ghatFogDensity = 0.035f;

        [Header("Wind (coastal / Aralvaimozhi wind farms)")]
        [Tooltip("Lateral force per tonne at full wind, in newtons. High-sided trucks notice it.")]
        [SerializeField] float windForcePerTonne = 55f;

        [Header("Scene refs")]
        [SerializeField] ParticleSystem rainParticles;
        [SerializeField] ParticleSystem splashParticles;
        [SerializeField] AudioSource rainAudio;
        [SerializeField] TruckController playerTruck;

        public WeatherType Current { get; private set; } = WeatherType.Clear;
        /// <summary>0..1, blended. Driving systems read this rather than the enum.</summary>
        public float Intensity { get; private set; }
        public float WindStrength { get; private set; }
        /// <summary>Grip multiplier applied to every wheel. 1 = dry.</summary>
        public float GripMultiplier { get; private set; } = 1f;

        GameClock _clock;
        SaveSystem _save;
        WeatherType _target = WeatherType.Clear;
        float _targetIntensity;
        float _nextRollHour;
        float _blend;
        bool  _lowTier;

        // Cached dry friction so the wet multiplier is applied to the authored value, not
        // compounded every frame.
        struct WheelFriction { public WheelCollider Wheel; public float ForwardStiffness; public float SidewaysStiffness; }
        WheelFriction[] _wheels = new WheelFriction[0];

        void Awake()
        {
            _clock = FindFirstObjectByType<GameClock>();
            ServiceLocator.TryGet<SaveSystem>(out _save);
            _lowTier = ServiceLocator.TryGet<QualityService>(out var quality) &&
                       quality.ActiveTier == DeviceTierLevel.Low;

            if (_save?.Data != null)
            {
                Current = (WeatherType)_save.Data.world.weather;
                _target = Current;
                Intensity = _targetIntensity = Current == WeatherType.Clear ? 0f : 0.6f;
            }

            CacheWheelFriction();
            ApplyVisuals(immediate: true);
        }

        void CacheWheelFriction()
        {
            if (playerTruck == null) return;

            var colliders = playerTruck.GetComponentsInChildren<WheelCollider>();
            _wheels = new WheelFriction[colliders.Length];
            for (int i = 0; i < colliders.Length; i++)
            {
                _wheels[i] = new WheelFriction
                {
                    Wheel             = colliders[i],
                    ForwardStiffness  = colliders[i].forwardFriction.stiffness,
                    SidewaysStiffness = colliders[i].sidewaysFriction.stiffness
                };
            }
        }

        void Update()
        {
            if (_clock == null) return;

            // Roll new weather on the in-game clock, not real time, so a long haul sees
            // the weather actually change.
            float absoluteHour = _clock.Day * 24f + _clock.HourOfDay;
            if (absoluteHour >= _nextRollHour)
            {
                _nextRollHour = absoluteHour + rollIntervalHours;
                Roll();
            }

            TickTransition(Time.deltaTime);
            ApplyGrip();
        }

        // Forces belong in the physics step, or wind strength would vary with framerate.
        void FixedUpdate() => ApplyWind();

        void Roll()
        {
            // Monsoon weighting. The north-east monsoon is Oct-Dec; summer (Mar-Jun) is dry.
            int month = System.DateTime.Now.Month;
            bool monsoon = month >= 10 || month == 1;
            bool summer  = month >= 3 && month <= 6;

            float rainChance = monsoon ? 0.55f : summer ? 0.08f : 0.22f;
            float fogChance  = _clock.IsDawn ? 0.3f : 0.05f;

            float roll = Random.value;

            if (roll < fogChance)
            {
                _target = WeatherType.Fog;
                _targetIntensity = Random.Range(0.4f, 0.9f);
            }
            else if (roll < fogChance + rainChance)
            {
                bool heavy = Random.value < (monsoon ? 0.4f : 0.2f);
                _target = heavy ? WeatherType.HeavyRain : WeatherType.Rain;
                _targetIntensity = heavy ? Random.Range(0.7f, 1f) : Random.Range(0.3f, 0.7f);
            }
            else if (roll < fogChance + rainChance + 0.2f)
            {
                _target = WeatherType.Cloudy;
                _targetIntensity = Random.Range(0.3f, 0.7f);
            }
            else
            {
                _target = WeatherType.Clear;
                _targetIntensity = 0f;
            }

            _blend = 0f;
        }

        void TickTransition(float dt)
        {
            if (_blend >= 1f && Current == _target) return;

            _blend = Mathf.MoveTowards(_blend, 1f, dt / Mathf.Max(1f, transitionSeconds));
            Intensity = Mathf.Lerp(Intensity, _targetIntensity, _blend);

            if (_blend >= 1f && Current != _target)
            {
                Current = _target;
                if (_save?.Data != null)
                {
                    _save.Data.world.weather = (int)Current;
                    _save.MarkDirty();
                }
                EventBus.Publish(new WeatherChangedEvent { Type = Current, Intensity = Intensity });
            }

            ApplyVisuals(immediate: false);
        }

        void ApplyVisuals(bool immediate)
        {
            bool raining = Current == WeatherType.Rain || Current == WeatherType.HeavyRain;

            if (rainParticles != null)
            {
                var emission = rainParticles.emission;
                // Low tier gets a reduced particle count rather than no rain — rain is a
                // headline feature and cutting it entirely is worse than thinning it.
                float budget = _lowTier ? 600f : 2500f;
                emission.rateOverTime = raining ? budget * Intensity : 0f;

                if (raining && !rainParticles.isPlaying) rainParticles.Play();
                if (!raining && rainParticles.isPlaying) rainParticles.Stop();
            }

            if (splashParticles != null)
            {
                var emission = splashParticles.emission;
                emission.rateOverTime = Current == WeatherType.HeavyRain ? 300f * Intensity : 0f;
            }

            if (rainAudio != null)
            {
                float targetVolume = raining ? Mathf.Clamp01(Intensity) * 0.8f : 0f;
                rainAudio.volume = immediate ? targetVolume
                    : Mathf.Lerp(rainAudio.volume, targetVolume, Time.deltaTime * 2f);
                if (targetVolume > 0.01f && !rainAudio.isPlaying) rainAudio.Play();
                if (targetVolume <= 0.01f && rainAudio.isPlaying) rainAudio.Stop();
            }

            float fogDensity = Current switch
            {
                WeatherType.Fog       => Mathf.Lerp(clearFogDensity, ghatFogDensity, Intensity),
                WeatherType.HeavyRain => Mathf.Lerp(clearFogDensity, rainFogDensity, Intensity),
                WeatherType.Rain      => Mathf.Lerp(clearFogDensity, rainFogDensity * 0.7f, Intensity),
                _                     => clearFogDensity
            };

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = fogDensity;
        }

        void ApplyGrip()
        {
            bool wet = Current == WeatherType.Rain || Current == WeatherType.HeavyRain;
            GripMultiplier = wet ? Mathf.Lerp(1f, wetGripMultiplier, Intensity) : 1f;

            foreach (var entry in _wheels)
            {
                if (entry.Wheel == null) continue;

                var forward = entry.Wheel.forwardFriction;
                forward.stiffness = entry.ForwardStiffness * GripMultiplier;
                entry.Wheel.forwardFriction = forward;

                var sideways = entry.Wheel.sidewaysFriction;
                sideways.stiffness = entry.SidewaysStiffness * GripMultiplier;
                entry.Wheel.sidewaysFriction = sideways;
            }

            // Rain-sensitive cargo under a torn tarp (section E.1).
            if (playerTruck != null && playerTruck.TryGetComponent<DamageSystem>(out var damage))
                damage.RainIntensity = wet ? Intensity : 0f;
        }

        void ApplyWind()
        {
            // Coastal ECR and the Aralvaimozhi wind farms get a steady lateral push.
            WindStrength = Current == WeatherType.HeavyRain ? Intensity : Intensity * 0.3f;
            if (playerTruck == null || WindStrength <= 0.05f) return;

            float tonnes = playerTruck.Body.mass / 1000f;
            var force = playerTruck.transform.right * (windForcePerTonne * tonnes * WindStrength);
            playerTruck.Body.AddForce(force);
        }

        /// <summary>Scripted weather for a tutorial beat, a ghat fog sequence, or a LiveOps event.</summary>
        public void ForceWeather(WeatherType type, float intensity, bool immediate = false)
        {
            _target = type;
            _targetIntensity = Mathf.Clamp01(intensity);
            _blend = immediate ? 1f : 0f;

            if (immediate)
            {
                Current = type;
                Intensity = _targetIntensity;
                ApplyVisuals(immediate: true);
                EventBus.Publish(new WeatherChangedEvent { Type = Current, Intensity = Intensity });
            }
        }
    }
}
