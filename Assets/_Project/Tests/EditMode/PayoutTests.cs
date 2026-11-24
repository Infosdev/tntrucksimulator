using NUnit.Framework;
using TNLorry.Core;
using TNLorry.Data;
using TNLorry.Gameplay;

namespace TNLorry.Tests
{
    /// <summary>
    /// The payout formula from section C. This is the single most player-visible piece of maths
    /// in the game — if it is wrong, the whole economy is wrong and no amount of content hides it.
    /// </summary>
    public class PayoutTests
    {
        EconomyConfig _config;
        SaveSystem _save;
        EconomyService _economy;

        [SetUp]
        public void SetUp()
        {
            _config = TestFixtures.Economy();
            _save = TempSave.Create();
            _economy = new EconomyService(_config, _save, new TestFixtures.PassthroughRemoteConfig());
        }

        [TearDown]
        public void TearDown() => TempSave.Cleanup();

        static Job Job(CargoData cargo = null, float distanceKm = 100f, float timeLimit = 150f,
                       bool ghat = false, float destinationMultiplier = 1f)
        {
            return new Job
            {
                Id = "test_job",
                Cargo = cargo ?? TestFixtures.Cargo(),
                OriginCity = TestFixtures.City("origin"),
                DestinationCity = TestFixtures.City("destination", destinationMultiplier),
                DistanceKm = distanceKm,
                TimeLimitMinutes = timeLimit,
                IsGhatRoute = ghat
            };
        }

        static DeliveryReport PerfectRun(float elapsedMinutes = 100f) => new DeliveryReport
        {
            ElapsedMinutes = elapsedMinutes,
            CargoDamage01 = 0f,
            FuelEfficient = true,
            ViolationCount = 0,
            FinesIncurred = 0
        };

        [Test]
        public void BasePayScalesWithDistanceAndRate()
        {
            var cargo = TestFixtures.Cargo(ratePerKm: 40f, massKg: 10000f);

            var short100 = _economy.CalculatePayout(Job(cargo, distanceKm: 100f), PerfectRun());
            var long200 = _economy.CalculatePayout(Job(cargo, distanceKm: 200f), PerfectRun());

            Assert.AreEqual(4000, short100.BasePay, 1, "100 km at ₹40/km is ₹4000 before multipliers.");
            Assert.AreEqual(short100.BasePay * 2, long200.BasePay, 2, "Base pay must be linear in distance.");
        }

        [Test]
        public void WeightBonusOnlyAppliesAboveTenTonnes()
        {
            var light = TestFixtures.Cargo(massKg: 8000f);
            var heavy = TestFixtures.Cargo(massKg: 26000f);

            Assert.AreEqual(0, _economy.CalculatePayout(Job(light), PerfectRun()).WeightBonus,
                "Under 10 t there is no weight premium.");
            Assert.Greater(_economy.CalculatePayout(Job(heavy), PerfectRun()).WeightBonus, 0,
                "A 26 t load must pay more than a light one.");
        }

        [Test]
        public void OnTimeDeliveryEarnsABonus_LateIsPenalised()
        {
            var onTime = _economy.CalculatePayout(Job(timeLimit: 150f), PerfectRun(elapsedMinutes: 120f));
            var late = _economy.CalculatePayout(Job(timeLimit: 150f), PerfectRun(elapsedMinutes: 180f));

            Assert.Greater(onTime.TimeAdjustment, 0, "Beating the clock must pay.");
            Assert.Less(late.TimeAdjustment, 0, "Missing it must cost.");
            Assert.Greater(onTime.Total, late.Total);
        }

        [Test]
        public void LatenessPenaltyRampsAndThenFloors()
        {
            var slightly = _economy.CalculatePayout(Job(timeLimit: 100f), PerfectRun(110f));
            var badly = _economy.CalculatePayout(Job(timeLimit: 100f), PerfectRun(145f));
            var absurdly = _economy.CalculatePayout(Job(timeLimit: 100f), PerfectRun(600f));

            Assert.Less(badly.TimeAdjustment, slightly.TimeAdjustment, "The penalty must ramp with lateness.");
            Assert.AreEqual(badly.TimeAdjustment, absurdly.TimeAdjustment, 2,
                "Past the full-penalty point it must floor, not fall forever into negative pay.");
        }

        [Test]
        public void CargoDamageCutsThePayout()
        {
            var clean = _economy.CalculatePayout(Job(), PerfectRun());

            var damagedReport = PerfectRun();
            damagedReport.CargoDamage01 = 0.5f;
            var damaged = _economy.CalculatePayout(Job(), damagedReport);

            Assert.AreEqual(0, clean.DamagePenalty);
            Assert.Less(damaged.DamagePenalty, 0);
            Assert.Less(damaged.Total, clean.Total);
        }

        [Test]
        public void TotalNeverGoesNegative()
        {
            // Wrecked the cargo, hours late, and a pile of fines: the worst possible run.
            var disaster = new DeliveryReport
            {
                ElapsedMinutes = 10000f,
                CargoDamage01 = 1f,
                FuelEfficient = false,
                ViolationCount = 12,
                FinesIncurred = 500000
            };

            var breakdown = _economy.CalculatePayout(Job(), disaster);

            Assert.GreaterOrEqual(breakdown.Total, 0,
                "A catastrophic run pays nothing — it must never bill the player through the payout.");
        }

        [Test]
        public void CleanDrivingAndFuelBonusesAreDiscretionary()
        {
            var perfect = _economy.CalculatePayout(Job(), PerfectRun());

            var sloppy = PerfectRun();
            sloppy.ViolationCount = 1;
            sloppy.FuelEfficient = false;
            var sloppyResult = _economy.CalculatePayout(Job(), sloppy);

            Assert.AreEqual(_config.cleanDrivingBonus, perfect.CleanDrivingBonus);
            Assert.AreEqual(_config.fuelEfficiencyBonus, perfect.FuelBonus);
            Assert.AreEqual(0, sloppyResult.CleanDrivingBonus, "One violation forfeits the clean-driving bonus.");
            Assert.AreEqual(0, sloppyResult.FuelBonus);
        }

        [Test]
        public void FinesAreSubtractedFromThePayout()
        {
            var withFines = PerfectRun();
            withFines.FinesIncurred = 2000;

            var breakdown = _economy.CalculatePayout(Job(), withFines);

            Assert.AreEqual(-2000, breakdown.Fines);
        }

        [Test]
        public void GhatHazardousAndFragileAllPayAPremium()
        {
            var plain = _economy.CalculatePayout(Job(), PerfectRun()).BasePay;

            var ghat = _economy.CalculatePayout(Job(ghat: true), PerfectRun()).BasePay;
            var hazardous = _economy.CalculatePayout(
                Job(TestFixtures.Cargo(flags: CargoFlags.Hazardous)), PerfectRun()).BasePay;
            var fragile = _economy.CalculatePayout(
                Job(TestFixtures.Cargo(flags: CargoFlags.Fragile)), PerfectRun()).BasePay;

            Assert.Greater(ghat, plain, "Hairpins must pay — section E ghat premium.");
            Assert.Greater(hazardous, plain, "Sivakasi fireworks must pay.");
            Assert.Greater(fragile, plain, "The Namakkal egg run must pay.");
        }

        [Test]
        public void DestinationMultiplierApplies()
        {
            var normal = _economy.CalculatePayout(Job(), PerfectRun()).BasePay;
            var lucrative = _economy.CalculatePayout(Job(destinationMultiplier: 1.2f), PerfectRun()).BasePay;

            Assert.AreEqual(normal * 1.2f, lucrative, normal * 0.01f);
        }

        [Test]
        public void BreakdownLinesSumToTheTotal()
        {
            var report = PerfectRun(elapsedMinutes: 120f);
            report.CargoDamage01 = 0.2f;
            report.FinesIncurred = 1000;
            report.ViolationCount = 1;

            var b = _economy.CalculatePayout(Job(), report);

            int sum = b.BasePay + b.WeightBonus + b.TimeAdjustment + b.DamagePenalty +
                      b.FuelBonus + b.CleanDrivingBonus + b.Fines;

            Assert.AreEqual(sum, b.Total,
                "The result screen itemises these lines; they must add up to what the player is paid.");
        }

        [Test]
        public void EstimateExcludesBonusesThePlayerHasNotEarnedYet()
        {
            var job = Job();
            int estimate = _economy.EstimatePayout(job);
            var actual = _economy.CalculatePayout(job, PerfectRun());

            Assert.Less(estimate, actual.Total,
                "The board must not promise the clean-driving and fuel bonuses up front.");
            Assert.Greater(estimate, 0);
        }

        [Test]
        public void MissingCargoYieldsAnEmptyBreakdownRatherThanThrowing()
        {
            var job = Job();
            job.Cargo = null;

            var breakdown = _economy.CalculatePayout(job, PerfectRun());

            Assert.AreEqual(0, breakdown.Total);
        }

        [Test]
        public void RemoteConfigCanScaleEveryPayout()
        {
            var doubled = new EconomyService(_config, _save,
                new TestFixtures.StubRemoteConfig().Set("econ.payout_scalar", 2f));

            int normal = _economy.CalculatePayout(Job(), PerfectRun()).BasePay;
            int boosted = doubled.CalculatePayout(Job(), PerfectRun()).BasePay;

            Assert.AreEqual(normal * 2, boosted, 2,
                "A live event must be able to double payouts without an app update.");
        }

        [Test]
        public void FuelAndTollCostsUseTheConfiguredRates()
        {
            Assert.AreEqual(_config.dieselPricePerLitre * 100f, _economy.FuelCost(100f), 1f);

            int cash = _economy.TollCost(50f, fastag: false);
            int fastag = _economy.TollCost(50f, fastag: true);

            Assert.Greater(cash, 0);
            Assert.Less(fastag, cash, "The FASTag discount is the reason to learn the lanes.");
        }
    }
}
