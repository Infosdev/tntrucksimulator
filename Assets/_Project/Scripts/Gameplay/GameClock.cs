using TNLorry.Core;
using TNLorry.Data;
using UnityEngine;

namespace TNLorry.Gameplay
{
    /// <summary>
    /// In-game time (section K.5). One real minute is about one in-game hour while driving,
    /// which keeps an 8-20 minute session feeling like a day's work.
    ///
    /// Everything time-related reads this, not Time.time: day/night lighting, job deadlines,
    /// fatigue, job board refresh and the LiveOps calendar.
    /// </summary>
    public class GameClock : MonoBehaviour
    {
        [SerializeField] EconomyConfig config;

        /// <summary>0..24.</summary>
        public float HourOfDay { get; private set; } = 8f;
        public int   Day       { get; private set; } = 1;
        public bool  Paused    { get; set; }

        /// <summary>Scales the clock: 1 while driving, higher while resting at a lodge.</summary>
        public float TimeScale { get; set; } = 1f;

        public bool IsNight => HourOfDay < 6f || HourOfDay >= 18.5f;
        /// <summary>Civil twilight band, when headlights become mandatory (section K.7).</summary>
        public bool IsDusk  => HourOfDay >= 17.75f && HourOfDay < 18.5f;
        public bool IsDawn  => HourOfDay >= 5.5f && HourOfDay < 6.5f;

        /// <summary>In-game minutes elapsed since the active job was accepted.</summary>
        public float JobElapsedMinutes { get; private set; }

        /// <summary>
        /// In-game hours that pass per real second, including TimeScale. Anything that consumes
        /// a per-game-hour rate (fuel burn, fatigue, perishable timers) must convert through this.
        /// </summary>
        public float GameHoursPerRealSecond =>
            (config != null ? config.gameHoursPerRealMinute : 1f) / 60f * TimeScale;

        SaveSystem _save;
        int _lastWholeHour;

        void Awake()
        {
            _save = ServiceLocator.Get<SaveSystem>();
            if (_save?.Data != null)
            {
                HourOfDay = _save.Data.world.hourOfDay;
                Day       = _save.Data.world.day;
            }
            _lastWholeHour = Mathf.FloorToInt(HourOfDay);

            if (config == null)
                Debug.LogError("[GameClock] EconomyConfig not assigned; using 1 game-hour per real minute.");
        }

        void Update()
        {
            if (Paused) return;

            float hoursPerRealMinute = config != null ? config.gameHoursPerRealMinute : 1f;
            float deltaHours = Time.deltaTime / 60f * hoursPerRealMinute * TimeScale;

            HourOfDay += deltaHours;
            JobElapsedMinutes += deltaHours * 60f;

            while (HourOfDay >= 24f)
            {
                HourOfDay -= 24f;
                Day++;
            }

            int hour = Mathf.FloorToInt(HourOfDay);
            if (hour != _lastWholeHour)
            {
                _lastWholeHour = hour;
                EventBus.Publish(new GameHourPassedEvent { Day = Day, Hour = hour });
                WriteToSave();
            }
        }

        public void ResetJobTimer() => JobElapsedMinutes = 0f;

        public void SetJobElapsed(float minutes) => JobElapsedMinutes = Mathf.Max(0f, minutes);

        /// <summary>
        /// Sleeping at a lodge or dhaba (section K.5). Advances the clock without running
        /// physics for hours, and doubles as the save point.
        /// </summary>
        public void AdvanceHours(float hours)
        {
            HourOfDay += hours;
            while (HourOfDay >= 24f)
            {
                HourOfDay -= 24f;
                Day++;
            }
            _lastWholeHour = Mathf.FloorToInt(HourOfDay);
            WriteToSave();
        }

        void WriteToSave()
        {
            if (_save?.Data == null) return;
            _save.Data.world.hourOfDay = HourOfDay;
            _save.Data.world.day = Day;
            _save.MarkDirty();
        }

        /// <summary>"14:35" for the HUD clock.</summary>
        public string FormatClock()
        {
            int hours = Mathf.FloorToInt(HourOfDay);
            int minutes = Mathf.FloorToInt((HourOfDay - hours) * 60f);
            return hours.ToString("00") + ":" + minutes.ToString("00");
        }

        void OnDisable() => WriteToSave();
    }
}
