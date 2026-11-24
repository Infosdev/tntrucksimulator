using UnityEngine;

namespace TNLorry.Data
{
    public enum DepotKind { Port, LogisticsPark, Warehouse, TruckJunction, Factory, MarketYard, Garage, Quarry }

    /// <summary>
    /// A city / depot node (section D.2). World position is in compressed game space
    /// (~1:20 of real distance), not real lat/long — see the scale note in section D.
    /// </summary>
    [CreateAssetMenu(menuName = "TN/City Data", fileName = "City_")]
    public class CityData : ScriptableObject
    {
        [Header("Identity")]
        public string id = "city_id";
        [Tooltip("Localization key, e.g. city.madurai")]
        public string nameKey = "city.";
        public DepotKind depot = DepotKind.Warehouse;

        [Header("World placement")]
        [Tooltip("Position in compressed world space (metres).")]
        public Vector3 worldPosition;
        [Tooltip("Road-graph node id this depot's gate attaches to.")]
        public int roadNodeId = -1;
        [Tooltip("Map icon position in normalized 0..1 minimap texture space.")]
        public Vector2 minimapUv;

        [Header("Progression")]
        public int unlockLevel = 1;
        [Tooltip("City ids that must be unlocked first (keeps the map opening outward).")]
        public string[] requiresCityIds = new string[0];

        [Header("Services available here")]
        public bool hasFuelStation = true;
        public bool hasGarage;
        public bool hasRestPoint;
        public bool hasTruckDealer;

        [Header("Economy (section E.2)")]
        [Tooltip("Cargo ids produced here - weights outgoing job generation.")]
        public string[] producesCargoIds = new string[0];
        [Tooltip("Cargo ids consumed here - weights incoming job generation.")]
        public string[] consumesCargoIds = new string[0];
        [Tooltip("Local rate multiplier. >1 = lucrative but usually far or awkward to reach.")]
        [Range(0.5f, 2f)] public float payoutMultiplier = 1f;

        [Header("Presentation")]
        [Tooltip("Ambience clip id for this depot (city, market, port).")]
        public string ambienceId = "city";
        public Sprite postcard;
    }
}
