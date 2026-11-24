using TNLorry.Core;
using UnityEngine;

namespace TNLorry.Gameplay
{
    /// <summary>
    /// Drives the company-side services off the in-game clock: hired-driver trips every hour,
    /// loan instalments on the day rollover.
    ///
    /// A separate component rather than Update loops inside FleetService and DriverService,
    /// because both are plain C# services (unit-testable, no MonoBehaviour) and the clock is
    /// the only thing that should decide when they advance.
    /// </summary>
    public class CompanyTicker : MonoBehaviour
    {
        [SerializeField] GameClock clock;

        FleetService _fleet;
        DriverService _drivers;
        int _lastDay = -1;

        void Awake()
        {
            if (clock == null) clock = FindFirstObjectByType<GameClock>();

            ServiceLocator.TryGet(out _fleet);
            ServiceLocator.TryGet(out _drivers);

            if (clock == null)
            {
                Debug.LogError("[CompanyTicker] No GameClock found; loans and drivers will never tick.", this);
                enabled = false;
                return;
            }

            _lastDay = clock.Day;
        }

        void OnEnable()  => EventBus.Subscribe<GameHourPassedEvent>(OnHourPassed);
        void OnDisable() => EventBus.Unsubscribe<GameHourPassedEvent>(OnHourPassed);

        void OnHourPassed(GameHourPassedEvent e)
        {
            // Hour-of-day plus day, not an absolute hour: DriverService combines them itself.
            _drivers?.TickHour(e.Hour, e.Day);

            if (e.Day == _lastDay) return;

            // Catch up one day at a time. A player who slept 36 hours at a lodge must still
            // be charged every instalment that fell due, not just the latest one.
            while (_lastDay < e.Day)
            {
                _lastDay++;
                _fleet?.TickDay(_lastDay);
            }
        }
    }
}
