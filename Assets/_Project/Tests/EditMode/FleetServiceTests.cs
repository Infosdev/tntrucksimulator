using NUnit.Framework;
using TNLorry.Core;
using TNLorry.Data;
using TNLorry.Gameplay;
using UnityEngine;
using UnityEngine.TestTools;

namespace TNLorry.Tests
{
    public class FleetServiceTests
    {
        EconomyConfig _config;
        SaveSystem _save;
        EconomyService _economy;
        ProgressionService _progression;
        GameDatabase _database;
        FleetService _fleet;

        const string CheapTruck = "kutty_lorry";
        const string BigTruck = "kaveri_k2518";

        [SetUp]
        public void SetUp()
        {
            _config = TestFixtures.Economy();
            _save = TempSave.Create();
            _economy = new EconomyService(_config, _save, new TestFixtures.PassthroughRemoteConfig());
            _progression = new ProgressionService(_config, _save);

            var cheap = TestFixtures.Truck();
            cheap.id = CheapTruck;
            cheap.price = 450000;
            cheap.unlockLevel = 1;
            cheap.repairCostPerPercent = 340f;

            var big = TestFixtures.Truck();
            big.id = BigTruck;
            big.price = 2400000;
            big.unlockLevel = 8;
            big.maxPayloadKg = 25000f;

            _database = ScriptableObject.CreateInstance<GameDatabase>();
            _database.trucks = new[] { cheap, big };
            _database.cargo = new CargoData[0];
            _database.cities = new CityData[0];
            _database.Build();

            _fleet = new FleetService(_config, _save, _economy, _progression, _database);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.Clear();
            TempSave.Cleanup();
        }

        void LevelUpTo(int level)
        {
            while (_progression.Level < level)
                _progression.AwardXp(_progression.XpForLevel(_progression.Level));
        }

        // ── Buying outright ────────────────────────────────────────────────

        [Test]
        public void BuyingOutrightDebitsTheFullPriceAndAddsTheTruck()
        {
            _economy.Credit(1000000, "test funds");
            long before = _economy.Balance;

            Assert.AreEqual(FleetService.PurchaseResult.Success, _fleet.BuyOutright(CheapTruck));

            Assert.AreEqual(before - 450000, _economy.Balance);
            Assert.AreEqual(1, _fleet.TruckCount);
            Assert.AreEqual(CheapTruck, _fleet.ActiveTruck.truckDataId);
            Assert.IsFalse(_fleet.HasActiveLoan, "An outright purchase must not open a loan.");
        }

        [Test]
        public void ANewTruckIsNotDeliveredWithAFullTank()
        {
            _economy.Credit(1000000, "test funds");
            _fleet.BuyOutright(CheapTruck);

            var truck = _database.GetTruck(CheapTruck);
            Assert.Less(_fleet.ActiveTruck.fuelLitres, truck.fuelCapacityLitres);
            Assert.Greater(_fleet.ActiveTruck.fuelLitres, 0f, "But it must have enough to reach a bunk.");
        }

        [Test]
        public void CannotBuyWhatYouCannotAfford()
        {
            // BigTruck is level-gated at 8, and FleetService checks level before affordability,
            // so without levelling first this would assert the wrong refusal reason.
            LevelUpTo(8);
            long before = _economy.Balance;

            Assert.Less(before, _database.GetTruck(BigTruck).price, "Precondition: genuinely unaffordable.");
            Assert.AreEqual(FleetService.PurchaseResult.CannotAfford, _fleet.BuyOutright(BigTruck));
            Assert.AreEqual(before, _economy.Balance, "A refused purchase must not move the balance.");
            Assert.AreEqual(0, _fleet.TruckCount);
        }

        [Test]
        public void CannotBuyAboveYourLevel()
        {
            _economy.Credit(10000000, "test funds");

            Assert.AreEqual(FleetService.PurchaseResult.LevelTooLow, _fleet.BuyOutright(BigTruck));
            Assert.AreEqual(0, _fleet.TruckCount);
        }

        [Test]
        public void UnknownTruckIdIsRejected()
        {
            Assert.AreEqual(FleetService.PurchaseResult.UnknownTruck, _fleet.BuyOutright("not_a_truck"));
        }

        // ── Loans ──────────────────────────────────────────────────────────

        [Test]
        public void EmiCoversPrincipalPlusInterest()
        {
            long principal = 1000000;
            const int instalments = 24;

            long emi = FleetService.CalculateEmi(principal, 0.011f, instalments);

            Assert.Greater(emi * instalments, principal, "An interest-bearing loan must repay more than it lent.");
            Assert.Less(emi * instalments, principal * 2, "…but 24 months at 1.1% is not a doubling.");
        }

        [Test]
        public void ZeroInterestSplitsThePrincipalEvenly()
        {
            long emi = FleetService.CalculateEmi(120000, 0f, 12);
            Assert.AreEqual(10000, emi);
        }

        [Test]
        public void MoreInstalmentsMeansASmallerPaymentButMoreTotalInterest()
        {
            long shortTerm = FleetService.CalculateEmi(1000000, 0.011f, 12);
            long longTerm = FleetService.CalculateEmi(1000000, 0.011f, 48);

            Assert.Less(longTerm, shortTerm, "Spreading it out must lower the monthly payment.");
            Assert.Greater(longTerm * 48, shortTerm * 12, "…and raise the total cost.");
        }

        [Test]
        public void BuyingOnLoanTakesOnlyTheDownPaymentUpFront()
        {
            LevelUpTo(_config.levelForBankLoan);
            _economy.Credit(1000000, "test funds");
            long before = _economy.Balance;

            var truck = _database.GetTruck(BigTruck);
            truck.unlockLevel = 1;   // level-gate the loan, not this test

            Assert.AreEqual(FleetService.PurchaseResult.Success, _fleet.BuyOnLoan(BigTruck, 24, currentDay: 1));

            long downPayment = _fleet.DownPaymentFor(truck);
            Assert.AreEqual(before - downPayment, _economy.Balance);
            Assert.Less(downPayment, truck.price, "The bank must be funding most of it.");
            Assert.IsTrue(_fleet.HasActiveLoan);
            Assert.Greater(_fleet.Outstanding, 0);
            Assert.AreEqual(1, _fleet.TruckCount);
        }

        [Test]
        public void LoansAreGatedOnLevel()
        {
            _economy.Credit(10000000, "test funds");
            var truck = _database.GetTruck(BigTruck);
            truck.unlockLevel = 1;

            Assert.IsFalse(_progression.CanTakeBankLoan);
            Assert.AreEqual(FleetService.PurchaseResult.LoanNotAvailable,
                            _fleet.BuyOnLoan(BigTruck, 24, currentDay: 1));
        }

        [Test]
        public void OnlyOneLoanAtATime()
        {
            LevelUpTo(_config.levelForBankLoan);
            _economy.Credit(5000000, "test funds");
            _database.GetTruck(BigTruck).unlockLevel = 1;

            Assert.AreEqual(FleetService.PurchaseResult.Success, _fleet.BuyOnLoan(BigTruck, 24, 1));
            Assert.AreEqual(FleetService.PurchaseResult.AlreadyHasLoan, _fleet.BuyOnLoan(CheapTruck, 12, 1));
        }

        [Test]
        public void EmiIsNotTakenBeforeItIsDue()
        {
            LevelUpTo(_config.levelForBankLoan);
            _economy.Credit(5000000, "test funds");
            _database.GetTruck(BigTruck).unlockLevel = 1;
            _fleet.BuyOnLoan(BigTruck, 24, currentDay: 1);

            long before = _economy.Balance;

            Assert.IsFalse(_fleet.TickDay(2), "Day 2 of a 30-day cycle is not due.");
            Assert.AreEqual(before, _economy.Balance);
            Assert.AreEqual(FleetService.DaysPerInstalment - 1, _fleet.DaysUntilNextEmi(2));
        }

        [Test]
        public void EmiIsTakenOnTheDueDay()
        {
            LevelUpTo(_config.levelForBankLoan);
            _economy.Credit(5000000, "test funds");
            _database.GetTruck(BigTruck).unlockLevel = 1;
            _fleet.BuyOnLoan(BigTruck, 24, currentDay: 1);

            long before = _economy.Balance;
            long emi = _fleet.MonthlyEmi;
            long outstandingBefore = _fleet.Outstanding;

            Assert.IsTrue(_fleet.TickDay(1 + FleetService.DaysPerInstalment));

            Assert.AreEqual(before - emi, _economy.Balance);
            Assert.AreEqual(outstandingBefore - emi, _fleet.Outstanding);
        }

        [Test]
        public void TheFinalInstalmentReportsItsRealAmountAndClosesTheLoan()
        {
            LevelUpTo(_config.levelForBankLoan);
            _economy.Credit(50000000, "test funds");
            _database.GetTruck(BigTruck).unlockLevel = 1;
            _fleet.BuyOnLoan(BigTruck, 6, currentDay: 1);

            LoanInstalmentDueEvent last = default;
            void Handler(LoanInstalmentDueEvent e) => last = e;
            EventBus.Subscribe<LoanInstalmentDueEvent>(Handler);

            int day = 1;
            for (int i = 0; i < 6; i++)
            {
                day += FleetService.DaysPerInstalment;
                _fleet.TickDay(day);
            }

            EventBus.Unsubscribe<LoanInstalmentDueEvent>(Handler);

            Assert.Greater(last.Amount, 0, "The last instalment must not be reported as zero.");
            Assert.AreEqual(0, last.RemainingInstalments);
            Assert.IsFalse(_fleet.HasActiveLoan, "Six of six paid closes the loan.");
            Assert.AreEqual(0, _fleet.Outstanding);
        }

        [Test]
        public void AMissedEmiGoesOverdrawnAndCostsReputation()
        {
            LevelUpTo(_config.levelForBankLoan);
            _economy.Credit(5000000, "test funds");
            _database.GetTruck(BigTruck).unlockLevel = 1;
            _fleet.BuyOnLoan(BigTruck, 24, currentDay: 1);

            // Spend everything, then let the instalment fall due.
            _economy.TryDebit(_economy.Balance, "spend it all");
            int reputationBefore = _progression.Reputation;

            bool paidEvent = true;
            void Handler(LoanInstalmentDueEvent e) => paidEvent = e.Paid;
            EventBus.Subscribe<LoanInstalmentDueEvent>(Handler);

            _fleet.TickDay(1 + FleetService.DaysPerInstalment);

            EventBus.Unsubscribe<LoanInstalmentDueEvent>(Handler);

            Assert.IsFalse(paidEvent, "The event must report that it could not be covered.");
            Assert.Less(_economy.Balance, 0, "The EMI is not optional — it takes the account negative.");
            Assert.Less(_progression.Reputation, reputationBefore);
        }

        [Test]
        public void SleepingThroughSeveralDueDatesChargesEveryInstalment()
        {
            // A long rest at a lodge can jump the clock past more than one due date.
            LevelUpTo(_config.levelForBankLoan);
            _economy.Credit(50000000, "test funds");
            _database.GetTruck(BigTruck).unlockLevel = 1;
            _fleet.BuyOnLoan(BigTruck, 24, currentDay: 1);

            long emi = _fleet.MonthlyEmi;
            long before = _economy.Balance;

            // CompanyTicker catches up a day at a time; this replicates that.
            for (int day = 2; day <= 1 + FleetService.DaysPerInstalment * 3; day++) _fleet.TickDay(day);

            Assert.AreEqual(before - emi * 3, _economy.Balance,
                "Three cycles must mean three instalments, not one.");
        }

        [Test]
        public void SettlingEarlyClearsTheDebtInOneGo()
        {
            LevelUpTo(_config.levelForBankLoan);
            _economy.Credit(50000000, "test funds");
            _database.GetTruck(BigTruck).unlockLevel = 1;
            _fleet.BuyOnLoan(BigTruck, 24, currentDay: 1);

            long outstanding = _fleet.Outstanding;
            long before = _economy.Balance;

            Assert.IsTrue(_fleet.SettleLoanEarly());

            Assert.AreEqual(before - outstanding, _economy.Balance);
            Assert.IsFalse(_fleet.HasActiveLoan);
            Assert.AreEqual(0, _fleet.Outstanding);
        }

        [Test]
        public void CannotSettleWhatYouCannotCover()
        {
            LevelUpTo(_config.levelForBankLoan);
            _economy.Credit(5000000, "test funds");
            _database.GetTruck(BigTruck).unlockLevel = 1;
            _fleet.BuyOnLoan(BigTruck, 24, currentDay: 1);

            _economy.TryDebit(_economy.Balance, "spend it all");

            Assert.IsFalse(_fleet.SettleLoanEarly());
            Assert.IsTrue(_fleet.HasActiveLoan);
        }

        // ── Repair & resale ────────────────────────────────────────────────

        [Test]
        public void RepairQuoteBillsEveryDamagedComponent()
        {
            _economy.Credit(1000000, "test funds");
            _fleet.BuyOutright(CheapTruck);

            var owned = _fleet.ActiveTruck;
            Assert.AreEqual(0, _fleet.RepairQuote(owned), "An undamaged truck is free to service.");

            owned.engineDamage = 0.5f;
            int engineOnly = _fleet.RepairQuote(owned);

            owned.chassisDamage = 0.5f;
            int both = _fleet.RepairQuote(owned);

            Assert.Greater(engineOnly, 0);
            Assert.AreEqual(engineOnly * 2, both, 1, "Two equally damaged components cost twice as much.");
        }

        [Test]
        public void RepairingClearsDamageAndCharges()
        {
            _economy.Credit(1000000, "test funds");
            _fleet.BuyOutright(CheapTruck);

            var owned = _fleet.ActiveTruck;
            owned.engineDamage = 0.6f;
            owned.wheelDamage = 0.3f;

            int quote = _fleet.RepairQuote(owned);
            long before = _economy.Balance;

            Assert.IsTrue(_fleet.Repair(owned));

            Assert.AreEqual(before - quote, _economy.Balance);
            Assert.AreEqual(0f, FleetService.WorstDamage(owned));
        }

        [Test]
        public void RepairIsRefusedWhenItCannotBePaidFor()
        {
            _economy.Credit(1000000, "test funds");
            _fleet.BuyOutright(CheapTruck);

            var owned = _fleet.ActiveTruck;
            owned.engineDamage = 1f;
            _economy.TryDebit(_economy.Balance, "spend it all");

            Assert.IsFalse(_fleet.Repair(owned));
            Assert.AreEqual(1f, owned.engineDamage, "A refused repair must not fix the truck for free.");
        }

        [Test]
        public void TradeInIsWellBelowNewAndFallsWithWearAndDamage()
        {
            _economy.Credit(1000000, "test funds");
            _fleet.BuyOutright(CheapTruck);

            var owned = _fleet.ActiveTruck;
            long pristine = _fleet.TradeInValue(owned);

            owned.odometerKm = 180000f;
            long worn = _fleet.TradeInValue(owned);

            owned.engineDamage = 0.9f;
            long wrecked = _fleet.TradeInValue(owned);

            Assert.Less(pristine, 450000, "Driving it off the forecourt must cost the player money.");
            Assert.Less(worn, pristine);
            Assert.Less(wrecked, worn);
            Assert.GreaterOrEqual(wrecked, 0);
        }

        [Test]
        public void TheLastTruckCannotBeSold()
        {
            _economy.Credit(1000000, "test funds");
            _fleet.BuyOutright(CheapTruck);

            LogAssert.ignoreFailingMessages = true;
            Assert.IsFalse(_fleet.Sell(0), "Selling the only truck would strand the player.");
            LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual(1, _fleet.TruckCount);
        }

        [Test]
        public void AFinancedTruckCannotBeSoldWhileItIsCollateral()
        {
            LevelUpTo(_config.levelForBankLoan);
            _economy.Credit(50000000, "test funds");
            _database.GetTruck(BigTruck).unlockLevel = 1;

            _fleet.BuyOutright(CheapTruck);
            _fleet.BuyOnLoan(BigTruck, 24, currentDay: 1);

            int financedIndex = _fleet.TruckCount - 1;

            LogAssert.ignoreFailingMessages = true;
            Assert.IsFalse(_fleet.Sell(financedIndex));
            LogAssert.ignoreFailingMessages = false;

            // Clearing the loan releases it.
            Assert.IsTrue(_fleet.SettleLoanEarly());
            Assert.IsTrue(_fleet.Sell(financedIndex));
        }

        [Test]
        public void SellingCreditsTheTradeInAndKeepsTheActiveIndexValid()
        {
            _economy.Credit(2000000, "test funds");
            _fleet.BuyOutright(CheapTruck);
            _fleet.BuyOutright(CheapTruck);

            long expected = _fleet.TradeInValue(_fleet.OwnedTrucks[1]);
            long before = _economy.Balance;

            Assert.IsTrue(_fleet.Sell(1));

            Assert.AreEqual(before + expected, _economy.Balance);
            Assert.AreEqual(1, _fleet.TruckCount);
            Assert.IsNotNull(_fleet.ActiveTruck, "The active index must stay inside the list after a sale.");
        }

        [Test]
        public void FleetSurvivesAReload()
        {
            _economy.Credit(1000000, "test funds");
            _fleet.BuyOutright(CheapTruck);
            _fleet.ActiveTruck.odometerKm = 1234f;
            _fleet.ActiveTruck.nickname = "Amma Thunai";
            _save.SaveImmediate();

            var reloaded = TempSave.Reopen();

            Assert.AreEqual(1, reloaded.Data.ownedTrucks.Count);
            Assert.AreEqual(CheapTruck, reloaded.Data.ownedTrucks[0].truckDataId);
            Assert.AreEqual(1234f, reloaded.Data.ownedTrucks[0].odometerKm, 0.01f);
            Assert.AreEqual("Amma Thunai", reloaded.Data.ownedTrucks[0].nickname);
        }

        [Test]
        public void LoanTermsSurviveAReload()
        {
            LevelUpTo(_config.levelForBankLoan);
            _economy.Credit(5000000, "test funds");
            _database.GetTruck(BigTruck).unlockLevel = 1;
            _fleet.BuyOnLoan(BigTruck, 18, currentDay: 4);

            long emi = _fleet.MonthlyEmi;
            long outstanding = _fleet.Outstanding;
            _save.SaveImmediate();

            var reloaded = TempSave.Reopen();

            Assert.IsTrue(reloaded.Data.loan.active);
            Assert.AreEqual(emi, reloaded.Data.loan.emiAmount);
            Assert.AreEqual(outstanding, reloaded.Data.debt);
            Assert.AreEqual(18, reloaded.Data.loan.remainingInstalments);
            Assert.AreEqual(4 + FleetService.DaysPerInstalment, reloaded.Data.loan.nextDueDay);
        }
    }
}
