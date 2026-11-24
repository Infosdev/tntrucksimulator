using TNLorry.Data;
using UnityEngine;

namespace TNLorry.Tests
{
    /// <summary>
    /// Shared builders for the data assets the systems under test need.
    ///
    /// ScriptableObject.CreateInstance works fine outside the AssetDatabase, so tests get real
    /// TruckData / CargoData / EconomyConfig objects rather than mocks — which means a test
    /// failure points at real misbehaviour and not at a stale fake.
    /// </summary>
    public static class TestFixtures
    {
        /// <summary>A plausible 6x4 rigid lorry, close to the seeded Kaveri K-1212.</summary>
        public static TruckData Truck(
            float[] gears = null,
            float idleRpm = 650f,
            float redlineRpm = 2500f,
            float maxRpm = 2600f,
            float peakTorque = 1100f)
        {
            var data = ScriptableObject.CreateInstance<TruckData>();
            data.id = "test_truck";
            data.massKg = 9000f;
            data.maxPayloadKg = 16000f;
            data.idleRpm = idleRpm;
            data.maxRpm = maxRpm;
            data.redlineRpm = redlineRpm;
            data.gearRatios = gears ?? new[] { 9.01f, 5.27f, 3.21f, 2.04f, 1.36f, 1.00f, 0.78f };
            data.finalDrive = 4.1f;
            data.drivetrainEfficiency = 0.85f;
            data.shiftTime = 0.5f;
            data.autoShift = true;
            data.engineBrakeTorque = 900f;
            data.rpmResponse = 6f;
            data.fuelCapacityLitres = 160f;
            data.idleBurnLitresPerHour = 1.6f;
            data.peakBurnLitresPerHour = 26f;
            data.price = 1200000;

            // Flat-plateau diesel curve, same shape the seeder builds.
            data.torqueCurve = new AnimationCurve(
                new Keyframe(idleRpm, peakTorque * 0.48f),
                new Keyframe(Mathf.Lerp(idleRpm, redlineRpm, 0.28f), peakTorque * 0.97f),
                new Keyframe(Mathf.Lerp(idleRpm, redlineRpm, 0.62f), peakTorque),
                new Keyframe(redlineRpm, peakTorque * 0.60f));

            return data;
        }

        public static CargoData Cargo(
            string id = "test_cargo",
            float massKg = 12000f,
            float ratePerKm = 40f,
            CargoFlags flags = CargoFlags.None,
            TrailerType trailer = TrailerType.Flatbed)
        {
            var data = ScriptableObject.CreateInstance<CargoData>();
            data.id = id;
            data.nameKey = "cargo." + id;
            data.massKg = massKg;
            data.baseRatePerKm = ratePerKm;
            data.flags = flags;
            data.requiredTrailer = trailer;
            data.unlockLevel = 1;
            data.producedByCityIds = new string[0];
            data.consumedByCityIds = new string[0];
            return data;
        }

        public static CityData City(string id = "test_city", float payoutMultiplier = 1f, int roadNodeId = 0)
        {
            var data = ScriptableObject.CreateInstance<CityData>();
            data.id = id;
            data.nameKey = "city." + id;
            data.payoutMultiplier = payoutMultiplier;
            data.roadNodeId = roadNodeId;
            data.unlockLevel = 1;
            data.producesCargoIds = new string[0];
            data.consumesCargoIds = new string[0];
            return data;
        }

        /// <summary>EconomyConfig with its shipped defaults — the values the game actually uses.</summary>
        public static EconomyConfig Economy()
        {
            return ScriptableObject.CreateInstance<EconomyConfig>();
        }

        /// <summary>Remote config that always returns the shipped fallback, i.e. offline behaviour.</summary>
        public class PassthroughRemoteConfig : Services.IRemoteConfigService
        {
            public float  GetFloat(string key, float fallback)   => fallback;
            public int    GetInt(string key, int fallback)       => fallback;
            public bool   GetBool(string key, bool fallback)     => fallback;
            public string GetString(string key, string fallback) => fallback;
        }

        /// <summary>Remote config with overrides, for testing that live tuning actually lands.</summary>
        public class StubRemoteConfig : Services.IRemoteConfigService
        {
            readonly System.Collections.Generic.Dictionary<string, float> _floats =
                new System.Collections.Generic.Dictionary<string, float>();

            public StubRemoteConfig Set(string key, float value) { _floats[key] = value; return this; }

            public float GetFloat(string key, float fallback)
                => _floats.TryGetValue(key, out var v) ? v : fallback;

            public int    GetInt(string key, int fallback)       => fallback;
            public bool   GetBool(string key, bool fallback)     => fallback;
            public string GetString(string key, string fallback) => fallback;
        }
    }
}
