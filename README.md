# TN Lorry Simulator — Project Scaffold

Unity 6 LTS (URP) Android truck simulator set on Tamil Nadu highways.
Full design and production plan: [`TN_Truck_Simulator_Development_Guide.md`](TN_Truck_Simulator_Development_Guide.md).

This repository currently holds the **code and data layer** — the runtime systems from the
guide's section K onward, the ScriptableObject data model, and a content seeder for the
Phase 1 "Central Corridor" map. Art, audio, scenes and prefabs are not here; see
[`Docs/PROGRESS.md`](Docs/PROGRESS.md) for exactly what exists and what is next.

---

## Getting it running

Nothing here has been compiled — there is no Unity install on the machine it was written on.
Expect to fix small things on first import (missing `.meta` files are generated automatically;
a stray namespace or API drift against your exact Unity version is possible).

### 1. Create the Unity project around these files

```
Unity Hub → New Project → Universal 3D (URP) → Unity 6 LTS
```

Create it **elsewhere**, then copy this repo's `Assets/` and `Packages/manifest.json` over the
new project's, keeping the generated `ProjectSettings/` folder. `ProjectSettings` is deliberately
not checked in: hand-written settings files are the fastest way to get an unopenable project.

Alternatively, open this folder directly in Unity 6 and let it generate `ProjectSettings/` and
`Library/` itself. It will warn about the missing settings on first open; that is expected.

### 2. Install Android Build Support

Unity Hub → Installs → your 6 LTS install → Add Modules → **Android Build Support**
(including OpenJDK and Android SDK & NDK Tools).

### 3. Project settings to apply by hand

| Setting | Value | Why |
|---|---|---|
| Player → Other → Scripting Backend | IL2CPP | Required for 64-bit Play Store builds |
| Player → Other → Target Architectures | ARM64 (and ARMv7 if you still want it) | Play Store requires ARM64 |
| Player → Resolution → Default Orientation | Landscape Left + Right | Driving game |
| Player → Other → Color Space | Linear | URP |
| Player → Other → Graphics APIs | Vulkan, then OpenGLES3 | Vulkan first, GLES3 fallback for old Adrenos |
| Player → Minimum API Level | 28 (Android 9) | Section A.3 device profile |
| Quality | Three levels named Low / Mid / High, in that order | `QualityService` indexes them by position |
| Time → Fixed Timestep | 0.02 | 50 Hz physics; `QualityService` overrides per tier |
| Physics → Default Solver Iterations | 8 | Stable WheelColliders under truck masses |
| Tags | `Pedestrian`, `AIVehicle` | Collision fine detection |
| Layers | `UrbanZone` | Traffic density switching |

### 4. Seed the Phase 1 content

```
TN Lorry → Seed Phase 1 Content
TN Lorry → Validate Phase 1 Content
```

This generates, under `Assets/_Project/Data/`:

- 11 `CityData` assets (Chennai → Madurai, positions derived from real lat/long at ~1:20 scale)
- 12 `CargoData` assets (containers, turmeric, Namakkal eggs, Sivakasi matchboxes, …)
- 6 `TruckData` assets with real diesel torque curves and gear ratios — **all brands fictional**
- `RoadGraph_Phase1_CentralCorridor` (NH48 / NH44 / NH544 + the Karur–Erode link)
- `EconomyConfig` and `GameDatabase`

Re-running updates assets in place by id and never deletes. Tune values in the Inspector,
not in the seeder.

### 5. Scene setup

Scenes are not in the repo (they are binary YAML and would be meaningless without the art).
Build them per section I.3:

**`00_Bootstrap.unity`** — one GameObject with `Bootstrap`. Assign `EconomyConfig`. Nothing else.

**`10_World_Core.unity`**
- `WorldInstaller` — assign `GameDatabase`, the road graph asset, `EconomyConfig`, player truck
- `GameClock`, `FloatingOrigin`, `TileStreamer`, `DayNightCycle`, `WeatherSystem`
- `TrafficManager` + `LaneGraph`
- Player truck prefab (see below)
- A `FloatingOriginExempt` component on the manager root and the UI canvas root

**Player truck prefab** — `Rigidbody` + `TruckController` (axles front-to-rear, mark drive and
steer axles), `FuelSystem`, `DamageSystem`, `TrailerCoupling`, `TruckLights`, `TruckAudio`,
`CameraRig`, `TruckInputRouter`, `TrafficRuleMonitor`, `OdometerTracker`. `FatigueSystem` and
`CompanyTicker` are scene-level, pointed at the truck.

**Roadside props** — each is a trigger collider plus one component, and each one is what makes
a system reachable from the world: `FuelStation` (petrol bunk), `RestPoint` (tea kadai / dhaba /
lodge — also the save point), `SpeedCamera`, `SignalStopLine` (set `laneId` to match the
signal's approach), `Weighbridge` (RTO check-post), `TollPlaza` + `TollLaneTrigger`,
`DeliveryBay`.

**Streamed tiles** — each `Tile_X_Y` scene root gets a `LaneSet` holding that tile's AI lanes.
Lane ids must be globally unique across tiles (`tileIndex * 1000 + localIndex`), because a lane
at a tile edge points into one owned by the next tile.

Order matters in two places and is enforced in code: `FatigueSystem` runs after the input
router (steering drift would otherwise be overwritten), and `FloatingOrigin` runs in
`FixedUpdate` before other physics consumers.

### 6. Run the tests

Window → General → Test Runner → EditMode → Run All. These cover the pure-logic layer (engine,
gearbox, A* routing, payout formula, progression, job board, fleet and loans, drivers, save) and
need no scenes, art or device — so they are the quickest signal that the first compile left the
logic intact.

CI runs the same suite plus an Android build on every push: `.github/workflows/ci.yml`. It needs
a `UNITY_LICENSE` repository secret first (free Personal licence works) — the workflow header
explains how.

### 7. Build an APK and put it on your cheapest phone

That is day 3 of the guide's first-7-days list, and it is the only test that matters for a
mobile sim.

---

## Architecture in one screen

```
Bootstrap (app lifetime)                 WorldInstaller (scene lifetime)
├── QualityService                       ├── GameDatabase
├── SaveSystem (AES, atomic)             ├── RoadGraph      (A* routing)
├── LocalizationService (ta/en)          ├── JobBoardService
├── AudioService                         ├── FleetService   (trucks, loans, repairs)
├── IAnalyticsService  ─┐                └── DriverService  (hired drivers, passive income)
├── IRemoteConfigService ├─ no-op stubs; swap for Firebase without touching call sites
├── IAdService         ─┘                      ticked by CompanyTicker off GameClock
├── EconomyService      (sole owner of the money balance)
└── ProgressionService  (sole owner of XP / level / unlocks)

              everything else talks through EventBus (typed structs, no allocation)
```

Rules the code holds to:

- **One owner per piece of state.** Money only moves through `EconomyService`; XP only through
  `ProgressionService`; the active job only through `JobBoardService`. Nothing else writes those
  save fields.
- **Services are interfaces where an SDK will land.** The project compiles and runs with zero
  plugins installed; ads and analytics fail closed rather than silently succeeding.
- **Data in ScriptableObjects, tunables in `EconomyConfig`, live values through Remote Config.**
- **The event bus is for discrete facts** (a fine was issued, a delivery completed). Continuous
  values are polled — at 30 fps, polling one object beats publishing 60 events a second.

## Packages

`Packages/manifest.json` lists only what the code actually uses. Input System, Addressables,
Localization, Splines, Cinemachine and Timeline were all removed, because none of them is
referenced yet and every pinned version is another chance for package resolution to fail a
build. Add each one back at the point you need it:

| Package | Add it when |
|---|---|
| Addressables | `TileStreamer` outgrows Build Settings scenes (the call site is marked) |
| Input System | you want gamepad support — rewrite the two files in `Scripts/Input/` |
| Localization | you replace the hand-rolled `LocalizationService` |
| Splines | the road-building tool starts generating geometry |
| Cinemachine | `CameraRig` is not enough |

Note that the runtime deliberately uses legacy `UnityEngine.Input`. With Input System absent
that is simply the default; if you add the package back, set Player → Active Input Handling to
**Both**, which `TN Lorry → Apply Android Build Settings` does for you.

## Things deliberately left as decisions

- **Tamil text shaping (section P.2).** TextMeshPro does not shape Indic scripts out of the box.
  `LocalizationService` resolves strings; it does not render them. Prototype a HarfBuzz-based
  TMP plugin on a real device in week 1 before committing to any UI layout.
- **Tamil string table (`strings_ta.json`) is a draft.** It must be read by a native proofreader
  before any build ships. Missing keys fall back to English automatically, so the game is never
  broken by an incomplete table.
- **The AES save key in `SaveSystem` is a placeholder** and is baked into the binary. It stops
  casual save editing and nothing more. Replace it, keep it out of public source control, and
  validate anything that must be trusted (IAP entitlements) server-side.
- **Vehicle physics is pure Unity `WheelCollider`.** No paid asset dependency. If you buy NWH
  or Edy's later, `TruckController` is the only file that changes — `Engine` and `Gearbox` are
  plain C# and portable.
- **Third-party and OSM attribution.** OpenStreetMap data is ODbL; attribution is required in
  the credits screen if you derive road geometry from it (section H / Appendix 1).

## Legal (Appendix 1, non-negotiable)

- Truck brands are **fictional**: Kaveri Motors, Vaigai Trucks, Pothigai Heavy, Mini.
  Never ship a real manufacturer's name, logo or badge shape.
- No film songs or copyrighted music. Commission original tracks with written assignment.
- Landmarks are **generic silhouettes**, not replicas. No identifiable faces on flex banners.
