using System;
using UnityEngine;

namespace TNLorry.Data
{
    [Flags]
    public enum CargoFlags
    {
        None          = 0,
        Fragile       = 1 << 0,   // eggs (Namakkal) - damage from potholes & hard braking
        Hazardous     = 1 << 1,   // Sivakasi fireworks, fuel - speed cap, zone bans
        Perishable    = 1 << 2,   // bananas, milk - hard timer
        RainSensitive = 1 << 3,   // turmeric, garments - penalty if tarp damaged in rain
        Oversize      = 1 << 4,   // machinery - escort/route restrictions at L4
        Liquid        = 1 << 5,   // tanker slosh physics (Phase 3)
        Seasonal      = 1 << 6    // sugarcane - Pongal event only
    }

    public enum TrailerType { Container, ClosedBody, OpenBodyTarp, Flatbed, Tipper, Tanker, InsulatedTanker }

    /// <summary>Cargo catalogue entry (section E.1).</summary>
    [CreateAssetMenu(menuName = "TN/Cargo Data", fileName = "Cargo_")]
    public class CargoData : ScriptableObject
    {
        [Header("Identity")]
        public string id = "cargo_id";
        [Tooltip("Localization key, e.g. cargo.turmeric")]
        public string nameKey = "cargo.";
        public Sprite icon;

        [Header("Physical")]
        public float massKg = 12000f;
        public TrailerType requiredTrailer = TrailerType.ClosedBody;
        public GameObject visualPrefab;

        [Header("Economy")]
        [Tooltip("Rupees per km before distance/weight/time multipliers (section C).")]
        public float baseRatePerKm = 42f;
        public int   unlockLevel = 1;

        [Header("Rules")]
        public CargoFlags flags = CargoFlags.None;
        [Tooltip("Hazardous/oversize speed ceiling in km/h. 0 = no special cap.")]
        public float speedCapKmh;
        [Tooltip("Damage taken per 1 g of vertical shock above the threshold, for Fragile cargo.")]
        public float fragilityMultiplier = 1f;
        [Tooltip("Minutes before a Perishable load starts losing value.")]
        public float perishMinutes;

        [Header("Origin / destination weighting (section E.2)")]
        [Tooltip("City ids that produce this cargo - jobs originate here.")]
        public string[] producedByCityIds = new string[0];
        [Tooltip("City ids that consume it. Empty = any unlocked city may be a destination.")]
        public string[] consumedByCityIds = new string[0];

        public bool Has(CargoFlags flag) => (flags & flag) != 0;
    }
}
