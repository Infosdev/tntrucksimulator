using TNLorry.Core;
using TNLorry.Data;
using TNLorry.Gameplay;
using TNLorry.Navigation;
using TNLorry.Services;
using UnityEngine;

namespace TNLorry.World
{
    /// <summary>
    /// Scene-scoped service installer, lives in 10_World_Core.unity (section I.3).
    ///
    /// Bootstrap owns the services that live for the whole app (save, localization, audio,
    /// economy). The ones installed here depend on world content that only exists once a
    /// gameplay scene is loaded — the road graph, the job board, the content database — so
    /// they are registered on load and torn down on unload rather than leaking across scenes.
    /// </summary>
    [DefaultExecutionOrder(-9000)]
    public class WorldInstaller : MonoBehaviour
    {
        [Header("Content")]
        [SerializeField] GameDatabase database;
        [SerializeField] RoadGraphAsset roadGraphAsset;
        [SerializeField] EconomyConfig economyConfig;
        [Tooltip("Optional until cosmetics exist; the garage is simply empty without it.")]
        [SerializeField] CosmeticCatalogue cosmeticCatalogue;

        [Header("Player")]
        [Tooltip("Spawned player truck. Its saved position is restored here.")]
        [SerializeField] Transform playerTruck;

        public RoadGraph Roads { get; private set; }
        public JobBoardService Jobs { get; private set; }
        public bool IsInstalled { get; private set; }

        SaveSystem _save;

        void Awake()
        {
            if (database == null || roadGraphAsset == null || economyConfig == null)
            {
                Debug.LogError("[WorldInstaller] Database, road graph and economy config must all be " +
                               "assigned; the gameplay scene cannot run without them.", this);
                enabled = false;
                return;
            }

            _save = ServiceLocator.Get<SaveSystem>();
            if (_save?.Data == null)
            {
                Debug.LogError("[WorldInstaller] No save loaded. This scene must be entered through " +
                               "00_Bootstrap, not played directly.", this);
                enabled = false;
                return;
            }

            database.Build();
            ServiceLocator.Register(database);

            Roads = roadGraphAsset.BuildGraph();
            ServiceLocator.Register(Roads);

            var economy = ServiceLocator.Get<EconomyService>();
            var progression = ServiceLocator.Get<ProgressionService>();

            Jobs = new JobBoardService(database, economyConfig, economy, _save, Roads);
            ServiceLocator.Register(Jobs);

            // Company-side services. Both are ticked by CompanyTicker off the game clock.
            ServiceLocator.Register(new FleetService(economyConfig, _save, economy, progression, database));
            ServiceLocator.Register(new DriverService(economyConfig, _save, economy, progression, database));

            // Daily missions and the login streak run on the real-world date, so this one is
            // independent of the game clock.
            ServiceLocator.Register(new DailyService(_save, economy, progression));

            cosmeticCatalogue?.Build();
            ServiceLocator.Register(new CustomizationService(_save, economy, progression, cosmeticCatalogue));

            // A job in progress survives a quit mid-haul (section S.1).
            Jobs.RestoreActiveJob();

            RestorePlayerPosition();

            IsInstalled = true;

            ServiceLocator.Get<IAnalyticsService>().LogEvent("world_loaded",
                ("nodes", Roads.NodeCount), ("edges", Roads.EdgeCount),
                ("has_active_job", Jobs.ActiveJob != null));
        }

        void RestorePlayerPosition()
        {
            if (playerTruck == null) return;

            var world = _save.Data.world;
            var saved = new Vector3(world.playerPosX, world.playerPosY, world.playerPosZ);

            // An all-zero saved position means a fresh profile; leave the truck on its
            // scene-authored spawn point instead of dropping it at the map origin.
            if (saved.sqrMagnitude < 0.01f) return;

            FloatingOrigin.ResetOffset();
            playerTruck.SetPositionAndRotation(saved, Quaternion.Euler(0f, world.playerYaw, 0f));
        }

        void OnDestroy()
        {
            if (!IsInstalled) return;

            WritePlayerPosition();

            // Scene-scoped services must not outlive the scene, or the next load gets a job
            // board pointing at a road graph that has been unloaded.
            ServiceLocator.Unregister<JobBoardService>();
            ServiceLocator.Unregister<FleetService>();
            ServiceLocator.Unregister<DriverService>();
            ServiceLocator.Unregister<DailyService>();
            ServiceLocator.Unregister<CustomizationService>();
            ServiceLocator.Unregister<RoadGraph>();
            ServiceLocator.Unregister<GameDatabase>();

            IsInstalled = false;
        }

        void WritePlayerPosition()
        {
            if (playerTruck == null || _save?.Data == null) return;

            // Absolute, not scene-space: the world has been shifted under the truck any number
            // of times by now (section K.9), and a scene-space position would not reload.
            var absolute = FloatingOrigin.ToAbsolute(playerTruck.position);
            var world = _save.Data.world;

            world.playerPosX = absolute.x;
            world.playerPosY = absolute.y;
            world.playerPosZ = absolute.z;
            world.playerYaw  = playerTruck.eulerAngles.y;

            _save.MarkDirty();
        }
    }
}
