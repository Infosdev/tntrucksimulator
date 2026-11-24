using UnityEngine;

namespace TNLorry.Data
{
    /// <summary>
    /// Every economy tunable in one asset (sections C, E.2, G). These are the shipped
    /// defaults; Remote Config may override individual values at runtime without an app
    /// update, so read them through EconomyService rather than touching this directly.
    ///
    /// Balance in a spreadsheet first (section E.2) and paste the result here — the target
    /// is a meaningful purchase every 30-60 min early game, 2-4 h mid game.
    /// </summary>
    [CreateAssetMenu(menuName = "TN/Economy Config", fileName = "EconomyConfig")]
    public class EconomyConfig : ScriptableObject
    {
        [Header("Starting state")]
        public long startingMoney = 150000;
        public int  startingReputation = 50;

        [Header("Payout formula (section C)")]
        [Tooltip("Global scalar on every payout. First knob to turn when the economy runs hot or cold.")]
        public float payoutScalar = 1f;
        [Tooltip("Extra pay per tonne of cargo above 10 t, as a fraction of base.")]
        public float weightFactorPerTonne = 0.015f;
        [Tooltip("Multiplier when delivered inside the time limit.")]
        public float onTimeBonusMultiplier = 1.15f;
        [Tooltip("Multiplier floor when very late. Payout lerps from 1.0 to this as lateness grows.")]
        public float latePenaltyFloor = 0.55f;
        [Tooltip("Minutes late at which latePenaltyFloor is reached.")]
        public float latenessFullPenaltyMinutes = 45f;
        [Tooltip("Fraction of payout lost at 100% cargo damage.")]
        [Range(0f, 1f)] public float cargoDamageMaxPenalty = 0.8f;
        [Tooltip("Flat bonus for finishing with better-than-expected fuel use.")]
        public int fuelEfficiencyBonus = 1200;
        [Tooltip("Flat bonus for a delivery with zero violations.")]
        public int cleanDrivingBonus = 1500;

        [Header("Ghat / difficulty premiums")]
        [Tooltip("Pay multiplier for routes flagged as ghat (hairpins, steep grade).")]
        public float ghatRouteMultiplier = 1.35f;
        [Tooltip("Pay multiplier for hazardous cargo (Sivakasi fireworks, fuel).")]
        public float hazardousMultiplier = 1.4f;
        [Tooltip("Pay multiplier for fragile cargo (Namakkal eggs).")]
        public float fragileMultiplier = 1.25f;

        [Header("Costs (section E.2 sinks)")]
        [Tooltip("Diesel price per litre in rupees. Also exposed to Remote Config for events.")]
        public float dieselPricePerLitre = 94f;
        [Tooltip("Rupees per km of toll on national highways.")]
        public float tollRatePerKm = 2.4f;
        [Tooltip("FASTag lane discount fraction.")]
        [Range(0f, 0.5f)] public float fastagDiscount = 0.05f;
        public float garageRentPerDay = 2500f;
        public float driverSalaryPerTrip = 3500f;
        [Tooltip("Monthly interest rate on truck loans, as a fraction.")]
        public float loanMonthlyInterest = 0.011f;
        [Tooltip("Fraction of the remaining payout lost when a job is abandoned.")]
        [Range(0f, 1f)] public float abandonPenaltyFraction = 0.25f;

        [Header("Fines (section K.7)")]
        public int fineOverspeed = 1000;
        public int fineRedLight = 1000;
        public int fineWrongSide = 1500;
        public int fineVehicleCollision = 500;
        public int finePedestrianCollision = 5000;
        public int fineNoHeadlights = 500;
        public int fineOverload = 2000;
        [Tooltip("Reputation lost per pedestrian collision.")]
        public int pedestrianReputationHit = 5;

        [Header("XP & levels (section E.2: levels 1-60)")]
        public int maxLevel = 60;
        [Tooltip("XP for a 100 km clean on-time delivery, before multipliers.")]
        public int xpPerHundredKm = 120;
        [Tooltip("XP needed for level 2. Later levels scale by levelCurveExponent.")]
        public int baseXpToLevel = 500;
        [Tooltip("xpToNext = baseXpToLevel * level^exponent. 1.35 gives a steady, non-brutal curve.")]
        public float levelCurveExponent = 1.35f;

        [Header("Progression gates (section E.2)")]
        public int levelForBankLoan = 5;      // buy your first truck
        public int levelForOwnGarage = 10;
        public int levelForHiringDrivers = 20;

        [Header("Job board (section K.3)")]
        public int minJobsPerDepot = 6;
        public int maxJobsPerDepot = 10;
        [Tooltip("In-game hours before the board refreshes.")]
        public float jobBoardRefreshHours = 24f;
        [Tooltip("Average km/h assumed when deriving a job's time limit.")]
        public float plannedAverageSpeedKmh = 45f;
        [Tooltip("Slack on top of the planned time. 1.3 = 30% headroom for tolls and traffic.")]
        public float timeLimitSlack = 1.3f;

        [Header("Fuel & fatigue (section K.5)")]
        [Tooltip("In-game hours that pass per real minute while driving.")]
        public float gameHoursPerRealMinute = 1f;
        [Tooltip("In-game driving hours before fatigue effects begin.")]
        public float fatigueOnsetHours = 10f;
        [Tooltip("In-game driving hours at which fatigue is maxed.")]
        public float fatigueMaxHours = 14f;

        void OnValidate()
        {
            maxLevel = Mathf.Max(2, maxLevel);
            if (minJobsPerDepot > maxJobsPerDepot) minJobsPerDepot = maxJobsPerDepot;
            if (latePenaltyFloor > 1f) latePenaltyFloor = 1f;
        }
    }
}
