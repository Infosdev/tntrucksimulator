using UnityEngine;

namespace TNLorry.Data
{
    /// <summary>
    /// Where a cosmetic goes on the lorry (section F.3). One item per slot, except
    /// <see cref="Accessory"/> — garlands, a nazar doll and tassels all coexist, which is
    /// exactly how a real TN lorry is decorated.
    /// </summary>
    public enum CosmeticSlot
    {
        FrontPanelArt,
        BumperArt,
        SidePanelArt,
        NamePlate,
        Horn,
        LightStrip,
        Accessory,
        SeatCover,
        SteeringCover,
        Dashboard,
        MudFlap,
        TrailerLivery
    }

    /// <summary>
    /// One cosmetic item.
    ///
    /// Appendix 1 applies hard here: lorry art must be **commissioned or created original**
    /// work. Do not trace photographed panels, and keep dashboard idols generic. Horn melodies
    /// must be original compositions — a recognisable film tune is a Content ID claim and a
    /// Play Store takedown.
    /// </summary>
    [CreateAssetMenu(menuName = "TN/Cosmetic Data", fileName = "Cosmetic_")]
    public class CosmeticData : ScriptableObject
    {
        [Header("Identity")]
        public string id = "cosmetic_id";
        [Tooltip("Localization key, e.g. cosmetic.peacock_front")]
        public string nameKey = "cosmetic.";
        public CosmeticSlot slot = CosmeticSlot.Accessory;
        public Sprite thumbnail;

        [Header("Visual")]
        [Tooltip("Decal/texture layer for art slots (section Q.4 shader decal layers).")]
        public Texture2D decal;
        [Tooltip("Mesh to attach for accessory slots. Keep it under ~500 tris (section Q.2).")]
        public GameObject prefab;
        [Tooltip("Attachment point name on the truck prefab. Must exist or the item cannot fit.")]
        public string mountPoint = "";

        [Header("Audio")]
        [Tooltip("Horn slot only. Must be an original composition (Appendix 1).")]
        public AudioClip hornClip;

        [Header("Price")]
        [Tooltip("Rupee price. 0 when this is a premium (Gold) item.")]
        public int priceMoney;
        [Tooltip("Gold price. 0 when this is bought with rupees.")]
        public int priceGold;
        public int unlockLevel = 1;
        [Tooltip("Granted free, e.g. the tutorial's first cosmetic (section G tutorial step 5).")]
        public bool grantedFree;

        public bool IsPremium => priceGold > 0;

        /// <summary>Art slots are decal layers; the rest attach meshes or swap audio.</summary>
        public bool IsArtLayer =>
            slot == CosmeticSlot.FrontPanelArt || slot == CosmeticSlot.BumperArt ||
            slot == CosmeticSlot.SidePanelArt  || slot == CosmeticSlot.TrailerLivery;

        void OnValidate()
        {
            if (priceMoney > 0 && priceGold > 0)
                Debug.LogWarning(name + ": priced in both rupees and Gold. Pick one, or the shop " +
                                 "has to guess which the player is spending.", this);

            if (slot == CosmeticSlot.Horn && hornClip == null && !grantedFree)
                Debug.LogWarning(name + ": a Horn cosmetic with no clip changes nothing.", this);

            if (IsArtLayer && decal == null && !grantedFree)
                Debug.LogWarning(name + ": an art-layer cosmetic needs a decal texture.", this);
        }
    }
}
