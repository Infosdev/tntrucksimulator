using System.Collections.Generic;
using TNLorry.Core;
using TNLorry.Data;
using UnityEngine;

namespace TNLorry.Gameplay
{
    /// <summary>
    /// The company side of the game (sections E.2, F.4, O.1 screen 10): owning trucks, buying
    /// them outright or on a bank loan, repairing them, and servicing the EMI.
    ///
    /// Sole writer of SaveData.ownedTrucks, activeTruckIndex, debt and loan. Money always moves
    /// through EconomyService, so every rupee here still fires MoneyChangedEvent.
    ///
    /// The loan is the game's main mid-game pressure: it arrives monthly whether or not the
    /// player drove, which is what turns "earn money" into "run a business".
    /// </summary>
    public class FleetService
    {
        /// <summary>In-game days per loan instalment — one in-game "month".</summary>
        public const int DaysPerInstalment = 30;

        readonly EconomyConfig _config;
        readonly SaveSystem _save;
        readonly EconomyService _economy;
        readonly ProgressionService _progression;
        readonly GameDatabase _database;

        public FleetService(EconomyConfig config, SaveSystem save, EconomyService economy,
                            ProgressionService progression, GameDatabase database)
        {
            _config = config;
            _save = save;
            _economy = economy;
            _progression = progression;
            _database = database;
        }

        // ── Fleet ───────────────────────────────────────────────────────────

        public IReadOnlyList<OwnedTruckSave> OwnedTrucks => _save.Data.ownedTrucks;
        public int TruckCount => _save.Data.ownedTrucks.Count;

        public OwnedTruckSave ActiveTruck
        {
            get
            {
                var trucks = _save.Data.ownedTrucks;
                if (trucks.Count == 0) return null;
                int index = Mathf.Clamp(_save.Data.activeTruckIndex, 0, trucks.Count - 1);
                return trucks[index];
            }
        }

        public TruckData ActiveTruckData
        {
            get
            {
                var owned = ActiveTruck;
                return owned == null ? null : _database.GetTruck(owned.truckDataId);
            }
        }

        public bool SetActiveTruck(int index)
        {
            if (index < 0 || index >= _save.Data.ownedTrucks.Count) return false;
            _save.Data.activeTruckIndex = index;
            _save.MarkDirty();
            return true;
        }

        // ── Buying ──────────────────────────────────────────────────────────

        public enum PurchaseResult { Success, CannotAfford, LevelTooLow, UnknownTruck, LoanNotAvailable, AlreadyHasLoan }

        public int PriceOf(TruckData truck) => truck == null ? 0 : truck.price;

        /// <summary>Minimum down payment as a fraction of price — the bank will not fund it all.</summary>
        public const float MinimumDownPaymentFraction = 0.25f;

        public long DownPaymentFor(TruckData truck)
            => (long)Mathf.Ceil(PriceOf(truck) * MinimumDownPaymentFraction);

        public PurchaseResult BuyOutright(string truckId)
        {
            var truck = _database.GetTruck(truckId);
            if (truck == null) return PurchaseResult.UnknownTruck;
            if (_progression.Level < truck.unlockLevel) return PurchaseResult.LevelTooLow;
            if (!_economy.TryDebit(truck.price, "buy_truck:" + truckId)) return PurchaseResult.CannotAfford;

            AddTruck(truck);
            return PurchaseResult.Success;
        }

        /// <summary>
        /// Buy on finance: pay the down payment now, owe the rest over `instalments` in-game
        /// months at the configured monthly interest. Gated on level (section E.2: level 5).
        /// </summary>
        public PurchaseResult BuyOnLoan(string truckId, int instalments, int currentDay)
        {
            var truck = _database.GetTruck(truckId);
            if (truck == null) return PurchaseResult.UnknownTruck;
            if (_progression.Level < truck.unlockLevel) return PurchaseResult.LevelTooLow;
            if (!_progression.CanTakeBankLoan) return PurchaseResult.LoanNotAvailable;
            if (_save.Data.loan.active) return PurchaseResult.AlreadyHasLoan;

            instalments = Mathf.Clamp(instalments, 6, 60);

            long downPayment = DownPaymentFor(truck);
            if (!_economy.CanAfford(downPayment)) return PurchaseResult.CannotAfford;

            long principal = truck.price - downPayment;
            float rate = _config.loanMonthlyInterest;

            _economy.TryDebit(downPayment, "down_payment:" + truckId);

            var loan = _save.Data.loan;
            loan.active = true;
            loan.principalBorrowed = principal;
            loan.monthlyInterest = rate;
            loan.emiAmount = CalculateEmi(principal, rate, instalments);
            loan.remainingInstalments = instalments;
            loan.missedInstalments = 0;
            loan.nextDueDay = currentDay + DaysPerInstalment;
            loan.purchasedTruckId = truckId;

            // Outstanding is the full repayable amount, so the UI can show what is still owed
            // rather than only the principal.
            _save.Data.debt = loan.emiAmount * instalments;

            AddTruck(truck);
            return PurchaseResult.Success;
        }

        /// <summary>
        /// Standard amortising EMI: P x r x (1+r)^n / ((1+r)^n - 1).
        /// Falls back to a flat split when the rate is zero, which the formula cannot handle.
        /// </summary>
        public static long CalculateEmi(long principal, float monthlyRate, int instalments)
        {
            if (instalments <= 0) return principal;
            if (monthlyRate <= 0.00001f) return (long)Mathf.Ceil(principal / (float)instalments);

            double r = monthlyRate;
            double compound = System.Math.Pow(1.0 + r, instalments);
            double emi = principal * r * compound / (compound - 1.0);

            return (long)System.Math.Ceiling(emi);
        }

        void AddTruck(TruckData truck)
        {
            var owned = new OwnedTruckSave
            {
                truckDataId = truck.id,
                nickname = "",
                fuelLitres = truck.fuelCapacityLitres * 0.5f,   // dealers do not fill the tank
                odometerKm = 0f
            };

            _save.Data.ownedTrucks.Add(owned);
            _save.Data.activeTruckIndex = _save.Data.ownedTrucks.Count - 1;
            _save.MarkDirty();

            EventBus.Publish(new TruckPurchasedEvent
            {
                TruckDataId = truck.id,
                Price = truck.price,
                OnLoan = _save.Data.loan.active && _save.Data.loan.purchasedTruckId == truck.id
            });
        }

        /// <summary>
        /// Trade-in value: depreciated by wear and condition. Deliberately well under the new
        /// price — selling should feel like a loss, so the fleet grows rather than churns.
        /// </summary>
        public long TradeInValue(OwnedTruckSave owned)
        {
            var truck = _database.GetTruck(owned?.truckDataId);
            if (truck == null) return 0;

            float odometerWear = Mathf.Clamp01(owned.odometerKm / 200000f);
            float condition = 1f - Mathf.Clamp01(WorstDamage(owned));

            float factor = 0.6f * (1f - odometerWear * 0.5f) * Mathf.Lerp(0.5f, 1f, condition);
            return (long)(truck.price * factor);
        }

        public bool Sell(int index)
        {
            var trucks = _save.Data.ownedTrucks;
            if (index < 0 || index >= trucks.Count) return false;

            if (trucks.Count == 1)
            {
                Debug.LogWarning("[FleetService] Refusing to sell the last truck — the player would be stranded.");
                return false;
            }

            var owned = trucks[index];

            // A financed truck is the bank's collateral until the loan clears.
            if (_save.Data.loan.active && _save.Data.loan.purchasedTruckId == owned.truckDataId)
            {
                Debug.LogWarning("[FleetService] Cannot sell a truck that is still under finance.");
                return false;
            }

            _economy.Credit(TradeInValue(owned), "sell_truck:" + owned.truckDataId);

            trucks.RemoveAt(index);
            _save.Data.activeTruckIndex = Mathf.Clamp(_save.Data.activeTruckIndex, 0, trucks.Count - 1);
            _save.MarkDirty();
            return true;
        }

        // ── Repair ──────────────────────────────────────────────────────────

        public static float WorstDamage(OwnedTruckSave owned)
        {
            if (owned == null) return 0f;
            return Mathf.Max(Mathf.Max(owned.engineDamage, owned.chassisDamage),
                             Mathf.Max(owned.cabinDamage, owned.wheelDamage));
        }

        public int RepairQuote(OwnedTruckSave owned)
        {
            var truck = _database.GetTruck(owned?.truckDataId);
            if (truck == null) return 0;

            // Every component is billed, not just the worst one.
            float total = owned.engineDamage + owned.chassisDamage + owned.cabinDamage + owned.wheelDamage;
            return Mathf.RoundToInt(truck.repairCostPerPercent * total * 100f);
        }

        public bool Repair(OwnedTruckSave owned)
        {
            int cost = RepairQuote(owned);
            if (cost <= 0) return true;
            if (!_economy.TryDebit(cost, "repair:" + owned.truckDataId)) return false;

            owned.engineDamage = owned.chassisDamage = owned.cabinDamage = owned.wheelDamage = 0f;
            _save.MarkDirty();
            return true;
        }

        // ── Loan servicing ──────────────────────────────────────────────────

        /// <summary>
        /// Called on each in-game day rollover. Returns true when an instalment was taken.
        ///
        /// The EMI is a ForceDebit: it is not optional, and going overdrawn is the intended
        /// consequence of over-borrowing. A missed instalment is tracked so the reputation hit
        /// can escalate rather than silently doing nothing.
        /// </summary>
        public bool TickDay(int currentDay)
        {
            var loan = _save.Data.loan;
            if (!loan.active || currentDay < loan.nextDueDay) return false;

            // Captured before ClearLoan zeroes the terms, or the final instalment would be
            // reported to the UI as ₹0.
            long amount = loan.emiAmount;

            bool paid = _economy.CanAfford(amount);
            _economy.ForceDebit(amount, "loan_emi");

            if (!paid)
            {
                loan.missedInstalments++;
                _progression.AdjustReputation(-2);
            }

            _save.Data.debt = System.Math.Max(0L, _save.Data.debt - amount);
            loan.remainingInstalments--;
            loan.nextDueDay = currentDay + DaysPerInstalment;

            int remaining = Mathf.Max(0, loan.remainingInstalments);
            if (loan.remainingInstalments <= 0) ClearLoan();

            _save.MarkDirty();

            EventBus.Publish(new LoanInstalmentDueEvent
            {
                Amount = amount,
                Paid = paid,
                RemainingInstalments = remaining,
                Outstanding = _save.Data.debt
            });

            return true;
        }

        /// <summary>Settle early. Saves the remaining interest, which is a real reward for saving up.</summary>
        public bool SettleLoanEarly()
        {
            var loan = _save.Data.loan;
            if (!loan.active) return false;

            long outstanding = _save.Data.debt;
            if (!_economy.TryDebit(outstanding, "loan_settle")) return false;

            ClearLoan();
            _save.Data.debt = 0;
            _save.MarkDirty();
            return true;
        }

        void ClearLoan()
        {
            var loan = _save.Data.loan;
            loan.active = false;
            loan.remainingInstalments = 0;
            loan.emiAmount = 0;
            loan.purchasedTruckId = "";
            _save.Data.debt = 0;
        }

        public bool HasActiveLoan => _save.Data.loan.active;
        public long Outstanding => _save.Data.debt;
        public long MonthlyEmi => _save.Data.loan.active ? _save.Data.loan.emiAmount : 0;
        public int  DaysUntilNextEmi(int currentDay) =>
            _save.Data.loan.active ? Mathf.Max(0, _save.Data.loan.nextDueDay - currentDay) : 0;
    }
}
