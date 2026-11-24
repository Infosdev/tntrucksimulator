using System.Collections.Generic;
using TNLorry.Core;
using TNLorry.Data;
using UnityEngine;

namespace TNLorry.Gameplay
{
    /// <summary>
    /// Hired AI drivers — the passive income that turns a driver into a company owner
    /// (sections E.2, G; unlocked at level 20).
    ///
    /// Trips resolve on the in-game clock, not real time, and only while the game is running.
    /// That is a deliberate design choice over wall-clock accrual: offline earnings would make
    /// not playing the optimal strategy, and the guide's monetization pillar is "no pay-to-win",
    /// which includes not paying with absence.
    ///
    /// Sole writer of SaveData.hiredDrivers.
    /// </summary>
    public class DriverService
    {
        /// <summary>In-game hours a hired driver takes to complete one round trip.</summary>
        public const float HoursPerTrip = 8f;

        readonly EconomyConfig _config;
        readonly SaveSystem _save;
        readonly EconomyService _economy;
        readonly ProgressionService _progression;
        readonly GameDatabase _database;

        public DriverService(EconomyConfig config, SaveSystem save, EconomyService economy,
                             ProgressionService progression, GameDatabase database)
        {
            _config = config;
            _save = save;
            _economy = economy;
            _progression = progression;
            _database = database;
        }

        public IReadOnlyList<HiredDriverSave> Drivers => _save.Data.hiredDrivers;
        public int DriverCount => _save.Data.hiredDrivers.Count;

        /// <summary>One extra driver slot per 5 levels past the hiring gate, capped at 6.</summary>
        public int MaxDrivers => _progression.CanHireDrivers
            ? Mathf.Clamp(1 + (_progression.Level - _config.levelForHiringDrivers) / 5, 1, 6)
            : 0;

        public bool CanHireMore => DriverCount < MaxDrivers;

        public enum HireResult { Success, NotUnlocked, NoSlots, CannotAfford, NoSpareTruck, UnknownTruck }

        /// <summary>Signing fee, scaled by skill. A 5-star driver is a real investment.</summary>
        public int HiringFee(int skill) => Mathf.RoundToInt(_config.driverSalaryPerTrip * 4f * Mathf.Clamp(skill, 1, 5));

        /// <summary>
        /// Absolute in-game hours since the profile began. Trip scheduling must use this and
        /// not the 0-24 hour of day, or a trip booked at 20:00 is compared against an hour
        /// number that goes *down* when the clock wraps past midnight.
        /// </summary>
        public static float AbsoluteHour(int day, float hourOfDay) => day * 24f + hourOfDay;

        /// <summary>
        /// Hires a driver onto a specific owned truck. A driver cannot share the player's
        /// active truck — the point is that the fleet works while the player drives.
        /// </summary>
        public HireResult Hire(string name, int skill, string truckDataId, string routeId,
                               int currentDay, float currentHour)
        {
            if (!_progression.CanHireDrivers) return HireResult.NotUnlocked;
            if (!CanHireMore) return HireResult.NoSlots;

            var truck = _database.GetTruck(truckDataId);
            if (truck == null) return HireResult.UnknownTruck;
            if (!IsTruckAvailableForHire(truckDataId)) return HireResult.NoSpareTruck;

            skill = Mathf.Clamp(skill, 1, 5);
            int fee = HiringFee(skill);
            if (!_economy.TryDebit(fee, "hire_driver")) return HireResult.CannotAfford;

            var driver = new HiredDriverSave
            {
                driverId = System.Guid.NewGuid().ToString("N").Substring(0, 8),
                name = name,
                skill = skill,
                assignedTruckDataId = truckDataId,
                routeId = routeId,
                payoutPerTrip = EstimateTripPayout(truck, skill),
                // nextPayoutUnix is reused as "absolute in-game hour the next trip lands", so
                // the existing save schema did not need to change for this feature.
                nextPayoutUnix = AbsoluteHour(currentDay, currentHour) + HoursPerTrip
            };

            _save.Data.hiredDrivers.Add(driver);
            _save.MarkDirty();

            EventBus.Publish(new DriverHiredEvent { DriverId = driver.driverId, Name = name, Skill = skill });
            return HireResult.Success;
        }

        /// <summary>
        /// A truck can carry one hired driver, and never the one the player is currently in.
        /// </summary>
        public bool IsTruckAvailableForHire(string truckDataId)
        {
            var data = _save.Data;

            bool owned = false;
            foreach (var truck in data.ownedTrucks)
                if (truck.truckDataId == truckDataId) { owned = true; break; }
            if (!owned) return false;

            var active = data.ownedTrucks.Count > 0
                ? data.ownedTrucks[Mathf.Clamp(data.activeTruckIndex, 0, data.ownedTrucks.Count - 1)]
                : null;
            if (active != null && active.truckDataId == truckDataId) return false;

            foreach (var driver in data.hiredDrivers)
                if (driver.assignedTruckDataId == truckDataId) return false;

            return true;
        }

        public bool Fire(string driverId)
        {
            var drivers = _save.Data.hiredDrivers;
            for (int i = 0; i < drivers.Count; i++)
            {
                if (drivers[i].driverId != driverId) continue;

                drivers.RemoveAt(i);
                _save.MarkDirty();
                EventBus.Publish(new DriverFiredEvent { DriverId = driverId });
                return true;
            }
            return false;
        }

        /// <summary>
        /// Expected net per trip. Skill raises the gross and lowers the incident rate, so a
        /// cheap driver really is worse rather than just slower.
        /// </summary>
        public int EstimateTripPayout(TruckData truck, int skill)
        {
            if (truck == null) return 0;

            // A hired trip is modelled as a fixed-length haul on the truck's payload class.
            float assumedKm = 220f;
            float assumedRatePerKm = 38f;
            float payloadFactor = Mathf.Lerp(0.8f, 1.4f, Mathf.Clamp01(truck.maxPayloadKg / 35000f));
            float skillFactor = Mathf.Lerp(0.85f, 1.15f, (skill - 1) / 4f);

            float gross = assumedKm * assumedRatePerKm * payloadFactor * skillFactor * _economy.PayoutScalar;

            // Running costs the player would otherwise pay themselves: diesel and the salary.
            float diesel = assumedKm / 100f * 30f * _economy.DieselPricePerLitre;
            float salary = _config.driverSalaryPerTrip;

            return Mathf.Max(0, Mathf.RoundToInt(gross - diesel - salary));
        }

        /// <summary>
        /// Called on each in-game hour tick. Resolves any trips that have come due.
        /// Returns the total credited this tick.
        /// </summary>
        public int TickHour(float currentHour, int currentDay)
        {
            // Absolute hours, so a trip spanning midnight does not resolve instantly when the
            // clock wraps from 23:59 back to 00:00.
            float absoluteHour = AbsoluteHour(currentDay, currentHour);
            int totalCredited = 0;

            foreach (var driver in _save.Data.hiredDrivers)
            {
                if (absoluteHour < driver.nextPayoutUnix) continue;

                bool incident = Random.value < IncidentChance(driver.skill);
                int earnings = driver.payoutPerTrip;

                if (incident)
                {
                    // Damage, a fine, or a late delivery. Costs money and a little reputation.
                    earnings = Mathf.RoundToInt(earnings * Random.Range(-0.3f, 0.4f));
                    _progression.AdjustReputation(-1);
                }

                if (earnings > 0) _economy.Credit(earnings, "driver:" + driver.driverId);
                else if (earnings < 0) _economy.ForceDebit(-earnings, "driver_incident:" + driver.driverId);

                totalCredited += earnings;
                driver.nextPayoutUnix = absoluteHour + HoursPerTrip;

                EventBus.Publish(new DriverTripCompletedEvent
                {
                    DriverId = driver.driverId,
                    NetEarnings = earnings,
                    HadIncident = incident
                });
            }

            if (_save.Data.hiredDrivers.Count > 0) _save.MarkDirty();
            return totalCredited;
        }

        /// <summary>Per-trip chance of something going wrong. 1-star: 30%, 5-star: 4%.</summary>
        public static float IncidentChance(int skill)
            => Mathf.Lerp(0.30f, 0.04f, Mathf.Clamp01((Mathf.Clamp(skill, 1, 5) - 1) / 4f));

        /// <summary>Expected rupees per in-game day across the whole fleet, for the company screen.</summary>
        public int ProjectedDailyIncome()
        {
            float tripsPerDay = 24f / HoursPerTrip;
            float total = 0f;

            foreach (var driver in _save.Data.hiredDrivers)
            {
                float incidentRate = IncidentChance(driver.skill);
                // An incident averages out to roughly +5% of the normal payout, so weight it in.
                float expectedPerTrip = driver.payoutPerTrip * (1f - incidentRate) +
                                        driver.payoutPerTrip * 0.05f * incidentRate;
                total += expectedPerTrip * tripsPerDay;
            }

            return Mathf.RoundToInt(total);
        }
    }
}
