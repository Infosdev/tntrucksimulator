using NUnit.Framework;
using TNLorry.Core;
using TNLorry.Data;
using TNLorry.Gameplay;
using UnityEngine;

namespace TNLorry.Tests
{
    public class CustomizationServiceTests
    {
        EconomyConfig _config;
        SaveSystem _save;
        EconomyService _economy;
        ProgressionService _progression;
        CosmeticCatalogue _catalogue;
        CustomizationService _custom;

        const string FrontArt    = "peacock_front";
        const string OtherFront  = "sunrise_front";
        const string AirHorn     = "air_horn";
        const string Garland     = "garland";
        const string NazarDoll   = "nazar_doll";
        const string PremiumArt  = "gold_panel";
        const string HighLevel   = "chrome_stack";
        const string FreeGift    = "starter_sticker";

        [SetUp]
        public void SetUp()
        {
            _config = TestFixtures.Economy();
            _save = TempSave.Create();
            _economy = new EconomyService(_config, _save, new TestFixtures.PassthroughRemoteConfig());
            _progression = new ProgressionService(_config, _save);

            _catalogue = ScriptableObject.CreateInstance<CosmeticCatalogue>();
            _catalogue.cosmetics = new[]
            {
                Cosmetic(FrontArt,   CosmeticSlot.FrontPanelArt, money: 20000),
                Cosmetic(OtherFront, CosmeticSlot.FrontPanelArt, money: 25000),
                Cosmetic(AirHorn,    CosmeticSlot.Horn,          money: 15000),
                Cosmetic(Garland,    CosmeticSlot.Accessory,     money: 2000),
                Cosmetic(NazarDoll,  CosmeticSlot.Accessory,     money: 1500),
                Cosmetic(PremiumArt, CosmeticSlot.SidePanelArt,  gold: 50),
                Cosmetic(HighLevel,  CosmeticSlot.LightStrip,    money: 5000, unlockLevel: 25),
                Cosmetic(FreeGift,   CosmeticSlot.MudFlap,       grantedFree: true)
            };
            _catalogue.Build();

            _custom = new CustomizationService(_save, _economy, _progression, _catalogue);

            // A truck to fit things to.
            _save.Data.ownedTrucks.Add(new OwnedTruckSave { truckDataId = "kaveri_k1212" });
            _save.Data.activeTruckIndex = 0;

            _economy.Credit(200000, "test funds");
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.Clear();
            TempSave.Cleanup();
        }

        static CosmeticData Cosmetic(string id, CosmeticSlot slot, int money = 0, int gold = 0,
                                     int unlockLevel = 1, bool grantedFree = false)
        {
            var data = ScriptableObject.CreateInstance<CosmeticData>();
            data.id = id;
            data.nameKey = "cosmetic." + id;
            data.slot = slot;
            data.priceMoney = money;
            data.priceGold = gold;
            data.unlockLevel = unlockLevel;
            data.grantedFree = grantedFree;
            return data;
        }

        OwnedTruckSave Truck => _save.Data.ownedTrucks[0];

        // ── Buying ─────────────────────────────────────────────────────────

        [Test]
        public void BuyingDebitsRupeesAndGrantsOwnership()
        {
            long before = _economy.Balance;

            Assert.AreEqual(CustomizationService.PurchaseResult.Success, _custom.Buy(FrontArt));

            Assert.AreEqual(before - 20000, _economy.Balance);
            Assert.IsTrue(_custom.Owns(FrontArt));
        }

        [Test]
        public void BuyingTwiceIsRefusedAndChargesOnce()
        {
            _custom.Buy(FrontArt);
            long after = _economy.Balance;

            Assert.AreEqual(CustomizationService.PurchaseResult.AlreadyOwned, _custom.Buy(FrontArt));
            Assert.AreEqual(after, _economy.Balance);
        }

        [Test]
        public void CannotBuyWhatYouCannotAfford()
        {
            _economy.TryDebit(_economy.Balance, "spend it all");

            Assert.AreEqual(CustomizationService.PurchaseResult.CannotAfford, _custom.Buy(FrontArt));
            Assert.IsFalse(_custom.Owns(FrontArt));
        }

        [Test]
        public void CannotBuyAboveYourLevel()
        {
            Assert.AreEqual(CustomizationService.PurchaseResult.LevelTooLow, _custom.Buy(HighLevel));
            Assert.IsFalse(_custom.Owns(HighLevel));
        }

        [Test]
        public void UnknownCosmeticIsRejected()
        {
            Assert.AreEqual(CustomizationService.PurchaseResult.Unknown, _custom.Buy("does_not_exist"));
        }

        [Test]
        public void PremiumItemsCostGoldNotRupees()
        {
            _custom.AddGold(60, "test");
            long rupeesBefore = _economy.Balance;

            Assert.AreEqual(CustomizationService.PurchaseResult.Success, _custom.Buy(PremiumArt));

            Assert.AreEqual(10, _custom.Gold);
            Assert.AreEqual(rupeesBefore, _economy.Balance, "A Gold item must not touch the rupee balance.");
        }

        [Test]
        public void PremiumItemIsRefusedWithoutEnoughGold()
        {
            _custom.AddGold(10, "test");

            Assert.AreEqual(CustomizationService.PurchaseResult.NotEnoughGold, _custom.Buy(PremiumArt));
            Assert.AreEqual(10, _custom.Gold);
            Assert.IsFalse(_custom.Owns(PremiumArt));
        }

        [Test]
        public void FreeItemCostsNothing()
        {
            long rupees = _economy.Balance;

            Assert.AreEqual(CustomizationService.PurchaseResult.Success, _custom.Buy(FreeGift));

            Assert.AreEqual(rupees, _economy.Balance);
            Assert.IsTrue(_custom.Owns(FreeGift));
        }

        [Test]
        public void GrantIsIdempotentAndDoesNotDuplicate()
        {
            _custom.Grant(FrontArt);
            _custom.Grant(FrontArt);

            int count = 0;
            foreach (var id in _save.Data.ownedCosmeticIds) if (id == FrontArt) count++;
            Assert.AreEqual(1, count);
        }

        [Test]
        public void GoldCannotBeOverspent()
        {
            _custom.AddGold(30, "test");

            Assert.IsFalse(_custom.SpendGold(31, "too much"));
            Assert.AreEqual(30, _custom.Gold);

            Assert.IsTrue(_custom.SpendGold(30, "exact"));
            Assert.AreEqual(0, _custom.Gold);
        }

        // ── Fitting ────────────────────────────────────────────────────────

        [Test]
        public void CannotFitSomethingYouDoNotOwn()
        {
            Assert.AreEqual(CustomizationService.FitResult.NotOwned, _custom.Fit(FrontArt));
        }

        [Test]
        public void CannotFitWithoutATruck()
        {
            _custom.Grant(FrontArt);
            _save.Data.ownedTrucks.Clear();

            Assert.AreEqual(CustomizationService.FitResult.NoTruck, _custom.Fit(FrontArt));
        }

        [Test]
        public void FittingAddsToTheActiveTruck()
        {
            _custom.Buy(FrontArt);

            Assert.AreEqual(CustomizationService.FitResult.Success, _custom.Fit(FrontArt));
            Assert.IsTrue(_custom.IsFitted(FrontArt));
            Assert.AreEqual(FrontArt, _custom.FittedInSlot(CosmeticSlot.FrontPanelArt).id);
        }

        [Test]
        public void FittingASecondItemInTheSameSlotReplacesTheFirst()
        {
            _custom.Buy(FrontArt);
            _custom.Buy(OtherFront);

            _custom.Fit(FrontArt);
            _custom.Fit(OtherFront);

            Assert.IsFalse(_custom.IsFitted(FrontArt), "Tapping a new front panel means 'use this one'.");
            Assert.IsTrue(_custom.IsFitted(OtherFront));
            Assert.AreEqual(OtherFront, _custom.FittedInSlot(CosmeticSlot.FrontPanelArt).id);
        }

        [Test]
        public void AccessoriesStackInsteadOfReplacing()
        {
            _custom.Buy(Garland);
            _custom.Buy(NazarDoll);

            _custom.Fit(Garland);
            _custom.Fit(NazarDoll);

            // A real lorry carries a garland AND a nazar doll AND tassels.
            Assert.IsTrue(_custom.IsFitted(Garland));
            Assert.IsTrue(_custom.IsFitted(NazarDoll));
            Assert.AreEqual(2, _custom.FittedAccessories().Count);
        }

        [Test]
        public void FittingTheSameItemTwiceIsASafeNoOp()
        {
            _custom.Buy(Garland);
            _custom.Fit(Garland);

            Assert.AreEqual(CustomizationService.FitResult.Success, _custom.Fit(Garland));
            Assert.AreEqual(1, _custom.FittedAccessories().Count);
        }

        [Test]
        public void RemovingTakesItOffButKeepsOwnership()
        {
            _custom.Buy(FrontArt);
            _custom.Fit(FrontArt);

            Assert.IsTrue(_custom.Remove(FrontArt));
            Assert.IsFalse(_custom.IsFitted(FrontArt));
            Assert.IsTrue(_custom.Owns(FrontArt), "Taking a panel off must not lose what you paid for.");
            Assert.IsNull(_custom.FittedInSlot(CosmeticSlot.FrontPanelArt));
        }

        [Test]
        public void RemovingSomethingNotFittedIsFalse()
        {
            Assert.IsFalse(_custom.Remove(FrontArt));
        }

        [Test]
        public void OwnedForSlotListsOnlyThatSlot()
        {
            _custom.Buy(FrontArt);
            _custom.Buy(OtherFront);
            _custom.Buy(AirHorn);

            var fronts = _custom.OwnedForSlot(CosmeticSlot.FrontPanelArt);

            Assert.AreEqual(2, fronts.Count);
            Assert.AreEqual(1, _custom.OwnedForSlot(CosmeticSlot.Horn).Count);
            Assert.AreEqual(0, _custom.OwnedForSlot(CosmeticSlot.SeatCover).Count);
        }

        [Test]
        public void FittingIsPerTruckNotPerAccount()
        {
            _custom.Buy(FrontArt);
            _custom.Fit(FrontArt);

            // Buy a second truck and switch to it.
            _save.Data.ownedTrucks.Add(new OwnedTruckSave { truckDataId = "kaveri_k2518" });
            _save.Data.activeTruckIndex = 1;

            Assert.IsFalse(_custom.IsFitted(FrontArt), "The new truck starts undecorated…");
            Assert.IsTrue(_custom.Owns(FrontArt), "…but the panel is still owned, so it can be moved over.");
            Assert.AreEqual(CustomizationService.FitResult.Success, _custom.Fit(FrontArt));
        }

        // ── Paint ──────────────────────────────────────────────────────────

        [Test]
        public void PaintRoundTripsThroughHex()
        {
            var colour = new Color32(0xE4, 0x52, 0x1B, 0xFF);

            Assert.IsTrue(_custom.SetPaint(colour));

            var read = (Color32)_custom.GetPaint();
            Assert.AreEqual(colour.r, read.r);
            Assert.AreEqual(colour.g, read.g);
            Assert.AreEqual(colour.b, read.b);
        }

        [Test]
        public void UnparseableHexFallsBackToTheDefaultNotMagenta()
        {
            Truck.paintColorHex = "not-a-colour";

            var fallback = _custom.GetPaint();

            Assert.AreNotEqual(Color.magenta, fallback);
            Assert.Greater(fallback.b, fallback.r, "The default is the blue the trucks ship in.");
        }

        // ── Name panel ─────────────────────────────────────────────────────

        [Test]
        public void AcceptsAnEnglishNamePlate()
        {
            Assert.AreEqual(CustomizationService.NamePlateResult.Success,
                            _custom.SetNamePlate("Mother's Blessing"));
            Assert.AreEqual("Mother's Blessing", _custom.NamePlateText);
        }

        [Test]
        public void AcceptsATamilNamePlate()
        {
            // The guide's own example panel. Tamil vowel signs are combining marks, not
            // letters, so a char.IsLetterOrDigit-only check rejects this string outright —
            // which would have shipped the feature broken for the game's primary language.
            const string tamil = "அம்மா துணை";

            Assert.AreEqual(CustomizationService.NamePlateResult.Success, _custom.SetNamePlate(tamil));
            Assert.AreEqual(tamil, _custom.NamePlateText);
        }

        [Test]
        public void TamilCombiningMarksAreAllowedIndividually()
        {
            Assert.IsTrue(CustomizationService.IsAllowedNamePlateChar('ா'), "TAMIL VOWEL SIGN AA (Mc)");
            Assert.IsTrue(CustomizationService.IsAllowedNamePlateChar('்'), "TAMIL SIGN VIRAMA (Mn)");
            Assert.IsTrue(CustomizationService.IsAllowedNamePlateChar('ை'), "TAMIL VOWEL SIGN AI (Mc)");
        }

        [Test]
        public void RejectsFormatAndControlCharacters()
        {
            // U+202E RIGHT-TO-LEFT OVERRIDE: a shaping problem in a rendered texture.
            Assert.IsFalse(CustomizationService.IsAllowedNamePlateChar('‮'));
            Assert.AreEqual(CustomizationService.NamePlateResult.InvalidCharacters,
                            _custom.SetNamePlate("Horn ‮OK"));

            Assert.AreEqual(CustomizationService.NamePlateResult.InvalidCharacters,
                            _custom.SetNamePlate("Horn\tOK"));
        }

        [Test]
        public void RejectsEmojiAndSymbols()
        {
            Assert.AreEqual(CustomizationService.NamePlateResult.InvalidCharacters,
                            _custom.SetNamePlate("Horn OK <3"));
        }

        [Test]
        public void RejectsAnEmptyOrWhitespaceOnlyPlate()
        {
            Assert.AreEqual(CustomizationService.NamePlateResult.Empty, _custom.SetNamePlate(""));
            Assert.AreEqual(CustomizationService.NamePlateResult.Empty, _custom.SetNamePlate("    "));
            Assert.AreEqual(CustomizationService.NamePlateResult.Empty, _custom.SetNamePlate(null));
        }

        [Test]
        public void RejectsAnOverlongPlate()
        {
            var tooLong = new string('a', CustomizationService.MaxNamePlateLength + 1);

            Assert.AreEqual(CustomizationService.NamePlateResult.TooLong, _custom.SetNamePlate(tooLong));
        }

        [Test]
        public void AcceptsExactlyTheMaximumLength()
        {
            var exact = new string('a', CustomizationService.MaxNamePlateLength);

            Assert.AreEqual(CustomizationService.NamePlateResult.Success, _custom.SetNamePlate(exact));
        }

        [Test]
        public void NamePlateIsTrimmed()
        {
            _custom.SetNamePlate("  Namma Lorry  ");
            Assert.AreEqual("Namma Lorry", _custom.NamePlateText);
        }

        [Test]
        public void ClearingTheNamePlateEmptiesIt()
        {
            _custom.SetNamePlate("Namma Lorry");

            Assert.IsTrue(_custom.ClearNamePlate());
            Assert.AreEqual("", _custom.NamePlateText);
        }

        [Test]
        public void NamePlateNeedsATruck()
        {
            _save.Data.ownedTrucks.Clear();

            Assert.AreEqual(CustomizationService.NamePlateResult.NoTruck, _custom.SetNamePlate("Anything"));
        }

        // ── Persistence ────────────────────────────────────────────────────

        [Test]
        public void EverythingSurvivesAReload()
        {
            _custom.AddGold(40, "test");
            _custom.Buy(FrontArt);
            _custom.Buy(Garland);
            _custom.Fit(FrontArt);
            _custom.Fit(Garland);
            _custom.SetPaint(new Color32(0x11, 0x22, 0x33, 0xFF));
            _custom.SetNamePlate("அம்மா துணை");

            _save.SaveImmediate();
            var reloaded = TempSave.Reopen().Data;

            Assert.AreEqual(40, reloaded.gold);
            Assert.Contains(FrontArt, reloaded.ownedCosmeticIds);
            Assert.Contains(Garland, reloaded.ownedCosmeticIds);
            Assert.Contains(FrontArt, reloaded.ownedTrucks[0].installedCosmeticIds);
            Assert.AreEqual("#112233", reloaded.ownedTrucks[0].paintColorHex);
            Assert.AreEqual("அம்மா துணை", reloaded.ownedTrucks[0].namePlateText,
                "A Tamil name panel must survive the JSON round trip intact.");
        }
    }
}
