using System.Collections.Generic;
using UnityEngine;

namespace TNLorry.Data
{
    /// <summary>
    /// Id -> CosmeticData lookup for the shop and garage. Separate from GameDatabase because
    /// the cosmetic set grows far faster than trucks or cargo do (it is the live-ops content
    /// stream), and it should be swappable for an Addressable-backed catalogue on its own.
    /// </summary>
    [CreateAssetMenu(menuName = "TN/Cosmetic Catalogue", fileName = "CosmeticCatalogue")]
    public class CosmeticCatalogue : ScriptableObject
    {
        public CosmeticData[] cosmetics = new CosmeticData[0];

        Dictionary<string, CosmeticData> _byId;

        public void Build()
        {
            _byId = new Dictionary<string, CosmeticData>(cosmetics.Length);

            foreach (var cosmetic in cosmetics)
            {
                if (cosmetic == null) continue;

                if (string.IsNullOrEmpty(cosmetic.id))
                {
                    Debug.LogError("[CosmeticCatalogue] " + cosmetic.name + " has an empty id.", cosmetic);
                    continue;
                }
                if (_byId.ContainsKey(cosmetic.id))
                {
                    Debug.LogError("[CosmeticCatalogue] Duplicate cosmetic id '" + cosmetic.id + "'.", cosmetic);
                    continue;
                }
                _byId[cosmetic.id] = cosmetic;
            }
        }

        public CosmeticData Get(string id)
        {
            if (_byId == null) Build();
            if (string.IsNullOrEmpty(id)) return null;
            return _byId.TryGetValue(id, out var found) ? found : null;
        }

        /// <summary>Everything in a slot that the player is high enough level to buy.</summary>
        public List<CosmeticData> ForSlot(CosmeticSlot slot, int playerLevel)
        {
            var result = new List<CosmeticData>();
            foreach (var cosmetic in cosmetics)
                if (cosmetic != null && cosmetic.slot == slot && cosmetic.unlockLevel <= playerLevel)
                    result.Add(cosmetic);
            return result;
        }
    }
}
