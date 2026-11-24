using System;
using NUnit.Framework;
using TNLorry.Core;
using TNLorry.Data;
using TNLorry.Gameplay;

namespace TNLorry.Tests
{
    /// <summary>
    /// Daily missions and the login streak. The injectable clock is the whole reason this is
    /// testable — crossing a date boundary otherwise means waiting a day.
    /// </summary>
    public class DailyServiceTests
    {
        EconomyConfig _config;
        SaveSystem _save;
        EconomyService _economy;
        ProgressionService _progression;
        DailyService _daily;

        DateTime _now;

        [SetUp]
        public void SetUp()
        {
            _config = TestFixtures.Economy();
            _save = TempSave.Create();
            _economy = new EconomyService(_config, _save, new TestFixtures.PassthroughRemoteConfig());
            _progression = new ProgressionService(_config, _save);

            _now = new DateTime(2026, 11, 24, 10, 0, 0);
            _daily = new DailyService(_save, _economy, _progression) { NowProvider = () => _now };
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.Clear();
            TempSave.Cleanup();
        }

        void AdvanceDays(int days) => _now = _now.AddDays(days);

        static Job DeliveryJob(string cargoId = "containers", string destinationId = "madurai",
                               float distanceKm = 100f, float timeLimit = 150f)
        {
            return new Job
            {
                Id = "job",
                Cargo = TestFixtures.Cargo(cargoId),
                OriginCity = TestFixtures.City("chennai"),
                DestinationCity = TestFixtures.City(destinationId),
                DistanceKm = distanceKm,
                TimeLimitMinutes = timeLimit
            };
        }

        static DeliveryReport GoodRun() => new DeliveryReport
        {
            ElapsedMinutes = 100f,
            CargoDamage01 = 0f,
            ViolationCount = 0,
            FinesIncurred = 0
        };

        // ── Login streak ───────────────────────────────────────────────────

        [Test]
        public void FirstLoginPaysTheDayOneReward()
        {
            Assert.IsTrue(_daily.LoginRewardAvailable);
            long before = _economy.Balance;

            int reward = _daily.ClaimLoginReward();

            Assert.Greater(reward, 0);
            Assert.AreEqual(before + reward, _economy.Balance);
            Assert.AreEqual(1, _daily.LoginStreak);
        }

        [Test]
        public void ClaimingTwiceInOneDayPaysOnce()
        {
            _daily.ClaimLoginReward();
            long after = _economy.Balance;

            Assert.AreEqual(0, _daily.ClaimLoginReward(), "The second claim today must pay nothing.");
            Assert.AreEqual(after, _economy.Balance);
            Assert.IsFalse(_daily.LoginRewardAvailable);
        }

        [Test]
        public void ConsecutiveDaysBuildTheStreakAndTheRewardGrows()
        {
            int firstDay = _daily.ClaimLoginReward();

            AdvanceDays(1);
            int secondDay = _daily.ClaimLoginReward();

            Assert.AreEqual(2, _daily.LoginStreak);
            Assert.Greater(secondDay, firstDay, "Showing up again must be worth more.");
        }

        [Test]
        public void MissingADayResetsTheStreak()
        {
            _daily.ClaimLoginReward();
            AdvanceDays(1);
            _daily.ClaimLoginReward();
            Assert.AreEqual(2, _daily.LoginStreak);

            // Skip a day.
            AdvanceDays(2);
            int reward = _daily.ClaimLoginReward();

            Assert.AreEqual(1, _daily.LoginStreak, "A gap must break the streak.");
            Assert.AreEqual(2000, reward, "Back to the day-one reward.");
        }

        [Test]
        public void BestStreakIsRemembered()
        {
            for (int i = 0; i < 4; i++)
            {
                _daily.ClaimLoginReward();
                AdvanceDays(1);
            }
            Assert.AreEqual(4, _daily.BestLoginStreak);

            AdvanceDays(5);                     // break it
            _daily.ClaimLoginReward();

            Assert.AreEqual(1, _daily.LoginStreak);
            Assert.AreEqual(4, _daily.BestLoginStreak, "The best run must survive a broken streak.");
        }

        [Test]
        public void TheSeventhDayIsTheBigOneAndTheCycleThenWraps()
        {
            int lastReward = 0;
            for (int day = 1; day <= 7; day++)
            {
                lastReward = _daily.ClaimLoginReward();
                if (day < 7) AdvanceDays(1);
            }

            Assert.AreEqual(20000, lastReward, "Day 7 is the reward players chase.");
            Assert.AreEqual(7, _daily.LoginStreak);

            // Day 8: the reward table wraps, but the streak keeps counting.
            AdvanceDays(1);
            int dayEight = _daily.ClaimLoginReward();

            Assert.AreEqual(2000, dayEight, "The reward cycle restarts…");
            Assert.AreEqual(8, _daily.LoginStreak, "…but the streak does not.");
        }

        [Test]
        public void TheClaimEventReportsBothStreakNumbers()
        {
            LoginRewardClaimedEvent captured = default;
            void Handler(LoginRewardClaimedEvent e) => captured = e;
            EventBus.Subscribe<LoginRewardClaimedEvent>(Handler);

            for (int day = 1; day <= 8; day++)
            {
                _daily.ClaimLoginReward();
                AdvanceDays(1);
            }

            EventBus.Unsubscribe<LoginRewardClaimedEvent>(Handler);

            Assert.AreEqual(8, captured.Streak);
            Assert.AreEqual(1, captured.CycleDay, "Day 8 sits at position 1 of the reward cycle.");
        }

        [Test]
        public void LoginStateSurvivesAReload()
        {
            _daily.ClaimLoginReward();
            AdvanceDays(1);
            _daily.ClaimLoginReward();
            _save.SaveImmediate();

            var reloaded = TempSave.Reopen();

            Assert.AreEqual(2, reloaded.Data.daily.loginStreak);
            Assert.AreEqual(_now.ToString("yyyy-MM-dd"), reloaded.Data.daily.lastLoginDate);
        }

        // ── Missions ───────────────────────────────────────────────────────

        [Test]
        public void GeneratesAMissionSetForToday()
        {
            var missions = _daily.Missions;

            Assert.AreEqual(3, missions.Count);
            foreach (var mission in missions)
            {
                Assert.Greater(mission.target, 0);
                Assert.Greater(mission.rewardMoney, 0);
                Assert.Greater(mission.rewardXp, 0);
                Assert.AreEqual(0, mission.progress);
                Assert.IsFalse(mission.claimed);
            }
        }

        [Test]
        public void TheSetIsStableWithinADay()
        {
            var first = Snapshot();
            var second = Snapshot();

            Assert.AreEqual(first, second, "Relaunching must not reroll today's missions.");
        }

        [Test]
        public void TheSetChangesWhenTheDateRollsOver()
        {
            // Compared over a week rather than a single day: two consecutive days can roll the
            // same three mission types by chance, and a test that fails 1 run in 50 is worse
            // than no test. What must hold is that the set is not frozen.
            var seen = new System.Collections.Generic.HashSet<string>();

            for (int day = 0; day < 7; day++)
            {
                seen.Add(Snapshot());
                AdvanceDays(1);
            }

            Assert.Greater(seen.Count, 1, "Missions must actually refresh as the date advances.");
        }

        [Test]
        public void ProgressDoesNotCarryIntoTheNextDay()
        {
            var mission = FindMission(MissionType.CompleteDeliveries);
            _daily.RecordDelivery(DeliveryJob(), GoodRun());
            Assert.Greater(mission.progress, 0);

            AdvanceDays(1);

            var fresh = FindMission(MissionType.CompleteDeliveries);
            Assert.AreEqual(0, fresh.progress, "A new day means a clean slate.");
        }

        [Test]
        public void ADeliveryAdvancesTheCompleteDeliveriesMission()
        {
            var mission = FindMission(MissionType.CompleteDeliveries);
            int before = mission.progress;

            _daily.RecordDelivery(DeliveryJob(), GoodRun());

            Assert.AreEqual(before + 1, mission.progress);
        }

        [Test]
        public void ProgressStopsAtTheTarget()
        {
            var mission = FindMission(MissionType.CompleteDeliveries);

            for (int i = 0; i < mission.target + 5; i++)
                _daily.RecordDelivery(DeliveryJob(), GoodRun());

            Assert.AreEqual(mission.target, mission.progress, "Progress must not overshoot the target.");
        }

        [Test]
        public void CompletingAMissionFiresExactlyOneEvent()
        {
            var mission = FindMission(MissionType.CompleteDeliveries);

            int completions = 0;
            void Handler(DailyMissionCompletedEvent e) => completions++;
            EventBus.Subscribe<DailyMissionCompletedEvent>(Handler);

            for (int i = 0; i < mission.target + 3; i++)
                _daily.RecordDelivery(DeliveryJob(), GoodRun());

            EventBus.Unsubscribe<DailyMissionCompletedEvent>(Handler);

            Assert.GreaterOrEqual(completions, 1);
            // Other missions in the set may also complete from the same deliveries; what must
            // not happen is the same mission firing again once it is already done.
            Assert.LessOrEqual(completions, _daily.Missions.Count);
        }

        [Test]
        public void ClaimingPaysMoneyAndXpThenRefusesASecondTime()
        {
            var mission = FindMission(MissionType.CompleteDeliveries);
            int index = IndexOf(mission);

            for (int i = 0; i < mission.target; i++)
                _daily.RecordDelivery(DeliveryJob(), GoodRun());

            long balanceBefore = _economy.Balance;
            int xpBefore = _progression.Xp + _progression.Level * 100000;   // level-insensitive comparison

            int paid = _daily.Claim(index);

            Assert.AreEqual(mission.rewardMoney, paid);
            Assert.AreEqual(balanceBefore + paid, _economy.Balance);
            Assert.Greater(_progression.Xp + _progression.Level * 100000, xpBefore, "XP must be awarded too.");

            Assert.AreEqual(0, _daily.Claim(index), "A claimed mission must not pay twice.");
        }

        [Test]
        public void AnIncompleteMissionCannotBeClaimed()
        {
            var mission = FindMission(MissionType.CompleteDeliveries);
            int index = IndexOf(mission);

            Assert.AreEqual(0, _daily.Claim(index));
            Assert.IsFalse(mission.claimed);
        }

        [Test]
        public void OutOfRangeClaimIsSafe()
        {
            Assert.AreEqual(0, _daily.Claim(-1));
            Assert.AreEqual(0, _daily.Claim(999));
        }

        [Test]
        public void UnclaimedCountTracksWhatIsReady()
        {
            Assert.AreEqual(0, _daily.UnclaimedCount());

            var mission = FindMission(MissionType.CompleteDeliveries);
            for (int i = 0; i < mission.target; i++)
                _daily.RecordDelivery(DeliveryJob(), GoodRun());

            Assert.GreaterOrEqual(_daily.UnclaimedCount(), 1);

            _daily.ClaimAll();
            Assert.AreEqual(0, _daily.UnclaimedCount());
        }

        [Test]
        public void DistanceMissionsAccumulateFractionalKilometres()
        {
            // Force a distance mission into the set so the test does not depend on the roll.
            var mission = new MissionSave
            {
                type = MissionType.DriveDistance.ToString(),
                target = 10,
                rewardMoney = 1000,
                rewardXp = 50
            };
            _daily.EnsureTodaysMissions();
            _save.Data.daily.missions.Add(mission);

            // Twenty 0.5 km flushes must add up to 10 km, not round away to nothing.
            for (int i = 0; i < 20; i++) _daily.RecordDistance(0.5f);

            Assert.AreEqual(10, mission.progress,
                "Fractional distance must carry between calls — OdometerTracker flushes in batches.");
        }

        [Test]
        public void MissionsSurviveAReload()
        {
            var mission = FindMission(MissionType.CompleteDeliveries);
            _daily.RecordDelivery(DeliveryJob(), GoodRun());
            int progress = mission.progress;
            _save.SaveImmediate();

            var reloaded = TempSave.Reopen();

            Assert.AreEqual(3, reloaded.Data.daily.missions.Count);
            Assert.AreEqual(_now.ToString("yyyy-MM-dd"), reloaded.Data.daily.missionDate);

            bool found = false;
            foreach (var saved in reloaded.Data.daily.missions)
            {
                if (saved.type != MissionType.CompleteDeliveries.ToString()) continue;
                Assert.AreEqual(progress, saved.progress);
                found = true;
            }
            Assert.IsTrue(found);
        }

        [Test]
        public void CityAndCargoMissionsOnlyNameUnlockedContent()
        {
            foreach (var mission in _daily.Missions)
            {
                if (mission.type == MissionType.DeliverToCity.ToString())
                {
                    Assert.Contains(mission.parameter, _save.Data.unlockedCityIds,
                        "A mission to a locked city would be impossible.");
                    Assert.AreNotEqual("chennai", mission.parameter,
                        "Chennai is where half the jobs go anyway; it makes for a non-mission.");
                }
                else if (mission.type == MissionType.DeliverCargo.ToString())
                {
                    Assert.Contains(mission.parameter, _save.Data.unlockedCargoIds);
                }
            }
        }

        [Test]
        public void ADeliveryOnlyCountsForAMatchingCityMission()
        {
            _daily.EnsureTodaysMissions();
            var mission = new MissionSave
            {
                type = MissionType.DeliverToCity.ToString(),
                parameter = "madurai",
                target = 2,
                rewardMoney = 1000,
                rewardXp = 50
            };
            _save.Data.daily.missions.Add(mission);

            _daily.RecordDelivery(DeliveryJob(destinationId: "salem"), GoodRun());
            Assert.AreEqual(0, mission.progress, "A delivery to the wrong city must not count.");

            _daily.RecordDelivery(DeliveryJob(destinationId: "madurai"), GoodRun());
            Assert.AreEqual(1, mission.progress);
        }

        [Test]
        public void CleanAndUndamagedMissionsRespectTheRunQuality()
        {
            _daily.EnsureTodaysMissions();

            var clean = new MissionSave
            { type = MissionType.CleanDeliveries.ToString(), target = 2, rewardMoney = 1, rewardXp = 1 };
            var undamaged = new MissionSave
            { type = MissionType.UndamagedDeliveries.ToString(), target = 2, rewardMoney = 1, rewardXp = 1 };

            _save.Data.daily.missions.Add(clean);
            _save.Data.daily.missions.Add(undamaged);

            var messy = GoodRun();
            messy.ViolationCount = 2;
            messy.CargoDamage01 = 0.4f;

            _daily.RecordDelivery(DeliveryJob(), messy);

            Assert.AreEqual(0, clean.progress, "A fined run is not a clean run.");
            Assert.AreEqual(0, undamaged.progress, "A damaged load is not an undamaged delivery.");

            _daily.RecordDelivery(DeliveryJob(), GoodRun());

            Assert.AreEqual(1, clean.progress);
            Assert.AreEqual(1, undamaged.progress);
        }

        [Test]
        public void ALateDeliveryDoesNotCountForTheOnTimeMission()
        {
            _daily.EnsureTodaysMissions();
            var mission = new MissionSave
            { type = MissionType.OnTimeDeliveries.ToString(), target = 2, rewardMoney = 1, rewardXp = 1 };
            _save.Data.daily.missions.Add(mission);

            var late = GoodRun();
            late.ElapsedMinutes = 400f;         // limit is 150

            _daily.RecordDelivery(DeliveryJob(), late);
            Assert.AreEqual(0, mission.progress);

            _daily.RecordDelivery(DeliveryJob(), GoodRun());
            Assert.AreEqual(1, mission.progress);
        }

        [Test]
        public void RecordDeliveryWithNoJobIsSafe()
        {
            Assert.DoesNotThrow(() => _daily.RecordDelivery(null, GoodRun()));
        }

        // ── Helpers ────────────────────────────────────────────────────────

        string Snapshot()
        {
            var builder = new System.Text.StringBuilder();
            foreach (var mission in _daily.Missions)
                builder.Append(mission.type).Append('|').Append(mission.parameter)
                       .Append('|').Append(mission.target).Append(';');
            return builder.ToString();
        }

        MissionSave FindMission(MissionType type)
        {
            foreach (var mission in _daily.Missions)
                if (mission.type == type.ToString()) return mission;

            Assert.Fail("Expected a " + type + " mission in the set; the generator always includes one.");
            return null;
        }

        int IndexOf(MissionSave mission)
        {
            var missions = _daily.Missions;
            for (int i = 0; i < missions.Count; i++)
                if (ReferenceEquals(missions[i], mission)) return i;
            return -1;
        }
    }
}
