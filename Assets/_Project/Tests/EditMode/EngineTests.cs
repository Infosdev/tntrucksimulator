using NUnit.Framework;
using TNLorry.Vehicle;
using UnityEngine;

namespace TNLorry.Tests
{
    public class EngineTests
    {
        const float Dt = 1f / 50f;

        static Engine Started(out Data.TruckData data)
        {
            data = TestFixtures.Truck();
            var engine = new Engine(data);
            engine.Start();
            return engine;
        }

        /// <summary>Runs the engine to a steady state at the given demand.</summary>
        static void Settle(Engine engine, float targetRpm, float throttle, bool clutch = true, int steps = 300)
        {
            for (int i = 0; i < steps; i++) engine.Tick(targetRpm, throttle, Dt, clutch);
        }

        [Test]
        public void NotRunningProducesNoTorqueAndSpinsDown()
        {
            var data = TestFixtures.Truck();
            var engine = new Engine(data);

            engine.Tick(1500f, 1f, Dt, true);

            Assert.IsFalse(engine.IsRunning);
            Assert.AreEqual(0f, engine.Torque, "A stopped engine must make no torque regardless of throttle.");
        }

        [Test]
        public void StartingPutsRpmAtIdle()
        {
            var engine = Started(out var data);
            Assert.AreEqual(data.idleRpm, engine.Rpm, 0.001f);
            Assert.IsTrue(engine.IsRunning);
        }

        [Test]
        public void StoppingClearsTorque()
        {
            var engine = Started(out _);
            Settle(engine, 1500f, 1f);
            Assert.Greater(engine.Torque, 0f);

            engine.Stop();
            Assert.AreEqual(0f, engine.Torque);
            Assert.IsFalse(engine.IsRunning);
        }

        [Test]
        public void RpmNeverFallsBelowIdleWithClutchEngaged()
        {
            var engine = Started(out var data);

            // Wheels nearly stopped, no throttle: the engine must idle, not stall.
            Settle(engine, 0f, 0f);

            Assert.GreaterOrEqual(engine.Rpm, data.idleRpm - 0.01f);
        }

        [Test]
        public void RpmNeverExceedsMax()
        {
            var engine = Started(out var data);
            Settle(engine, data.maxRpm * 3f, 1f);
            Assert.LessOrEqual(engine.Rpm, data.maxRpm + 0.01f);
        }

        [Test]
        public void RevLimiterCutsTorqueAtRedline()
        {
            var engine = Started(out var data);

            // Drive rpm above the redline; the limiter must zero the torque even at full throttle.
            Settle(engine, data.maxRpm, 1f);

            Assert.GreaterOrEqual(engine.Rpm, data.redlineRpm);
            Assert.AreEqual(0f, engine.Torque, 0.001f, "Past the redline the limiter must cut fuel.");
            Assert.IsTrue(engine.AtRedline);
        }

        [Test]
        public void TorqueScalesWithThrottle()
        {
            var engine = Started(out _);
            const float cruiseRpm = 1400f;

            Settle(engine, cruiseRpm, 0.25f);
            float quarter = engine.Torque;

            Settle(engine, cruiseRpm, 1f);
            float full = engine.Torque;

            Assert.Greater(full, quarter * 2f, "Torque must track throttle, not sit at a fixed value.");
        }

        [Test]
        public void TorqueCurveHasABroadDieselPlateau()
        {
            var data = TestFixtures.Truck();

            // The defining characteristic of the drive feel: near-peak torque across the
            // mid-range, so the truck pulls without needing to be revved.
            float peak = data.TorqueAt(Mathf.Lerp(data.idleRpm, data.redlineRpm, 0.62f));
            float early = data.TorqueAt(Mathf.Lerp(data.idleRpm, data.redlineRpm, 0.3f));
            float late = data.TorqueAt(Mathf.Lerp(data.idleRpm, data.redlineRpm, 0.55f));

            Assert.Greater(early / peak, 0.9f, "Torque must be up and usable just off idle.");
            Assert.Greater(late / peak, 0.9f, "The plateau must extend across the mid-range.");
            Assert.AreEqual(0f, data.TorqueAt(data.redlineRpm + 100f), "Past redline must be zero.");
        }

        [Test]
        public void EngineDamageCutsPowerButNeverAllOfIt()
        {
            var engine = Started(out _);
            Settle(engine, 1400f, 1f);
            float healthy = engine.Torque;

            engine.Damage01 = 1f;
            Settle(engine, 1400f, 1f);
            float wrecked = engine.Torque;

            Assert.Less(wrecked, healthy, "Damage must cost power.");
            Assert.Greater(wrecked, 0f, "A wrecked engine must still limp home, not brick the run.");
            Assert.Less(wrecked / healthy, 0.5f, "Full damage should be keenly felt.");
        }

        [Test]
        public void EngineBrakingIsStrongestOffThrottleAtHighRpm()
        {
            var engine = Started(out var data);

            Settle(engine, data.redlineRpm * 0.8f, 0f);
            float highRpmOffThrottle = engine.EngineBrakeTorque(0f);
            float highRpmOnThrottle = engine.EngineBrakeTorque(1f);

            Assert.Greater(highRpmOffThrottle, 0f, "Closed throttle at speed must retard the driveline.");
            Assert.AreEqual(0f, highRpmOnThrottle, 0.001f, "On full throttle there is no engine braking.");

            Settle(engine, data.idleRpm, 0f);
            float idleOffThrottle = engine.EngineBrakeTorque(0f);

            Assert.Less(idleOffThrottle, highRpmOffThrottle,
                "Engine braking must scale with rpm — that is why you downshift for a ghat.");
        }

        [Test]
        public void ClutchDisengagedLetsTheEngineFreeRev()
        {
            var engine = Started(out var data);

            // Wheels stopped, throttle open, clutch out (mid-shift or neutral): rpm should
            // climb on throttle demand instead of being pinned by the wheels.
            Settle(engine, 0f, 1f, clutch: false);

            Assert.Greater(engine.Rpm, data.idleRpm * 2f,
                "With the clutch out the engine must follow the pedal, not the wheels.");
        }

        [Test]
        public void Rpm01IsNormalisedAcrossTheUsableRange()
        {
            var engine = Started(out var data);

            Settle(engine, data.idleRpm, 0f);
            Assert.AreEqual(0f, engine.Rpm01, 0.02f);

            Settle(engine, data.maxRpm, 1f);
            Assert.AreEqual(1f, engine.Rpm01, 0.02f);
        }
    }
}
