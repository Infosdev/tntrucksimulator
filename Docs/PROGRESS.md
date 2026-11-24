# Build progress against the Master Task Checklist

Tracks Appendix 3 of the development guide. Status is honest: **Code** means the system is
written but has never been compiled or run; **Done** means verified in the editor on a device.
Nothing is Done yet — there is no Unity install on the machine this was built on.

Legend: `[ ]` not started · `[~]` code written, unverified · `[x]` verified

---

## Pre-production

- [ ] Finalize game name & check Play Store/trademark availability
- [ ] Write GDD v1 (the guide is the input; per-system specs still needed)
- [ ] Economy spreadsheet & progression curve — **the numbers now live in `EconomyConfig`; build the sheet and paste the result back**
- [ ] Art style guide + mood board
- [ ] Field trip reference photos/videos (NH44 Salem–Madurai, Mettupalayam–Ooty)
- [ ] Prototype: truck physics on low-end phone ← **next action; everything else is blocked on this answer**
- [ ] Prototype: Tamil text in TMP/UI on device ← **next action; blocks all UI layout**
- [ ] Prototype: tile streaming + floating origin over 30 km
- [ ] Choose & buy third-party assets — *currently no paid dependency; `WheelCollider` only*
- [~] Set up Git + LFS, CI, project management board — repo initialised, `.gitignore` and LFS
      `.gitattributes` in place. CI written: `.github/workflows/ci.yml` runs the EditMode tests
      and an Android build via GameCI. **Needs a `UNITY_LICENSE` repo secret before it will
      pass** — see the comment at the top of the workflow. No project board.

## Core tech

- [~] Bootstrap & service architecture — `Core/Bootstrap.cs`, `ServiceLocator`, `EventBus`,
      `World/WorldInstaller.cs`
- [~] Input system (4 steering modes + gamepad) — `Input/TouchInputSource.cs` (wheel, buttons,
      tilt, slider), `KeyboardInputSource`, `TruckInputRouter`. **Uses legacy `Input`; port to the
      Input System package when gamepad support is prioritised — only those two files change**
- [~] Truck controller, engine, gearbox (auto/manual), brakes, retarder — `Vehicle/TruckController.cs`,
      `Engine.cs`, `Gearbox.cs`. Per-axle anti-roll, air-brake lag, speed-sensitive steering,
      load-dependent centre of mass
- [~] Trailer coupling & reverse parking — `Vehicle/TrailerCoupling.cs` (ConfigurableJoint
      fifth wheel, proximity coupling), `Trailer.cs`, `Gameplay/DeliveryBay.cs` (scored parking)
- [~] Cameras (cab, chase, top, trailer), mirrors — `Vehicle/CameraRig.cs`. **Mirrors not done**
      (render textures at low res / low refresh, disabled on Low tier — section O.3)
- [~] Damage model — `Vehicle/DamageSystem.cs`. Per-component by impact location; cargo damage
      from vertical shock, lateral shift and rain through a torn tarp
- [~] World streaming, floating origin, LOD/HLOD — `World/TileStreamer.cs` (forward-biased,
      tier-scaled), `FloatingOrigin.cs` (FixedUpdate, particle-aware). **HLOD/impostors not done**
- [~] Road graph, A* routing, GPS mini-map, voice nav — `Navigation/RoadGraph.cs` (directed,
      binary-heap A*, toll aversion, ghat penalty), `RoadGraphAsset.cs`, `GpsService.cs`
      (distance-banded announcements, reroute on grace timer). **Pre-baked minimap texture not done**
- [~] AI traffic (lanes, IDM, signals, overtaking, pooling) — `Traffic/LaneGraph.cs`,
      `AIVehicle.cs` (IDM car-following, kinematic with physics handover near the player,
      four-state overtaking with a closing-distance safety check and per-class patience),
      `TrafficManager.cs` (pooled, weighted mix, time-of-day density), `TrafficSignal.cs`.
      Overtaking needs `Lane.oncomingLaneId` wired up on authored two-lane roads to do anything
- [~] Day/night, rain, fog, wet roads — `World/DayNightCycle.cs`, `WeatherSystem.cs`
      (grip multiplier on every wheel, AI slowdown, monsoon-weighted rolls, lateral wind).
      **Wet road shader and wiper animation not done** — art-side
- [~] Save/load + cloud sync — `Core/SaveSystem.cs` (AES-CBC, temp→backup→main atomic write,
      forward-only migrations), `SaveData.cs`. **Cloud sync not done** (section S.2, Firebase)

## Gameplay

- [~] Job board & job generator — `Gameplay/JobBoardService.cs`. Seeded per
      (profile, depot, in-game day) so offers cannot be rerolled by relaunching; weighted by
      what each city produces and consumes
- [~] Delivery & parking bays, result screen — `Gameplay/DeliveryBay.cs`.
      **Result screen UI not done** (the itemised `PayoutBreakdown` it needs is ready)
- [~] Fuel, fatigue, time, tolls, fines — `Gameplay/FuelSystem.cs` (burn modelled on rpm,
      throttle, load and gradient), `FatigueSystem.cs`, `GameClock.cs`, `TollPlaza.cs`
      (FASTag vs cash), `TrafficRuleMonitor.cs` (cooldowns, tolerances). Reachable from a scene
      via `FuelStation` (partial fills sized to the balance, hazardous loads refused),
      `RestPoint` (tea kadai / dhaba / lodge, doubles as the save point), `SpeedCamera`,
      `SignalStopLine` and `Weighbridge`; `OdometerTracker` feeds per-truck wear
- [~] XP, levels, unlocks — `Gameplay/ProgressionService.cs`
- [~] Garage, truck purchase, loans — `Gameplay/FleetService.cs`: buy outright or on finance
      (25% down, amortising EMI at the configured monthly rate), repair quotes per component,
      depreciated trade-in, early settlement, and a monthly instalment that goes overdrawn
      rather than being skipped. Ticked by `Gameplay/CompanyTicker.cs` off the game clock.
      **No garage/dealer UI**
- [~] Hire AI drivers (passive income) — `Gameplay/DriverService.cs`: skill-tiered hiring fees,
      one driver per spare truck (never the player's own), trips resolving on the in-game clock
      with a skill-dependent incident rate, projected daily income. **No company-screen UI**
- [ ] Customization (lorry art, horns, lights, accessories) — `OwnedTruckSave` carries cosmetic
      and paint fields; no editor yet
- [~] Daily missions & login rewards — `Gameplay/DailyService.cs`: 7-day login streak that
      wraps while the continuous count keeps climbing, and three deterministic missions per
      real-world day across seven types. Hooked into the delivery flow and `OdometerTracker`.
      **No missions/rewards UI**
- [ ] Tutorial

## Content (Phase 1)

- [ ] Modular kits: highway, town, rural, industrial, toll, petrol bunk, tea kadai
- [~] Map: Chennai–Vellore–Krishnagiri (NH48) — road graph seeded; no geometry
- [~] Map: Krishnagiri–Salem–Namakkal–Karur–Dindigul–Madurai (NH44) — road graph seeded
- [~] Map: Salem–Erode–Coimbatore (NH544) + Tiruppur — road graph seeded
- [~] 4–6 trucks, 4 trailer types, 10+ cargo — 6 `TruckData`, 7 `TrailerType`s, 12 `CargoData`
      seeded as data. **No models**
- [ ] AI vehicle set (10–15 models) — `TrafficManager` mix table is ready to fill
- [ ] Bilingual signboards
- [ ] Engine/horn/ambience SFX, radio tracks, Tamil + English VO — `TruckAudio` expects
      RPM-layered loops; `GpsService` expects `Resources/Audio/VO_<lang>/<key>` clips

## Services & release

- [~] Firebase (Auth, Remote Config, Analytics, Crashlytics, FCM) — interfaces and no-op
      implementations in `Services/ServiceInterfaces.cs`; **no SDK wired**
- [~] AdMob + UMP consent, rewarded/interstitial placements — `IAdService` with placements
      defined and failing closed; **no SDK wired**
- [ ] Unity IAP / Play Billing + server receipt validation
- [~] Quality-tier auto-detection & settings menu — `Services/QualityService.cs` +
      `DeviceTier`. **Settings menu UI not done**
- [~] Localization complete & proofread — `en` table complete for shipped systems; `ta` table
      is a **draft needing a native proofreader**
- [ ] Device QA pass (6–8 devices) + Firebase Test Lab
- [ ] Play Console: closed test, listing, data safety, rating, privacy policy
- [ ] Pre-registration & marketing assets
- [ ] Soft launch → KPI tuning → global launch

---

## Testing (section W)

- [~] EditMode unit tests — `Assets/_Project/Tests/EditMode/`, ~150 assertions over the
      pure-logic systems. Written, never executed (no local Unity):
      - `EngineTests` — rev limiter, idle floor, damage power loss, engine braking vs rpm,
        the diesel torque plateau, clutch-out free revving
      - `GearboxTests` — shift timing and torque interruption, reverse ratio, auto-shift
        up/down thresholds, brake-holds-low-gear on a ghat, and that steady cruise never hunts
      - `RoadGraphTests` — A* optimality, one-way enforcement, faster-but-longer routing,
        toll aversion flipping the route, ghat flags, nearest-edge snapping, a 300-node chain
      - `PayoutTests` — every term of the section C formula, the lateness floor, and that the
        itemised breakdown sums to what the player is actually paid
      - `EconomyBalanceTests` — purchases cannot overdraw, fines can, events fire exactly once
      - `ProgressionTests` — curve monotonicity, multi-level awards, unlock gates, the cap
      - `JobBoardTests` — board determinism per (profile, depot, day), unlock filtering,
        job restore across a reload, stale-content handling
      - `FleetServiceTests` — EMI maths, missed instalments, catching up after a long sleep,
        collateral rules, repair and trade-in
      - `DriverServiceTests` — hiring gates and slots, trip scheduling across midnight, economics
      - `DailyServiceTests` — streak build/break/wrap on an injected clock, mission determinism
        within a day, progress not carrying over, per-type match rules, fractional distance
      - `SaveSystemTests` — field round-trip, encryption at rest, backup recovery from a
        corrupt main file, atomic-write leftovers, migration behaviour
      - `FormattingTests` — Indian lakh/crore grouping, and the FlatJson `\u` decoding that
        Tamil string tables depend on
- [ ] Device QA pass, Firebase Test Lab — needs builds first
- [ ] PlayMode tests — nothing yet; the physics and streaming behaviour needs them

**Bugs already found by writing these:**

- `DriverService.Hire` stored a within-day hour while `TickHour` compared absolute hours, so a
  driver hired in the evening banked their first trip a full day late. Both now go through
  `DriverService.AbsoluteHour`.
- `FleetService.TickDay` zeroed the loan terms before publishing the instalment event, so the
  final payment reported ₹0 to the UI. The amount is now captured first.
- `TrafficRuleMonitor.ReportCollision` used `CompareTag`, which **throws** if the tag has not
  been declared in Project Settings — a missing tag would have crashed on first collision.
  Now a plain string comparison, with an `AIVehicle` component check as a fallback.
- `CurrencyFormat` used the current culture, so a device set to a comma-decimal locale rendered
  "₹45,2K". Pinned to `InvariantCulture`.

## What to do next, in order

1. **Open the project in Unity 6 and make it compile.** It never has. Fix what the console says.
2. **Run the EditMode tests** (Window → General → Test Runner). They are the fastest check that
   the logic layer survived the first compile, and they need no scenes, art or device.
   Alternatively add the `UNITY_LICENSE` secret and let CI run them on every push.
3. **Seed and validate the content** (`TN Lorry → Seed Phase 1 Content`, then Validate).
4. **Build the two week-1 prototypes that can invalidate design decisions:**
   - a truck on `WheelCollider`s on a 2 km test road with a hairpin and a slope, on the cheapest
     phone you have. If the physics does not feel right here, no amount of content saves it.
   - `கொடைக்கானல்` and `தூத்துக்குடி` rendered on that same device. If TMP cannot shape Tamil,
     the entire UI plan changes, and it is cheaper to learn that now than after the HUD is built.
5. **Only then** start the art pipeline and the modular road kit.

## Known gaps in the code as written

- `TileStreamer` expects tile scenes in Build Settings; swap `SceneManager.LoadSceneAsync` for
  `Addressables.LoadSceneAsync` before the APK gets large (the call site is marked).
- Lane ids must be globally unique across streamed tiles (`LaneSet` registers per tile and
  `LaneGraph.AddLanes` rejects duplicates loudly). The map tool has to allocate them as
  `tileIndex * 1000 + localIndex` or traffic cannot cross a tile boundary.
- `TrafficManager.IsPlayerInUrbanArea` requires an `UrbanZone` layer and trigger volumes around
  each depot; without them every road is treated as highway.
- AI overtaking only engages where an authored lane sets `oncomingLaneId`. On a divided
  carriageway it correctly never fires; on a two-lane road that forgets the field, it also
  never fires, and silently.
- `FleetService` and `DriverService` have no UI. The logic, the save fields and the events are
  all in place, so the garage, dealer and company screens are view work against a settled API.
- Hired-driver trips only advance while the game is running (no offline accrual) — a deliberate
  choice against making absence profitable, but worth revisiting against retention data.
- No PlayMode tests. The physics, streaming and floating-origin behaviour are exactly the things
  EditMode tests cannot cover, and they are the riskiest parts of the project.
