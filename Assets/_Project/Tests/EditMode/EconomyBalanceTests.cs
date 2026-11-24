using NUnit.Framework;
using TNLorry.Core;
using TNLorry.Gameplay;

namespace TNLorry.Tests
{
    /// <summary>
    /// EconomyService is the sole owner of the money balance. These tests pin the three
    /// behaviours the rest of the game depends on: purchases cannot overdraw, fines can, and
    /// every movement marks the save dirty and publishes an event.
    /// </summary>
    public class EconomyBalanceTests
    {
        SaveSystem _save;
        EconomyService _economy;

        [SetUp]
        public void SetUp()
        {
            _save = TempSave.Create();
            _economy = new EconomyService(TestFixtures.Economy(), _save,
                                          new TestFixtures.PassthroughRemoteConfig());
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.Clear();
            TempSave.Cleanup();
        }

        [Test]
        public void CreditIncreasesTheBalanceAndPublishes()
        {
            long before = _economy.Balance;
            MoneyChangedEvent captured = default;
            int events = 0;

            void Handler(MoneyChangedEvent e) { captured = e; events++; }
            EventBus.Subscribe<MoneyChangedEvent>(Handler);

            _economy.Credit(5000, "test");

            EventBus.Unsubscribe<MoneyChangedEvent>(Handler);

            Assert.AreEqual(before + 5000, _economy.Balance);
            Assert.AreEqual(1, events, "Exactly one event per balance movement.");
            Assert.AreEqual(5000, captured.Delta);
            Assert.AreEqual(_economy.Balance, captured.Balance);
            Assert.IsTrue(_save.IsDirty, "A balance change must mark the save dirty.");
        }

        [Test]
        public void NonPositiveAmountsAreNoOps()
        {
            long before = _economy.Balance;
            int events = 0;

            void Handler(MoneyChangedEvent e) { events++; }
            EventBus.Subscribe<MoneyChangedEvent>(Handler);

            _economy.Credit(0, "zero");
            _economy.Credit(-100, "negative");
            _economy.ForceDebit(0, "zero");

            EventBus.Unsubscribe<MoneyChangedEvent>(Handler);

            Assert.AreEqual(before, _economy.Balance);
            Assert.AreEqual(0, events, "A no-op must not spam the event bus.");
        }

        [Test]
        public void TryDebitRefusesWhenUnaffordableAndChangesNothing()
        {
            long before = _economy.Balance;

            Assert.IsFalse(_economy.TryDebit(before + 1, "too expensive"));
            Assert.AreEqual(before, _economy.Balance, "A refused purchase must not move the balance.");
        }

        [Test]
        public void TryDebitSucceedsAtExactlyTheBalance()
        {
            long before = _economy.Balance;

            Assert.IsTrue(_economy.TryDebit(before, "spend everything"));
            Assert.AreEqual(0, _economy.Balance);
        }

        [Test]
        public void ForceDebitCanGoNegativeBecauseFinesAreNotOptional()
        {
            _economy.TryDebit(_economy.Balance, "empty the account");

            _economy.ForceDebit(2000, "fine:Overspeed");

            Assert.AreEqual(-2000, _economy.Balance,
                "Fines and tolls must be able to put the player in debt — that is the pressure.");
        }

        [Test]
        public void CanAffordMatchesTryDebit()
        {
            long balance = _economy.Balance;

            Assert.IsTrue(_economy.CanAfford(balance));
            Assert.IsFalse(_economy.CanAfford(balance + 1));
        }

        [Test]
        public void BalancePersistsAcrossAReload()
        {
            _economy.Credit(12345, "test");
            long expected = _economy.Balance;

            _save.SaveImmediate();
            var reloaded = TempSave.Reopen();

            Assert.AreEqual(expected, reloaded.Data.money);
        }

        [Test]
        public void RepairCostScalesWithDamage()
        {
            var truck = TestFixtures.Truck();
            truck.repairCostPerPercent = 900f;

            Assert.AreEqual(0, _economy.RepairCost(truck, 0f));
            Assert.AreEqual(45000, _economy.RepairCost(truck, 0.5f), 1);
            Assert.AreEqual(90000, _economy.RepairCost(truck, 1f), 1);
            Assert.AreEqual(90000, _economy.RepairCost(truck, 5f), 1, "Damage is clamped to 100%.");
        }
    }
}
