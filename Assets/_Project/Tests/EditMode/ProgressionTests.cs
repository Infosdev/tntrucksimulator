using NUnit.Framework;
using TNLorry.Core;
using TNLorry.Data;
using TNLorry.Gameplay;

namespace TNLorry.Tests
{
    public class ProgressionTests
    {
        EconomyConfig _config;
        SaveSystem _save;
        ProgressionService _progression;

        [SetUp]
        public void SetUp()
        {
            _config = TestFixtures.Economy();
            _save = TempSave.Create();
            _progression = new ProgressionService(_config, _save);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.Clear();
            TempSave.Cleanup();
        }

        [Test]
        public void StartsAtLevelOne()
        {
            Assert.AreEqual(1, _progression.Level);
            Assert.AreEqual(0, _progression.Xp);
        }

        [Test]
        public void LevelCurveIsStrictlyIncreasing()
        {
            // A flat or dipping curve means a later level is cheaper than an earlier one,
            // which players find instantly and exploit.
            int previous = 0;
            for (int level = 1; level < _config.maxLevel; level++)
            {
                int needed = _progression.XpForLevel(level);
                Assert.Greater(needed, previous, "XP for level " + level + " must exceed level " + (level - 1));
                previous = needed;
            }
        }

        [Test]
        public void AwardingExactlyEnoughXpLevelsUpOnce()
        {
            int needed = _progression.XpForLevel(1);

            _progression.AwardXp(needed);

            Assert.AreEqual(2, _progression.Level);
            Assert.AreEqual(0, _progression.Xp, "Spending exactly the requirement must leave no remainder.");
        }

        [Test]
        public void SurplusXpCarriesIntoTheNextLevel()
        {
            int needed = _progression.XpForLevel(1);

            _progression.AwardXp(needed + 37);

            Assert.AreEqual(2, _progression.Level);
            Assert.AreEqual(37, _progression.Xp);
        }

        [Test]
        public void OneHugeAwardCanCrossSeveralLevels()
        {
            // A Chennai-to-Madurai haul on a fresh profile should not stop at one level-up.
            int enoughForThree = _progression.XpForLevel(1) + _progression.XpForLevel(2) + _progression.XpForLevel(3);

            _progression.AwardXp(enoughForThree);

            Assert.AreEqual(4, _progression.Level);
        }

        [Test]
        public void LevelUpEventReportsTheFinalLevel()
        {
            XpChangedEvent captured = default;
            void Handler(XpChangedEvent e) => captured = e;
            EventBus.Subscribe<XpChangedEvent>(Handler);

            _progression.AwardXp(_progression.XpForLevel(1) + _progression.XpForLevel(2));

            EventBus.Unsubscribe<XpChangedEvent>(Handler);

            Assert.IsTrue(captured.LeveledUp);
            Assert.AreEqual(_progression.Level, captured.Level, "The event must report where the player ended up.");
        }

        [Test]
        public void SubLevelXpDoesNotFireALevelUp()
        {
            XpChangedEvent captured = default;
            void Handler(XpChangedEvent e) => captured = e;
            EventBus.Subscribe<XpChangedEvent>(Handler);

            _progression.AwardXp(10);

            EventBus.Unsubscribe<XpChangedEvent>(Handler);

            Assert.IsFalse(captured.LeveledUp);
            Assert.AreEqual(1, _progression.Level);
        }

        [Test]
        public void MaxLevelIsACeiling()
        {
            _progression.AwardXp(int.MaxValue / 2);
            _progression.AwardXp(int.MaxValue / 2);

            Assert.AreEqual(_config.maxLevel, _progression.Level);
            Assert.AreEqual(0, _progression.Xp, "At the cap there is nothing to accumulate toward.");
        }

        [Test]
        public void ReachingGateLevelsUnlocksTheirCities()
        {
            Assert.IsFalse(_progression.IsCityUnlocked("namakkal"));

            // Level 3 is the Namakkal gate.
            while (_progression.Level < 3) _progression.AwardXp(_progression.XpForLevel(_progression.Level));

            Assert.IsTrue(_progression.IsCityUnlocked("namakkal"));
            Assert.IsTrue(_progression.IsCityUnlocked("salem"), "Starting unlocks must survive levelling.");
        }

        [Test]
        public void CrossingSeveralGatesAtOnceAppliesEveryUnlock()
        {
            // One enormous award: every gate crossed must still be applied, not only the last.
            while (_progression.Level < 10) _progression.AwardXp(_progression.XpForLevel(_progression.Level));

            Assert.IsTrue(_progression.IsCityUnlocked("namakkal"), "Level 3 gate");
            Assert.IsTrue(_progression.IsCityUnlocked("erode"), "Level 6 gate");
            Assert.IsTrue(_progression.IsCityUnlocked("coimbatore"), "Level 9 gate");
            Assert.IsTrue(_progression.IsCargoUnlocked("eggs"), "Level 4 cargo gate");
        }

        [Test]
        public void UnlocksAreNotDuplicatedOnRepeatedLevelling()
        {
            while (_progression.Level < 4) _progression.AwardXp(_progression.XpForLevel(_progression.Level));

            int namakkalCount = 0;
            foreach (var id in _save.Data.unlockedCityIds)
                if (id == "namakkal") namakkalCount++;

            Assert.AreEqual(1, namakkalCount);
        }

        [Test]
        public void ProgressGatesMatchTheConfig()
        {
            Assert.IsFalse(_progression.CanTakeBankLoan);

            while (_progression.Level < _config.levelForBankLoan)
                _progression.AwardXp(_progression.XpForLevel(_progression.Level));

            Assert.IsTrue(_progression.CanTakeBankLoan, "Level 5 is the first-truck loan gate.");
            Assert.IsFalse(_progression.CanHireDrivers, "Hiring is level 20, not 5.");
        }

        [Test]
        public void LevelProgressRunsZeroToOne()
        {
            Assert.AreEqual(0f, _progression.LevelProgress01, 0.001f);

            _progression.AwardXp(_progression.XpForLevel(1) / 2);

            Assert.Greater(_progression.LevelProgress01, 0.4f);
            Assert.Less(_progression.LevelProgress01, 0.6f);
        }

        [Test]
        public void ReputationIsClampedToZeroHundred()
        {
            _progression.AdjustReputation(1000);
            Assert.AreEqual(100, _progression.Reputation);

            _progression.AdjustReputation(-1000);
            Assert.AreEqual(0, _progression.Reputation);
        }

        [Test]
        public void DeliveryXpRewardsDistanceAndDifficulty()
        {
            var easy = new Job
            {
                Cargo = TestFixtures.Cargo(),
                DistanceKm = 100f,
                TimeLimitMinutes = 200f
            };
            var hard = new Job
            {
                Cargo = TestFixtures.Cargo(flags: CargoFlags.Hazardous),
                DistanceKm = 100f,
                TimeLimitMinutes = 200f,
                IsGhatRoute = true
            };

            var clean = new DeliveryReport { ElapsedMinutes = 100f, ViolationCount = 0, CargoDamage01 = 0f };

            Assert.Greater(_progression.CalculateDeliveryXp(hard, clean),
                           _progression.CalculateDeliveryXp(easy, clean),
                           "A hazardous ghat run must be worth more XP than a flat milk run.");
        }

        [Test]
        public void DeliveryXpIsReducedByDamageAndViolations()
        {
            var job = new Job { Cargo = TestFixtures.Cargo(), DistanceKm = 200f, TimeLimitMinutes = 300f };

            var clean = new DeliveryReport { ElapsedMinutes = 200f, ViolationCount = 0, CargoDamage01 = 0f };
            var messy = new DeliveryReport { ElapsedMinutes = 400f, ViolationCount = 4, CargoDamage01 = 0.8f };

            Assert.Greater(_progression.CalculateDeliveryXp(job, clean),
                           _progression.CalculateDeliveryXp(job, messy));
            Assert.GreaterOrEqual(_progression.CalculateDeliveryXp(job, messy), 1,
                "Even a bad run must award at least something, or the player stalls entirely.");
        }

        [Test]
        public void NextCityUnlockLookaheadFindsTheNearestGate()
        {
            Assert.IsTrue(ProgressionService.TryGetNextCityUnlock(1, out int atLevel, out var cities));

            Assert.AreEqual(3, atLevel, "From level 1 the next city gate is level 3.");
            Assert.Contains("namakkal", cities);

            Assert.IsFalse(ProgressionService.TryGetNextCityUnlock(100, out _, out _),
                "Past every gate there is nothing left to look ahead to.");
        }
    }
}
