using TNLorry.Core;
using UnityEngine;

namespace TNLorry.Vehicle
{
    /// <summary>
    /// RPM-layered diesel engine sound plus the noises that actually sell "truck" (section R):
    /// turbo whistle, air-brake hiss, gear clunk, reverse beeper, horn and air horn.
    ///
    /// Four crossfaded loops rather than one pitched clip — a single clip pitched across
    /// 650-2600 rpm sounds like a chainsaw. If the project moves to FMOD, this class becomes a
    /// parameter writer and the crossfade logic goes away.
    /// </summary>
    [RequireComponent(typeof(TruckController))]
    public class TruckAudio : MonoBehaviour
    {
        [System.Serializable]
        public class EngineLayer
        {
            public AudioSource source;
            [Tooltip("Engine rpm at which this layer is at full volume.")]
            public float centreRpm;
            [Tooltip("Rpm range either side over which it fades out.")]
            public float falloffRpm = 500f;
            [Tooltip("Volume ceiling for this layer.")]
            [Range(0f, 1f)] public float maxVolume = 1f;
            [Tooltip("Only audible under load (on-throttle). Off-load layers use the inverse.")]
            public bool onLoadOnly;
            public bool offLoadOnly;
        }

        [Header("Engine layers (idle -> low -> mid -> high)")]
        [SerializeField] EngineLayer[] layers = new EngineLayer[0];
        [Tooltip("Slight pitch movement within a layer keeps it from sounding static.")]
        [SerializeField] float pitchRange = 0.25f;

        [Header("One-shots")]
        [SerializeField] AudioSource oneShotSource;
        [SerializeField] AudioClip airBrakeHiss;
        [SerializeField] AudioClip gearClunk;
        [SerializeField] AudioClip engineStart;

        [Header("Loops")]
        [SerializeField] AudioSource turboSource;
        [SerializeField] AudioSource hornSource;
        [SerializeField] AudioClip hornClip;
        [SerializeField] AudioClip airHornClip;
        [SerializeField] AudioSource reverseBeeperSource;
        [SerializeField] AudioSource tyreRollSource;

        [Header("Mix")]
        [SerializeField] float crossfadeSpeed = 8f;
        [Tooltip("Brake release above this value triggers the air hiss.")]
        [SerializeField] float hissBrakeThreshold = 0.35f;

        TruckController _truck;
        float _previousBrake;
        bool  _hornWasPlaying;

        void Awake()
        {
            _truck = GetComponent<TruckController>();

            foreach (var layer in layers)
            {
                if (layer.source == null) continue;
                layer.source.loop = true;
                layer.source.volume = 0f;
                layer.source.playOnAwake = false;
                layer.source.Play();      // always running; volume does the work
            }

            if (turboSource != null)         { turboSource.loop = true; turboSource.volume = 0f; turboSource.Play(); }
            if (tyreRollSource != null)      { tyreRollSource.loop = true; tyreRollSource.volume = 0f; tyreRollSource.Play(); }
            if (reverseBeeperSource != null) { reverseBeeperSource.loop = true; reverseBeeperSource.Stop(); }

            if (engineStart != null && oneShotSource != null) oneShotSource.PlayOneShot(engineStart);
        }

        void OnEnable()  => EventBus.Subscribe<GearChangedEvent>(OnGearChanged);
        void OnDisable() => EventBus.Unsubscribe<GearChangedEvent>(OnGearChanged);

        void OnGearChanged(GearChangedEvent e)
        {
            if (gearClunk != null && oneShotSource != null) oneShotSource.PlayOneShot(gearClunk, 0.5f);
        }

        void Update()
        {
            if (_truck?.Engine == null) return;

            float dt       = Time.deltaTime;
            float rpm      = _truck.Engine.Rpm;
            float throttle = _truck.Throttle;

            UpdateEngineLayers(rpm, throttle, dt);

            if (turboSource != null)
            {
                float turbo = Mathf.Clamp01(_truck.Engine.Rpm01 * throttle) * 0.6f;
                turboSource.volume = Mathf.Lerp(turboSource.volume, turbo, crossfadeSpeed * dt);
                turboSource.pitch  = 0.9f + _truck.Engine.Rpm01 * 0.5f;
            }

            if (tyreRollSource != null)
            {
                float speed01 = Mathf.Clamp01(_truck.SpeedKmh / 80f);
                tyreRollSource.volume = Mathf.Lerp(tyreRollSource.volume, speed01 * 0.4f, crossfadeSpeed * dt);
                tyreRollSource.pitch  = 0.8f + speed01 * 0.4f;
            }

            // Air-brake hiss fires on *release*, which is when a real truck hisses.
            if (_previousBrake > hissBrakeThreshold && _truck.Brake <= 0.05f &&
                airBrakeHiss != null && oneShotSource != null)
                oneShotSource.PlayOneShot(airBrakeHiss, 0.7f);
            _previousBrake = _truck.Brake;

            if (reverseBeeperSource != null)
            {
                bool shouldBeep = _truck.Gearbox.CurrentGear == Gearbox.Reverse;
                if (shouldBeep && !reverseBeeperSource.isPlaying) reverseBeeperSource.Play();
                else if (!shouldBeep && reverseBeeperSource.isPlaying) reverseBeeperSource.Stop();
            }
        }

        void UpdateEngineLayers(float rpm, float throttle, float dt)
        {
            bool onLoad = throttle > 0.1f;

            foreach (var layer in layers)
            {
                if (layer.source == null) continue;

                float distance = Mathf.Abs(rpm - layer.centreRpm);
                float weight   = 1f - Mathf.Clamp01(distance / Mathf.Max(1f, layer.falloffRpm));

                if (layer.onLoadOnly  && !onLoad) weight = 0f;
                if (layer.offLoadOnly && onLoad)  weight = 0f;

                float target = weight * layer.maxVolume;
                layer.source.volume = Mathf.Lerp(layer.source.volume, target, crossfadeSpeed * dt);

                // Pitch tracks rpm within the layer's own window, so the sound rises
                // continuously instead of stepping at each crossover.
                float local = Mathf.Clamp((rpm - layer.centreRpm) / Mathf.Max(1f, layer.falloffRpm), -1f, 1f);
                layer.source.pitch = 1f + local * pitchRange;
            }
        }

        public void SetHorn(bool horn, bool airHorn)
        {
            if (hornSource == null) return;

            bool shouldPlay = horn || airHorn;
            var clip = airHorn ? airHornClip : hornClip;

            if (shouldPlay && clip != null)
            {
                if (!_hornWasPlaying || hornSource.clip != clip)
                {
                    hornSource.clip = clip;
                    hornSource.loop = true;
                    hornSource.Play();
                }
            }
            else if (_hornWasPlaying)
            {
                hornSource.Stop();
            }

            _hornWasPlaying = shouldPlay;
        }
    }
}
