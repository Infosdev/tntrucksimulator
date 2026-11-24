using NUnit.Framework;
using TNLorry.Vehicle;

namespace TNLorry.Tests
{
    public class GearboxTests
    {
        const float Dt = 1f / 50f;    // 50 Hz physics step

        static Gearbox Build(out Data.TruckData data, bool autoShift = true)
        {
            data = TestFixtures.Truck();
            data.autoShift = autoShift;
            return new Gearbox(data);
        }

        /// <summary>Runs the gearbox until the in-flight shift completes.</summary>
        static void SettleShift(Gearbox gearbox, Data.TruckData data, float rpm = 1500f, float speed = 40f)
        {
            int guard = 0;
            while (gearbox.IsShifting && guard++ < 1000)
                gearbox.Tick(Dt, rpm, 0.5f, speed, 0f);
        }

        [Test]
        public void StartsInNeutralWithZeroRatio()
        {
            var gearbox = Build(out _);

            Assert.AreEqual(Gearbox.Neutral, gearbox.CurrentGear);
            Assert.AreEqual(0f, gearbox.CurrentRatio, "Neutral must deliver no torque.");
            Assert.AreEqual("N", gearbox.GearLabel);
            Assert.IsFalse(gearbox.IsClutchEngaged);
        }

        [Test]
        public void ShiftIsNotInstant_AndInterruptsTorqueWhileInProgress()
        {
            var gearbox = Build(out var data, autoShift: false);

            gearbox.ShiftTo(1);
            Assert.IsTrue(gearbox.IsShifting, "A shift must take data.shiftTime, not land immediately.");
            Assert.AreEqual(0f, gearbox.CurrentRatio, "Torque must be cut while the shift is in progress.");

            SettleShift(gearbox, data);

            Assert.AreEqual(1, gearbox.CurrentGear);
            Assert.Greater(gearbox.CurrentRatio, 0f);
        }

        [Test]
        public void ForwardRatiosDescendWithGear()
        {
            var gearbox = Build(out var data, autoShift: false);

            float previous = float.MaxValue;
            for (int gear = 1; gear <= data.GearCount; gear++)
            {
                gearbox.ShiftTo(gear);
                SettleShift(gearbox, data);

                float ratio = gearbox.CurrentRatio;
                Assert.Less(ratio, previous, "Gear " + gear + " must be taller than gear " + (gear - 1));
                previous = ratio;
            }
        }

        [Test]
        public void ReverseHasNegativeRatioAndUsesFirstGearReduction()
        {
            var gearbox = Build(out var data, autoShift: false);

            gearbox.ShiftTo(Gearbox.Reverse);
            SettleShift(gearbox, data);

            Assert.AreEqual(Gearbox.Reverse, gearbox.CurrentGear);
            Assert.AreEqual("R", gearbox.GearLabel);
            Assert.Less(gearbox.CurrentRatio, 0f, "Reverse must invert drive torque.");
            Assert.AreEqual(data.gearRatios[0] * data.finalDrive, -gearbox.CurrentRatio, 0.001f);
        }

        [Test]
        public void ManualShiftDownFromFirstGoesNeutralThenReverse()
        {
            var gearbox = Build(out var data, autoShift: false);

            gearbox.ShiftTo(1);
            SettleShift(gearbox, data);

            gearbox.ShiftDown();
            SettleShift(gearbox, data);
            Assert.AreEqual(Gearbox.Neutral, gearbox.CurrentGear, "First gear down must reach neutral, not reverse.");

            gearbox.ShiftDown();
            SettleShift(gearbox, data);
            Assert.AreEqual(Gearbox.Reverse, gearbox.CurrentGear);
        }

        [Test]
        public void CannotShiftAboveTopGearOrBelowReverse()
        {
            var gearbox = Build(out var data, autoShift: false);

            for (int i = 0; i < data.GearCount + 3; i++)
            {
                gearbox.ShiftUp();
                SettleShift(gearbox, data);
            }
            Assert.AreEqual(data.GearCount, gearbox.CurrentGear);

            for (int i = 0; i < data.GearCount + 5; i++)
            {
                gearbox.ShiftDown();
                SettleShift(gearbox, data);
            }
            Assert.AreEqual(Gearbox.Reverse, gearbox.CurrentGear);
        }

        [Test]
        public void AutoShiftUpshiftsNearRedline()
        {
            var gearbox = Build(out var data);

            gearbox.ShiftTo(2);
            SettleShift(gearbox, data);

            // Sit above the upshift threshold with the throttle open and no brake.
            float highRpm = data.redlineRpm * 0.95f;
            for (int i = 0; i < 400 && gearbox.CurrentGear == 2; i++)
                gearbox.Tick(Dt, highRpm, 0.9f, 55f, 0f);

            SettleShift(gearbox, data, highRpm, 55f);
            Assert.AreEqual(3, gearbox.CurrentGear, "Auto-shift should have upshifted off the limiter.");
        }

        [Test]
        public void AutoShiftHoldsLowGearUnderBraking()
        {
            var gearbox = Build(out var data);

            gearbox.ShiftTo(3);
            SettleShift(gearbox, data);

            // High rpm but braking hard: this is a ghat descent, and upshifting here would
            // throw away the engine braking the player is relying on.
            float highRpm = data.redlineRpm * 0.95f;
            for (int i = 0; i < 400; i++)
                gearbox.Tick(Dt, highRpm, 0.0f, 45f, 0.8f);

            Assert.LessOrEqual(gearbox.CurrentGear, 3,
                "Braking must suppress the upshift so engine braking is retained.");
        }

        [Test]
        public void AutoShiftDownshiftsWhenLugging()
        {
            var gearbox = Build(out var data);

            gearbox.ShiftTo(5);
            SettleShift(gearbox, data);

            float lowRpm = data.redlineRpm * 0.2f;
            for (int i = 0; i < 400 && gearbox.CurrentGear == 5; i++)
                gearbox.Tick(Dt, lowRpm, 0.6f, 30f, 0f);

            SettleShift(gearbox, data, lowRpm, 30f);
            Assert.AreEqual(4, gearbox.CurrentGear, "Auto-shift should drop a gear when the engine lugs.");
        }

        [Test]
        public void AutoShiftDoesNotHuntBetweenTwoGears()
        {
            var gearbox = Build(out var data);

            gearbox.ShiftTo(4);
            SettleShift(gearbox, data);

            // Park the rpm exactly between the thresholds; neither condition should fire.
            float betweenRpm = data.redlineRpm * 0.6f;
            int shifts = 0;
            int lastGear = gearbox.CurrentGear;

            for (int i = 0; i < 1000; i++)
            {
                gearbox.Tick(Dt, betweenRpm, 0.5f, 50f, 0f);
                if (gearbox.CurrentGear != lastGear)
                {
                    shifts++;
                    lastGear = gearbox.CurrentGear;
                }
            }

            Assert.AreEqual(0, shifts, "Steady cruise must not produce any shifts.");
        }

        [Test]
        public void WheelToEngineRpmNeverDropsBelowIdle()
        {
            var gearbox = Build(out var data, autoShift: false);

            gearbox.ShiftTo(1);
            SettleShift(gearbox, data);

            Assert.AreEqual(data.idleRpm, gearbox.WheelToEngineRpm(0f, data.idleRpm), 0.001f,
                "A stationary truck in gear must still idle, not stall at 0 rpm.");

            gearbox.ForceNeutral();
            Assert.AreEqual(data.idleRpm, gearbox.WheelToEngineRpm(500f, data.idleRpm), 0.001f,
                "In neutral the wheels must not drive engine rpm at all.");
        }

        [Test]
        public void MaxSpeedIncreasesWithGear()
        {
            var gearbox = Build(out var data, autoShift: false);
            const float wheelRadius = 0.5f;

            gearbox.ShiftTo(1);
            SettleShift(gearbox, data);
            float first = gearbox.MaxSpeedKmh(wheelRadius);

            gearbox.ShiftTo(data.GearCount);
            SettleShift(gearbox, data);
            float top = gearbox.MaxSpeedKmh(wheelRadius);

            Assert.Greater(top, first);
            Assert.Greater(top, 60f, "Top gear should be good for highway speed on a 1 m wheel.");
        }
    }
}
