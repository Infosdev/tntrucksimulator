using TNLorry.Gameplay;
using TNLorry.Services;
using TNLorry.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace TNLorry.World
{
    /// <summary>
    /// Sun/moon rotation, sky and ambient colour driven by GameClock (section N).
    ///
    /// Gradients rather than a physical sky model: a gradient evaluated once per frame costs
    /// nothing, looks right for a stylised art direction (section Q.1), and lets an artist
    /// tune dawn on the Western Ghats without touching code.
    /// </summary>
    public class DayNightCycle : MonoBehaviour
    {
        [Header("Lights")]
        [SerializeField] Light sun;
        [SerializeField] Light moon;
        [Tooltip("Sun colour across the day: 0 = midnight, 0.5 = noon, 1 = midnight.")]
        [SerializeField] Gradient sunColorOverDay;
        [SerializeField] Gradient ambientColorOverDay;
        [SerializeField] Gradient fogColorOverDay;

        [Header("Intensity")]
        [SerializeField] float peakSunIntensity = 1.35f;
        [SerializeField] float moonIntensity = 0.12f;
        [Tooltip("Sun elevation in degrees below which the sun light is switched off entirely.")]
        [SerializeField] float sunCutoffElevation = -4f;

        [Header("Orientation")]
        [Tooltip("Compass direction the sun rises from, in degrees. ~85 for Tamil Nadu.")]
        [SerializeField] float sunriseAzimuth = 85f;
        [Tooltip("Peak sun elevation at noon. TN sits near 11 N, so the sun gets very high.")]
        [SerializeField] float maxSunElevation = 78f;

        [Header("Street lighting")]
        [Tooltip("Emissive renderers switched on after dusk - street lights, shop fronts.")]
        [SerializeField] Renderer[] streetLights = new Renderer[0];
        [SerializeField] float streetLightOnHour = 18.2f;
        [SerializeField] float streetLightOffHour = 6.2f;

        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        GameClock _clock;
        bool _streetLightsOn;
        bool _lowTier;

        void Awake()
        {
            _clock = FindFirstObjectByType<GameClock>();
            if (_clock == null)
            {
                Debug.LogError("[DayNightCycle] No GameClock in the scene; lighting will not animate.", this);
                enabled = false;
                return;
            }

            _lowTier = ServiceLocator.TryGet<QualityService>(out var quality) &&
                       quality.ActiveTier == DeviceTierLevel.Low;

            if (sun == null) Debug.LogError("[DayNightCycle] No sun light assigned.", this);
        }

        void Update()
        {
            float hour = _clock.HourOfDay;
            float dayFraction = hour / 24f;

            // Elevation peaks at noon, is negative at night.
            float elevation = Mathf.Sin((hour / 24f - 0.25f) * Mathf.PI * 2f) * maxSunElevation;

            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(elevation, sunriseAzimuth, 0f);

                bool sunUp = elevation > sunCutoffElevation;
                sun.enabled = sunUp;

                if (sunUp)
                {
                    // Fade in over the first few degrees so sunrise is not a light switch.
                    float fade = Mathf.Clamp01((elevation - sunCutoffElevation) / 12f);
                    sun.intensity = peakSunIntensity * fade;
                    sun.color = sunColorOverDay.Evaluate(dayFraction);
                }
            }

            if (moon != null)
            {
                bool moonUp = elevation <= sunCutoffElevation;
                moon.enabled = moonUp && !_lowTier;   // one directional light is enough on Low
                if (moonUp)
                {
                    moon.transform.rotation = Quaternion.Euler(-elevation, sunriseAzimuth + 180f, 0f);
                    moon.intensity = moonIntensity;
                }
            }

            var ambient = ambientColorOverDay.Evaluate(dayFraction);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;

            if (RenderSettings.fog) RenderSettings.fogColor = fogColorOverDay.Evaluate(dayFraction);

            UpdateStreetLights(hour);
        }

        void UpdateStreetLights(float hour)
        {
            bool shouldBeOn = hour >= streetLightOnHour || hour < streetLightOffHour;
            if (shouldBeOn == _streetLightsOn) return;

            _streetLightsOn = shouldBeOn;
            var color = shouldBeOn ? new Color(1f, 0.82f, 0.5f) * 2f : Color.black;

            foreach (var renderer in streetLights)
                if (renderer != null) renderer.material.SetColor(EmissionColorId, color);
        }

        /// <summary>
        /// Called after a tile loads so its street lights match the current time instead of
        /// popping on at the next threshold crossing.
        /// </summary>
        public void RefreshStreetLights()
        {
            _streetLightsOn = !_streetLightsOn;    // force the next check to apply
            UpdateStreetLights(_clock.HourOfDay);
        }
    }
}
