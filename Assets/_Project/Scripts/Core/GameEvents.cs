namespace TNLorry.Core
{
    // ── Economy / progression ───────────────────────────────────────────────
    public struct MoneyChangedEvent { public long Balance; public long Delta; public string Reason; }
    public struct XpChangedEvent    { public int Xp; public int Level; public bool LeveledUp; }

    // ── Fleet & finance ─────────────────────────────────────────────────────
    public struct TruckPurchasedEvent { public string TruckDataId; public int Price; public bool OnLoan; }
    public struct LoanInstalmentDueEvent
    {
        public long Amount;
        public bool Paid;               // false when the balance could not cover it
        public int  RemainingInstalments;
        public long Outstanding;
    }
    public struct DriverHiredEvent   { public string DriverId; public string Name; public int Skill; }
    public struct DriverFiredEvent   { public string DriverId; }
    public struct DriverTripCompletedEvent
    {
        public string DriverId;
        public int    NetEarnings;      // payout minus the driver's salary
        public bool   HadIncident;
    }

    // ── Jobs ────────────────────────────────────────────────────────────────
    public struct JobOfferedEvent   { public int JobCount; }
    public struct JobAcceptedEvent  { public string JobId; public string CargoId; public string FromCity; public string ToCity; public float DistanceKm; public int Payout; }
    public struct JobAbandonedEvent { public string JobId; public int Penalty; }
    public struct DeliveryCompletedEvent
    {
        public string JobId;
        public int    Payout;
        public float  CargoDamagePercent;
        public bool   OnTime;
        public float  ElapsedMinutes;
        public int    XpAwarded;
    }

    // ── Truck state ─────────────────────────────────────────────────────────
    public struct FuelChangedEvent   { public float Litres; public float CapacityLitres; }
    public struct OutOfFuelEvent     { }
    public struct DamageChangedEvent { public float EnginePercent; public float ChassisPercent; public float CargoPercent; }
    public struct GearChangedEvent   { public int GearIndex; public string Label; }
    public struct TrailerCoupledEvent   { public string TrailerId; }
    public struct TrailerDecoupledEvent { public string TrailerId; }

    // ── Rules ───────────────────────────────────────────────────────────────
    public struct FineIssuedEvent { public ViolationType Violation; public int Amount; public int ReputationDelta; }
    public struct TollPaidEvent   { public string PlazaId; public int Amount; public bool UsedFastag; }

    // ── Roadside services ───────────────────────────────────────────────────
    public struct RefuelledEvent { public string StationId; public float Litres; public int Cost; }
    public struct WeighbridgeReadingEvent { public string PostId; public float CargoKg; public float ExcessKg; }

    // ── Customization (section F.3) ─────────────────────────────────────────
    public struct GoldChangedEvent      { public int Balance; public int Delta; public string Reason; }
    public struct CosmeticAcquiredEvent { public string CosmeticId; }
    public struct CosmeticFittedEvent   { public string CosmeticId; public string Slot; }
    public struct CosmeticRemovedEvent  { public string CosmeticId; }
    public struct PaintChangedEvent     { public string ColorHex; }
    public struct NamePlateChangedEvent { public string Text; }

    // ── Daily engagement ────────────────────────────────────────────────────
    public struct LoginRewardClaimedEvent
    {
        public int Amount;
        public int Streak;      // continuous days shown up
        public int CycleDay;    // 1-7 position in the reward table
    }
    public struct DailyMissionsRefreshedEvent { public int Count; }
    public struct DailyMissionCompletedEvent
    {
        public string Type;
        public int    RewardMoney;
        public int    RewardXp;
    }

    // ── Fatigue & clock ─────────────────────────────────────────────────────
    public struct FatigueChangedEvent { public float Normalized; } // 0..1
    public struct RestCompletedEvent  { public int HoursSlept; }
    public struct GameHourPassedEvent { public int Day; public int Hour; }

    // ── World ───────────────────────────────────────────────────────────────
    public struct OriginShiftedEvent  { public UnityEngine.Vector3 Offset; }
    public struct WeatherChangedEvent { public WeatherType Type; public float Intensity; }
    public struct TileLoadedEvent     { public int X; public int Y; }
    public struct TileUnloadedEvent   { public int X; public int Y; }

    public enum ViolationType { Overspeed, RedLight, WrongSide, VehicleCollision, PedestrianCollision, NoHeadlights, Overload }
    public enum WeatherType   { Clear, Cloudy, Rain, HeavyRain, Fog }
}
