using System.Collections;
using System.Collections.Generic;
using TNLorry.Core;
using TNLorry.Services;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TNLorry.World
{
    /// <summary>
    /// Streams 1 km world tiles as additive scenes (section K.10). Loads ahead in the driving
    /// direction, keeps one behind, unloads the rest.
    ///
    /// Asymmetric on purpose: at 70 km/h you cover a tile in under a minute, so the radius has
    /// to be biased forward or you spend the budget loading road you have already driven.
    ///
    /// Tile scenes must be named "Tile_X_Y" and be in Build Settings (or registered as
    /// Addressables — see LoadTile for where that swap goes).
    /// </summary>
    public class TileStreamer : MonoBehaviour
    {
        [Header("Grid")]
        [Tooltip("Tile edge length in metres.")]
        [SerializeField] float tileSize = 1000f;
        [SerializeField] string tileScenePrefix = "Tile_";

        [Header("Radius")]
        [Tooltip("Tiles loaded ahead along the direction of travel.")]
        [SerializeField] int lookAheadTiles = 2;
        [Tooltip("Tiles kept behind the player.")]
        [SerializeField] int lookBehindTiles = 1;
        [Tooltip("Tiles loaded either side of the road.")]
        [SerializeField] int lateralTiles = 1;

        [Header("Budget")]
        [Tooltip("Seconds between streaming evaluations. No need to run this every frame.")]
        [SerializeField] float evaluateInterval = 0.5f;
        [Tooltip("Maximum tiles loading at once. More than 1 on a low-end phone causes hitches.")]
        [SerializeField] int maxConcurrentLoads = 1;

        [SerializeField] Transform player;

        public int LoadedTileCount => _loaded.Count;
        public Vector2Int CurrentTile { get; private set; }

        readonly Dictionary<Vector2Int, SceneInstanceState> _loaded = new Dictionary<Vector2Int, SceneInstanceState>();
        readonly Queue<Vector2Int> _loadQueue = new Queue<Vector2Int>();
        readonly HashSet<Vector2Int> _wanted = new HashSet<Vector2Int>();

        int _activeLoads;
        float _evaluateTimer;
        Vector3 _lastPlayerPosition;
        Vector3 _travelDirection = Vector3.forward;

        enum SceneInstanceState { Loading, Loaded, Unloading }

        void Awake()
        {
            if (player == null)
            {
                Debug.LogError("[TileStreamer] No player transform assigned; disabling.", this);
                enabled = false;
                return;
            }

            // The quality tier decides how far we can afford to see.
            if (ServiceLocator.TryGet<QualityService>(out var quality))
            {
                lookAheadTiles = quality.ActiveTier switch
                {
                    DeviceTierLevel.Low  => 1,
                    DeviceTierLevel.Mid  => 2,
                    _                    => 3
                };
                lateralTiles = quality.ActiveTier == DeviceTierLevel.Low ? 0 : 1;
            }

            _lastPlayerPosition = player.position;
        }

        void OnEnable()  => FloatingOrigin.OnShift += OnOriginShift;
        void OnDisable() => FloatingOrigin.OnShift -= OnOriginShift;

        /// <summary>
        /// Tile coordinates are absolute world space, so they must be computed from the
        /// shift-independent position or the grid slides every time the origin moves.
        /// </summary>
        void OnOriginShift(Vector3 offset) => _lastPlayerPosition -= offset;

        void Update()
        {
            _evaluateTimer += Time.deltaTime;
            if (_evaluateTimer < evaluateInterval) return;
            _evaluateTimer = 0f;

            UpdateTravelDirection();
            Evaluate();
            PumpQueue();
        }

        void UpdateTravelDirection()
        {
            var position = player.position;
            var delta = position - _lastPlayerPosition;
            delta.y = 0f;

            // Below a threshold the direction is noise; keep the last known heading so a
            // stationary truck does not thrash the load set.
            if (delta.sqrMagnitude > 4f) _travelDirection = delta.normalized;

            _lastPlayerPosition = position;
        }

        void Evaluate()
        {
            var absolute = FloatingOrigin.ToAbsolute(player.position);
            CurrentTile = WorldToTile(absolute);

            _wanted.Clear();

            // Project the look-ahead box along the direction of travel.
            var forward = new Vector2Int(
                Mathf.RoundToInt(Mathf.Sign(_travelDirection.x) * (Mathf.Abs(_travelDirection.x) > 0.5f ? 1 : 0)),
                Mathf.RoundToInt(Mathf.Sign(_travelDirection.z) * (Mathf.Abs(_travelDirection.z) > 0.5f ? 1 : 0)));

            for (int step = -lookBehindTiles; step <= lookAheadTiles; step++)
            {
                var spine = CurrentTile + forward * step;
                for (int lateral = -lateralTiles; lateral <= lateralTiles; lateral++)
                {
                    // Lateral offset is perpendicular to travel.
                    var side = new Vector2Int(-forward.y, forward.x) * lateral;
                    _wanted.Add(spine + side);
                }
            }

            // Always keep the tile under the player, even if the heading maths says otherwise.
            _wanted.Add(CurrentTile);

            foreach (var tile in _wanted)
            {
                if (_loaded.ContainsKey(tile) || _loadQueue.Contains(tile)) continue;
                _loadQueue.Enqueue(tile);
            }

            // Unload what we no longer want. Copy the keys first — the dictionary is mutated.
            var toUnload = new List<Vector2Int>();
            foreach (var kv in _loaded)
                if (!_wanted.Contains(kv.Key) && kv.Value == SceneInstanceState.Loaded)
                    toUnload.Add(kv.Key);

            foreach (var tile in toUnload) StartCoroutine(UnloadTile(tile));
        }

        void PumpQueue()
        {
            while (_loadQueue.Count > 0 && _activeLoads < maxConcurrentLoads)
            {
                var tile = _loadQueue.Dequeue();
                if (_loaded.ContainsKey(tile)) continue;
                if (!_wanted.Contains(tile)) continue;      // moved on while queued
                StartCoroutine(LoadTile(tile));
            }
        }

        IEnumerator LoadTile(Vector2Int tile)
        {
            string sceneName = SceneNameFor(tile);
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                // Expected for tiles outside the authored map — the grid is rectangular,
                // Tamil Nadu is not. Cache the miss so we do not retry every evaluation.
                _loaded[tile] = SceneInstanceState.Loaded;
                yield break;
            }

            _loaded[tile] = SceneInstanceState.Loading;
            _activeLoads++;

            // Swap point for Addressables: Addressables.LoadSceneAsync(key, Additive).
            var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            op.priority = 0;                  // keep the main thread responsive
            while (!op.isDone) yield return null;

            _activeLoads--;
            _loaded[tile] = SceneInstanceState.Loaded;
            EventBus.Publish(new TileLoadedEvent { X = tile.x, Y = tile.y });
        }

        IEnumerator UnloadTile(Vector2Int tile)
        {
            string sceneName = SceneNameFor(tile);
            var scene = SceneManager.GetSceneByName(sceneName);

            if (!scene.IsValid() || !scene.isLoaded)
            {
                _loaded.Remove(tile);
                yield break;
            }

            _loaded[tile] = SceneInstanceState.Unloading;

            var op = SceneManager.UnloadSceneAsync(scene);
            while (op != null && !op.isDone) yield return null;

            _loaded.Remove(tile);
            EventBus.Publish(new TileUnloadedEvent { X = tile.x, Y = tile.y });
        }

        Vector2Int WorldToTile(Vector3 worldPosition) => new Vector2Int(
            Mathf.FloorToInt(worldPosition.x / tileSize),
            Mathf.FloorToInt(worldPosition.z / tileSize));

        string SceneNameFor(Vector2Int tile) => tileScenePrefix + tile.x + "_" + tile.y;

        /// <summary>Centre of a tile in absolute world space, for the map and for spawn points.</summary>
        public Vector3 TileCentre(Vector2Int tile) => new Vector3(
            (tile.x + 0.5f) * tileSize, 0f, (tile.y + 0.5f) * tileSize);
    }
}
