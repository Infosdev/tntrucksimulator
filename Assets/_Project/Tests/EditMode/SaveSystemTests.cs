using System.IO;
using NUnit.Framework;
using UnityEngine.TestTools;
using TNLorry.Core;

namespace TNLorry.Tests
{
    /// <summary>
    /// The save file is the player's entire investment in the game. These tests cover the three
    /// ways it can be lost: a torn write, a corrupt file, and a schema change.
    /// </summary>
    public class SaveSystemTests
    {
        SaveSystem _save;

        [SetUp]
        public void SetUp() => _save = TempSave.Create();

        [TearDown]
        public void TearDown() => TempSave.Cleanup();

        [Test]
        public void FreshProfileHasStartingCapitalAndPhaseOneUnlocks()
        {
            var data = _save.Data;

            Assert.IsNotEmpty(data.profileId);
            Assert.Greater(data.money, 0, "A new driver needs working capital.");
            Assert.AreEqual(1, data.level);
            Assert.Contains("chennai", data.unlockedCityIds);
            Assert.Contains("salem", data.unlockedCityIds);
            Assert.Contains("containers", data.unlockedCargoIds);
        }

        [Test]
        public void RoundTripsEveryFieldItClaimsToPersist()
        {
            var data = _save.Data;
            data.money = 987654;
            data.debt = 250000;
            data.level = 17;
            data.xp = 432;
            data.reputation = 73;
            data.deliveriesCompleted = 41;
            data.totalFinesPaid = 9500;
            data.totalDistanceKmX100 = 1234567;
            data.world.day = 12;
            data.world.hourOfDay = 18.25f;
            data.world.fatigue = 0.4f;
            data.settings.steeringMode = 2;
            data.settings.language = "ta";
            data.settings.manualGearbox = true;
            data.ownedTrucks.Add(new OwnedTruckSave { truckDataId = "kaveri_k1212", odometerKm = 4821f });

            _save.SaveImmediate();
            var reloaded = TempSave.Reopen().Data;

            Assert.AreEqual(987654, reloaded.money);
            Assert.AreEqual(250000, reloaded.debt);
            Assert.AreEqual(17, reloaded.level);
            Assert.AreEqual(432, reloaded.xp);
            Assert.AreEqual(73, reloaded.reputation);
            Assert.AreEqual(41, reloaded.deliveriesCompleted);
            Assert.AreEqual(9500, reloaded.totalFinesPaid);
            Assert.AreEqual(1234567, reloaded.totalDistanceKmX100);
            Assert.AreEqual(12, reloaded.world.day);
            Assert.AreEqual(18.25f, reloaded.world.hourOfDay, 0.001f);
            Assert.AreEqual(0.4f, reloaded.world.fatigue, 0.001f);
            Assert.AreEqual(2, reloaded.settings.steeringMode);
            Assert.AreEqual("ta", reloaded.settings.language);
            Assert.IsTrue(reloaded.settings.manualGearbox);
            Assert.AreEqual(1, reloaded.ownedTrucks.Count);
            Assert.AreEqual("kaveri_k1212", reloaded.ownedTrucks[0].truckDataId);
            Assert.AreEqual(4821f, reloaded.ownedTrucks[0].odometerKm, 0.01f);
        }

        [Test]
        public void ProfileIdSurvivesAReload()
        {
            string id = _save.Data.profileId;

            _save.SaveImmediate();

            Assert.AreEqual(id, TempSave.Reopen().Data.profileId,
                "The job board seed is derived from this; a changing id rerolls every board.");
        }

        [Test]
        public void FileOnDiskIsNotPlainText()
        {
            _save.Data.money = 1234567;
            _save.SaveImmediate();

            var bytes = File.ReadAllBytes(Path.Combine(TempSave.Directory, "tnlorry.sav"));
            string asText = System.Text.Encoding.UTF8.GetString(bytes);

            Assert.IsFalse(asText.Contains("1234567"),
                "A plain-text balance is a one-tap cheat; the file must be encrypted.");
            Assert.IsFalse(asText.Contains("money"));
            Assert.Greater(bytes.Length, 16, "Must carry at least the IV plus a ciphertext block.");
        }

        [Test]
        public void SecondSaveLeavesARecoverableBackup()
        {
            _save.Data.money = 111;
            _save.SaveImmediate();
            _save.Data.money = 222;
            _save.SaveImmediate();

            Assert.IsTrue(File.Exists(Path.Combine(TempSave.Directory, "tnlorry.sav")));
            Assert.IsTrue(File.Exists(Path.Combine(TempSave.Directory, "tnlorry.sav.bak")),
                "The rolling backup is what survives a kill mid-write.");
        }

        [Test]
        public void NoTempFileIsLeftBehindAfterAWrite()
        {
            _save.SaveImmediate();

            Assert.IsFalse(File.Exists(Path.Combine(TempSave.Directory, "tnlorry.sav.tmp")),
                "A leftover temp file means the atomic rename did not complete.");
        }

        [Test]
        public void CorruptMainFileFallsBackToTheBackup()
        {
            _save.Data.money = 555555;
            _save.SaveImmediate();     // writes main
            _save.Data.money = 666666;
            _save.SaveImmediate();     // 555555 becomes the backup, 666666 the main

            // Simulate a half-finished write or a bit-rotted file.
            File.WriteAllBytes(Path.Combine(TempSave.Directory, "tnlorry.sav"),
                               new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

            // SaveSystem logs the read failure on purpose; that diagnostic is the point, so the
            // test must not be failed by it.
            LogAssert.ignoreFailingMessages = true;
            var recovered = TempSave.Reopen().Data;
            LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual(555555, recovered.money,
                "A corrupt main file must cost the player one save, not the whole profile.");
        }

        [Test]
        public void BothFilesCorruptYieldsAFreshProfileRatherThanACrash()
        {
            _save.SaveImmediate();
            _save.SaveImmediate();

            foreach (var name in new[] { "tnlorry.sav", "tnlorry.sav.bak" })
                File.WriteAllBytes(Path.Combine(TempSave.Directory, name), new byte[] { 9, 9, 9 });

            LogAssert.ignoreFailingMessages = true;
            var data = TempSave.Reopen().Data;
            LogAssert.ignoreFailingMessages = false;

            Assert.IsNotNull(data);
            Assert.IsNotEmpty(data.profileId, "Unreadable saves must start a new profile, not hang boot.");
            Assert.AreEqual(1, data.level);
        }

        [Test]
        public void DirtyFlagTracksUnsavedChanges()
        {
            Assert.IsFalse(_save.IsDirty, "A freshly loaded save has nothing pending.");

            _save.MarkDirty();
            Assert.IsTrue(_save.IsDirty);

            _save.SaveImmediate();
            Assert.IsFalse(_save.IsDirty);
        }

        [Test]
        public void SaveAsyncSkipsWritingWhenNothingChanged()
        {
            _save.SaveImmediate();
            var path = Path.Combine(TempSave.Directory, "tnlorry.sav");
            var firstWrite = File.GetLastWriteTimeUtc(path);

            // Not dirty and not forced: should be a no-op rather than churning the flash.
            _save.SaveAsync().GetAwaiter().GetResult();

            Assert.AreEqual(firstWrite, File.GetLastWriteTimeUtc(path));
        }

        [Test]
        public void DeleteLocalStartsACleanProfile()
        {
            string originalId = _save.Data.profileId;
            _save.Data.money = 9999999;
            _save.SaveImmediate();

            _save.DeleteLocal();

            Assert.AreNotEqual(originalId, _save.Data.profileId);
            Assert.AreNotEqual(9999999, _save.Data.money);
            Assert.IsFalse(File.Exists(Path.Combine(TempSave.Directory, "tnlorry.sav")));
        }

        [Test]
        public void MigrationStampsTheCurrentSchemaVersion()
        {
            _save.Data.schemaVersion = 0;

            SaveMigrations.Apply(_save.Data);

            Assert.AreEqual(SaveData.CurrentSchemaVersion, _save.Data.schemaVersion);
        }

        [Test]
        public void ANewerSchemaIsLeftAloneRatherThanDowngraded()
        {
            // This happens for real: cloud sync after the player downgrades the app.
            // Mangling the file would destroy progress made on the newer build.
            _save.Data.schemaVersion = SaveData.CurrentSchemaVersion + 5;
            _save.Data.money = 777;

            SaveMigrations.Apply(_save.Data);

            Assert.AreEqual(777, _save.Data.money);
        }
    }
}
