using System.Collections;
using System.Threading.Tasks;
using TNLorry.Services;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TNLorry.Core
{
    /// <summary>
    /// Single entry point, lives in 00_Bootstrap.unity (section I.3).
    /// Initialises every global service in a deterministic order, then hands off to the menu.
    /// Nothing else in the game may assume a service exists before this has run.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public class Bootstrap : MonoBehaviour
    {
        [Header("Scene flow")]
        [SerializeField] string mainMenuScene = "01_MainMenu";
        [SerializeField] float  minSplashSeconds = 1.0f;

        [Header("Config")]
        [SerializeField] Data.EconomyConfig economyConfig;

        [Header("Autosave")]
        [Tooltip("Seconds between background autosaves while driving.")]
        [SerializeField] float autosaveInterval = 60f;

        public static Bootstrap Instance { get; private set; }
        public bool IsReady { get; private set; }

        SaveSystem _save;
        float _nextAutosave;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            Application.targetFrameRate = 60;                 // overridden per quality tier below
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            QualitySettings.vSyncCount = 0;
        }

        void Start() => StartCoroutine(Boot());

        IEnumerator Boot()
        {
            var startTime = Time.realtimeSinceStartup;

            // 1. Device tier first -- everything else may read it.
            var tier = DeviceTier.Detect();
            ServiceLocator.Register(new QualityService(tier));
            Debug.Log("[Bootstrap] Device tier: " + tier);

            // 2. Save (async file IO + decrypt).
            _save = new SaveSystem();
            var loadTask = _save.LoadAsync();
            while (!loadTask.IsCompleted) yield return null;
            LogIfFaulted(loadTask, "save load");
            ServiceLocator.Register(_save);

            var settings = _save.Data.settings;
            ServiceLocator.Get<QualityService>().Apply(settings.qualityTier);

            // 3. Localization -- must be ready before any UI shows a string.
            var locTask = LocalizationService.CreateAsync(settings.language);
            while (!locTask.IsCompleted) yield return null;
            LogIfFaulted(locTask, "localization init");
            ServiceLocator.Register(locTask.Result);

            // 4. Audio.
            ServiceLocator.Register(new AudioService(settings));

            // 5. Remote-ish services. Each stub is safe to ship: it no-ops without an SDK,
            //    so the game runs offline and in-editor with no plugins installed.
            ServiceLocator.Register<IAnalyticsService>(new NullAnalyticsService());
            ServiceLocator.Register<IRemoteConfigService>(new LocalRemoteConfigService(economyConfig));
            ServiceLocator.Register<IAdService>(new NullAdService());

            // 6. Economy + progression need save data and remote config, so they come last.
            ServiceLocator.Register(new Gameplay.EconomyService(
                economyConfig, _save, ServiceLocator.Get<IRemoteConfigService>()));
            ServiceLocator.Register(new Gameplay.ProgressionService(economyConfig, _save));

            ServiceLocator.Get<IAnalyticsService>().LogEvent("app_open",
                ("tier", tier.ToString()), ("level", _save.Data.level));

            var elapsed = Time.realtimeSinceStartup - startTime;
            if (elapsed < minSplashSeconds) yield return new WaitForSeconds(minSplashSeconds - elapsed);

            IsReady = true;
            _nextAutosave = Time.realtimeSinceStartup + autosaveInterval;

            var op = SceneManager.LoadSceneAsync(mainMenuScene, LoadSceneMode.Single);
            while (!op.isDone) yield return null;
        }

        static void LogIfFaulted(Task task, string what)
        {
            if (task.IsFaulted)
                Debug.LogError("[Bootstrap] " + what + " failed: " + task.Exception?.GetBaseException().Message);
        }

        void Update()
        {
            if (!IsReady || _save == null) return;

            _save.Data.playTimeSeconds += (long)Time.unscaledDeltaTime;

            if (Time.realtimeSinceStartup >= _nextAutosave)
            {
                _nextAutosave = Time.realtimeSinceStartup + autosaveInterval;
                if (_save.IsDirty) _ = _save.SaveAsync();
            }
        }

        // Android can kill the process straight from the background, so pause is the
        // last reliable chance to persist. Write synchronously here.
        void OnApplicationPause(bool paused)
        {
            if (paused && IsReady) _save?.SaveImmediate();
        }

        void OnApplicationQuit()
        {
            if (IsReady) _save?.SaveImmediate();
        }
    }
}
