using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace TNLorry.Core
{
    /// <summary>
    /// Local save: JSON + AES-CBC, atomic write, one rolling backup (section S.1).
    ///
    /// The key is baked into the binary, so this is tamper *resistance*, not security —
    /// it stops casual save editing. Anything the economy must actually trust
    /// (IAP entitlements, leaderboard numbers) has to be validated server-side.
    /// </summary>
    public class SaveSystem
    {
        const string FileName       = "tnlorry.sav";
        const string BackupFileName = "tnlorry.sav.bak";
        const string TempFileName   = "tnlorry.sav.tmp";

        // 32 bytes -> AES-256. Replace before shipping and keep it out of public source control.
        static readonly byte[] Key = Encoding.UTF8.GetBytes("TNLorrySim_v1_ChangeThisKey_2026");

        readonly string _dir;
        readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);

        public SaveData Data { get; private set; }
        public bool IsDirty { get; private set; }

        public SaveSystem(string directory = null)
        {
            _dir = directory ?? Application.persistentDataPath;
        }

        string PathMain   => Path.Combine(_dir, FileName);
        string PathBackup => Path.Combine(_dir, BackupFileName);
        string PathTemp   => Path.Combine(_dir, TempFileName);

        public void MarkDirty() => IsDirty = true;

        // -- Load ------------------------------------------------------------

        public async Task<SaveData> LoadAsync()
        {
            await _ioLock.WaitAsync();
            try
            {
                Data = TryRead(PathMain) ?? TryRead(PathBackup) ?? CreateNewProfile();
                SaveMigrations.Apply(Data);
                IsDirty = false;
                return Data;
            }
            finally { _ioLock.Release(); }
        }

        SaveData TryRead(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                var json = Decrypt(File.ReadAllBytes(path));
                var data = JsonUtility.FromJson<SaveData>(json);
                if (data == null || string.IsNullOrEmpty(data.profileId))
                {
                    Debug.LogWarning("[SaveSystem] " + Path.GetFileName(path) + " parsed to an empty profile; ignoring.");
                    return null;
                }
                return data;
            }
            catch (Exception e)
            {
                Debug.LogError("[SaveSystem] Failed to read " + Path.GetFileName(path) + ": " + e.Message);
                return null;
            }
        }

        SaveData CreateNewProfile()
        {
            var now = DateTime.UtcNow.ToString("o");
            var data = new SaveData
            {
                profileId    = Guid.NewGuid().ToString("N"),
                createdUtc   = now,
                lastSavedUtc = now
            };
            // Phase 1 starting unlocks -- the Central Corridor (section D.1).
            data.unlockedCityIds.AddRange(new[] { "chennai", "vellore", "krishnagiri", "salem" });
            data.unlockedCargoIds.AddRange(new[] { "containers", "garments", "turmeric", "steel_coils" });
            return data;
        }

        // -- Save ------------------------------------------------------------

        /// <summary>Writes temp -> backup -> main so a kill mid-write can never leave a torn save.</summary>
        public async Task SaveAsync(bool force = false)
        {
            if (Data == null) return;
            if (!IsDirty && !force) return;

            Data.schemaVersion = SaveData.CurrentSchemaVersion;
            Data.lastSavedUtc  = DateTime.UtcNow.ToString("o");

            var json = JsonUtility.ToJson(Data);

            await _ioLock.WaitAsync();
            try
            {
                var bytes = Encrypt(json);
                await Task.Run(() => WriteAtomic(bytes));
                IsDirty = false;
            }
            catch (Exception e)
            {
                Debug.LogError("[SaveSystem] Save failed: " + e.Message);
            }
            finally { _ioLock.Release(); }
        }

        /// <summary>Blocking variant for OnApplicationPause/Quit, where awaiting is not safe.</summary>
        public void SaveImmediate()
        {
            if (Data == null) return;
            try
            {
                Data.schemaVersion = SaveData.CurrentSchemaVersion;
                Data.lastSavedUtc  = DateTime.UtcNow.ToString("o");
                WriteAtomic(Encrypt(JsonUtility.ToJson(Data)));
                IsDirty = false;
            }
            catch (Exception e)
            {
                Debug.LogError("[SaveSystem] SaveImmediate failed: " + e.Message);
            }
        }

        void WriteAtomic(byte[] bytes)
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllBytes(PathTemp, bytes);

            if (File.Exists(PathMain))
            {
                if (File.Exists(PathBackup)) File.Delete(PathBackup);
                File.Move(PathMain, PathBackup);
            }
            File.Move(PathTemp, PathMain);
        }

        public void DeleteLocal()
        {
            foreach (var p in new[] { PathMain, PathBackup, PathTemp })
                if (File.Exists(p)) File.Delete(p);
            Data = CreateNewProfile();
            IsDirty = true;
        }

        // -- Crypto ----------------------------------------------------------
        // Layout: [16-byte IV][AES-CBC ciphertext]

        static byte[] Encrypt(string plain)
        {
            using (var aes = Aes.Create())
            {
                aes.Key     = Key;
                aes.Mode    = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.GenerateIV();

                var plainBytes = Encoding.UTF8.GetBytes(plain);
                var body = aes.CreateEncryptor().TransformFinalBlock(plainBytes, 0, plainBytes.Length);

                var output = new byte[aes.IV.Length + body.Length];
                Buffer.BlockCopy(aes.IV, 0, output, 0, aes.IV.Length);
                Buffer.BlockCopy(body, 0, output, aes.IV.Length, body.Length);
                return output;
            }
        }

        static string Decrypt(byte[] payload)
        {
            if (payload.Length <= 16) throw new InvalidDataException("Save file too short.");

            using (var aes = Aes.Create())
            {
                aes.Key     = Key;
                aes.Mode    = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                var iv = new byte[16];
                Buffer.BlockCopy(payload, 0, iv, 0, 16);
                aes.IV = iv;

                var plain = aes.CreateDecryptor().TransformFinalBlock(payload, 16, payload.Length - 16);
                return Encoding.UTF8.GetString(plain);
            }
        }
    }

    /// <summary>Forward-only save migrations. Each step upgrades exactly one schema version.</summary>
    public static class SaveMigrations
    {
        public static void Apply(SaveData data)
        {
            if (data == null) return;

            if (data.schemaVersion > SaveData.CurrentSchemaVersion)
            {
                // Save came from a newer build (cloud sync after a downgrade). Do not mangle it.
                Debug.LogWarning("[SaveMigrations] Save schema v" + data.schemaVersion +
                                 " is newer than this build (v" + SaveData.CurrentSchemaVersion + "); loading as-is.");
                return;
            }

            // while (data.schemaVersion < SaveData.CurrentSchemaVersion)
            //     switch (data.schemaVersion) { case 1: MigrateV1ToV2(data); break; }

            data.schemaVersion = SaveData.CurrentSchemaVersion;
        }
    }
}
