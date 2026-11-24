using TNLorry.Core;
using TNLorry.Data;
using TNLorry.Services;
using UnityEngine;

namespace TNLorry.Gameplay
{
    /// <summary>
    /// Single owner of the money balance and the payout formula (sections C, E.2).
    /// Nothing else may write SaveData.money — route every credit and debit through here so
    /// balance changes always fire MoneyChangedEvent and always mark the save dirty.
    ///
    /// Tunables are read through IRemoteConfigService, keyed "econ.*", so a live event can
    /// change diesel prices or payout scalars without an app update.
    /// </summary>
    public class EconomyService
    {
        readonly EconomyConfig _config;
        readonly SaveSystem _save;
        readonly IRemoteConfigService _remote;

        public EconomyService(EconomyConfig config, SaveSystem save, IRemoteConfigService remote)
        {
            _config = config;
            _save   = save;
            _remote = remote;

            if (_config == null)
                Debug.LogError("[EconomyService] EconomyConfig is not assigned on the Bootstrap object.");
        }

        public long Balance => _save.Data.money;
        public long Debt    => _save.Data.debt;

        public float DieselPricePerLitre =>
            _remote.GetFloat("econ.diesel_price", _config != null ? _config.dieselPricePerLitre : 94f);

        public float PayoutScalar =>
            _remote.GetFloat("econ.payout_scalar", _config != null ? _config.payoutScalar : 1f);

        // ── Balance ─────────────────────────────────────────────────────────

        public void Credit(long amount, string reason)
        {
            if (amount <= 0) return;
            _save.Data.money += amount;
            _save.MarkDirty();
            EventBus.Publish(new MoneyChangedEvent { Balance = _save.Data.money, Delta = amount, Reason = reason });
        }

        /// <summary>Returns false and changes nothing when the player cannot afford it.</summary>
        public bool TryDebit(long amount, string reason)
        {
            if (amount <= 0) return true;
            if (_save.Data.money < amount) return false;

            _save.Data.money -= amount;
            _save.MarkDirty();
            EventBus.Publish(new MoneyChangedEvent { Balance = _save.Data.money, Delta = -amount, Reason = reason });
            return true;
        }

        /// <summary>
        /// Fines and tolls are not optional, so they may take the balance negative — that debt
        /// is the pressure that makes careless driving matter. Purchases must use TryDebit.
        /// </summary>
        public void ForceDebit(long amount, string reason)
        {
            if (amount <= 0) return;
            _save.Data.money -= amount;
            _save.MarkDirty();
            EventBus.Publish(new MoneyChangedEvent { Balance = _save.Data.money, Delta = -amount, Reason = reason });
        }

        public bool CanAfford(long amount) => _save.Data.money >= amount;

        // ── Payout formula (section C) ───────────────────────────────────────

        public struct PayoutBreakdown
        {
            public int BasePay;
            public int WeightBonus;
            public int TimeAdjustment;      // negative when late
            public int DamagePenalty;       // negative
            public int FuelBonus;
            public int CleanDrivingBonus;
            public int Fines;               // negative
            public int Total;
        }

        /// <summary>
        /// Payout = BaseRate x Distance x WeightFactor x TimeBonus x ConditionFactor
        ///          + FuelBonus + RulesBonus - Fines
        /// Returned as a breakdown because the result screen itemises it (section O.1).
        /// </summary>
        public PayoutBreakdown CalculatePayout(Job job, DeliveryReport report)
        {
            var b = new PayoutBreakdown();
            if (job?.Cargo == null || _config == null) return b;

            float baseRate = job.Cargo.baseRatePerKm;
            float gross    = baseRate * job.DistanceKm;

            // Weight premium above 10 t.
            float tonnesOverTen = Mathf.Max(0f, (job.Cargo.massKg - 10000f) / 1000f);
            float weightFactor  = 1f + tonnesOverTen * _config.weightFactorPerTonne;

            // Difficulty and risk premiums.
            float difficulty = 1f;
            if (job.IsGhatRoute)                              difficulty *= _config.ghatRouteMultiplier;
            if (job.Cargo.Has(CargoFlags.Hazardous))          difficulty *= _config.hazardousMultiplier;
            if (job.Cargo.Has(CargoFlags.Fragile))            difficulty *= _config.fragileMultiplier;
            if (job.DestinationCity != null)                  difficulty *= job.DestinationCity.payoutMultiplier;

            float basePay = gross * difficulty * PayoutScalar;
            b.BasePay     = Mathf.RoundToInt(basePay);
            b.WeightBonus = Mathf.RoundToInt(basePay * (weightFactor - 1f));

            float subtotal = b.BasePay + b.WeightBonus;

            // Time: bonus when inside the limit, a ramped penalty when not.
            float minutesLate = report.ElapsedMinutes - job.TimeLimitMinutes;
            if (minutesLate <= 0f)
            {
                b.TimeAdjustment = Mathf.RoundToInt(subtotal * (_config.onTimeBonusMultiplier - 1f));
            }
            else
            {
                float t = Mathf.Clamp01(minutesLate / Mathf.Max(1f, _config.latenessFullPenaltyMinutes));
                float multiplier = Mathf.Lerp(1f, _config.latePenaltyFloor, t);
                b.TimeAdjustment = Mathf.RoundToInt(subtotal * (multiplier - 1f));   // negative
            }

            // Condition.
            float damage01 = Mathf.Clamp01(report.CargoDamage01);
            b.DamagePenalty = -Mathf.RoundToInt(subtotal * damage01 * _config.cargoDamageMaxPenalty);

            // Flat bonuses.
            if (report.FuelEfficient)   b.FuelBonus          = _config.fuelEfficiencyBonus;
            if (report.ViolationCount == 0) b.CleanDrivingBonus = _config.cleanDrivingBonus;

            b.Fines = -report.FinesIncurred;

            b.Total = Mathf.Max(0,
                b.BasePay + b.WeightBonus + b.TimeAdjustment + b.DamagePenalty +
                b.FuelBonus + b.CleanDrivingBonus + b.Fines);

            return b;
        }

        /// <summary>Preview payout shown on the job board, before anything can go wrong.</summary>
        public int EstimatePayout(Job job)
        {
            var ideal = new DeliveryReport
            {
                ElapsedMinutes  = job.TimeLimitMinutes * 0.8f,
                CargoDamage01   = 0f,
                FuelEfficient   = false,
                ViolationCount  = 0,
                FinesIncurred   = 0
            };
            var b = CalculatePayout(job, ideal);
            return b.BasePay + b.WeightBonus + b.TimeAdjustment;   // exclude discretionary bonuses
        }

        public int FuelCost(float litres) => Mathf.RoundToInt(litres * DieselPricePerLitre);

        public int TollCost(float segmentKm, bool fastag)
        {
            float cost = segmentKm * _remote.GetFloat("econ.toll_per_km", _config.tollRatePerKm);
            if (fastag) cost *= 1f - _config.fastagDiscount;
            return Mathf.Max(1, Mathf.RoundToInt(cost));
        }

        public int RepairCost(TruckData truck, float damage01)
            => Mathf.RoundToInt(truck.repairCostPerPercent * Mathf.Clamp01(damage01) * 100f);
    }

    /// <summary>What actually happened on a run, handed to the payout formula at the dock.</summary>
    public struct DeliveryReport
    {
        public float ElapsedMinutes;
        public float CargoDamage01;
        public bool  FuelEfficient;
        public int   ViolationCount;
        public int   FinesIncurred;
        public float DistanceDrivenKm;
    }
}
