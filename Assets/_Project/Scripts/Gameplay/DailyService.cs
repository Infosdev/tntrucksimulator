using System;
using System.Collections.Generic;
using TNLorry.Core;
using UnityEngine;

namespace TNLorry.Gameplay
{
    public enum MissionType
    {
        /// <summary>Complete N deliveries, of anything, anywhere.</summary>
        CompleteDeliveries,
        /// <summary>Deliver N loads to a specific city.</summary>
        DeliverToCity,
        /// <summary>Deliver N loads of a specific cargo.</summary>
        DeliverCargo,
        /// <summary>Drive N km in total.</summary>
        DriveDistance,
        /// <summary>Complete N deliveries with zero violations.</summary>
        CleanDeliveries,
        /// <summary>Complete N deliveries with cargo damage under 5%.</summary>
        UndamagedDeliveries,
        /// <summary>Complete N deliveries inside the time limit.</summary>
        OnTimeDeliveries
    }

    /// <summary>
    /// Daily missions and the login-reward streak (section G, MVP scope).
    ///
    /// Both run on the **real-world** date, not the in-game clock: a login reward that can be
    /// farmed by sleeping at a lodge is not a login reward, and a daily mission that resets
    /// every eight minutes of play is not daily.
    ///
    /// Missions are generated deterministically from (profileId, date), so the set is stable
    /// across relaunches for the same reason the job board is.
    ///
    /// Sole writer of SaveData.daily.
    /// </summary>
    public class DailyService
    {
        const int MissionsPerDay = 3;

        readonly SaveSystem _save;
        readonly EconomyService _economy;
        readonly ProgressionService _progression;

        public DailyService(SaveSystem save, EconomyService economy, ProgressionService progression)
        {
            _save = save;
            _economy = economy;
            _progression = progression;
        }

        /// <summary>Injectable clock so tests can cross a date boundary without waiting a day.</summary>
        public Func<DateTime> NowProvider { get; set; } = () => DateTime.Now;

        string Today => NowProvider().ToString("yyyy-MM-dd");

        // ── Login streak ────────────────────────────────────────────────────

        /// <summary>
        /// Login reward curve (section G). Day 7 is the one players chase, so it is worth
        /// visibly more than the six before it.
        /// </summary>
        static readonly int[] StreakRewards = { 2000, 3000, 4500, 6000, 8000, 11000, 20000 };

        public int LoginStreak => _save.Data.daily.loginStreak;
        public int BestLoginStreak => _save.Data.daily.bestLoginStreak;

        public bool LoginRewardAvailable => _save.Data.daily.lastLoginDate != Today;

        /// <summary>Reward the player would get if they claimed right now.</summary>
        public int PendingLoginReward()
        {
            if (!LoginRewardAvailable) return 0;
            int streak = WouldBeStreak();
            return StreakRewards[Mathf.Clamp(streak - 1, 0, StreakRewards.Length - 1)];
        }

        /// <summary>
        /// Two different numbers, computed once so they cannot drift apart:
        ///   continuous — the profile stat, "you have shown up N days running"
        ///   cycleDay   — position in the 7-day reward table, which repeats
        /// </summary>
        void ResolveStreak(out int continuous, out int cycleDay)
        {
            var daily = _save.Data.daily;

            bool consecutive = !string.IsNullOrEmpty(daily.lastLoginDate) &&
                               DateTime.TryParse(daily.lastLoginDate, out var last) &&
                               (NowProvider().Date - last.Date).Days == 1;

            continuous = consecutive ? daily.loginStreak + 1 : 1;

            // The reward cycle wraps, so day 8 pays a day-1 reward again while the continuous
            // count keeps climbing.
            cycleDay = consecutive
                ? (daily.loginStreak % StreakRewards.Length) + 1
                : 1;
        }

        int WouldBeStreak()
        {
            ResolveStreak(out _, out int cycleDay);
            return cycleDay;
        }

        /// <summary>
        /// Claims today's login reward. Returns 0 when it has already been claimed today,
        /// so calling this on every app open is safe.
        /// </summary>
        public int ClaimLoginReward()
        {
            if (!LoginRewardAvailable) return 0;

            var daily = _save.Data.daily;
            ResolveStreak(out int continuous, out int cycleDay);

            int reward = StreakRewards[Mathf.Clamp(cycleDay - 1, 0, StreakRewards.Length - 1)];

            daily.loginStreak = continuous;
            daily.bestLoginStreak = Mathf.Max(daily.bestLoginStreak, continuous);
            daily.lastLoginDate = Today;

            _economy.Credit(reward, "login_reward");
            _save.MarkDirty();

            EventBus.Publish(new LoginRewardClaimedEvent
            {
                Amount = reward,
                Streak = continuous,
                CycleDay = cycleDay
            });

            return reward;
        }

        // ── Missions ────────────────────────────────────────────────────────

        public IReadOnlyList<MissionSave> Missions
        {
            get
            {
                EnsureTodaysMissions();
                return _save.Data.daily.missions;
            }
        }

        /// <summary>Regenerates the set when the real date has rolled over.</summary>
        public void EnsureTodaysMissions()
        {
            var daily = _save.Data.daily;
            if (daily.missionDate == Today && daily.missions.Count > 0) return;

            daily.missionDate = Today;
            daily.missions = Generate();
            _save.MarkDirty();

            EventBus.Publish(new DailyMissionsRefreshedEvent { Count = daily.missions.Count });
        }

        List<MissionSave> Generate()
        {
            var data = _save.Data;
            var rng = new System.Random(StableHash(data.profileId) ^ StableHash(Today));
            var missions = new List<MissionSave>(MissionsPerDay);

            // Always one simple "just play" mission, so a short session can still finish
            // something. The other two are picked from the varied pool.
            missions.Add(BuildMission(MissionType.CompleteDeliveries, "", rng));

            var pool = new List<MissionType>
            {
                MissionType.DriveDistance,
                MissionType.CleanDeliveries,
                MissionType.UndamagedDeliveries,
                MissionType.OnTimeDeliveries,
                MissionType.DeliverToCity,
                MissionType.DeliverCargo
            };

            while (missions.Count < MissionsPerDay && pool.Count > 0)
            {
                int index = rng.Next(pool.Count);
                var type = pool[index];
                pool.RemoveAt(index);

                string parameter = ParameterFor(type, rng);
                // A city/cargo mission with nothing unlocked to name is unachievable; skip it.
                if ((type == MissionType.DeliverToCity || type == MissionType.DeliverCargo) &&
                    string.IsNullOrEmpty(parameter))
                    continue;

                missions.Add(BuildMission(type, parameter, rng));
            }

            return missions;
        }

        string ParameterFor(MissionType type, System.Random rng)
        {
            var data = _save.Data;

            switch (type)
            {
                case MissionType.DeliverToCity:
                    // Never the starting city: "deliver to Chennai" is where half the jobs go
                    // anyway, which makes for a mission the player completes without noticing.
                    var cities = new List<string>(data.unlockedCityIds);
                    cities.Remove("chennai");
                    return cities.Count == 0 ? "" : cities[rng.Next(cities.Count)];

                case MissionType.DeliverCargo:
                    return data.unlockedCargoIds.Count == 0
                        ? ""
                        : data.unlockedCargoIds[rng.Next(data.unlockedCargoIds.Count)];

                default:
                    return "";
            }
        }

        MissionSave BuildMission(MissionType type, string parameter, System.Random rng)
        {
            // Targets are sized for one session, not one week. The guide's session target is
            // 8-20 minutes per delivery, so three deliveries is already a long evening.
            int target = type switch
            {
                MissionType.CompleteDeliveries  => rng.Next(2, 4),
                MissionType.DeliverToCity       => rng.Next(1, 3),
                MissionType.DeliverCargo        => rng.Next(1, 3),
                MissionType.DriveDistance       => rng.Next(2, 6) * 100,   // 200-500 km
                MissionType.CleanDeliveries     => rng.Next(1, 3),
                MissionType.UndamagedDeliveries => rng.Next(1, 3),
                MissionType.OnTimeDeliveries    => rng.Next(2, 4),
                _                               => 1
            };

            // Reward scales with the player's level so dailies stay relevant past the early
            // game, and with how demanding the mission is.
            float difficulty = type switch
            {
                MissionType.CompleteDeliveries  => 1f,
                MissionType.DriveDistance       => 1.1f,
                MissionType.OnTimeDeliveries    => 1.3f,
                MissionType.CleanDeliveries     => 1.4f,
                MissionType.UndamagedDeliveries => 1.4f,
                _                               => 1.2f
            };

            float levelScale = 1f + (_progression.Level - 1) * 0.08f;
            int baseReward = type == MissionType.DriveDistance ? 25 * target : 2500 * target;

            return new MissionSave
            {
                type = type.ToString(),
                parameter = parameter,
                target = target,
                progress = 0,
                rewardMoney = Mathf.RoundToInt(baseReward * difficulty * levelScale),
                rewardXp = Mathf.RoundToInt(40 * target * difficulty),
                claimed = false
            };
        }

        // ── Progress tracking ───────────────────────────────────────────────

        /// <summary>
        /// Called by the delivery flow. Advances every mission the delivery satisfies —
        /// one good run can tick three missions at once, which is the point.
        /// </summary>
        public void RecordDelivery(Job job, DeliveryReport report)
        {
            if (job == null) return;
            EnsureTodaysMissions();

            bool onTime = report.ElapsedMinutes <= job.TimeLimitMinutes;
            bool clean = report.ViolationCount == 0;
            bool undamaged = report.CargoDamage01 < 0.05f;

            foreach (var mission in _save.Data.daily.missions)
            {
                if (mission.claimed || mission.progress >= mission.target) continue;
                if (!Enum.TryParse(mission.type, out MissionType type)) continue;

                bool counts = type switch
                {
                    MissionType.CompleteDeliveries  => true,
                    MissionType.OnTimeDeliveries    => onTime,
                    MissionType.CleanDeliveries     => clean,
                    MissionType.UndamagedDeliveries => undamaged,
                    MissionType.DeliverToCity       => job.DestinationCity != null &&
                                                       job.DestinationCity.id == mission.parameter,
                    MissionType.DeliverCargo        => job.Cargo != null && job.Cargo.id == mission.parameter,
                    _                               => false
                };

                if (counts) Advance(mission, 1);
            }

            _save.MarkDirty();
        }

        /// <summary>Called as the player drives, with kilometres covered since the last call.</summary>
        public void RecordDistance(float km)
        {
            if (km <= 0f) return;
            EnsureTodaysMissions();

            _distanceCarry += km;
            if (_distanceCarry < 1f) return;        // only whole kilometres count

            int whole = Mathf.FloorToInt(_distanceCarry);
            _distanceCarry -= whole;

            foreach (var mission in _save.Data.daily.missions)
            {
                if (mission.claimed || mission.progress >= mission.target) continue;
                if (mission.type != MissionType.DriveDistance.ToString()) continue;
                Advance(mission, whole);
            }
        }

        float _distanceCarry;

        void Advance(MissionSave mission, int amount)
        {
            int before = mission.progress;
            mission.progress = Mathf.Min(mission.target, mission.progress + amount);

            if (before < mission.target && mission.progress >= mission.target)
            {
                EventBus.Publish(new DailyMissionCompletedEvent
                {
                    Type = mission.type,
                    RewardMoney = mission.rewardMoney,
                    RewardXp = mission.rewardXp
                });
            }
        }

        public bool IsComplete(MissionSave mission) => mission != null && mission.progress >= mission.target;

        public int UnclaimedCount()
        {
            EnsureTodaysMissions();
            int count = 0;
            foreach (var mission in _save.Data.daily.missions)
                if (!mission.claimed && IsComplete(mission)) count++;
            return count;
        }

        /// <summary>Claims one completed mission. Returns the money paid, 0 if not claimable.</summary>
        public int Claim(int index)
        {
            EnsureTodaysMissions();

            var missions = _save.Data.daily.missions;
            if (index < 0 || index >= missions.Count) return 0;

            var mission = missions[index];
            if (mission.claimed || !IsComplete(mission)) return 0;

            mission.claimed = true;
            _economy.Credit(mission.rewardMoney, "daily_mission:" + mission.type);
            _progression.AwardXp(mission.rewardXp);
            _save.MarkDirty();

            return mission.rewardMoney;
        }

        public int ClaimAll()
        {
            EnsureTodaysMissions();

            int total = 0;
            for (int i = 0; i < _save.Data.daily.missions.Count; i++) total += Claim(i);
            return total;
        }

        /// <summary>
        /// FNV-1a, for the same reason the job board uses it: string.GetHashCode is not stable
        /// across runs, so the mission set would reshuffle on every launch.
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
