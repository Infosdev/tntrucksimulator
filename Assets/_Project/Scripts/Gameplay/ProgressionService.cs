using System.Collections.Generic;
using TNLorry.Core;
using TNLorry.Data;
using UnityEngine;

namespace TNLorry.Gameplay
{
    /// <summary>
    /// XP, levels, reputation and unlocks (section E.2). Owner of SaveData.xp/level/reputation
    /// and of the unlocked-id lists — nothing else writes them.
    /// </summary>
    public class ProgressionService
    {
        readonly EconomyConfig _config;
        readonly SaveSystem _save;

        public ProgressionService(EconomyConfig config, SaveSystem save)
        {
            _config = config;
            _save   = save;
        }

        public int Level      => _save.Data.level;
        public int Xp         => _save.Data.xp;
        public int Reputation => _save.Data.reputation;

        public bool CanTakeBankLoan   => Level >= _config.levelForBankLoan;
        public bool CanOwnGarage      => Level >= _config.levelForOwnGarage;
        public bool CanHireDrivers    => Level >= _config.levelForHiringDrivers;

        /// <summary>Total XP required to go from `level` to `level + 1`.</summary>
        public int XpForLevel(int level)
            => Mathf.RoundToInt(_config.baseXpToLevel * Mathf.Pow(level, _config.levelCurveExponent));

        public int XpToNextLevel => Mathf.Max(0, XpForLevel(Level) - Xp);

        public float LevelProgress01
        {
            get
            {
                int needed = XpForLevel(Level);
                return needed <= 0 ? 1f : Mathf.Clamp01((float)Xp / needed);
            }
        }

        public int CalculateDeliveryXp(Job job, DeliveryReport report)
        {
            float xp = _config.xpPerHundredKm * (job.DistanceKm / 100f);

            if (job.IsGhatRoute) xp *= 1.4f;
            if (job.Cargo != null && job.Cargo.Has(CargoFlags.Fragile))   xp *= 1.2f;
            if (job.Cargo != null && job.Cargo.Has(CargoFlags.Hazardous)) xp *= 1.3f;

            if (report.ElapsedMinutes <= job.TimeLimitMinutes) xp *= 1.2f;
            if (report.ViolationCount == 0)                   xp *= 1.15f;

            xp *= 1f - Mathf.Clamp01(report.CargoDamage01) * 0.5f;

            return Mathf.Max(1, Mathf.RoundToInt(xp));
        }

        /// <summary>
        /// Awards XP and resolves every level gained. Loops because a long haul can cross
        /// more than one level at once; the event reports the final level.
        /// </summary>
        public void AwardXp(int amount)
        {
            if (amount <= 0) return;

            var data = _save.Data;
            data.xp += amount;

            bool leveledUp = false;
            while (data.level < _config.maxLevel)
            {
                int needed = XpForLevel(data.level);
                if (data.xp < needed) break;

                data.xp -= needed;
                data.level++;
                leveledUp = true;
                ApplyLevelUnlocks(data.level);
            }

            if (data.level >= _config.maxLevel) data.xp = 0;

            _save.MarkDirty();
            EventBus.Publish(new XpChangedEvent { Xp = data.xp, Level = data.level, LeveledUp = leveledUp });
        }

        public void AdjustReputation(int delta)
        {
            var data = _save.Data;
            data.reputation = Mathf.Clamp(data.reputation + delta, 0, 100);
            _save.MarkDirty();
        }

        // ── Unlocks ─────────────────────────────────────────────────────────

        /// <summary>
        /// Phase 1 unlock table (sections D.1, E.1). Moves to CityData.unlockLevel /
        /// CargoData.unlockLevel driven iteration once the full content set exists —
        /// kept explicit here so the MVP curve is readable in one place.
        /// </summary>
        static readonly Dictionary<int, string[]> CityUnlocks = new Dictionary<int, string[]>
        {
            { 3,  new[] { "namakkal" } },
            { 6,  new[] { "erode" } },
            { 9,  new[] { "coimbatore", "tiruppur" } },
            { 12, new[] { "karur" } },
            { 15, new[] { "dindigul" } },
            { 18, new[] { "madurai" } },
        };

        static readonly Dictionary<int, string[]> CargoUnlocks = new Dictionary<int, string[]>
        {
            { 2,  new[] { "cement" } },
            { 4,  new[] { "eggs" } },             // Namakkal fragile run
            { 7,  new[] { "bananas", "milk" } },  // perishables
            { 10, new[] { "motor_pumps" } },
            { 13, new[] { "auto_parts" } },
            { 16, new[] { "matchboxes" } },       // Sivakasi hazardous
            { 20, new[] { "salt" } },
        };

        void ApplyLevelUnlocks(int level)
        {
            var data = _save.Data;

            if (CityUnlocks.TryGetValue(level, out var cities))
                foreach (var id in cities)
                    if (!data.unlockedCityIds.Contains(id)) data.unlockedCityIds.Add(id);

            if (CargoUnlocks.TryGetValue(level, out var cargo))
                foreach (var id in cargo)
                    if (!data.unlockedCargoIds.Contains(id)) data.unlockedCargoIds.Add(id);
        }

        public bool IsCityUnlocked(string id)  => _save.Data.unlockedCityIds.Contains(id);
        public bool IsCargoUnlocked(string id) => _save.Data.unlockedCargoIds.Contains(id);

        /// <summary>Which cities unlock at which level, for the "coming up" panel on the map.</summary>
        public static bool TryGetNextCityUnlock(int currentLevel, out int atLevel, out string[] cityIds)
        {
            atLevel = int.MaxValue;
            cityIds = null;
            foreach (var kv in CityUnlocks)
            {
                if (kv.Key > currentLevel && kv.Key < atLevel)
                {
                    atLevel = kv.Key;
                    cityIds = kv.Value;
                }
            }
            return cityIds != null;
        }
    }
}
