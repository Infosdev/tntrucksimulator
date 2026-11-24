using System.Collections.Generic;
using NUnit.Framework;
using TNLorry.Core;
using TNLorry.Data;
using TNLorry.Gameplay;
using TNLorry.Navigation;
using UnityEngine;
using UnityEngine.TestTools;

namespace TNLorry.Tests
{
    /// <summary>
    /// The job board's central design claim is that offers cannot be rerolled: quitting and
    /// relaunching must show the same jobs, or players will farm the board for a jackpot.
    /// </summary>
    public class JobBoardTests
    {
        EconomyConfig _config;
        SaveSystem _save;
        EconomyService _economy;
        ProgressionService _progression;
        GameDatabase _database;
        RoadGraph _roads;

        [SetUp]
        public void SetUp()
        {
            _config = TestFixtures.Economy();
            _save = TempSave.Create();
            _economy = new EconomyService(_config, _save, new TestFixtures.PassthroughRemoteConfig());
            _progression = new ProgressionService(_config, _save);
            _database = BuildDatabase();
            _roads = BuildRoads();

            _save.Data.unlockedCityIds.Clear();
            _save.Data.unlockedCityIds.AddRange(new[] { "chennai", "salem", "madurai" });
            _save.Data.unlockedCargoIds.Clear();
            _save.Data.unlockedCargoIds.AddRange(new[] { "containers", "steel_coils" });
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.Clear();
            TempSave.Cleanup();
        }

        GameDatabase BuildDatabase()
        {
            var chennai = TestFixtures.City("chennai", roadNodeId: 0);
            chennai.worldPosition = Vector3.zero;
            chennai.producesCargoIds = new[] { "containers" };

            var salem = TestFixtures.City("salem", roadNodeId: 1);
            salem.worldPosition = new Vector3(0f, 0f, 6000f);
            salem.producesCargoIds = new[] { "steel_coils" };
            salem.consumesCargoIds = new[] { "containers" };

            var madurai = TestFixtures.City("madurai", 1.2f, roadNodeId: 2);
            madurai.worldPosition = new Vector3(0f, 0f, 12000f);
            madurai.consumesCargoIds = new[] { "containers", "steel_coils" };

            var database = ScriptableObject.CreateInstance<GameDatabase>();
            database.cities = new[] { chennai, salem, madurai };
            database.cargo = new[]
            {
                TestFixtures.Cargo("containers", 22000f, 48f, trailer: TrailerType.Container),
                TestFixtures.Cargo("steel_coils", 24000f, 52f, trailer: TrailerType.Flatbed)
            };
            database.trucks = new TruckData[0];
            database.Build();
            return database;
        }

        static RoadGraph BuildRoads()
        {
            var graph = new RoadGraph();
            graph.AddNode(new RoadGraph.Node { Id = 0, Position = Vector3.zero, CityId = "chennai" });
            graph.AddNode(new RoadGraph.Node { Id = 1, Position = new Vector3(0f, 0f, 6000f), CityId = "salem" });
            graph.AddNode(new RoadGraph.Node { Id = 2, Position = new Vector3(0f, 0f, 12000f), CityId = "madurai" });

            graph.AddEdge(new RoadGraph.Edge
            { From = 0, To = 1, LengthM = 7000f, SpeedLimitKmh = 90f, Lanes = 2 }, true);
            graph.AddEdge(new RoadGraph.Edge
            { From = 1, To = 2, LengthM = 8000f, SpeedLimitKmh = 90f, Lanes = 2 }, true);
            return graph;
        }

        JobBoardService Board() =>
            new JobBoardService(_database, _config, _economy, _save, _roads);

        static List<string> Signature(List<Job> jobs)
        {
            var signature = new List<string>(jobs.Count);
            foreach (var job in jobs)
                signature.Add(job.Id + "|" + job.Cargo.id + "|" + job.DestinationCity.id + "|" + job.Payout);
            return signature;
        }

        [Test]
        public void GeneratesTheConfiguredNumberOfOffers()
        {
            var jobs = Board().GetBoard("chennai");

            Assert.GreaterOrEqual(jobs.Count, 1);
            Assert.LessOrEqual(jobs.Count, _config.maxJobsPerDepot);
        }

        [Test]
        public void TheSameDepotOnTheSameDayGivesTheSameBoard()
        {
            // Two fresh service instances: this is what happens when the app is killed and
            // relaunched. The offers must be identical.
            var first = Signature(Board().GetBoard("chennai"));
            var second = Signature(Board().GetBoard("chennai"));

            Assert.AreEqual(first, second, "Relaunching must not reroll the board.");
        }

        [Test]
        public void ADifferentDayGivesADifferentBoard()
        {
            var day1 = Signature(Board().GetBoard("chennai"));

            _save.Data.world.day = 2;
            var day2 = Signature(Board().GetBoard("chennai"));

            Assert.AreNotEqual(day1, day2, "The board must actually refresh with the in-game day.");
        }

        [Test]
        public void DifferentDepotsGiveDifferentBoards()
        {
            var board = Board();
            var chennai = Signature(board.GetBoard("chennai"));
            var salem = Signature(board.GetBoard("salem"));

            Assert.AreNotEqual(chennai, salem);
        }

        [Test]
        public void ADifferentProfileGetsADifferentBoard()
        {
            var original = Signature(Board().GetBoard("chennai"));

            _save.Data.profileId = "a-completely-different-profile";
            var other = Signature(Board().GetBoard("chennai"));

            Assert.AreNotEqual(original, other, "Two players on the same day should not see identical boards.");
        }

        [Test]
        public void NeverOffersAJobBackToItsOwnOrigin()
        {
            foreach (var job in Board().GetBoard("chennai"))
                Assert.AreNotEqual("chennai", job.DestinationCity.id);
        }

        [Test]
        public void OnlyOffersUnlockedDestinations()
        {
            _save.Data.unlockedCityIds.Remove("madurai");

            foreach (var job in Board().GetBoard("chennai"))
                Assert.AreNotEqual("madurai", job.DestinationCity.id,
                    "A locked city must never be offered as a destination.");
        }

        [Test]
        public void OnlyOffersUnlockedCargo()
        {
            _save.Data.unlockedCargoIds.Remove("steel_coils");

            foreach (var job in Board().GetBoard("chennai"))
                Assert.AreNotEqual("steel_coils", job.Cargo.id);
        }

        [Test]
        public void EveryOfferHasAPositivePayoutAndAReachableDistance()
        {
            foreach (var job in Board().GetBoard("chennai"))
            {
                Assert.Greater(job.Payout, 0, "An offer the player cannot profit from is a bug.");
                Assert.Greater(job.DistanceKm, 0f);
                Assert.Greater(job.TimeLimitMinutes, 0f);
            }
        }

        [Test]
        public void TimeLimitLeavesHeadroomOverThePlannedDrive()
        {
            foreach (var job in Board().GetBoard("chennai"))
            {
                float plannedMinutes = job.DistanceKm / _config.plannedAverageSpeedKmh * 60f;
                Assert.Greater(job.TimeLimitMinutes, plannedMinutes,
                    "Without slack for tolls and traffic every job would be impossible.");
            }
        }

        [Test]
        public void RouteDistanceComesFromTheRoadGraphNotTheCrowFlies()
        {
            // Chennai -> Salem is 6 km as the crow flies but 7 km of authored road.
            bool checkedOne = false;

            foreach (var job in Board().GetBoard("chennai"))
            {
                if (job.DestinationCity.id != "salem") continue;

                Assert.AreEqual(7f, job.DistanceKm, 0.01f,
                    "Job distance must use the graph's authored road length.");
                checkedOne = true;
            }

            Assert.IsTrue(checkedOne, "With Salem unlocked the board should have offered at least one job to it.");
        }

        [Test]
        public void AcceptingAJobStoresItAndBlocksASecond()
        {
            var board = Board();
            var jobs = board.GetBoard("chennai");

            Assert.IsTrue(board.Accept(jobs[0]));
            Assert.AreEqual(jobs[0].Id, board.ActiveJob.Id);
            Assert.IsTrue(_save.Data.activeJob.hasJob);

            LogAssert.ignoreFailingMessages = true;    // the refusal is logged on purpose
            Assert.IsFalse(board.Accept(jobs[0]), "A driver can only haul one load at a time.");
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void AnAcceptedJobSurvivesAReload()
        {
            var board = Board();
            var job = board.GetBoard("chennai")[0];
            board.Accept(job);
            _save.SaveImmediate();

            // New session: reload the save, rebuild the services, restore the job.
            var reloadedSave = TempSave.Reopen();
            var reloadedEconomy = new EconomyService(_config, reloadedSave,
                                                     new TestFixtures.PassthroughRemoteConfig());
            var reloadedBoard = new JobBoardService(_database, _config, reloadedEconomy, reloadedSave, _roads);
            reloadedBoard.RestoreActiveJob();

            Assert.IsNotNull(reloadedBoard.ActiveJob, "Quitting mid-haul must not lose the job.");
            Assert.AreEqual(job.Id, reloadedBoard.ActiveJob.Id);
            Assert.AreEqual(job.Cargo.id, reloadedBoard.ActiveJob.Cargo.id);
            Assert.AreEqual(job.DistanceKm, reloadedBoard.ActiveJob.DistanceKm, 0.01f);
        }

        [Test]
        public void ASavedJobReferencingMissingContentIsDroppedNotCrashed()
        {
            // This is the real case after a content update removes a cargo type.
            _save.Data.activeJob = new ActiveJobSave
            {
                hasJob = true,
                jobId = "stale",
                cargoId = "cargo_that_no_longer_exists",
                fromCityId = "chennai",
                toCityId = "salem"
            };

            var board = Board();

            LogAssert.ignoreFailingMessages = true;
            board.RestoreActiveJob();
            LogAssert.ignoreFailingMessages = false;

            Assert.IsNull(board.ActiveJob);
            Assert.IsFalse(_save.Data.activeJob.hasJob, "The stale job must be cleared from the save.");
        }

        [Test]
        public void CompletingAJobPaysCreditsXpAndClearsTheSlot()
        {
            var board = Board();
            var job = board.GetBoard("chennai")[0];
            board.Accept(job);

            long balanceBefore = _economy.Balance;
            int deliveriesBefore = _save.Data.deliveriesCompleted;

            var report = new DeliveryReport
            {
                ElapsedMinutes = job.TimeLimitMinutes * 0.8f,
                CargoDamage01 = 0f,
                FuelEfficient = true,
                ViolationCount = 0
            };

            var breakdown = board.Complete(report, _progression);

            Assert.Greater(breakdown.Total, 0);
            Assert.AreEqual(balanceBefore + breakdown.Total, _economy.Balance);
            Assert.AreEqual(deliveriesBefore + 1, _save.Data.deliveriesCompleted);
            Assert.IsNull(board.ActiveJob);
            Assert.IsFalse(_save.Data.activeJob.hasJob);
            Assert.Greater(_progression.Xp + _progression.Level, 1, "The delivery must award XP.");
        }

        [Test]
        public void AbandoningChargesAPenaltyAndClearsTheSlot()
        {
            var board = Board();
            var job = board.GetBoard("chennai")[0];
            board.Accept(job);

            long before = _economy.Balance;
            int failedBefore = _save.Data.deliveriesFailed;

            board.Abandon();

            Assert.Less(_economy.Balance, before, "Walking away from a contract must cost something.");
            Assert.AreEqual(failedBefore + 1, _save.Data.deliveriesFailed);
            Assert.IsNull(board.ActiveJob);
        }

        [Test]
        public void CompletingWithNoActiveJobIsASafeNoOp()
        {
            var board = Board();
            var breakdown = board.Complete(default, _progression);

            Assert.AreEqual(0, breakdown.Total);
        }
    }
}
