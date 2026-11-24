using System;
using System.Collections.Generic;
using UnityEngine;

namespace TNLorry.Core
{
    /// <summary>
    /// Root save payload (section S.1). Kept flat and JsonUtility-friendly:
    /// no dictionaries, no interfaces, no polymorphism — Unity's serializer cannot handle them.
    /// Bump SchemaVersion whenever a field changes meaning, and migrate in SaveMigrations.
    /// </summary>
    [Serializable]
    public class SaveData
    {
        public const int CurrentSchemaVersion = 1;

        public int    schemaVersion = CurrentSchemaVersion;
        public string profileId = "";
        public string createdUtc = "";
        public string lastSavedUtc = "";
        public long   playTimeSeconds;

        // Progression
        public long  money = 150000;          // starting capital, ₹1.5 L (see EconomyConfig)
        public long  debt;                    // outstanding truck loan
        public int   xp;
        public int   level = 1;
        public int   reputation = 50;         // 0..100
        public List<string> unlockedCityIds  = new List<string>();
        public List<string> unlockedCargoIds = new List<string>();
        /// <summary>Cosmetics bought, account-wide. Installing them is per-truck.</summary>
        public List<string> ownedCosmeticIds = new List<string>();
        /// <summary>Premium currency ("Gold", section E.2). Earned sparingly, bought via IAP.</summary>
        public int gold;

        // Fleet
        public List<OwnedTruckSave> ownedTrucks = new List<OwnedTruckSave>();
        public int    activeTruckIndex;
        public List<HiredDriverSave> hiredDrivers = new List<HiredDriverSave>();
        /// <summary>Loan terms. `debt` above is the outstanding balance; FleetService owns both.</summary>
        public LoanSave loan = new LoanSave();

        // Current run
        public ActiveJobSave activeJob = new ActiveJobSave();
        public WorldStateSave world    = new WorldStateSave();

        // Settings (mirrored so a reinstall restores feel immediately)
        public SettingsSave settings = new SettingsSave();

        // Daily engagement
        public DailySave daily = new DailySave();

        // Stats for analytics / profile screen
        public long   totalDistanceKmX100;    // stored x100 to stay integral
        public int    deliveriesCompleted;
        public int    deliveriesFailed;
        public long   totalFinesPaid;
    }

    [Serializable]
    public class OwnedTruckSave
    {
        public string truckDataId = "";
        public string nickname = "";
        public float  odometerKm;
        public float  fuelLitres;
        public float  engineDamage;   // 0..1
        public float  chassisDamage;
        public float  wheelDamage;
        public float  cabinDamage;
        public List<string> installedUpgradeIds = new List<string>();
        public List<string> installedCosmeticIds = new List<string>();
        public string paintColorHex = "#1B6AA5";
        /// <summary>
        /// Player-written name panel ("அம்மா துணை"), rendered to a texture (section F.3).
        /// Validated through CustomizationService — never write it directly.
        /// </summary>
        public string namePlateText = "";
    }

    /// <summary>
    /// Daily missions and the login streak (section G). Like LoanSave, this was added after v1
    /// and deserialises safely from an older save as a zeroed object, so no schema bump.
    /// </summary>
    [Serializable]
    public class DailySave
    {
        /// <summary>Real-world date of the last claimed login reward, "yyyy-MM-dd" in local time.</summary>
        public string lastLoginDate = "";
        public int    loginStreak;
        public int    bestLoginStreak;

        /// <summary>Real-world date the current mission set was generated for.</summary>
        public string missionDate = "";
        public List<MissionSave> missions = new List<MissionSave>();
    }

    [Serializable]
    public class MissionSave
    {
        public string type = "";          // (string)MissionType
        public string parameter = "";     // cargo id, city id, etc. "" when unscoped
        public int    target;
        public int    progress;
        public int    rewardMoney;
        public int    rewardXp;
        public bool   claimed;
    }

    /// <summary>
    /// Truck loan terms (section E.2). Added after v1 shipped; a save written before it exists
    /// simply deserialises with a zeroed LoanSave, which reads as "no loan" — so this needed
    /// no schema bump or migration.
    /// </summary>
    [Serializable]
    public class LoanSave
    {
        public bool   active;
        public long   principalBorrowed;
        public float  monthlyInterest;      // fraction, e.g. 0.011
        public long   emiAmount;            // due each in-game month
        public int    nextDueDay;           // in-game day the next EMI falls due
        public int    remainingInstalments;
        public int    missedInstalments;
        public string purchasedTruckId = "";
    }

    [Serializable]
    public class HiredDriverSave
    {
        public string driverId = "";
        public string name = "";
        public int    skill;              // 1..5
        public string assignedTruckDataId = "";
        public string routeId = "";
        public double nextPayoutUnix;     // passive income tick
        public int    payoutPerTrip;
    }

    [Serializable]
    public class ActiveJobSave
    {
        public bool   hasJob;
        public string jobId = "";
        public string cargoId = "";
        public string fromCityId = "";
        public string toCityId = "";
        public float  distanceKm;
        public float  timeLimitMinutes;
        public float  elapsedMinutes;
        public int    payout;
        public float  cargoDamage;        // 0..1
        public bool   cargoLoaded;
    }

    [Serializable]
    public class WorldStateSave
    {
        public int    day = 1;
        public float  hourOfDay = 8f;
        public int    weather;            // (int)WeatherType
        public float  fatigue;            // 0..1
        public float  playerPosX, playerPosY, playerPosZ;
        public float  playerYaw;
        public string lastRestPointId = "";
    }

    [Serializable]
    public class SettingsSave
    {
        public string language = "";          // "" = follow device, else "ta" / "en"
        public int    steeringMode = 0;       // (int)SteeringMode
        public float  steeringSensitivity = 1f;
        public float  tiltSensitivity = 1f;
        public bool   invertTilt;
        public int    qualityTier = -1;       // -1 = auto-detect
        public bool   manualGearbox;
        public float  masterVolume = 1f;
        public float  musicVolume = 0.6f;
        public float  sfxVolume = 1f;
        public bool   hapticsEnabled = true;
        public bool   speedUnitsMph;
    }
}
