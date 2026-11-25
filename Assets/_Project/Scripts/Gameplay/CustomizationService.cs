using System.Collections.Generic;
using TNLorry.Core;
using TNLorry.Data;
using UnityEngine;

namespace TNLorry.Gameplay
{
    /// <summary>
    /// Lorry customization (section F.3): buying cosmetics, fitting them per truck, paint, and
    /// the player-written name panel.
    ///
    /// Ownership is account-wide, fitting is per-truck — buy a peacock front panel once and you
    /// can move it between lorries. That split is the whole reason the shop feels fair.
    ///
    /// Sole writer of SaveData.ownedCosmeticIds, SaveData.gold, and each
    /// OwnedTruckSave's installedCosmeticIds / paintColorHex / namePlateText.
    /// </summary>
    public class CustomizationService
    {
        /// <summary>Longest name panel that still renders legibly at texture resolution.</summary>
        public const int MaxNamePlateLength = 24;

        readonly SaveSystem _save;
        readonly EconomyService _economy;
        readonly ProgressionService _progression;
        readonly CosmeticCatalogue _catalogue;

        public CustomizationService(SaveSystem save, EconomyService economy,
                                    ProgressionService progression, CosmeticCatalogue catalogue)
        {
            _save = save;
            _economy = economy;
            _progression = progression;
            _catalogue = catalogue;
        }

        public int Gold => _save.Data.gold;

        public enum PurchaseResult { Success, AlreadyOwned, CannotAfford, NotEnoughGold, LevelTooLow, Unknown }
        public enum FitResult { Success, NotOwned, NoTruck, SlotOccupied, Unknown }

        // ── Ownership ───────────────────────────────────────────────────────

        public bool Owns(string cosmeticId) => _save.Data.ownedCosmeticIds.Contains(cosmeticId);

        public PurchaseResult Buy(string cosmeticId)
        {
            var cosmetic = _catalogue?.Get(cosmeticId);
            if (cosmetic == null) return PurchaseResult.Unknown;
            if (Owns(cosmeticId)) return PurchaseResult.AlreadyOwned;
            if (_progression.Level < cosmetic.unlockLevel) return PurchaseResult.LevelTooLow;

            if (cosmetic.grantedFree)
            {
                Grant(cosmeticId);
                return PurchaseResult.Success;
            }

            if (cosmetic.IsPremium)
            {
                if (_save.Data.gold < cosmetic.priceGold) return PurchaseResult.NotEnoughGold;
                _save.Data.gold -= cosmetic.priceGold;
            }
            else if (!_economy.TryDebit(cosmetic.priceMoney, "cosmetic:" + cosmeticId))
            {
                return PurchaseResult.CannotAfford;
            }

            Grant(cosmeticId);
            return PurchaseResult.Success;
        }

        /// <summary>Awards a cosmetic without charging — tutorial reward, event prize, rewarded ad.</summary>
        public void Grant(string cosmeticId)
        {
            if (string.IsNullOrEmpty(cosmeticId) || Owns(cosmeticId)) return;

            _save.Data.ownedCosmeticIds.Add(cosmeticId);
            _save.MarkDirty();

            EventBus.Publish(new CosmeticAcquiredEvent { CosmeticId = cosmeticId });
        }

        public void AddGold(int amount, string reason)
        {
            if (amount <= 0) return;
            _save.Data.gold += amount;
            _save.MarkDirty();
            EventBus.Publish(new GoldChangedEvent { Balance = _save.Data.gold, Delta = amount, Reason = reason });
        }

        public bool SpendGold(int amount, string reason)
        {
            if (amount <= 0) return true;
            if (_save.Data.gold < amount) return false;

            _save.Data.gold -= amount;
            _save.MarkDirty();
            EventBus.Publish(new GoldChangedEvent { Balance = _save.Data.gold, Delta = -amount, Reason = reason });
            return true;
        }

        // ── Fitting ─────────────────────────────────────────────────────────

        OwnedTruckSave ActiveTruck
        {
            get
            {
                var trucks = _save.Data.ownedTrucks;
                if (trucks.Count == 0) return null;
                return trucks[Mathf.Clamp(_save.Data.activeTruckIndex, 0, trucks.Count - 1)];
            }
        }

        public bool IsFitted(string cosmeticId)
        {
            var truck = ActiveTruck;
            return truck != null && truck.installedCosmeticIds.Contains(cosmeticId);
        }

        /// <summary>
        /// Fits a cosmetic to the active truck, replacing whatever occupied that slot.
        ///
        /// Replacing rather than refusing is the right default: a player tapping a new front
        /// panel means "use this one", not "error, remove the old one first". Accessories are
        /// the exception — they stack.
        /// </summary>
        public FitResult Fit(string cosmeticId)
        {
            var cosmetic = _catalogue?.Get(cosmeticId);
            if (cosmetic == null) return FitResult.Unknown;
            if (!Owns(cosmeticId)) return FitResult.NotOwned;

            var truck = ActiveTruck;
            if (truck == null) return FitResult.NoTruck;

            if (truck.installedCosmeticIds.Contains(cosmeticId)) return FitResult.Success;

            if (cosmetic.slot != CosmeticSlot.Accessory)
            {
                var occupant = FittedInSlot(cosmetic.slot);
                if (occupant != null) truck.installedCosmeticIds.Remove(occupant.id);
            }

            truck.installedCosmeticIds.Add(cosmeticId);
            _save.MarkDirty();

            EventBus.Publish(new CosmeticFittedEvent { CosmeticId = cosmeticId, Slot = cosmetic.slot.ToString() });
            return FitResult.Success;
        }

        public bool Remove(string cosmeticId)
        {
            var truck = ActiveTruck;
            if (truck == null || !truck.installedCosmeticIds.Remove(cosmeticId)) return false;

            _save.MarkDirty();
            EventBus.Publish(new CosmeticRemovedEvent { CosmeticId = cosmeticId });
            return true;
        }

        /// <summary>What currently occupies a slot on the active truck, or null.</summary>
        public CosmeticData FittedInSlot(CosmeticSlot slot)
        {
            var truck = ActiveTruck;
            if (truck == null || _catalogue == null) return null;

            foreach (var id in truck.installedCosmeticIds)
            {
                var cosmetic = _catalogue.Get(id);
                if (cosmetic != null && cosmetic.slot == slot) return cosmetic;
            }
            return null;
        }

        /// <summary>All accessories fitted to the active truck (the stacking slot).</summary>
        public List<CosmeticData> FittedAccessories()
        {
            var result = new List<CosmeticData>();
            var truck = ActiveTruck;
            if (truck == null || _catalogue == null) return result;

            foreach (var id in truck.installedCosmeticIds)
            {
                var cosmetic = _catalogue.Get(id);
                if (cosmetic != null && cosmetic.slot == CosmeticSlot.Accessory) result.Add(cosmetic);
            }
            return result;
        }

        /// <summary>Everything owned that could go in a given slot, for the garage's slot picker.</summary>
        public List<CosmeticData> OwnedForSlot(CosmeticSlot slot)
        {
            var result = new List<CosmeticData>();
            if (_catalogue == null) return result;

            foreach (var id in _save.Data.ownedCosmeticIds)
            {
                var cosmetic = _catalogue.Get(id);
                if (cosmetic != null && cosmetic.slot == slot) result.Add(cosmetic);
            }
            return result;
        }

        // ── Paint ───────────────────────────────────────────────────────────

        public bool SetPaint(Color color)
        {
            var truck = ActiveTruck;
            if (truck == null) return false;

            truck.paintColorHex = "#" + ColorUtility.ToHtmlStringRGB(color);
            _save.MarkDirty();

            EventBus.Publish(new PaintChangedEvent { ColorHex = truck.paintColorHex });
            return true;
        }

        /// <summary>Falls back to the default blue rather than magenta-on-parse-failure.</summary>
        public Color GetPaint()
        {
            var truck = ActiveTruck;
            var fallback = new Color32(0x1B, 0x6A, 0xA5, 0xFF);

            if (truck == null || string.IsNullOrEmpty(truck.paintColorHex)) return fallback;
            return ColorUtility.TryParseHtmlString(truck.paintColorHex, out var parsed) ? parsed : fallback;
        }

        // ── Name panel ──────────────────────────────────────────────────────

        public string NamePlateText => ActiveTruck?.namePlateText ?? "";

        public enum NamePlateResult { Success, TooLong, Empty, InvalidCharacters, NoTruck }

        /// <summary>
        /// Sets the name panel text (section F.3). Tamil and English are both expected, which
        /// is why validation is on length and category rather than an alphabet allowlist.
        ///
        /// NOTE: this is player-authored text rendered onto the vehicle. It is safe while it is
        /// only ever seen by its author. Before anything makes it visible to other players —
        /// convoy multiplayer (v2.0) or shareable livery codes (Phase 3) — it needs server-side
        /// moderation. A client-side word list is not moderation.
        /// </summary>
        public NamePlateResult SetNamePlate(string text)
        {
            var truck = ActiveTruck;
            if (truck == null) return NamePlateResult.NoTruck;

            text = (text ?? "").Trim();

            if (text.Length == 0) return NamePlateResult.Empty;
            if (text.Length > MaxNamePlateLength) return NamePlateResult.TooLong;

            foreach (char c in text)
            {
                if (IsAllowedNamePlateChar(c)) continue;
                return NamePlateResult.InvalidCharacters;
            }

            truck.namePlateText = text;
            _save.MarkDirty();

            EventBus.Publish(new NamePlateChangedEvent { Text = text });
            return NamePlateResult.Success;
        }

        /// <summary>
        /// Letters, digits, combining marks, spaces and a little punctuation.
        ///
        /// The combining marks are the part that matters. `char.IsLetterOrDigit` alone returns
        /// FALSE for Tamil vowel signs and the virama — "ா" is a SpacingCombiningMark and "்"
        /// a NonSpacingMark — so a letters-only check rejects "அம்மா துணை", which is the
        /// example name panel in the design guide. Tamil is the game's primary language; an
        /// English-only name plate would be an embarrassing bug to ship.
        ///
        /// Format characters (Cf) stay rejected on purpose: a right-to-left override inside a
        /// string that gets rendered to a texture is a glyph-shaping problem, not a feature.
        /// </summary>
        public static bool IsAllowedNamePlateChar(char c)
        {
            if (char.IsLetterOrDigit(c)) return true;
            if (c == ' ' || c == '.' || c == '-' || c == '\'') return true;

            var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            return category == System.Globalization.UnicodeCategory.NonSpacingMark ||
                   category == System.Globalization.UnicodeCategory.SpacingCombiningMark;
        }

        public bool ClearNamePlate()
        {
            var truck = ActiveTruck;
            if (truck == null) return false;

            truck.namePlateText = "";
            _save.MarkDirty();
            EventBus.Publish(new NamePlateChangedEvent { Text = "" });
            return true;
        }
    }
}
