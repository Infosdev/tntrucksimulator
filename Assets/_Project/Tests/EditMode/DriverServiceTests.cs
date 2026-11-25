using NUnit.Framework;
using TNLorry.Core;
using TNLorry.Data;
using TNLorry.Gameplay;
using UnityEngine;

namespace TNLorry.Tests
{
    public class DriverServiceTests
    {
        EconomyConfig _config;
        SaveSystem _save;
        EconomyService _economy;
        ProgressionService _progression;
        GameDatabase _database;
        DriverService _drivers;

        const string PlayerTruck = "kaveri_k1212";
        const string SpareTruck = "kaveri_k2518";

        [SetUp]
        public void SetUp()
        {
            _config = TestFixtures.Economy();
            _save = TempSave.Create();
            _economy = new EconomyService(_config, _save, new TestFixtures.PassthroughRemoteConfig());
            _progression = new ProgressionService(_config, _save);

            var player = TestFixtures.Truck();
            player.id = PlayerTruck;

            var spare = TestFixtures.Truck();
            spare.id = SpareTruck;
            spare.maxPayloadKg = 25000f;

            _database = ScriptableObject.CreateInstance<GameDatabase>();
            _database.trucks = new[] { player, spare };
            _database.cargo = new CargoData[0];
            _database.cities = new CityData[0];
            _database.Build();

            _drivers = new DriverService(_config, _save, _economy, _progression, _database);

            // Two owned trucks: index 0 is the one the player drives, index 1 is spare.
            _save.Data.ownedTrucks.Add(new OwnedTruckSave { truckDataId = PlayerTruck });
            _save.Data.ownedTrucks.Add(new OwnedTruckSave { truckDataId = SpareTruck });
            _save.Data.activeTruckIndex = 0;

            _economy.Credit(500000, "test funds");
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.Clear();
            TempSave.Cleanup();
        }

        void UnlockHiring()
        {
            while (_progression.Level < _config.levelForHiringDrivers)
                _progression.AwardXp(_progression.XpForLevel(_progression.Level));
        }

        [Test]
        public void HiringIsLockedUntilLevelTwenty()
        {
            Assert.AreEqual(0, _drivers.MaxDrivers);
            Assert.AreEqual(DriverService.HireResult.NotUnlocked,
                            _drivers.Hire("Murugan", 3, SpareTruck, "nh44", 0, 8f));
        }

        [Test]
        public void HiringChargesAFeeAndAddsTheDriver()
        {
            UnlockHiring();
            long before = _economy.Balance;
            int fee = _drivers.HiringFee(3);

            Assert.AreEqual(DriverService.HireResult.Success,
                            _drivers.Hire("Murugan", 3, SpareTruck, "nh44", 0, 8f));

            Assert.AreEqual(before - fee, _economy.Balance);
            Assert.AreEqual(1, _drivers.DriverCount);
            Assert.AreEqual(SpareTruck, _drivers.Drivers[0].assignedTruckDataId);
            Assert.IsNotEmpty(_drivers.Drivers[0].driverId);
        }

        [Test]
        public void ABetterDriverCostsMoreToSign()
        {
            Assert.Greater(_drivers.HiringFee(5), _drivers.HiringFee(1));
        }

        [Test]
        public void CannotHireOntoTheTruckThePlayerIsDriving()
        {
            UnlockHiring();

            Assert.IsFalse(_drivers.IsTruckAvailableForHire(PlayerTruck),
                "The player's own truck is not a fleet asset while they are in it.");
            Assert.AreEqual(DriverService.HireResult.NoSpareTruck,
                            _drivers.Hire("Murugan", 3, PlayerTruck, "nh44", 0, 8f));
        }

        [Test]
        public void CannotHireTwoDriversOntoOneTruck()
        {
            // Level 25, not 20: at the gate level there is only one driver slot, so a second
            // hire is refused as NoSlots before the one-driver-per-truck rule is ever reached.
            // This test is about the truck rule, so the slot limit has to be out of the way.
            while (_progression.Level < _config.levelForHiringDrivers + 5)
                _progression.AwardXp(_progression.XpForLevel(_progression.Level));

            Assert.Greater(_drivers.MaxDrivers, 1, "Need more than one slot to test the truck rule.");
            _drivers.Hire("Murugan", 3, SpareTruck, "nh44", 0, 8f);

            Assert.AreEqual(DriverService.HireResult.NoSpareTruck,
                            _drivers.Hire("Selvam", 3, SpareTruck, "nh44", 0, 8f));
        }

        [Test]
        public void CannotHireOntoATruckYouDoNotOwn()
        {
            UnlockHiring();
            _save.Data.ownedTrucks.RemoveAt(1);

            Assert.AreEqual(DriverService.HireResult.NoSpareTruck,
                            _drivers.Hire("Murugan", 3, SpareTruck, "nh44", 0, 8f));
        }

        [Test]
        public void SlotsOpenUpWithLevel()
        {
            UnlockHiring();
            int atTwenty = _drivers.MaxDrivers;

            while (_progression.Level < _config.levelForHiringDrivers + 10)
                _progression.AwardXp(_progression.XpForLevel(_progression.Level));

            Assert.Greater(_drivers.MaxDrivers, atTwenty, "Levelling must widen the fleet.");
            Assert.LessOrEqual(_drivers.MaxDrivers, 6, "…but not without limit.");
        }

        [Test]
        public void SlotLimitIsEnforced()
        {
            UnlockHiring();

            // One slot at level 20; add more owned trucks so the only blocker is the slot count.
            for (int i = 0; i < 4; i++)
                _save.Data.ownedTrucks.Add(new OwnedTruckSave { truckDataId = SpareTruck + "_" + i });

            Assert.AreEqual(DriverService.HireResult.Success,
                            _drivers.Hire("Murugan", 3, SpareTruck, "nh44", 0, 8f));
            Assert.AreEqual(DriverService.HireResult.NoSlots,
                            _drivers.Hire("Selvam", 3, SpareTruck + "_0", "nh44", 0, 8f));
        }

        [Test]
        public void HiringIsRefusedWhenTheFeeCannotBePaid()
        {
            UnlockHiring();
            _economy.TryDebit(_economy.Balance, "spend it all");

            Assert.AreEqual(DriverService.HireResult.CannotAfford,
                            _drivers.Hire("Murugan", 3, SpareTruck, "nh44", 0, 8f));
            Assert.AreEqual(0, _drivers.DriverCount);
        }

        [Test]
        public void FiringRemovesTheDriver()
        {
            UnlockHiring();
            _drivers.Hire("Murugan", 3, SpareTruck, "nh44", 0, 8f);
            string id = _drivers.Drivers[0].driverId;

            Assert.IsTrue(_drivers.Fire(id));
            Assert.AreEqual(0, _drivers.DriverCount);
            Assert.IsFalse(_drivers.Fire(id), "Firing twice must not throw or double-count.");
        }

        [Test]
        public void SkillRaisesPayAndLowersIncidentRate()
        {
            var truck = _database.GetTruck(SpareTruck);

            Assert.Greater(_drivers.EstimateTripPayout(truck, 5), _drivers.EstimateTripPayout(truck, 1));
            Assert.Less(DriverService.IncidentChance(5), DriverService.IncidentChance(1));
            Assert.Greater(DriverService.IncidentChance(1), 0f);
            Assert.Less(DriverService.IncidentChance(5), 0.1f);
        }

        [Test]
        public void ABiggerTruckEarnsMorePerTrip()
        {
            Assert.Greater(_drivers.EstimateTripPayout(_database.GetTruck(SpareTruck), 3),
                           _drivers.EstimateTripPayout(_database.GetTruck(PlayerTruck), 3));
        }

        [Test]
        public void TripPayoutIsNetOfFuelAndSalary()
        {
            // If this ever goes negative the whole feature is a money sink and nobody hires.
            Assert.Greater(_drivers.EstimateTripPayout(_database.GetTruck(SpareTruck), 3), 0);
        }

        [Test]
        public void NoTripResolvesBeforeItIsDue()
        {
            UnlockHiring();
            _drivers.Hire("Murugan", 3, SpareTruck, "nh44", currentDay: 0, currentHour: 8f);
            long before = _economy.Balance;

            // The trip was booked for hour 8 + 8 = 16 on day 0.
            Assert.AreEqual(0, _drivers.TickHour(12f, 0));
            Assert.AreEqual(before, _economy.Balance);
        }

        [Test]
        public void ATripResolvesOnceItIsDue()
        {
            UnlockHiring();
            _drivers.Hire("Murugan", 5, SpareTruck, "nh44", currentDay: 0, currentHour: 8f);

            int resolved = 0;
            void Handler(DriverTripCompletedEvent e) => resolved++;
            EventBus.Subscribe<DriverTripCompletedEvent>(Handler);

            _drivers.TickHour(18f, 0);

            EventBus.Unsubscribe<DriverTripCompletedEvent>(Handler);

            Assert.AreEqual(1, resolved, "A due trip must resolve exactly once.");
        }

        [Test]
        public void TheSameTripDoesNotResolveTwice()
        {
            UnlockHiring();
            _drivers.Hire("Murugan", 5, SpareTruck, "nh44", currentDay: 0, currentHour: 8f);

            int resolved = 0;
            void Handler(DriverTripCompletedEvent e) => resolved++;
            EventBus.Subscribe<DriverTripCompletedEvent>(Handler);

            _drivers.TickHour(18f, 0);
            _drivers.TickHour(19f, 0);
            _drivers.TickHour(20f, 0);

            EventBus.Unsubscribe<DriverTripCompletedEvent>(Handler);

            Assert.AreEqual(1, resolved, "Each hour tick must not re-bank the same trip.");
        }

        [Test]
        public void TripSchedulingUsesAbsoluteHoursSoMidnightDoesNotFireItEarly()
        {
            UnlockHiring();
            // Booked late in the day: the trip lands at 20 + 8 = hour 4 of the next day.
            _drivers.Hire("Murugan", 5, SpareTruck, "nh44", currentDay: 0, currentHour: 20f);

            int resolved = 0;
            void Handler(DriverTripCompletedEvent e) => resolved++;
            EventBus.Subscribe<DriverTripCompletedEvent>(Handler);

            // 00:30 on day 1 is absolute hour 24.5 — past midnight but still short of hour 28,
            // so the trip must NOT have landed yet.
            _drivers.TickHour(0.5f, 1);
            Assert.AreEqual(0, resolved, "The clock wrapping past midnight must not land the trip early.");

            // 04:30 on day 1 is absolute hour 28.5 — now it is due.
            _drivers.TickHour(4.5f, 1);

            EventBus.Unsubscribe<DriverTripCompletedEvent>(Handler);

            Assert.AreEqual(1, resolved,
                "Absolute hours are what make a trip across midnight resolve at the right time.");
        }

        [Test]
        public void ProjectedIncomeIsZeroWithNoDriversAndPositiveWithOne()
        {
            Assert.AreEqual(0, _drivers.ProjectedDailyIncome());

            UnlockHiring();
            _drivers.Hire("Murugan", 5, SpareTruck, "nh44", 0, 8f);

            Assert.Greater(_drivers.ProjectedDailyIncome(), 0);
        }

        [Test]
        public void DriversSurviveAReload()
        {
            UnlockHiring();
            _drivers.Hire("Murugan", 4, SpareTruck, "nh44", 0, 8f);
            string id = _drivers.Drivers[0].driverId;
            _save.SaveImmediate();

            var reloaded = TempSave.Reopen();

            Assert.AreEqual(1, reloaded.Data.hiredDrivers.Count);
            Assert.AreEqual(id, reloaded.Data.hiredDrivers[0].driverId);
            Assert.AreEqual("Murugan", reloaded.Data.hiredDrivers[0].name);
            Assert.AreEqual(4, reloaded.Data.hiredDrivers[0].skill);
            Assert.AreEqual(SpareTruck, reloaded.Data.hiredDrivers[0].assignedTruckDataId);
        }
    }
}
