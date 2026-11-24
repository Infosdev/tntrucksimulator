using UnityEngine;
using UnityEngine.Audio;
using TNLorry.Core;

namespace TNLorry.Services
{
    /// <summary>
    /// Volume + mixer routing (section R). Engine sound itself is driven by TruckAudio;
    /// this service owns only the global mix and the one-shot UI/notification channel,
    /// so it stays valid whether the project ends up on Unity Audio or FMOD.
    /// </summary>
    public class AudioService
    {
        const string MasterParam = "MasterVolume";
        const string MusicParam  = "MusicVolume";
        const string SfxParam    = "SfxVolume";

        readonly AudioMixer _mixer;
        readonly SettingsSave _settings;

        public AudioService(SettingsSave settings)
        {
            _settings = settings;
            _mixer = Resources.Load<AudioMixer>("Audio/MainMixer");
            if (_mixer == null)
                Debug.LogWarning("[AudioService] Resources/Audio/MainMixer not found; volume settings will not apply.");
            ApplyAll();
        }

        public void ApplyAll()
        {
            SetMaster(_settings.masterVolume);
            SetMusic(_settings.musicVolume);
            SetSfx(_settings.sfxVolume);
        }

        public void SetMaster(float linear01) { _settings.masterVolume = linear01; Set(MasterParam, linear01); }
        public void SetMusic (float linear01) { _settings.musicVolume  = linear01; Set(MusicParam,  linear01); }
        public void SetSfx   (float linear01) { _settings.sfxVolume    = linear01; Set(SfxParam,    linear01); }

        /// <summary>
        /// Mixer volumes are in dB. A linear slider mapped straight to dB feels wrong
        /// (most of the travel does nothing), so convert logarithmically and treat 0 as -80 dB.
        /// </summary>
        void Set(string param, float linear01)
        {
            if (_mixer == null) return;
            linear01 = Mathf.Clamp01(linear01);
            float db = linear01 <= 0.0001f ? -80f : Mathf.Log10(linear01) * 20f;
            _mixer.SetFloat(param, db);
        }

        /// <summary>Short vibration for fines, collisions and gear clunks. Respects the settings toggle.</summary>
        public void Haptic()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_settings.hapticsEnabled) Handheld.Vibrate();
#endif
        }
    }
}
