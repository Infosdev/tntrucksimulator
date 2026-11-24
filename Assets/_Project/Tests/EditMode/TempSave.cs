using System.IO;
using TNLorry.Core;

namespace TNLorry.Tests
{
    /// <summary>
    /// A SaveSystem pointed at a throwaway directory.
    ///
    /// SaveSystem takes its directory by constructor injection precisely so tests never touch
    /// Application.persistentDataPath — a test run must not be able to eat a real save.
    /// </summary>
    public static class TempSave
    {
        static string _directory;

        public static string Directory => _directory;

        public static SaveSystem Create()
        {
            Cleanup();

            _directory = Path.Combine(Path.GetTempPath(), "tnlorry_tests_" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(_directory);

            var save = new SaveSystem(_directory);

            // LoadAsync completes synchronously here: the semaphore is uncontended and the
            // work is plain file IO plus a JsonUtility parse, so there is no deadlock risk.
            save.LoadAsync().GetAwaiter().GetResult();
            return save;
        }

        /// <summary>Reopens the same directory, to test that what was written actually reloads.</summary>
        public static SaveSystem Reopen()
        {
            var save = new SaveSystem(_directory);
            save.LoadAsync().GetAwaiter().GetResult();
            return save;
        }

        public static void Cleanup()
        {
            if (string.IsNullOrEmpty(_directory)) return;

            try
            {
                if (System.IO.Directory.Exists(_directory))
                    System.IO.Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
                // A locked file on Windows is not worth failing a test over; the OS temp
                // directory gets cleaned up eventually.
            }

            _directory = null;
        }
    }
}
