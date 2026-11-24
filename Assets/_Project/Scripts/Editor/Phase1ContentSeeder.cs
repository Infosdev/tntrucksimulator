using System.Collections.Generic;
using System.IO;
using TNLorry.Data;
using TNLorry.Navigation;
using UnityEditor;
using UnityEngine;

namespace TNLorry.EditorTools
{
    /// <summary>
    /// Generates the Phase 1 "Central Corridor" content set as real ScriptableObjects
    /// (sections D.1, D.2, E.1, F.1).
    ///
    /// Run once from <c>TN Lorry &gt; Seed Phase 1 Content</c>. After that the assets are
    /// normal project files that designers edit in the Inspector — this tool is a bootstrap,
    /// not a pipeline. Re-running updates existing assets in place (matched by id) and never
    /// deletes anything, so hand-tuned values survive as long as you change them in the asset
    /// rather than here.
    ///
    /// Map scale: ~1:20 (section D's scale note). City positions are derived from real
    /// lat/long so the map's shape and the order of towns are right; the authoritative road
    /// lengths are set explicitly on the graph edges from real compressed road distances,
    /// because straight lines between depots are shorter than the roads that join them.
    /// </summary>
    public static class Phase1ContentSeeder
    {
        const string DataRoot   = "Assets/_Project/Data";
        const string CityDir    = DataRoot + "/Cities";
        const string CargoDir   = DataRoot + "/Cargo";
        const string TruckDir   = DataRoot + "/Trucks";
        const string RouteDir   = DataRoot + "/Routes";
        const string EconomyDir = DataRoot + "/Economy";

        /// <summary>Map compression factor: 1 game metre represents this many real metres.</summary>
        const float MapScale = 20f;

        // Chennai is the map origin. Metres per degree at ~11.5 N.
        const double OriginLat = 13.08, OriginLon = 80.27;
        const double MetresPerDegreeLat = 111320.0;
        const double MetresPerDegreeLon = 109100.0;

        [MenuItem("TN Lorry/Seed Phase 1 Content")]
        public static void Seed()
        {
            EnsureFolders();

            var cities = SeedCities();
            var cargo  = SeedCargo();
            var trucks = SeedTrucks();
            var graph  = SeedRoadGraph(cities);
            var economy = SeedEconomy();

            SeedDatabase(trucks, cargo, cities);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Phase1Seeder] Seeded " + cities.Count + " cities, " + cargo.Count + " cargo types, " +
                      trucks.Count + " trucks, " + graph.nodes.Length + " road nodes, " +
                      graph.edges.Length + " road edges. Economy: " + economy.name);
        }

        static void EnsureFolders()
        {
            foreach (var path in new[] { CityDir, CargoDir, TruckDir, RouteDir, EconomyDir })
            {
                if (Directory.Exists(path)) continue;
                Directory.CreateDirectory(path);
            }
            AssetDatabase.Refresh();
        }

        // ── Cities (section D.2) ────────────────────────────────────────────

        struct CitySeed
        {
            public string Id;
            public DepotKind Depot;
            public double Lat, Lon;
            public int UnlockLevel;
            public bool Fuel, Garage, Rest, Dealer;
            public string[] Produces;
            public string[] Consumes;
            public float PayoutMultiplier;
            public string Ambience;
        }

        static readonly CitySeed[] CitySeeds =
        {
            new CitySeed { Id = "chennai", Depot = DepotKind.Port, Lat = 13.08, Lon = 80.27, UnlockLevel = 1,
                Fuel = true, Garage = true, Rest = true, Dealer = true,
                Produces = new[] { "containers", "auto_parts" },
                Consumes = new[] { "garments", "turmeric", "steel_coils", "eggs", "bananas", "cement" },
                PayoutMultiplier = 1.1f, Ambience = "city_port" },

            new CitySeed { Id = "vellore", Depot = DepotKind.Warehouse, Lat = 12.92, Lon = 79.13, UnlockLevel = 1,
                Fuel = true, Rest = true,
                Produces = new string[0],
                Consumes = new[] { "containers", "cement", "milk" },
                PayoutMultiplier = 1f, Ambience = "town" },

            new CitySeed { Id = "krishnagiri", Depot = DepotKind.TruckJunction, Lat = 12.52, Lon = 78.21, UnlockLevel = 1,
                Fuel = true, Rest = true,
                Produces = new[] { "bananas" },
                Consumes = new[] { "containers", "cement" },
                PayoutMultiplier = 1f, Ambience = "highway_junction" },

            new CitySeed { Id = "salem", Depot = DepotKind.Factory, Lat = 11.66, Lon = 78.14, UnlockLevel = 1,
                Fuel = true, Garage = true, Rest = true, Dealer = true,
                Produces = new[] { "steel_coils" },
                Consumes = new[] { "containers", "auto_parts", "cement" },
                PayoutMultiplier = 1f, Ambience = "city" },

            new CitySeed { Id = "namakkal", Depot = DepotKind.Garage, Lat = 11.22, Lon = 78.17, UnlockLevel = 3,
                Fuel = true, Garage = true, Rest = true, Dealer = true,
                Produces = new[] { "eggs" },
                Consumes = new[] { "steel_coils", "cement" },
                PayoutMultiplier = 1.05f, Ambience = "lorry_hub" },

            new CitySeed { Id = "erode", Depot = DepotKind.MarketYard, Lat = 11.34, Lon = 77.72, UnlockLevel = 6,
                Fuel = true, Rest = true,
                Produces = new[] { "turmeric" },
                Consumes = new[] { "containers", "eggs", "milk" },
                PayoutMultiplier = 1f, Ambience = "market" },

            new CitySeed { Id = "tiruppur", Depot = DepotKind.Factory, Lat = 11.11, Lon = 77.35, UnlockLevel = 9,
                Fuel = true, Rest = true,
                Produces = new[] { "garments" },
                Consumes = new[] { "containers", "cement" },
                PayoutMultiplier = 1.15f, Ambience = "industrial" },

            new CitySeed { Id = "coimbatore", Depot = DepotKind.Factory, Lat = 11.02, Lon = 76.96, UnlockLevel = 9,
                Fuel = true, Garage = true, Rest = true, Dealer = true,
                Produces = new[] { "motor_pumps" },
                Consumes = new[] { "steel_coils", "containers", "turmeric", "cement" },
                PayoutMultiplier = 1.1f, Ambience = "city" },

            new CitySeed { Id = "karur", Depot = DepotKind.Warehouse, Lat = 10.96, Lon = 78.08, UnlockLevel = 12,
                Fuel = true, Rest = true,
                Produces = new[] { "garments" },
                Consumes = new[] { "steel_coils", "cement" },
                PayoutMultiplier = 1f, Ambience = "town" },

            new CitySeed { Id = "dindigul", Depot = DepotKind.Factory, Lat = 10.37, Lon = 77.98, UnlockLevel = 15,
                Fuel = true, Rest = true,
                Produces = new[] { "bananas" },
                Consumes = new[] { "containers", "steel_coils", "cement" },
                PayoutMultiplier = 1f, Ambience = "town" },

            new CitySeed { Id = "madurai", Depot = DepotKind.MarketYard, Lat = 9.93, Lon = 78.12, UnlockLevel = 18,
                Fuel = true, Garage = true, Rest = true, Dealer = true,
                Produces = new[] { "bananas" },
                Consumes = new[] { "containers", "garments", "eggs", "milk", "cement", "motor_pumps" },
                PayoutMultiplier = 1.2f, Ambience = "city" }
        };

        static Vector3 ToWorld(double lat, double lon)
        {
            float x = (float)((lon - OriginLon) * MetresPerDegreeLon / MapScale);
            float z = (float)((lat - OriginLat) * MetresPerDegreeLat / MapScale);
            return new Vector3(x, 0f, z);
        }

        static List<CityData> SeedCities()
        {
            var result = new List<CityData>();

            for (int i = 0; i < CitySeeds.Length; i++)
            {
                var seed = CitySeeds[i];
                var asset = LoadOrCreate<CityData>(CityDir + "/City_" + Capitalise(seed.Id) + ".asset");

                asset.id = seed.Id;
                asset.nameKey = "city." + seed.Id;
                asset.depot = seed.Depot;
                asset.worldPosition = ToWorld(seed.Lat, seed.Lon);
                asset.roadNodeId = NodeIdFor(seed.Id);
                asset.unlockLevel = seed.UnlockLevel;
                asset.hasFuelStation = seed.Fuel;
                asset.hasGarage = seed.Garage;
                asset.hasRestPoint = seed.Rest;
                asset.hasTruckDealer = seed.Dealer;
                asset.producesCargoIds = seed.Produces;
                asset.consumesCargoIds = seed.Consumes;
                asset.payoutMultiplier = seed.PayoutMultiplier;
                asset.ambienceId = seed.Ambience;

                EditorUtility.SetDirty(asset);
                result.Add(asset);
            }

            return result;
        }

        /// <summary>
        /// Depot gate node ids are the city's index in CitySeeds, so CityData.roadNodeId and the
        /// road graph can never drift apart. Junction nodes start at 100.
        /// </summary>
        static int NodeIdFor(string cityId)
        {
            for (int i = 0; i < CitySeeds.Length; i++)
                if (CitySeeds[i].Id == cityId) return i;
            return -1;
        }

        // ── Cargo (section E.1) ────────────────────────────────────────────

        struct CargoSeed
        {
            public string Id;
            public float MassKg;
            public TrailerType Trailer;
            public float RatePerKm;
            public int UnlockLevel;
            public CargoFlags Flags;
            public float SpeedCapKmh;
            public float PerishMinutes;
            public string[] ProducedBy;
            public string[] ConsumedBy;
        }

        static readonly CargoSeed[] CargoSeeds =
        {
            new CargoSeed { Id = "containers", MassKg = 22000f, Trailer = TrailerType.Container, RatePerKm = 48f,
                UnlockLevel = 1, ProducedBy = new[] { "chennai" },
                ConsumedBy = new[] { "salem", "coimbatore", "madurai", "vellore" } },

            new CargoSeed { Id = "steel_coils", MassKg = 24000f, Trailer = TrailerType.Flatbed, RatePerKm = 52f,
                UnlockLevel = 1, ProducedBy = new[] { "salem" },
                ConsumedBy = new[] { "coimbatore", "chennai", "karur", "namakkal" } },

            new CargoSeed { Id = "garments", MassKg = 9000f, Trailer = TrailerType.ClosedBody, RatePerKm = 44f,
                UnlockLevel = 1, Flags = CargoFlags.RainSensitive,
                ProducedBy = new[] { "tiruppur", "karur" }, ConsumedBy = new[] { "chennai", "madurai" } },

            new CargoSeed { Id = "turmeric", MassKg = 14000f, Trailer = TrailerType.OpenBodyTarp, RatePerKm = 40f,
                UnlockLevel = 1, Flags = CargoFlags.RainSensitive,
                ProducedBy = new[] { "erode" }, ConsumedBy = new[] { "chennai", "coimbatore" } },

            new CargoSeed { Id = "cement", MassKg = 26000f, Trailer = TrailerType.Tipper, RatePerKm = 34f,
                UnlockLevel = 2, ProducedBy = new[] { "krishnagiri" },
                ConsumedBy = new[] { "salem", "madurai", "coimbatore", "vellore", "karur", "dindigul" } },

            // Fragile: the Namakkal egg run, the game's gentle-driving tutorial cargo.
            new CargoSeed { Id = "eggs", MassKg = 7000f, Trailer = TrailerType.ClosedBody, RatePerKm = 62f,
                UnlockLevel = 4, Flags = CargoFlags.Fragile,
                ProducedBy = new[] { "namakkal" }, ConsumedBy = new[] { "chennai", "madurai", "erode" } },

            new CargoSeed { Id = "bananas", MassKg = 11000f, Trailer = TrailerType.OpenBodyTarp, RatePerKm = 46f,
                UnlockLevel = 7, Flags = CargoFlags.Perishable, PerishMinutes = 300f,
                ProducedBy = new[] { "dindigul", "madurai", "krishnagiri" }, ConsumedBy = new[] { "chennai" } },

            new CargoSeed { Id = "milk", MassKg = 16000f, Trailer = TrailerType.InsulatedTanker, RatePerKm = 55f,
                UnlockLevel = 7, Flags = CargoFlags.Perishable | CargoFlags.Liquid, PerishMinutes = 240f,
                ProducedBy = new[] { "erode", "namakkal" }, ConsumedBy = new[] { "chennai", "madurai" } },

            new CargoSeed { Id = "motor_pumps", MassKg = 13000f, Trailer = TrailerType.Flatbed, RatePerKm = 58f,
                UnlockLevel = 10, ProducedBy = new[] { "coimbatore" },
                ConsumedBy = new[] { "madurai", "chennai", "dindigul" } },

            new CargoSeed { Id = "auto_parts", MassKg = 8000f, Trailer = TrailerType.ClosedBody, RatePerKm = 66f,
                UnlockLevel = 13, ProducedBy = new[] { "chennai" },
                ConsumedBy = new[] { "coimbatore", "salem" } },

            // Hazardous: Sivakasi is Phase 3, but the cargo type and its rules land now.
            new CargoSeed { Id = "matchboxes", MassKg = 6000f, Trailer = TrailerType.ClosedBody, RatePerKm = 85f,
                UnlockLevel = 16, Flags = CargoFlags.Hazardous, SpeedCapKmh = 50f,
                ProducedBy = new[] { "madurai" }, ConsumedBy = new[] { "chennai", "coimbatore" } },

            new CargoSeed { Id = "salt", MassKg = 25000f, Trailer = TrailerType.Tipper, RatePerKm = 32f,
                UnlockLevel = 20, ProducedBy = new[] { "madurai" },
                ConsumedBy = new[] { "chennai", "salem" } }
        };

        static List<CargoData> SeedCargo()
        {
            var result = new List<CargoData>();

            foreach (var seed in CargoSeeds)
            {
                var asset = LoadOrCreate<CargoData>(CargoDir + "/Cargo_" + Capitalise(seed.Id) + ".asset");

                asset.id = seed.Id;
                asset.nameKey = "cargo." + seed.Id;
                asset.massKg = seed.MassKg;
                asset.requiredTrailer = seed.Trailer;
                asset.baseRatePerKm = seed.RatePerKm;
                asset.unlockLevel = seed.UnlockLevel;
                asset.flags = seed.Flags;
                asset.speedCapKmh = seed.SpeedCapKmh;
                asset.perishMinutes = seed.PerishMinutes;
                asset.fragilityMultiplier = (seed.Flags & CargoFlags.Fragile) != 0 ? 2.5f : 1f;
                asset.producedByCityIds = seed.ProducedBy ?? new string[0];
                asset.consumedByCityIds = seed.ConsumedBy ?? new string[0];

                EditorUtility.SetDirty(asset);
                result.Add(asset);
            }

            return result;
        }

        // ── Trucks (section F.1 — all brands fictional, see Appendix 1) ─────

        struct TruckSeed
        {
            public string Id, Brand, Model;
            public DriveLayout Layout;
            public float MassKg, MaxPayloadKg;
            public float IdleRpm, MaxRpm, RedlineRpm, PeakTorqueNm;
            public float[] Gears;
            public float FinalDrive;
            public float MaxSteerAngle, MaxBrakeTorque, RetarderTorque;
            public float FuelCapacity, IdleBurn, PeakBurn;
            public int Price, UnlockLevel;
        }

        static readonly TruckSeed[] TruckSeeds =
        {
            // Tutorial LCV. Light, slow, forgiving — and the only one a level-1 player can afford.
            new TruckSeed { Id = "kutty_lorry", Brand = "Mini", Model = "Kutty Lorry",
                Layout = DriveLayout.FourByTwo, MassKg = 2800f, MaxPayloadKg = 3500f,
                IdleRpm = 750f, MaxRpm = 3600f, RedlineRpm = 3400f, PeakTorqueNm = 300f,
                Gears = new[] { 4.9f, 2.85f, 1.7f, 1.0f, 0.78f }, FinalDrive = 4.3f,
                MaxSteerAngle = 40f, MaxBrakeTorque = 6500f, RetarderTorque = 0f,
                FuelCapacity = 60f, IdleBurn = 0.8f, PeakBurn = 9f,
                Price = 450000, UnlockLevel = 1 },

            // The classic TN "Namakkal body" lorry — the game's signature truck.
            new TruckSeed { Id = "kaveri_k1212", Brand = "Kaveri Motors", Model = "K-1212",
                Layout = DriveLayout.SixByFour, MassKg = 9000f, MaxPayloadKg = 16000f,
                IdleRpm = 650f, MaxRpm = 2600f, RedlineRpm = 2500f, PeakTorqueNm = 1100f,
                Gears = new[] { 9.01f, 5.27f, 3.21f, 2.04f, 1.36f, 1.0f, 0.78f }, FinalDrive = 4.1f,
                MaxSteerAngle = 35f, MaxBrakeTorque = 22000f, RetarderTorque = 0f,
                FuelCapacity = 160f, IdleBurn = 1.6f, PeakBurn = 26f,
                Price = 1200000, UnlockLevel = 1 },

            new TruckSeed { Id = "kaveri_k2518", Brand = "Kaveri Motors", Model = "K-2518",
                Layout = DriveLayout.TenWheeler, MassKg = 12500f, MaxPayloadKg = 25000f,
                IdleRpm = 620f, MaxRpm = 2400f, RedlineRpm = 2300f, PeakTorqueNm = 1500f,
                Gears = new[] { 10.2f, 6.1f, 3.8f, 2.4f, 1.55f, 1.0f, 0.73f }, FinalDrive = 4.3f,
                MaxSteerAngle = 33f, MaxBrakeTorque = 30000f, RetarderTorque = 5000f,
                FuelCapacity = 240f, IdleBurn = 2.0f, PeakBurn = 34f,
                Price = 2400000, UnlockLevel = 8 },

            new TruckSeed { Id = "vaigai_vhaul3718", Brand = "Vaigai Trucks", Model = "V-Haul 3718",
                Layout = DriveLayout.TwelveWheeler, MassKg = 15000f, MaxPayloadKg = 31000f,
                IdleRpm = 600f, MaxRpm = 2300f, RedlineRpm = 2200f, PeakTorqueNm = 1750f,
                Gears = new[] { 11.5f, 7.0f, 4.4f, 2.8f, 1.8f, 1.15f, 1.0f, 0.74f }, FinalDrive = 4.6f,
                MaxSteerAngle = 31f, MaxBrakeTorque = 36000f, RetarderTorque = 6500f,
                FuelCapacity = 300f, IdleBurn = 2.3f, PeakBurn = 40f,
                Price = 3600000, UnlockLevel = 16 },

            new TruckSeed { Id = "vaigai_vtractor4923", Brand = "Vaigai Trucks", Model = "V-Tractor 4923",
                Layout = DriveLayout.SixByFour, MassKg = 8500f, MaxPayloadKg = 36000f,
                IdleRpm = 600f, MaxRpm = 2200f, RedlineRpm = 2100f, PeakTorqueNm = 2100f,
                Gears = new[] { 12.0f, 7.3f, 4.6f, 2.9f, 1.85f, 1.17f, 1.0f, 0.73f }, FinalDrive = 4.1f,
                MaxSteerAngle = 36f, MaxBrakeTorque = 34000f, RetarderTorque = 7000f,
                FuelCapacity = 400f, IdleBurn = 2.4f, PeakBurn = 44f,
                Price = 4800000, UnlockLevel = 24 },

            // Premium cab-over. The aspirational purchase, with the retarder that makes ghats sane.
            new TruckSeed { Id = "pothigai_ph55", Brand = "Pothigai Heavy", Model = "PH-55",
                Layout = DriveLayout.SixByTwo, MassKg = 9500f, MaxPayloadKg = 34000f,
                IdleRpm = 580f, MaxRpm = 2100f, RedlineRpm = 2000f, PeakTorqueNm = 2400f,
                Gears = new[] { 11.0f, 6.8f, 4.3f, 2.7f, 1.74f, 1.1f, 1.0f, 0.72f, 0.62f }, FinalDrive = 3.7f,
                MaxSteerAngle = 37f, MaxBrakeTorque = 38000f, RetarderTorque = 9000f,
                FuelCapacity = 500f, IdleBurn = 2.1f, PeakBurn = 42f,
                Price = 7200000, UnlockLevel = 34 }
        };

        static List<TruckData> SeedTrucks()
        {
            var result = new List<TruckData>();

            foreach (var seed in TruckSeeds)
            {
                var asset = LoadOrCreate<TruckData>(TruckDir + "/Truck_" + Capitalise(seed.Id) + ".asset");

                asset.id = seed.Id;
                asset.nameKey = "truck." + seed.Id;
                asset.fictionalBrand = seed.Brand;
                asset.layout = seed.Layout;
                asset.massKg = seed.MassKg;
                asset.maxPayloadKg = seed.MaxPayloadKg;
                asset.idleRpm = seed.IdleRpm;
                asset.maxRpm = seed.MaxRpm;
                asset.redlineRpm = seed.RedlineRpm;
                asset.torqueCurve = BuildDieselTorqueCurve(seed.IdleRpm, seed.RedlineRpm, seed.PeakTorqueNm);
                asset.engineBrakeTorque = seed.PeakTorqueNm * 0.8f;
                asset.gearRatios = seed.Gears;
                asset.finalDrive = seed.FinalDrive;
                asset.maxSteerAngle = seed.MaxSteerAngle;
                asset.maxBrakeTorque = seed.MaxBrakeTorque;
                asset.retarderTorque = seed.RetarderTorque;
                asset.fuelCapacityLitres = seed.FuelCapacity;
                asset.idleBurnLitresPerHour = seed.IdleBurn;
                asset.peakBurnLitresPerHour = seed.PeakBurn;
                asset.price = seed.Price;
                asset.unlockLevel = seed.UnlockLevel;
                asset.repairCostPerPercent = seed.Price * 0.00075f;

                // Heavier trucks sit higher and need a lower balance point to stay upright.
                float heaviness = Mathf.Clamp01(seed.MassKg / 15000f);
                asset.centerOfMass = new Vector3(0f, Mathf.Lerp(-0.2f, -0.45f, heaviness), 0.2f);

                EditorUtility.SetDirty(asset);
                result.Add(asset);
            }

            return result;
        }

        /// <summary>
        /// Diesel torque shape: rises fast off idle, plateaus broadly across the mid-range,
        /// then falls away before the redline. The plateau is what makes a lorry pull.
        /// </summary>
        static AnimationCurve BuildDieselTorqueCurve(float idleRpm, float redlineRpm, float peakNm)
        {
            float plateauStart = Mathf.Lerp(idleRpm, redlineRpm, 0.28f);
            float plateauEnd   = Mathf.Lerp(idleRpm, redlineRpm, 0.62f);

            var curve = new AnimationCurve(
                new Keyframe(idleRpm,      peakNm * 0.48f),
                new Keyframe(plateauStart, peakNm * 0.97f),
                new Keyframe(plateauEnd,   peakNm),
                new Keyframe(redlineRpm,   peakNm * 0.60f));

            for (int i = 0; i < curve.length; i++)
            {
                curve.SmoothTangents(i, 0.4f);
            }
            return curve;
        }

        // ── Road graph (section D.1 Phase 1) ───────────────────────────────

        struct EdgeSeed
        {
            public string From, To;
            /// <summary>Real road distance in km; divided by MapScale to get game metres.</summary>
            public float RealKm;
            public float SpeedLimit;
            public int Lanes;
            public bool Toll, Urban;
            public string Highway;
        }

        static readonly EdgeSeed[] EdgeSeeds =
        {
            // NH48: Chennai -> Vellore -> Krishnagiri. 6-lane, tolled, heavy port traffic.
            new EdgeSeed { From = "chennai", To = "vellore", RealKm = 140f, SpeedLimit = 100f, Lanes = 3,
                Toll = true, Highway = "NH48" },
            new EdgeSeed { From = "vellore", To = "krishnagiri", RealKm = 100f, SpeedLimit = 100f, Lanes = 3,
                Toll = true, Highway = "NH48" },

            // NH44: the spine. Krishnagiri -> Salem -> Namakkal -> Karur -> Dindigul -> Madurai.
            new EdgeSeed { From = "krishnagiri", To = "salem", RealKm = 110f, SpeedLimit = 100f, Lanes = 3,
                Toll = true, Highway = "NH44" },
            new EdgeSeed { From = "salem", To = "namakkal", RealKm = 55f, SpeedLimit = 90f, Lanes = 2,
                Toll = true, Highway = "NH44" },
            new EdgeSeed { From = "namakkal", To = "karur", RealKm = 50f, SpeedLimit = 90f, Lanes = 2,
                Toll = true, Highway = "NH44" },
            new EdgeSeed { From = "karur", To = "dindigul", RealKm = 75f, SpeedLimit = 90f, Lanes = 2,
                Toll = true, Highway = "NH44" },
            new EdgeSeed { From = "dindigul", To = "madurai", RealKm = 65f, SpeedLimit = 90f, Lanes = 2,
                Toll = true, Highway = "NH44" },

            // NH544: the industrial belt. Salem -> Erode -> Tiruppur -> Coimbatore.
            new EdgeSeed { From = "salem", To = "erode", RealKm = 65f, SpeedLimit = 90f, Lanes = 2,
                Toll = true, Highway = "NH544" },
            new EdgeSeed { From = "erode", To = "tiruppur", RealKm = 55f, SpeedLimit = 80f, Lanes = 2,
                Highway = "NH544" },
            new EdgeSeed { From = "tiruppur", To = "coimbatore", RealKm = 50f, SpeedLimit = 80f, Lanes = 2,
                Urban = true, Highway = "NH544" },

            // SH: the Karur-Erode link, which gives the router a real alternative instead of
            // forcing every west-bound job back through Salem.
            new EdgeSeed { From = "karur", To = "erode", RealKm = 65f, SpeedLimit = 70f, Lanes = 1,
                Highway = "SH" }
        };

        static RoadGraphAsset SeedRoadGraph(List<CityData> cities)
        {
            var asset = LoadOrCreate<RoadGraphAsset>(RouteDir + "/RoadGraph_Phase1_CentralCorridor.asset");
            asset.routeName = "Phase 1 — Central Corridor (NH48 / NH44 / NH544)";

            var nodes = new List<RoadGraphAsset.NodeEntry>();
            foreach (var city in cities)
            {
                nodes.Add(new RoadGraphAsset.NodeEntry
                {
                    id = city.roadNodeId,
                    position = city.worldPosition,
                    isIntersection = true,
                    hasSignal = false,
                    cityId = city.id
                });
            }

            var edges = new List<RoadGraphAsset.EdgeEntry>();
            foreach (var seed in EdgeSeeds)
            {
                int from = NodeIdFor(seed.From);
                int to   = NodeIdFor(seed.To);

                if (from < 0 || to < 0)
                {
                    Debug.LogError("[Phase1Seeder] Edge " + seed.From + "->" + seed.To + " names an unknown city.");
                    continue;
                }

                edges.Add(new RoadGraphAsset.EdgeEntry
                {
                    from = from,
                    to = to,
                    // Authoritative length, from the real road distance at map scale. Straight
                    // lines between depot markers would under-report every route.
                    lengthM = seed.RealKm * 1000f / MapScale,
                    speedLimitKmh = seed.SpeedLimit,
                    lanes = seed.Lanes,
                    toll = seed.Toll,
                    urban = seed.Urban,
                    ghat = false,          // Phase 2 adds Ooty / Yercaud
                    bidirectional = true
                });
            }

            asset.nodes = nodes.ToArray();
            asset.edges = edges.ToArray();

            EditorUtility.SetDirty(asset);
            return asset;
        }

        // ── Economy & database ─────────────────────────────────────────────

        static EconomyConfig SeedEconomy()
        {
            var asset = LoadOrCreate<EconomyConfig>(EconomyDir + "/EconomyConfig.asset");
            EditorUtility.SetDirty(asset);     // defaults come from the class; nothing to override yet
            return asset;
        }

        static void SeedDatabase(List<TruckData> trucks, List<CargoData> cargo, List<CityData> cities)
        {
            var asset = LoadOrCreate<GameDatabase>(DataRoot + "/GameDatabase.asset");
            asset.trucks = trucks.ToArray();
            asset.cargo  = cargo.ToArray();
            asset.cities = cities.ToArray();
            asset.Build();
            EditorUtility.SetDirty(asset);
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;

            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        static string Capitalise(string id)
        {
            var parts = id.Split('_');
            var sb = new System.Text.StringBuilder();
            foreach (var part in parts)
            {
                if (part.Length == 0) continue;
                sb.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1) sb.Append(part.Substring(1));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Sanity check on the seeded set: every cargo must have a producer and a consumer that
        /// the player can actually reach, or the job board silently generates nothing for it.
        /// </summary>
        [MenuItem("TN Lorry/Validate Phase 1 Content")]
        public static void Validate()
        {
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataRoot + "/GameDatabase.asset");
            if (db == null)
            {
                Debug.LogError("[Phase1Seeder] No GameDatabase found. Run Seed Phase 1 Content first.");
                return;
            }

            db.Build();
            int problems = 0;

            foreach (var cargo in db.cargo)
            {
                if (cargo == null) continue;

                if (cargo.producedByCityIds.Length == 0)
                {
                    Debug.LogWarning("[Validate] " + cargo.id + " has no producing city; it will never appear.", cargo);
                    problems++;
                }

                foreach (var cityId in cargo.producedByCityIds)
                {
                    if (db.GetCity(cityId) == null)
                    {
                        Debug.LogError("[Validate] " + cargo.id + " names unknown producer '" + cityId + "'.", cargo);
                        problems++;
                    }
                }

                foreach (var cityId in cargo.consumedByCityIds)
                {
                    if (db.GetCity(cityId) == null)
                    {
                        Debug.LogError("[Validate] " + cargo.id + " names unknown consumer '" + cityId + "'.", cargo);
                        problems++;
                    }
                }
            }

            foreach (var city in db.cities)
            {
                if (city == null) continue;
                if (city.roadNodeId < 0)
                {
                    Debug.LogError("[Validate] " + city.id + " has no road node; jobs to it cannot be routed.", city);
                    problems++;
                }
            }

            // Every unlocked city must be reachable from Chennai, or a job board can offer a
            // destination the player cannot drive to.
            var graphAsset = AssetDatabase.LoadAssetAtPath<RoadGraphAsset>(
                RouteDir + "/RoadGraph_Phase1_CentralCorridor.asset");

            if (graphAsset != null)
            {
                var graph = graphAsset.BuildGraph();
                int chennai = NodeIdFor("chennai");

                foreach (var city in db.cities)
                {
                    if (city == null || city.id == "chennai") continue;
                    var route = graph.FindRoute(chennai, city.roadNodeId);
                    if (!route.IsValid)
                    {
                        Debug.LogError("[Validate] " + city.id + " is unreachable from Chennai in the road graph.", city);
                        problems++;
                    }
                }
            }

            Debug.Log(problems == 0
                ? "[Validate] Phase 1 content is consistent."
                : "[Validate] Found " + problems + " problem(s); see the errors above.");
        }
    }
}
