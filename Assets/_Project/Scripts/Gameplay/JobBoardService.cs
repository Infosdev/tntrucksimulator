using System.Collections.Generic;
using TNLorry.Core;
using TNLorry.Data;
using TNLorry.Navigation;
using UnityEngine;

namespace TNLorry.Gameplay
{
    /// <summary>
    /// Generates 6-10 offers per depot per in-game day, weighted by what each city produces
    /// and consumes, by player level, and by active events (section K.3).
    ///
    /// Determinism matters: the board is regenerated from a seed derived from
    /// (profile, depot, in-game day), so quitting and reopening the app shows the same offers
    /// instead of letting players reroll for a jackpot.
    /// </summary>
    public class JobBoardService
    {
        readonly GameDatabase _db;
        readonly EconomyConfig _config;
        readonly EconomyService _economy;
        readonly SaveSystem _save;
        readonly RoadGraph _roads;

        readonly Dictionary<string, List<Job>> _boards = new Dictionary<string, List<Job>>();
        int _boardDay = -1;

        public JobBoardService(GameDatabase db, EconomyConfig config, EconomyService economy,
                               SaveSystem save, RoadGraph roads)
        {
            _db      = db;
            _config  = config;
            _economy = economy;
            _save    = save;
            _roads   = roads;
        }

        public Job ActiveJob { get; private set; }

        /// <summary>Offers at a depot for the current in-game day. Cached until the day rolls over.</summary>
        public List<Job> GetBoard(string cityId)
        {
            int day = _save.Data.world.day;
            if (day != _boardDay)
            {
                _boards.Clear();
                _boardDay = day;
            }

            if (_boards.TryGetValue(cityId, out var cached)) return cached;

            var generated = Generate(cityId, day);
            _boards[cityId] = generated;
            EventBus.Publish(new JobOfferedEvent { JobCount = generated.Count });
            return generated;
        }

        List<Job> Generate(string originId, int day)
        {
            var jobs   = new List<Job>();
            var origin = _db.GetCity(originId);
            if (origin == null) return jobs;

            var data = _save.Data;

            // Same seed -> same board. Players cannot reroll by relaunching.
            int seed = StableHash(data.profileId) ^ StableHash(originId) ^ (day * 7919);
            var rng  = new System.Random(seed);

            var destinations = _db.UnlockedCities(data.unlockedCityIds);
            destinations.RemoveAll(c => c == null || c.id == originId);
            if (destinations.Count == 0) return jobs;

            var cargoPool = BuildWeightedCargoPool(origin, data);
            if (cargoPool.Count == 0) return jobs;

            int count = rng.Next(_config.minJobsPerDepot, _config.maxJobsPerDepot + 1);

            for (int i = 0; i < count; i++)
            {
                var cargo = cargoPool[rng.Next(cargoPool.Count)];
                var dest  = PickDestination(cargo, destinations, rng);
                if (dest == null) continue;

                float distanceKm = RouteDistanceKm(origin, dest, out bool isGhat);
                if (distanceKm <= 0.5f) continue;

                var job = new Job
                {
                    Id               = originId + "_" + day + "_" + i,
                    Cargo            = cargo,
                    OriginCity       = origin,
                    DestinationCity  = dest,
                    DistanceKm       = distanceKm,
                    IsGhatRoute      = isGhat
                };

                job.TimeLimitMinutes = DeriveTimeLimit(job, rng);
                job.Payout           = _economy.EstimatePayout(job);
                jobs.Add(job);
            }

            return jobs;
        }

        /// <summary>
        /// Cargo the origin actually produces is weighted 3x, so a Tiruppur board is mostly
        /// garments and an Erode board is mostly turmeric. Anything else unlocked can still
        /// appear, which keeps boards from going stale.
        /// </summary>
        List<CargoData> BuildWeightedCargoPool(CityData origin, SaveData data)
        {
            var pool      = new List<CargoData>();
            var available = _db.AvailableCargo(data.level, data.unlockedCargoIds);

            foreach (var cargo in available)
            {
                bool producedHere =
                    System.Array.IndexOf(origin.producesCargoIds, cargo.id) >= 0 ||
                    System.Array.IndexOf(cargo.producedByCityIds, origin.id) >= 0;

                int weight = producedHere ? 3 : 1;

                // Seasonal cargo (sugarcane) only during its event window.
                if (cargo.Has(CargoFlags.Seasonal) && !IsSeasonalWindowOpen(cargo)) continue;

                for (int w = 0; w < weight; w++) pool.Add(cargo);
            }
            return pool;
        }

        CityData PickDestination(CargoData cargo, List<CityData> candidates, System.Random rng)
        {
            // Prefer a city that consumes this cargo; fall back to any unlocked city.
            var consumers = new List<CityData>();
            foreach (var c in candidates)
            {
                if (System.Array.IndexOf(cargo.consumedByCityIds, c.id) >= 0 ||
                    System.Array.IndexOf(c.consumesCargoIds, cargo.id) >= 0)
                    consumers.Add(c);
            }

            var pick = consumers.Count > 0 && rng.NextDouble() < 0.75 ? consumers : candidates;
            return pick.Count == 0 ? null : pick[rng.Next(pick.Count)];
        }

        /// <summary>
        /// Real route length from the road graph when it is loaded (menus may run before the
        /// world is streamed in), otherwise straight-line distance as a stand-in.
        /// </summary>
        float RouteDistanceKm(CityData from, CityData to, out bool isGhat)
        {
            isGhat = false;

            if (_roads != null && from.roadNodeId >= 0 && to.roadNodeId >= 0)
            {
                var route = _roads.FindRoute(from.roadNodeId, to.roadNodeId);
                if (route != null && route.IsValid)
                {
                    isGhat = route.HasGhatSection;
                    return route.LengthKm;
                }
            }

            return Vector3.Distance(from.worldPosition, to.worldPosition) / 1000f;
        }

        float DeriveTimeLimit(Job job, System.Random rng)
        {
            float plannedMinutes = job.DistanceKm / Mathf.Max(5f, _config.plannedAverageSpeedKmh) * 60f;
            float slack = _config.timeLimitSlack;

            if (job.IsGhatRoute) slack += 0.25f;                                   // hairpins are slow
            if (job.Cargo.Has(CargoFlags.Fragile)) slack += 0.15f;                 // gentle driving takes longer
            if (job.Cargo.Has(CargoFlags.Perishable)) slack -= 0.15f;              // that is the whole point

            // +-10% variation so two jobs on the same route are not identical.
            slack *= 0.9f + (float)rng.NextDouble() * 0.2f;

            float limit = plannedMinutes * Mathf.Max(1.05f, slack);

            // Perishable cargo can never be given more time than its own spoil timer.
            if (job.Cargo.Has(CargoFlags.Perishable) && job.Cargo.perishMinutes > 0f)
                limit = Mathf.Min(limit, job.Cargo.perishMinutes);

            return Mathf.Round(limit);
        }

        static bool IsSeasonalWindowOpen(CargoData cargo)
        {
            // Pongal sugarcane window, mid-Jan (section Z.2). Replaced by Remote Config
            // event flags once LiveOps is wired up.
            var now = System.DateTime.Now;
            return now.Month == 1 && now.Day >= 8 && now.Day <= 20;
        }

        // ── Accept / complete / abandon ─────────────────────────────────────

        public bool Accept(Job job)
        {
            if (job == null) return false;
            if (ActiveJob != null)
            {
                Debug.LogWarning("[JobBoard] Cannot accept: a job is already active.");
                return false;
            }

            ActiveJob = job;
            _save.Data.activeJob = job.ToSave();
            _save.MarkDirty();

            EventBus.Publish(new JobAcceptedEvent
            {
                JobId      = job.Id,
                CargoId    = job.Cargo.id,
                FromCity   = job.OriginCity.id,
                ToCity     = job.DestinationCity.id,
                DistanceKm = job.DistanceKm,
                Payout     = job.Payout
            });
            return true;
        }

        public void RestoreActiveJob()
        {
            ActiveJob = Job.FromSave(_save.Data.activeJob, _db);
            if (ActiveJob == null && _save.Data.activeJob.hasJob)
            {
                _save.Data.activeJob = new ActiveJobSave();
                _save.MarkDirty();
            }
        }

        /// <summary>
        /// Called by the delivery bay once the truck is parked and the cargo unloaded.
        ///
        /// `daily` is optional so the job board stays usable without the daily-mission service
        /// registered — the tutorial and Quick Job mode both run without it.
        /// </summary>
        public EconomyService.PayoutBreakdown Complete(DeliveryReport report,
                                                       ProgressionService progression,
                                                       DailyService daily = null)
        {
            var job = ActiveJob;
            if (job == null) return default;

            // Before the job is cleared — the mission tracker needs the cargo and destination.
            daily?.RecordDelivery(job, report);

            var breakdown = _economy.CalculatePayout(job, report);
            _economy.Credit(breakdown.Total, "delivery:" + job.Id);

            int xp = progression.CalculateDeliveryXp(job, report);
            progression.AwardXp(xp);

            _save.Data.deliveriesCompleted++;

            // Actual distance driven where we have it, planned route length as a fallback —
            // a player who took a scenic detour drove further than the route said.
            float creditedKm = report.DistanceDrivenKm > 0.5f ? report.DistanceDrivenKm : job.DistanceKm;
            _save.Data.totalDistanceKmX100 += (long)(creditedKm * 100f);
            _save.Data.activeJob = new ActiveJobSave();
            _save.MarkDirty();

            EventBus.Publish(new DeliveryCompletedEvent
            {
                JobId              = job.Id,
                Payout             = breakdown.Total,
                CargoDamagePercent = job.CargoDamage01 * 100f,
                OnTime             = report.ElapsedMinutes <= job.TimeLimitMinutes,
                ElapsedMinutes     = report.ElapsedMinutes,
                XpAwarded          = xp
            });

            ActiveJob = null;
            return breakdown;
        }

        public void Abandon()
        {
            var job = ActiveJob;
            if (job == null) return;

            int penalty = Mathf.RoundToInt(job.Payout * _config.abandonPenaltyFraction);
            _economy.ForceDebit(penalty, "abandon:" + job.Id);
            _save.Data.deliveriesFailed++;
            _save.Data.activeJob = new ActiveJobSave();
            _save.MarkDirty();

            EventBus.Publish(new JobAbandonedEvent { JobId = job.Id, Penalty = penalty });
            ActiveJob = null;
        }

        /// <summary>
        /// string.GetHashCode is not stable across runs or platforms in .NET, which would make
        /// the board reroll on every launch. FNV-1a is.
        /// </summary>
        static int StableHash(string s)
        {
            if (string.IsNullOrEmpty(s)) return 17;
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < s.Length; i++)
                {
                    hash ^= s[i];
                    hash *= 16777619u;
                }
                return (int)(hash & 0x7FFFFFFF);
            }
        }
    }
}
