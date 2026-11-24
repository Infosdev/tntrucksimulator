using TNLorry.Core;
using TNLorry.Vehicle;
using UnityEngine;

namespace TNLorry.Gameplay
{
    /// <summary>
    /// Accumulates distance driven and spends it where it is needed: the owned truck's
    /// odometer (which drives trade-in value and service intervals) and the daily
    /// distance mission.
    ///
    /// Nothing else was incrementing `OwnedTruckSave.odometerKm`, so a truck stayed worth its
    /// pristine trade-in value forever no matter how far it had been driven.
    ///
    /// Put this on the player truck.
    /// </summary>
    [RequireComponent(typeof(TruckController))]
    public class OdometerTracker : MonoBehaviour
    {
        [Tooltip("Seconds between writes to the save. Distance accrues every frame regardless.")]
        [SerializeField] float flushInterval = 5f;

        /// <summary>Kilometres driven this session, across all jobs and free roam.</summary>
        public float SessionKm { get; private set; }

        TruckController _truck;
        SaveSystem _save;
        FleetService _fleet;
        DailyService _daily;

        float _pendingKm;
        float _flushTimer;

        void Awake()
        {
            _truck = GetComponent<TruckController>();
            ServiceLocator.TryGet(out _save);
            ServiceLocator.TryGet(out _fleet);
            ServiceLocator.TryGet(out _daily);
        }

        void Update()
        {
            float dt = Time.deltaTime;

            // Physical distance in game space. The world is compressed ~1:20, so this is the
            // distance the truck actually covered, not the real-world distance it represents.
            float km = Mathf.Abs(_truck.SpeedKmh) * dt / 3600f;
            if (km <= 0f) return;

            SessionKm += km;
            _pendingKm += km;

            _flushTimer += dt;
            if (_flushTimer < flushInterval) return;

            _flushTimer = 0f;
            Flush();
        }

        void Flush()
        {
            if (_pendingKm <= 0.0001f) return;

            var owned = _fleet?.ActiveTruck;
            if (owned != null)
            {
                owned.odometerKm += _pendingKm;
                _save?.MarkDirty();
            }

            // RecordDistance carries fractional kilometres internally, so flushing in batches
            // does not lose the remainder.
            _daily?.RecordDistance(_pendingKm);

            _pendingKm = 0f;
        }

        // Flush on the way out so the last few hundred metres before a scene change or a
        // backgrounding are not lost.
        void OnDisable() => Flush();
    }
}
