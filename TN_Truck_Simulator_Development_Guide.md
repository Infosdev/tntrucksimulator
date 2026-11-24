# 🚛 TN Lorry Simulator — Complete A-to-Z Development Guide
### Android Truck Simulator Game Featuring Tamil Nadu Routes

> **Working title:** *TN Lorry Simulator* (alt: *Namma Lorry*, *Tamil Nadu Truck Driver*, *Highway Raja*)
> **Platform:** Android (primary), iOS (later)
> **Engine:** Unity 6 LTS (URP)
> **Language:** C#
> **Target:** Mid & low-end Android phones (3 GB RAM+), offline-first
> **Document type:** Master development plan — Vision → Design → Tech → Build → Test → Launch → LiveOps

---

## 📑 Table of Contents

- [A. Vision & Concept](#a-vision--concept)
- [B. Market & Competitor Research](#b-market--competitor-research)
- [C. Core Gameplay Loop](#c-core-gameplay-loop)
- [D. Tamil Nadu World Design (Routes & Cities)](#d-tamil-nadu-world-design-routes--cities)
- [E. Cargo & Economy Design](#e-cargo--economy-design)
- [F. Trucks & Vehicle Design](#f-trucks--vehicle-design)
- [G. Game Modes & Progression](#g-game-modes--progression)
- [H. Technology Stack](#h-technology-stack)
- [I. Project Architecture](#i-project-architecture)
- [J. Unity Project Folder Structure](#j-unity-project-folder-structure)
- [K. Core Systems — Implementation](#k-core-systems--implementation)
- [L. Map & Road Building Pipeline](#l-map--road-building-pipeline)
- [M. AI Traffic System](#m-ai-traffic-system)
- [N. Weather, Day/Night & Seasons](#n-weather-daynight--seasons)
- [O. UI / UX & Controls](#o-ui--ux--controls)
- [P. Tamil Language & Localization](#p-tamil-language--localization)
- [Q. Art Production Pipeline](#q-art-production-pipeline)
- [R. Audio & Music](#r-audio--music)
- [S. Save System & Backend](#s-save-system--backend)
- [T. Performance Optimization for Android](#t-performance-optimization-for-android)
- [U. Monetization](#u-monetization)
- [V. Analytics & Crash Reporting](#v-analytics--crash-reporting)
- [W. Testing & QA](#w-testing--qa)
- [X. Build, CI/CD & Play Store Release](#x-build-cicd--play-store-release)
- [Y. Team, Budget & Timeline](#y-team-budget--timeline)
- [Z. Post-Launch, LiveOps & Roadmap](#z-post-launch-liveops--roadmap)
- [Appendix 1: Legal & IP Checklist](#appendix-1-legal--ip-checklist)
- [Appendix 2: Risk Register](#appendix-2-risk-register)
- [Appendix 3: Master Task Checklist](#appendix-3-master-task-checklist)

---

## A. Vision & Concept

### A.1 One-line pitch
> *Drive a decorated Tamil Nadu lorry across real-feel highways — from Chennai Port to Kanyakumari, up the Ooty hairpin bends, through monsoon rain — delivering Tiruppur textiles, Erode turmeric and Sivakasi matchboxes while building your own transport company.*

### A.2 Design pillars
| # | Pillar | Meaning |
|---|--------|---------|
| 1 | **Authentically Tamil** | Recognizable roads, landmarks, signboards in Tamil + English, local cargo, lorry art, tea-kadai stops, Tamil voice/radio |
| 2 | **Satisfying driving** | Weighty truck physics, clear gear feel, good brakes, rewarding ghat-road challenge |
| 3 | **Runs on every phone** | Smooth on ₹10k–15k phones; small download; offline play |
| 4 | **Build an empire** | Grow from one driver to a fleet-owning transport company |
| 5 | **Fair monetization** | No pay-to-win; ads optional; cosmetics & convenience |

### A.3 Target audience
- **Primary:** Tamil-speaking males & females 15–40, TN + Tamil diaspora (Sri Lanka, Malaysia, Singapore, Gulf)
- **Secondary:** Indian simulation fans (Bus Simulator Indonesia / Indian truck sim players)
- **Device profile:** Android 9+, 3–6 GB RAM, Mali/Adreno mid-range GPUs, often patchy 4G

### A.4 Unique Selling Points (USPs)
1. First truck sim **built specifically for Tamil Nadu** roads and culture.
2. **Full Tamil UI + Tamil voice navigation** ("இன்னும் 500 மீட்டரில் இடது பக்கம் திரும்பவும்").
3. **Ghat-road mastery** — Kallatti (Ooty), Kodaikanal, Yercaud, Valparai 40 hairpin bends.
4. **Monsoon gameplay** — NE monsoon rain, waterlogging, slippery roads.
5. **Lorry customization culture** — painted bumpers, "Horn OK Please", nazar dolls, garlands, LED panels, air horns.

---

## B. Market & Competitor Research

| Game | Strength | Gap we fill |
|------|----------|-------------|
| Bus Simulator Indonesia (BUSSID) | Livery mods, local identity | No Tamil Nadu, trucks secondary |
| Truckers of Europe 3 | Strong physics | European roads, heavy on low-end |
| Indian Truck Simulator titles | Indian theme | Generic, poor quality, ad-heavy |
| Euro Truck Simulator 2 (PC) | Gold standard | PC only |

**Research tasks before production:**
- [ ] Play top 10 truck/bus sims on Play Store; note controls, monetization, ratings complaints.
- [ ] Read 1-star reviews of competitors → list of "don'ts" (ad spam, lag, bad steering).
- [ ] Survey 50+ target players (WhatsApp/Instagram/YouTube Tamil gaming communities).
- [ ] Interview 3–5 real lorry drivers (Namakkal is a lorry hub) for authenticity: routes, check-posts, loading, dhaba culture, toll habits.

---

## C. Core Gameplay Loop

```
┌────────────────────────────────────────────────────────────┐
│  1. Pick a job (Job Board at depot / phone app in cab)     │
│  2. Drive to pickup → hitch trailer / load cargo           │
│  3. Navigate route (GPS) — tolls, traffic, weather, ghats  │
│  4. Manage fuel, fatigue, damage, time limit               │
│  5. Deliver → park in bay → get paid + XP                  │
│  6. Spend: upgrades, repairs, new trucks, hire drivers     │
│  7. Unlock new cities / routes / cargo types               │
└──────────────────────────── loop ──────────────────────────┘
```

**Session length target:** 8–20 minutes per delivery (mobile-friendly). Long routes (Chennai→Kanyakumari) are split by fast-travel checkpoints / "sleep at lodge" saves.

**Scoring per delivery:**
```
Payout = BaseRate(cargo) × Distance × Weight factor
       × TimeBonus (on time / late)
       × ConditionFactor (cargo damage %)
       + FuelEfficiencyBonus + TrafficRulesBonus
       − Fines (overspeed, red light, collision, wrong side)
```

---

## D. Tamil Nadu World Design (Routes & Cities)

> ⚠️ **Scale note:** A 1:1 map is impossible on mobile. Use a **compressed scale (~1:15 to 1:25)** — highways keep their character, landmarks and order of towns, but distances shrink. ETS2 uses ~1:19.

### D.1 Phase-wise map rollout

**🟢 Phase 1 (MVP / Launch) — "Central Corridor"**
| Route | Real highway | Key features |
|-------|--------------|--------------|
| Chennai ⇄ Vellore ⇄ Krishnagiri | NH48 | 6-lane expressway, toll plazas, Chennai port traffic |
| Krishnagiri ⇄ Salem ⇄ Namakkal ⇄ Karur ⇄ Dindigul ⇄ Madurai | NH44 | Long straights, lorry hub (Namakkal), flyovers |
| Salem ⇄ Erode ⇄ Coimbatore | NH544 | Industrial belt, Tiruppur link |

**🟡 Phase 2 (Update 1) — "Hills & Delta"**
| Route | Highway | Features |
|-------|---------|----------|
| Coimbatore ⇄ Mettupalayam ⇄ Ooty | NH181 | 36 hairpin bends, fog, low gear challenge |
| Trichy ⇄ Thanjavur ⇄ Nagapattinam | NH83 / NH36 | Cauvery delta, paddy fields, narrow bridges |
| Salem ⇄ Yercaud | State Hwy | Short ghat training route |

**🟠 Phase 3 (Update 2) — "Coast & South"**
| Route | Highway | Features |
|-------|---------|----------|
| Chennai ⇄ Puducherry ⇄ Cuddalore (ECR) | NH32 / ECR | Coastal road, sea views, beach towns |
| Madurai ⇄ Ramanathapuram ⇄ Rameswaram | NH87 | Pamban bridge (iconic!), sea wind |
| Madurai ⇄ Tirunelveli ⇄ Kanyakumari | NH44 | Wind farms (Aralvaimozhi), southern tip |
| Madurai ⇄ Thoothukudi | NH38 | Port, salt pans |

**🔵 Phase 4 — "Extras"**
- Kodaikanal ghat, Valparai 40 hairpins, Hosur industrial, Kanchipuram, Sivakasi, Tirupati border (NH716), Bengaluru border entry.

### D.2 City / Depot list (Phase 1)
| City | Depot type | Signature visual |
|------|-----------|------------------|
| Chennai | Port + Logistics park | Container terminal, Kathipara flyover style, metro pillars |
| Vellore | Warehouse | Fort silhouette in background |
| Krishnagiri | Truck junction | Mango orchards, NH junction |
| Salem | Steel plant + transport nagar | Hills backdrop |
| Namakkal | Lorry body workshop / garage hub | Rows of lorries, body-building sheds |
| Erode | Turmeric market | Yellow sacks, market yard |
| Coimbatore | Engineering/pumps factory | Western Ghats backdrop |
| Tiruppur | Garment export unit | Textile godowns |
| Karur | Textile + bus body | — |
| Dindigul | Lock / leather units | Rock fort |
| Madurai | Wholesale market | Temple gopuram silhouette (generic, not exact replica) |

### D.3 Road "flavour" elements (what makes it feel Tamil Nadu)
- Bilingual green highway signboards (தமிழ் + English)
- Tea kadai / mess / dhaba stops with plastic chairs, banana bunches
- Toll plazas (FASTag lane vs cash lane)
- Speed breakers in towns, cattle crossing, goats on roadside
- Two-wheelers with 3 people, share autos, TNSTC-style buses (fictional livery), mini-lorries, tractors with sugarcane
- Political/wedding flex banners (generic, fictional faces/names only)
- Temple arches on village entry roads, roadside Ayyanar statues
- Coconut groves, paddy fields, palm trees (palmyra — TN state tree)
- Petrol bunks with fictional brands
- RTO / check-post (weighbridge mini-game in Phase 2)

---

## E. Cargo & Economy Design

### E.1 Cargo catalogue
| Cargo | Origin | Type | Special rule |
|-------|--------|------|--------------|
| Containers | Chennai Port, Thoothukudi | Container trailer | Heavy, slow |
| Garments / Cotton yarn | Tiruppur, Karur | Closed body | Rain-sensitive if tarp damaged |
| Turmeric sacks | Erode | Open + tarpaulin | Rain penalty without tarp |
| Matchboxes / fireworks | Sivakasi | Hazardous (ADR-like) | No smoking zones, strict speed cap, banned in tunnels/cities at day |
| Eggs | Namakkal | Fragile | Damage from potholes & hard braking |
| Steel coils / TMT | Salem | Flatbed | High weight, braking distance |
| Motor pumps / machinery | Coimbatore | Flatbed | Oversize at L4 |
| Auto parts | Hosur, Chennai | Closed | Time-critical (JIT) |
| Bananas / vegetables | Theni, Dindigul | Open | Perishable timer |
| Salt | Thoothukudi | Tipper | — |
| Sugarcane | Delta region | Open | Seasonal (Pongal event) |
| Cement | Ariyalur | Bulk | — |
| Fuel / LPG | Refinery | Tanker | Hazardous, slosh physics (Phase 3) |
| Milk | Dairy | Insulated tanker | Perishable |

### E.2 Economy model
```
Currency:   ₹ (in-game Rupees)  — earned by deliveries
Premium:    "Gold" (optional IAP)  — cosmetics, skip timers, extra garage slots
XP:         levels 1–60, unlock cargo/routes/trucks
Reputation: per-company rating → better contracts
```

**Starting state:** Player is a hired driver (company truck, small pay). Level 5 → can take bank loan to buy first truck. Level 10 → own garage in a city. Level 20 → hire AI drivers (passive income).

**Cost sinks:** Fuel (diesel ₹/litre), tolls, repairs, fines, loan EMI, driver salaries, garage rent, upgrades.

**Balancing:** Build the economy in a spreadsheet first (Google Sheets): income per hour at each level vs. cost of next truck. Target: next meaningful purchase every 30–60 min of play early game, 2–4 hrs mid game.

---

## F. Trucks & Vehicle Design

> ⚠️ **Do NOT use real brand names, logos or exact designs** (Ashok Leyland, Tata, BharatBenz, Eicher, Volvo). Create **fictional brands** inspired by Indian truck styles.

### F.1 Fictional brands & models
| Brand (fictional) | Model | Type | Real-world inspiration (style only) |
|-------------------|-------|------|-------------------------------------|
| **Kaveri Motors** | K-1212 | 6-wheel rigid lorry | Classic TN "Namakkal body" lorry |
| Kaveri Motors | K-2518 | 10-wheel | Multi-axle rigid |
| **Vaigai Trucks** | V-Haul 3718 | 12-wheel | Heavy rigid tipper/closed |
| Vaigai Trucks | V-Tractor 4923 | Tractor-trailer | Container haulage |
| **Pothigai Heavy** | PH-55 | Premium cab-over | Modern export-style cab |
| **Mini** | Kutty Lorry | 4-wheel LCV | Small pickup / mini truck (tutorial truck) |

### F.2 Truck specs (data-driven via ScriptableObjects)
```
Engine power (hp), torque curve, gear ratios (5–9 speeds),
max RPM, fuel tank (L), fuel consumption map,
mass (kg), axle count, wheelbase, brake force,
engine brake / exhaust brake, retarder (premium),
suspension stiffness/damping, steering lock angle,
cab model, sound bank, customization slots
```

### F.3 Customization (big revenue + engagement driver)
- **Lorry art paint**: front panel paintings, bumper patterns, side panel art, name panel ("அம்மா துணை", "Mother's Blessing" — player-editable text)
- Horn types: air horn, musical horns
- Lights: LED strips, top lights, side markers
- Accessories: garlands, nazar battu doll, tassels, mud flaps, chrome
- Cab interior: dashboard idols (generic), seat covers, steering cover
- Trailer liveries
- **Livery editor** (Phase 3): layers + stickers, shareable codes

### F.4 Upgrades (gameplay)
Engine tune, gearbox, tyres (grip/wear), brakes, suspension, fuel tank, turbo, retarder.

---

## G. Game Modes & Progression

| Mode | Description | Phase |
|------|-------------|-------|
| **Career** | Main mode: driver → owner → fleet empire | MVP |
| **Quick Job** | Random delivery, no progression stress | MVP |
| **Free Roam** | Explore map, no cargo | MVP |
| **Challenges** | Ghat road time trial, parking challenges, fuel-efficiency runs | v1.1 |
| **Daily / Weekly Missions** | "Deliver 3 cargoes to Madurai", login rewards | MVP |
| **Festival Events** | Pongal (sugarcane hauling), Deepavali (Sivakasi fireworks rush), Karthigai Deepam | LiveOps |
| **Convoy Multiplayer** | 2–8 players drive together | v2.0 |
| **Parking Mini-game** | Reverse trailer into dock | MVP (part of delivery) |

**Tutorial flow (first 10 min — critical for retention):**
1. Cinematic intro: Namakkal garage, father/uncle hands over keys (2D comic panels, cheap to make).
2. Drive "Kutty Lorry" around depot → learn accelerate/brake/steer.
3. Pick up first cargo (eggs!) → teaches gentle driving.
4. Short highway drive with GPS → toll plaza.
5. Park at bay → first payout → buy first cosmetic (free).

---

## H. Technology Stack

| Layer | Choice | Why |
|-------|--------|-----|
| Engine | **Unity 6 LTS** with **URP** | Best Android support, huge asset ecosystem, C# |
| Language | C# | — |
| Vehicle physics | Unity WheelCollider (custom) **or** NWH Vehicle Physics 2 / Edy's Vehicle Physics (paid assets) | Saves months of physics work |
| Road building | EasyRoads3D Pro or Road Architect / custom spline tool (Unity Splines package) | Spline roads, intersections |
| Terrain | Unity Terrain + MicroSplat, or mesh terrain tiles | Mesh tiles are faster on mobile |
| Map data | **OpenStreetMap** (road geometry reference) + **SRTM/Copernicus DEM** (elevation) | Free; ODbL attribution required |
| GIS tooling | QGIS, Blender + BlenderGIS addon | Convert real geography to game tiles |
| 3D modelling | Blender | Free |
| Texturing | Substance Painter or Blender + Krita/GIMP | — |
| UI | Unity UI (uGUI) + TextMeshPro | Mature; Tamil shaping caveat — see section P |
| Localization | Unity Localization package | String tables, Smart Strings |
| Audio | FMOD (free indie tier) or Unity Audio | FMOD = better engine sound layering |
| Async/Addressables | Unity Addressables | Download routes as DLC packs / reduce APK |
| Backend | Firebase (Auth, Firestore, Remote Config, Cloud Messaging) or Unity Gaming Services | Free tier, fast |
| Ads | Google AdMob (+ mediation) / Unity LevelPlay | Rewarded ads |
| IAP | Unity IAP → Google Play Billing | — |
| Analytics | Firebase Analytics / Unity Analytics | — |
| Crashes | Firebase Crashlytics | — |
| Multiplayer (later) | Photon Fusion / Unity Netcode + Relay | — |
| Version control | Git + Git LFS (GitHub/GitLab) or Unity Version Control (Plastic) | Large binary assets |
| CI/CD | GitHub Actions + GameCI, or Unity Build Automation | Automated APK/AAB |
| Project mgmt | Jira / Linear / Trello / Notion | — |

### H.1 Minimum device target
| Tier | Example GPU | RAM | Target FPS | Settings |
|------|-------------|-----|------------|----------|
| Low | Mali-G52 / Adreno 610 | 3 GB | 30 | Low draw distance, no shadows, 0.7 render scale |
| Mid | Mali-G57/G68 / Adreno 618–642 | 4–6 GB | 30–45 | Medium |
| High | Adreno 7xx / Mali-G710+ | 8 GB+ | 60 | High, shadows, post-FX |

---

## I. Project Architecture

### I.1 High-level architecture
```
┌──────────────────────────────────────────────────────────────────┐
│                         GAME BOOTSTRAP                           │
│  (Services init: Save, Localization, Audio, Ads, Analytics, RC)  │
└───────────────┬──────────────────────────────────────────────────┘
                │
   ┌────────────┼──────────────┬───────────────┬─────────────────┐
   ▼            ▼              ▼               ▼                 ▼
┌───────┐  ┌──────────┐  ┌────────────┐  ┌──────────────┐  ┌──────────┐
│ Menu  │  │  World   │  │  Vehicle   │  │  Gameplay    │  │   UI     │
│ Scene │  │ Streamer │  │  System    │  │  Systems     │  │  Layer   │
└───────┘  │ (tiles)  │  │ (physics,  │  │ Jobs, Econ,  │  │ HUD, GPS │
           │ Traffic  │  │  input,    │  │ Fuel, Fatigue│  │ Menus    │
           │ Weather  │  │  damage)   │  │ Fines, XP    │  │          │
           └──────────┘  └────────────┘  └──────────────┘  └──────────┘
                │              │                │               │
                └──────────────┴──── Event Bus ─┴───────────────┘
                                        │
                               ┌────────▼────────┐
                               │  Data Layer      │
                               │ ScriptableObjects│
                               │ Save (JSON+AES)  │
                               │ Cloud Sync       │
                               └──────────────────┘
```

### I.2 Architecture principles
- **Data-driven:** trucks, cargo, cities, routes, prices are **ScriptableObjects** / JSON → designers tweak without code.
- **Event Bus / ScriptableObject events** for decoupling (e.g., `OnDeliveryCompleted`, `OnFineIssued`).
- **Service Locator** (or a lightweight DI like VContainer) for global services.
- **Additive scenes:** `Bootstrap` → `MainMenu` → `World_Core` + streamed tile scenes.
- **Addressables** for every world tile, truck and trailer → load/unload by distance, and remote route DLC packs.
- **Remote Config** for prices, events, ad frequency — tune without app update.

### I.3 Scene list
```
00_Bootstrap.unity
01_MainMenu.unity
02_Garage.unity          (3D customization showroom)
10_World_Core.unity      (player, managers, lighting, traffic manager)
Tiles/Tile_X_Y.unity     (streamed terrain + roads + props)
20_Tutorial.unity
```

---

## J. Unity Project Folder Structure

```
Assets/
├── _Project/
│   ├── Art/
│   │   ├── Models/ (Trucks, Trailers, Props, Buildings, Vegetation)
│   │   ├── Textures/
│   │   ├── Materials/
│   │   ├── Shaders/ (URP ShaderGraph: Vehicle, Road, Terrain, Water, Rain)
│   │   └── VFX/ (Rain, Dust, Exhaust, Headlights)
│   ├── Audio/ (Engine, Horns, Ambience, Radio, VO_Tamil, VO_English, UI)
│   ├── Data/
│   │   ├── Trucks/        (TruckData.asset)
│   │   ├── Cargo/         (CargoData.asset)
│   │   ├── Cities/        (CityData.asset)
│   │   ├── Routes/        (RouteGraph.asset)
│   │   ├── Economy/       (EconomyConfig.asset)
│   │   └── Localization/  (String Tables: ta, en)
│   ├── Prefabs/ (Player, AI, Props, UI)
│   ├── Scenes/
│   ├── Scripts/
│   │   ├── Core/          (Bootstrap, ServiceLocator, EventBus, SaveSystem)
│   │   ├── Vehicle/       (TruckController, Engine, Gearbox, Wheels, Damage, Trailer)
│   │   ├── Input/         (TouchSteering, Tilt, Pedals, Gamepad)
│   │   ├── World/         (TileStreamer, FloatingOrigin, DayNight, Weather)
│   │   ├── Traffic/       (TrafficManager, LaneGraph, AIDriver, Signals)
│   │   ├── Gameplay/      (JobSystem, Economy, Fuel, Fatigue, Fines, Toll, XP)
│   │   ├── Navigation/    (RoadGraph, Pathfinding A*, GPS, VoiceNav)
│   │   ├── UI/            (HUD, Menus, Map, PhoneApp, Popups)
│   │   ├── Services/      (Ads, IAP, Analytics, RemoteConfig, Cloud)
│   │   └── Utils/
│   └── Settings/ (URP assets per quality tier, Input Actions)
├── Plugins/
├── ThirdParty/ (vehicle physics, road tool, etc.)
└── AddressableAssetsData/
```

---

## K. Core Systems — Implementation

### K.1 Truck controller (simplified skeleton)
```csharp
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class TruckController : MonoBehaviour
{
    [SerializeField] TruckData data;              // ScriptableObject
    [SerializeField] WheelCollider[] driveWheels;
    [SerializeField] WheelCollider[] steerWheels;
    [SerializeField] WheelCollider[] allWheels;

    Rigidbody rb;
    Engine engine;
    Gearbox gearbox;

    public float Throttle { get; set; }   // 0..1
    public float Brake    { get; set; }   // 0..1
    public float Steer    { get; set; }   // -1..1
    public bool  HandBrake { get; set; }

    public float SpeedKmh => rb.linearVelocity.magnitude * 3.6f;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.mass = data.massKg;
        rb.centerOfMass = data.centerOfMass;      // keep LOW to avoid rollovers
        engine  = new Engine(data.torqueCurve, data.maxRpm, data.idleRpm);
        gearbox = new Gearbox(data.gearRatios, data.finalDrive, data.autoShift);
    }

    void FixedUpdate()
    {
        float wheelRpm = AverageDriveWheelRpm();
        float engineRpm = gearbox.WheelToEngineRpm(wheelRpm, engine.IdleRpm);
        engine.Update(engineRpm, Throttle);
        gearbox.UpdateAutoShift(engine.Rpm, Throttle, SpeedKmh);

        float wheelTorque = engine.Torque * gearbox.CurrentRatio * data.drivetrainEfficiency;
        float perWheel = wheelTorque / driveWheels.Length;

        foreach (var w in driveWheels) w.motorTorque = perWheel;

        // speed-sensitive steering: trucks steer less at high speed
        float steerAngle = Steer * Mathf.Lerp(data.maxSteerAngle, data.maxSteerAngle * 0.25f, SpeedKmh / 90f);
        foreach (var w in steerWheels) w.steerAngle = steerAngle;

        float brakeTorque = Brake * data.maxBrakeTorque + engine.EngineBrakeTorque(Throttle);
        foreach (var w in allWheels) w.brakeTorque = brakeTorque;
        if (HandBrake) foreach (var w in driveWheels) w.brakeTorque = data.maxBrakeTorque * 2f;

        ApplyDownforceAndAntiRoll();
    }

    float AverageDriveWheelRpm()
    {
        float sum = 0; foreach (var w in driveWheels) sum += w.rpm;
        return sum / driveWheels.Length;
    }

    void ApplyDownforceAndAntiRoll() { /* anti-roll bars per axle + small downforce */ }
}
```

**Physics tips for trucks:**
- Fixed timestep 0.02 (50 Hz) on low-end, 0.0167 on high-end; tune WheelCollider substeps (`ConfigureVehicleSubsteps`).
- Low center of mass + anti-roll bars → stable but still allows rollover on sharp ghat turns at high speed.
- Use **sprung mass per wheel** realistically (truck 8–40 t).
- Trailer: `ConfigurableJoint` / `HingeJoint` at fifth-wheel; trailer has its own WheelColliders.
- Air-brake hiss & delay = feel.

### K.2 Input system (mobile-first)
Offer **4 steering modes** (players are picky):
1. **Steering wheel** on screen (rotate drag) — default
2. **Arrow buttons** (left/right)
3. **Tilt** (accelerometer) with sensitivity slider
4. **Slider** steering
+ Gamepad / Bluetooth controller support (Unity Input System).

Pedals: accelerator, brake, handbrake. Buttons: horn (long-press = air horn), indicators, hazard, headlights, wipers, camera, gear (auto/manual), engine start, retarder.

### K.3 Job system
```csharp
[CreateAssetMenu(menuName = "TN/Cargo")]
public class CargoData : ScriptableObject {
    public string id;
    public LocalizedString displayName;   // Tamil + English
    public float baseRatePerKm;
    public float massKg;
    public CargoFlags flags;              // Fragile, Hazardous, Perishable, RainSensitive, Oversize
    public TrailerType requiredTrailer;
    public int unlockLevel;
}

public class Job {
    public CargoData cargo;
    public CityData from, to;
    public float distanceKm;
    public float timeLimitMinutes;
    public int payout;
    public float damagePercent;
}
```
`JobBoardService` generates 6–10 jobs per depot every in-game day, weighted by city's production/consumption lists, player level and active events (Remote Config).

### K.4 Navigation & GPS
- Build a **road graph** (nodes = intersections, edges = road segments with length, speed limit, toll flag, ghat flag).
- **A\*** pathfinding with cost = time (length / speed) + toll cost weight.
- GPS mini-map: render-texture top camera **or** (cheaper) a pre-baked 2D map texture + drawn route line (`LineRenderer` on UI). Prefer pre-baked for performance.
- **Voice navigation:** pre-recorded Tamil & English clips ("இடது பக்கம் திரும்பவும்", "Turn left in 500 metres") triggered by distance thresholds.

### K.5 Fuel, fatigue & time
- In-game clock: 1 real minute = ~1 in-game hour on highway (configurable).
- **Fuel:** consumption = f(rpm, throttle, load, gear). Refuel at petrol bunks (drive into bay, mini-UI).
- **Fatigue:** after ~10 in-game hours driving → screen blink/vignette, slower reactions. Rest at lodge/dhaba → sleep → save point. (Promotes real-world safe-driving message.)

### K.6 Damage system
- Collision impulses → damage % on engine, cabin, chassis, wheels, trailer, cargo.
- Visual: swap to damaged material/mesh deformation (simple vertex offset on low-end).
- Effects: engine damage → power loss; tyre damage → pulling; cargo damage → payout cut.
- Repair at garages / "call mechanic" (rewarded ad = free tow option).

### K.7 Traffic rules & fines
| Violation | Detection | Fine |
|-----------|-----------|------|
| Overspeed | Speed > zone limit + tolerance at speed cameras | ₹ |
| Red light | Trigger at stop line while signal red | ₹ |
| Wrong side | Lane graph direction check | ₹ |
| Collision with vehicle/pedestrian | Collision event | ₹ + reputation |
| No headlights at night / rain | Light state check | ₹ |
| Overload (Phase 2) | Weighbridge check-post | ₹ |

### K.8 Toll plazas
Approach triggers lane selection; FASTag lane = auto deduct & barrier opens; cash lane = stop, pay animation. Toll costs pulled from route data.

### K.9 Floating origin (MUST for large worlds)
Unity float precision degrades beyond ~5–10 km from origin → jitter. Shift the world back to (0,0,0) when the player exceeds a threshold:
```csharp
public class FloatingOrigin : MonoBehaviour {
    [SerializeField] float threshold = 2000f;
    [SerializeField] Transform player;
    public static System.Action<Vector3> OnShift;
    void LateUpdate() {
        Vector3 p = player.position;
        if (p.magnitude < threshold) return;
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            root.transform.position -= p;
        OnShift?.Invoke(p);    // notify streamer, traffic, particles, trails
    }
}
```
(Apply across all loaded tile scenes; physics must be handled carefully — shift in `FixedUpdate`-safe manner.)

### K.10 World streaming
- World split into **tiles (e.g., 1 km × 1 km)** as Addressable scenes.
- Load radius: 2 tiles ahead in driving direction, 1 behind; unload others.
- Async load with `Addressables.LoadSceneAsync(..., LoadSceneMode.Additive)`.
- Each tile has LOD groups; far tiles use **impostor / low-poly terrain** "HLOD".
- Pre-warm shaders (ShaderVariantCollection) to prevent stutter.

---

## L. Map & Road Building Pipeline

```
 OpenStreetMap extract (Tamil Nadu .osm.pbf)
        │  (filter: motorway, trunk, primary highways + key towns)
        ▼
 QGIS → simplify geometry, export GeoJSON of chosen routes
        │
 DEM (SRTM 30m / Copernicus) → heightmap for corridor
        │
        ▼
 Scale & compress (≈1:20), keep town order + landmarks
        │
        ▼
 Unity: import heightmap → terrain tiles (or Blender mesh tiles)
        │
        ▼
 Spline roads (EasyRoads3D / Unity Splines) following GeoJSON lines
        │  intersections, flyovers, bridges, toll plazas
        ▼
 Lane graph generation (for AI + GPS) from road splines
        │
        ▼
 Set dressing: modular town kits, vegetation scatter (palm, coconut,
 neem, paddy), signboards, tea kadais, petrol bunks, temples
        │
        ▼
 Bake: lighting (lightmaps or baked probes), occlusion, NavMesh (peds)
        │
        ▼
 Split into Addressable tile scenes → streaming test
```

**Tools to build in-house (Editor scripts):**
- GeoJSON → spline importer
- Signboard generator (city name + km → bilingual texture)
- Vegetation/prop scatter brush with density rules (road edge, fields)
- Lane graph auto-builder & visual debugger
- Tile splitter & Addressables labeling tool

**Modular kits to model:**
- Highway: 2/4/6-lane road, median, crash barriers, street lights, flyover, bridge, culvert, toll plaza, rest stop
- Town: shop row (with shutter), tea kadai, lodge, house types (tiled roof, concrete), compound walls, temple arch, bus stand
- Rural: thatched hut, paddy field, coconut grove, well, cattle shed
- Industrial: warehouse, factory, port cranes, container stacks, petrol bunk
- Hills: hairpin road kit, retaining walls, tea estates, fog cards

---

## M. AI Traffic System

### M.1 Vehicle mix (Tamil Nadu feel)
Two-wheelers (lots!), autos, cars, share autos, govt-style buses (fictional), private omni buses, mini-lorries, tractors, lorries, bullock cart (rural, rare), cows/goats (rural roads).

### M.2 Design
- **Lane graph** with waypoints; AI vehicles follow splines (kinematic, NOT full physics — cheap).
- Switch to physics rigidbody only when near player & collision likely.
- Behaviours: follow lane, keep distance (IDM — Intelligent Driver Model), overtaking (on 2-lane roads — very Indian!), honking, stop at signals/tolls, give way.
- **Object pooling**: 30–60 active AI vehicles max (tiered by device).
- Spawn outside camera view, despawn behind.
- Density by time of day & area (city high, highway medium, ghat low).

```csharp
// Intelligent Driver Model acceleration (per AI vehicle)
float IdmAccel(float v, float vLead, float gap, float vDesired) {
    const float a = 1.2f, b = 2.0f, s0 = 3f, T = 1.5f; const int delta = 4;
    float dv = v - vLead;
    float sStar = s0 + Mathf.Max(0, v * T + v * dv / (2 * Mathf.Sqrt(a * b)));
    return a * (1 - Mathf.Pow(v / vDesired, delta) - Mathf.Pow(sStar / Mathf.Max(gap, 0.1f), 2));
}
```

### M.3 Pedestrians (light)
Simple animated crowds at towns/bus stops using GPU instancing / baked animation textures; crossings only at marked zebra spots.

---

## N. Weather, Day/Night & Seasons

| Feature | Implementation |
|---------|----------------|
| Day/night cycle | Directional light rotation + gradient sky + ambient colour; street lights & headlights turn on |
| Rain (monsoon) | Particle rain around camera, wet road shader (smoothness + puddle mask), wiper animation, reduced grip |
| Heavy rain / waterlogging | Puddle decals, splash VFX, low visibility |
| Fog | Ghat routes in morning; URP fog + fog cards |
| Heat haze | Summer midday shimmer (cheap post-FX on high only) |
| Wind | Coastal & Aralvaimozhi — slight lateral force on high trucks |
| Seasons/events | Remote Config toggles: Pongal decorations, Deepavali lights |

Weather drives gameplay: grip coefficient multiplier on WheelColliders, AI speed reduction, rain-sensitive cargo penalties.

---

## O. UI / UX & Controls

### O.1 Screen list
1. Splash → Language select (தமிழ் / English) on first launch
2. Main Menu (Career, Quick Job, Free Roam, Garage, Shop, Settings)
3. Garage / Showroom (3D truck rotate, customize)
4. Job Board (list with map preview, payout, distance, cargo icon)
5. World Map (pinch-zoom TN map, owned garages, routes)
6. In-game HUD
7. In-cab "Phone" app (jobs, map, bank, messages from boss in Tamil)
8. Pause / Settings
9. Delivery Result screen (stars, payout breakdown)
10. Company management (fleet, drivers, loans)
11. Shop (cosmetics, Gold, remove ads)

### O.2 HUD layout (landscape)
```
┌──────────────────────────────────────────────────────────────────┐
│ [₹ 45,200] [Lv 12 ▓▓▓░]        🕒 14:35  ☁ Rain       [⏸ Pause]  │
│                                                                  │
│ ┌──────────┐                                     ┌─────────────┐ │
│ │ MINI MAP │                 (driving view)      │ Speed 54    │ │
│ │  GPS     │                                     │ Limit (60)  │ │
│ └──────────┘                                     │ Gear 6  RPM │ │
│  ⬅ 500m Turn left (Salem)                        │ Fuel ▓▓▓░   │ │
│                                                  └─────────────┘ │
│ [◀][▶] or (Steering)   [Horn][Ind][Lights][Cam]  [BRAKE][ACCEL]  │
└──────────────────────────────────────────────────────────────────┘
```

### O.3 UX rules
- All buttons ≥ 48dp; HUD layout editor (players can move/resize buttons) — a beloved feature in mobile sims.
- Safe-area support for notches.
- Cameras: cab (interior), chase, top, wheel, trailer cam, free orbit.
- Mirrors: cheap render textures at low res/low FPS (update every 2–3 frames), disable on low tier.
- Haptics on gear shift / collision (optional toggle).
- Accessibility: colourblind-safe route line, text size option, subtitles for voice.

---

## P. Tamil Language & Localization

### P.1 Scope
- Languages at launch: **Tamil (ta)** and **English (en)**. Later: Hindi, Malayalam, Telugu, Kannada (expands market).
- Localize: UI strings, signboards, voice nav, boss messages, tutorial, store listing, notifications.

### P.2 ⚠️ Critical: Tamil text rendering
Tamil is a **complex script** (vowel signs ொ ோ ை attach/reorder around consonants). **TextMeshPro does not do complex-script shaping out of the box**, so Tamil may render broken.

**Solutions (pick one, prototype in Week 1):**
1. Use a **HarfBuzz-based shaping plugin / RTL-&-Indic text asset** for TMP (several exist on the Asset Store / GitHub) — test on real devices.
2. Use **Unity UI Toolkit / newer text system** if your Unity version supports advanced text shaping for Indic scripts — verify with Tamil test strings.
3. **Pre-render** fixed texts (signboards, logos) as textures — perfect for road signs anyway.
4. Fallback: native Android `TextView` overlay for long text (complex, last resort).

**Test string set** (must render correctly):
`கொடைக்கானல்`, `தூத்துக்குடி`, `கன்னியாகுமரி`, `சரக்கு வழங்கப்பட்டது!`, `எரிபொருள் குறைவு`, `ஸ்ரீ`, `க்ஷ`

**Fonts:** Use open-licensed fonts (e.g., Noto Sans Tamil / Noto Serif Tamil — SIL OFL; Mukta Malar; Catamaran). Check each font's license.

### P.3 Voice-over
- Hire Tamil voice artists (male + female options for GPS). Record clean WAV, 44.1 kHz.
- Script ~300–500 lines for MVP (navigation, boss, tutorial, radio DJ).
- Dialect: neutral Chennai/standard Tamil for nav; regional flavour (Madurai/Kongu slang) for fun NPC lines.

### P.4 String table example
| Key | en | ta |
|-----|----|----|
| `ui.start_job` | Start Job | வேலையைத் தொடங்கு |
| `hud.fuel_low` | Fuel low! | எரிபொருள் குறைவு! |
| `nav.turn_left_500` | Turn left in 500 metres | 500 மீட்டரில் இடது பக்கம் திரும்பவும் |
| `result.delivered` | Cargo delivered! | சரக்கு வழங்கப்பட்டது! |
| `fine.overspeed` | Overspeeding fine | அதிவேக அபராதம் |

> Get all Tamil text reviewed by a native proofreader — machine translation is not enough.

---

## Q. Art Production Pipeline

### Q.1 Art style
**Stylized-realistic**: realistic proportions, slightly saturated colours (TN is vibrant!), hand-painted feel on lorry art. Avoids "uncanny" low-quality realism and runs better.

### Q.2 Polygon & texture budgets (mobile)
| Asset | LOD0 tris | LOD1 | LOD2 | Texture |
|-------|-----------|------|------|---------|
| Player truck (exterior) | 40–60k | 20k | 6k | 2048 atlas (1024 on low) |
| Player cab interior | 20–30k | — | — | 1024–2048 |
| Trailer | 10–20k | 6k | 2k | 1024 |
| AI car | 3–6k | 1.5k | 400 | 512 shared atlas |
| AI two-wheeler + rider | 2–3k | 800 | impostor | 512 |
| Building (modular) | 500–3k | 300 | impostor | shared trim sheet |
| Tree (palm/coconut) | 1–2k | 400 | billboard | atlas |

### Q.3 Pipeline
```
Reference photos (own field trip along NH44/NH48 + Ooty ghat) 
  → Concept/sketch → Blockout (Blender) → Modelling → UV (atlas/trim sheets)
  → Texture (Substance/Krita) → LODs (Blender decimate or Simplygon/Unity LOD tool)
  → Export FBX (Y-up, scale 1, applied transforms) → Unity import preset
  → Material (URP Lit / custom mobile shader) → Prefab with LODGroup + colliders
```

**Field trip (highly recommended):** Drive/ride NH44 Salem–Madurai & Mettupalayam–Ooty with a GoPro/phone — photograph signboards, lorries, tea shops, road textures, hairpins. Saves months of guesswork and gives authenticity. (Photograph public roads; avoid capturing identifiable faces/trademarks in final textures.)

### Q.4 Lorry art system
- Shader with **decal layers** (front panel, bumper, sides) → swappable textures.
- Commission or create **original** Tamil lorry-art paintings (floral, peacock, sunrise, landscape themes).
- Player name plate: dynamic text rendered to texture.

---

## R. Audio & Music

| Category | Notes |
|----------|-------|
| Engine | Record or license **RPM-layered** loops (idle, low, mid, high, on/off load), crossfade by RPM; diesel character |
| Turbo, air brake hiss, gear clunk, reverse beeper | Essential "truck feel" sounds |
| Horns | Standard, air horn, musical horns (original melodies only) |
| Ambience | Highway wind, rain on cab roof, village (birds, temple bell distant), city (autos, crowd) |
| Radio | In-cab FM with **original royalty-free Tamil-style music** (commission composers — folk, kuthu, melody, devotional-style instrumental) + fictional DJ banter |
| UI | Clicks, payout cash sound, level-up |
| VO | Tamil + English navigation, boss calls, tutorial |

> 🚫 **Never use film songs or copyrighted music** — Content ID claims, Play Store takedown risk. Commission original tracks with written license/assignment.

FMOD setup: `Engine` event with RPM + Load parameters; `Environment` snapshot for tunnel/cab interior muffling.

---

## S. Save System & Backend

### S.1 Local save
- JSON serialized `SaveData` → encrypted (AES) → `Application.persistentDataPath`.
- Autosave: on delivery, on purchase, on app pause (`OnApplicationPause(true)`), every 2 min.
- Keep 2 rotating backups to survive corruption.
- Version field + migration functions for future updates.

```csharp
[System.Serializable]
public class SaveData {
    public int version = 1;
    public long money;
    public int level, xp;
    public List<OwnedTruck> trucks;
    public List<string> unlockedCities;
    public List<HiredDriver> drivers;
    public Job activeJob;
    public Vector3Serializable lastPosition;
    public float inGameTime;
    public Settings settings;
    public string lastSavedUtc;
}
```

### S.2 Cloud (Firebase)
| Feature | Firebase service |
|---------|------------------|
| Login (Google Play Games / Anonymous → link Google) | Auth + Play Games Services |
| Cloud save sync | Firestore or Play Games Saved Games |
| Live tuning, events, ad frequency | Remote Config |
| Push notifications ("Your drivers earned ₹25,000!") | Cloud Messaging |
| Leaderboards (fuel efficiency, delivery count) | Play Games Services |
| Anti-cheat validation for IAP | Cloud Functions (server-side receipt verification) |

**Conflict rule:** newest `lastSavedUtc` wins, but prompt user if cloud save has much higher progress.

---

## T. Performance Optimization for Android

### T.1 Budgets (low tier, 30 FPS)
| Metric | Budget |
|--------|--------|
| Draw calls (SetPass) | < 150–250 |
| Triangles on screen | < 300–500k |
| Texture memory | < 500 MB |
| Total RAM | < 1.2 GB |
| APK/AAB base download | < 150 MB (rest via Play Asset Delivery / Addressables) |
| Frame time | < 33 ms (CPU & GPU) |

### T.2 Techniques checklist
- [ ] URP Mobile renderer, Forward rendering, MSAA 2x or off on low
- [ ] **Quality tiers** auto-detected by GPU/RAM benchmark on first launch + manual override
- [ ] Dynamic render scale (0.6–1.0) based on frame time
- [ ] GPU instancing / SRP Batcher for props & vegetation
- [ ] Texture atlases & trim sheets; ASTC compression
- [ ] LOD Groups + aggressive culling distances; occlusion culling in towns
- [ ] Baked lighting where possible; one realtime directional light; shadows only for player truck on low
- [ ] Mirrors at reduced resolution & frame rate
- [ ] Object pooling for traffic, VFX, audio sources
- [ ] Avoid GC spikes: no LINQ/allocations in Update; cache components
- [ ] Physics: reduce solver iterations for AI; AI kinematic
- [ ] Shader variant stripping; warm-up on load screen
- [ ] Audio: compressed (Vorbis) streams for music, ADPCM for SFX
- [ ] Thermal throttling: offer 30 FPS cap, "battery saver" mode
- [ ] Profile on **real low-end device** every sprint (Unity Profiler + Android GPU Inspector / Snapdragon Profiler / Arm Mobile Studio)

### T.3 Delivery size
- **Android App Bundle (AAB)** required by Play.
- **Play Asset Delivery**: install-time pack (core + Phase 1 route), fast-follow / on-demand packs (Hills, Coast, South).

---

## U. Monetization

### U.1 Model: Free-to-play, fair
| Revenue stream | Details |
|----------------|---------|
| **Rewarded ads** (main) | 2× delivery payout, free repair/tow, skip rest timer, free fuel top-up, daily spin |
| Interstitial ads | Only after delivery result, max 1 per 5–8 min, never during driving. Capped via Remote Config |
| **Remove Ads** IAP | One-time purchase (removes interstitials, keeps optional rewarded with auto-reward option) |
| Cosmetic IAP | Lorry art packs, horns, lights, premium liveries |
| Truck packs | Premium trucks (also earnable by grind — not pay-to-win) |
| Route DLC (optional) | Or keep all routes free & monetize cosmetics — test both |
| Season Pass (later) | Festival-themed cosmetic track |

### U.2 Pricing (India-first)
Use Play Console **local pricing** (₹ tiers like ₹10 / ₹49 / ₹99 / ₹199 / ₹499). Indian players respond well to low-price micro-purchases and UPI support (Play billing supports UPI).

### U.3 Compliance
- Google Play Families policy if targeting under-13 → **avoid**; rate the game 12+ / Teen-appropriate and don't target kids.
- Ad consent (UMP SDK) for GDPR regions; India DPDP Act compliance for personal data.
- Clear IAP descriptions; no loot boxes with paid randomness (or disclose odds).

---

## V. Analytics & Crash Reporting

### V.1 Key events to track
```
tutorial_step (step_id)          job_started (cargo, from, to, distance)
job_completed (payout, damage, time, on_time)     job_failed (reason)
truck_purchased (truck_id, price)                 level_up (level)
fine_issued (type)                                ad_shown / ad_rewarded (placement)
iap_purchase (sku, price)                         settings_changed (steering_mode, quality)
session_start / session_end                       route_unlocked
fps_bucket (avg fps per session, device tier)     crash (auto via Crashlytics)
```

### V.2 KPIs
| KPI | Target |
|-----|--------|
| D1 retention | ≥ 35–40% |
| D7 retention | ≥ 12–15% |
| D30 retention | ≥ 5% |
| Tutorial completion | ≥ 75% |
| Crash-free users | ≥ 99.5% |
| ANR rate | < 0.47% (Play threshold) |
| Avg session length | 15–25 min |
| Rating | ≥ 4.3 ★ |

Use funnels (install → tutorial → first delivery → first truck purchase) to find drop-offs.

---

## W. Testing & QA

| Type | How |
|------|-----|
| Unit tests | Unity Test Framework (EditMode): economy formulas, pathfinding, save/load, job generator |
| Play mode tests | Automated drive along route waypoints (bot driver) to catch streaming holes, falls through terrain |
| Performance tests | Scripted fly-through camera per route, log FPS/memory per tile → CSV |
| Device lab | Minimum 6–8 real devices: low (Redmi A-series / Samsung A0x / Realme C-series), mid (Redmi Note / Galaxy M/A5x), high; plus Firebase Test Lab for wider coverage |
| Localization QA | Native Tamil reviewer checks every screen for broken shaping, overflow, tone |
| Playtests | Weekly internal; monthly external with 10–20 target players (record screen + face, observe confusion) |
| Closed testing | **Play Console closed test** — new personal developer accounts must run a closed test with a minimum number of testers for a set period before production release (check current Play Console requirement) |
| Open beta | 2–4 weeks, collect feedback via in-game form + Discord/WhatsApp group |

**Bug severity:** S1 crash/progress loss → S2 blocker → S3 major → S4 minor/cosmetic. Never ship with S1/S2.

---

## X. Build, CI/CD & Play Store Release

### X.1 Build settings
- Scripting backend: **IL2CPP**, target architecture **ARM64** (+ ARMv7 only if you need very old devices)
- Min API: Android 9 (API 28) suggested; **Target API: whatever Google Play currently requires** (it rises yearly — check Play Console)
- Graphics API: Vulkan + OpenGLES3 fallback (test both; some Mali drivers are buggy on Vulkan)
- Keystore: create upload key, back it up in 2 safe places; enroll in **Play App Signing**
- Output: **AAB** + Play Asset Delivery packs

### X.2 CI/CD pipeline
```
git push (develop) ──► GitHub Actions (GameCI) 
                       ├─ Run EditMode tests
                       ├─ Build Android AAB (dev, IL2CPP)
                       ├─ Upload artifact / Firebase App Distribution (testers)
merge → main ────────► Release build → upload to Play Console internal track (fastlane / Play Developer API)
```
Branching: `main` (release), `develop`, `feature/*`, `hotfix/*`. Use Git LFS for `.fbx .png .psd .wav .tga .exr`.

### X.3 Play Store listing
- [ ] App name (EN) + Tamil localized listing
- [ ] Short & full description in Tamil and English (keywords: truck simulator, lorry, Tamil Nadu, Indian truck, லாரி)
- [ ] Icon 512×512, feature graphic 1024×500
- [ ] 8 screenshots (landscape) per language; show Ooty hairpins, Pamban-style bridge, lorry art, rain
- [ ] Trailer video 30–60s (YouTube)
- [ ] Content rating questionnaire (IARC)
- [ ] Data safety form (Firebase, AdMob, analytics disclosures)
- [ ] Privacy policy URL (hosted page)
- [ ] Ads declaration = Yes
- [ ] Target audience = 13+ (not children)
- [ ] Pre-registration campaign (4–8 weeks before launch, with reward e.g., exclusive lorry art)

### X.4 Launch plan
1. **Soft launch** in limited region (e.g., Sri Lanka / Malaysia or TN-only via staged rollout) → tune KPIs.
2. Staged rollout 10% → 25% → 50% → 100% watching crash/ANR.
3. Marketing: Tamil gaming YouTubers & Instagram reels creators (ghat-road clips go viral), Moj/ShareChat, Reddit r/TamilNadu, r/IndianGaming, Discord community, Pongal/Deepavali timed launch.

---

## Y. Team, Budget & Timeline

### Y.1 Team options
**Option A — Indie/small (3–5 people):**
| Role | Count |
|------|-------|
| Unity developer (lead, gameplay + physics) | 1 |
| Unity developer (world/tools/optimization) | 1 |
| 3D artist (vehicles + environment) | 1–2 |
| Game designer / producer (you) | 1 |
| Freelance: UI artist, sound designer, Tamil VO, composer, QA testers | as needed |

**Option B — Studio (8–12 people):** add tech artist, 2 more environment artists, UI/UX designer, QA lead, backend dev, community manager.

### Y.2 Rough budget (India, Option A, ~14 months) — indicative only
| Item | Estimate (₹) |
|------|--------------|
| Salaries (dev ×2, artist ×2, part-time) | 30–55 lakh |
| Paid Unity assets (vehicle physics, roads, tools) | 0.5–1.5 lakh |
| Unity license (depends on revenue/funding tier — check current Unity terms) | varies |
| Freelance audio, VO, music | 2–5 lakh |
| Devices for testing (6–8 phones) | 1–1.5 lakh |
| Field trip & reference capture | 0.3–0.6 lakh |
| Google Play developer account | one-time fee (≈ $25) |
| Marketing (influencers, ads) | 3–10 lakh |
| Contingency (15%) | — |
| **Total** | **~40–75 lakh** |

> Solo/hobby path is possible: 1 dev + bought asset packs + freelancers, 18–24 months, ₹3–8 lakh out of pocket.

### Y.3 Timeline / milestones (Option A)
| Phase | Duration | Deliverables | Exit criteria |
|-------|----------|--------------|---------------|
| **0. Pre-production** | Month 1–2 | GDD, economy sheet, art style guide, tech prototypes (truck physics, Tamil text, tile streaming, floating origin) | Truck *feels* good on low-end phone; Tamil renders correctly |
| **1. Vertical slice** | Month 3–4 | Salem→Namakkal 1 route section fully polished, 1 truck, 3 cargo, HUD, GPS, traffic, day/night | "This is fun" playtest verdict |
| **2. Alpha** | Month 5–8 | Full Phase-1 map (Chennai–Krishnagiri–Salem–Madurai–Coimbatore), 4 trucks, 10 cargo, career, economy, save, garage customization, rain | Feature complete for MVP |
| **3. Beta** | Month 9–11 | Content polish, optimization, ads/IAP, analytics, localization complete, closed test | Crash-free ≥ 99%, 30 FPS on low tier |
| **4. Soft launch** | Month 12 | Limited release, KPI tuning | D1 ≥ 35% |
| **5. Global launch** | Month 13–14 | Full release + marketing | — |
| **6. LiveOps** | Ongoing | Updates every 6–8 weeks | — |

### Y.4 Sprint cadence
2-week sprints; each sprint ends with a playable Android build on a low-end device + 30-min team playtest.

---

## Z. Post-Launch, LiveOps & Roadmap

### Z.1 Update roadmap
| Version | Content |
|---------|---------|
| v1.0 | Central corridor (NH48/NH44/NH544), 4–6 trucks, career, customization |
| v1.1 (+6 wks) | Ooty & Yercaud ghats, challenges mode, HUD editor improvements |
| v1.2 | Cauvery delta (Trichy–Thanjavur–Nagapattinam), weighbridge check-posts |
| v1.3 | ECR coastal road, Puducherry |
| v1.4 | Madurai–Rameswaram (Pamban-style bridge), Kanyakumari |
| v1.5 | Tankers (slosh physics), hazardous cargo expansion |
| v2.0 | **Convoy multiplayer**, livery editor & sharing, company leaderboards |
| v2.x | Kerala / Karnataka border routes (Bengaluru, Kochi), more Indian languages |

### Z.2 LiveOps calendar (Tamil Nadu festivals)
| Month | Event |
|-------|-------|
| January | **Pongal** — sugarcane & turmeric hauling, kolam lorry art, bonus payouts |
| March–April | Tamil New Year (Puthandu), Chithirai festival Madurai |
| Summer | "Heat wave" challenge, mango season (Krishnagiri) |
| Oct–Dec | **NE monsoon** season event, **Deepavali** (Sivakasi rush), Karthigai Deepam lights |
| Weekly | Weekend double-XP, rotating cargo bounties |

### Z.3 Community
- Discord / WhatsApp channel / YouTube devlogs in Tamil.
- Livery & screenshot contests.
- Public roadmap & feedback board — players love being heard.

---

## Appendix 1: Legal & IP Checklist

- [ ] **No real truck brands/logos** (Ashok Leyland, Tata, BharatBenz, Eicher, Volvo, Scania…) — fictional brands only
- [ ] **No real company logos** (petrol companies, banks, courier firms, TNSTC/SETC govt bus liveries) — fictional equivalents
- [ ] **No real politicians, film stars or party symbols** on banners/posters
- [ ] **No copyrighted film music** — commission original music with written assignment
- [ ] Landmarks: generic/inspired designs; avoid exact replicas of trademarked/protected architecture where unsure
- [ ] **OpenStreetMap ODbL**: include attribution "© OpenStreetMap contributors" in credits
- [ ] DEM data licenses (SRTM public domain / Copernicus terms) noted in credits
- [ ] Fonts: verify licenses (Noto = SIL OFL ✓)
- [ ] Asset Store assets: Standard Unity Asset Store EULA — fine for games, not for redistribution as source
- [ ] Contracts with freelancers: work-for-hire / IP assignment clauses
- [ ] Privacy policy + Data Safety form + India DPDP Act consent flows
- [ ] Register the game name / trademark search before marketing spend
- [ ] Company structure (e.g., Pvt Ltd / LLP) for Play Store payouts & GST — consult a CA

*(This is a checklist, not legal advice — consult an IP lawyer/CA for final decisions.)*

---

## Appendix 2: Risk Register

| Risk | Impact | Likelihood | Mitigation |
|------|--------|-----------|------------|
| Poor performance on low-end phones | High | High | Profile on low device from week 1; quality tiers; strict budgets |
| Tamil text rendering broken | High | Medium | Prototype in pre-production; pre-rendered signboards |
| Truck physics feels bad | High | Medium | Use proven physics asset; weekly feel tests |
| World too big to finish | High | High | Phased map; modular kits; procedural set dressing |
| Large download size | Medium | High | Play Asset Delivery; Addressables; ASTC |
| Copyright/trademark claims | High | Low–Medium | Appendix 1 checklist |
| Low retention | High | Medium | Strong tutorial; early rewards; daily missions; analytics funnels |
| Ad overload → bad reviews | Medium | Medium | Rewarded-first, caps via Remote Config |
| Key person leaves | Medium | Medium | Documentation, code reviews, shared knowledge |
| Scope creep (multiplayer early) | High | High | Multiplayer strictly post-launch v2.0 |

---

## Appendix 3: Master Task Checklist

### 🔹 Pre-production
- [ ] Finalize game name & check Play Store/trademark availability
- [ ] Write GDD v1 (this doc → detailed per-system specs)
- [ ] Economy spreadsheet & progression curve
- [ ] Art style guide + mood board (lorries, NH44, ghats, monsoon)
- [ ] Field trip reference photos/videos
- [ ] Prototype: truck physics on low-end phone
- [ ] Prototype: Tamil text in TMP/UI on device
- [ ] Prototype: tile streaming + floating origin over 30 km
- [ ] Choose & buy third-party assets
- [ ] Set up Git + LFS, CI, project management board

### 🔹 Core tech
- [ ] Bootstrap & service architecture
- [ ] Input system (4 steering modes + gamepad)
- [ ] Truck controller, engine, gearbox (auto/manual), brakes, retarder
- [ ] Trailer coupling & reverse parking
- [ ] Cameras (cab, chase, top, trailer), mirrors
- [ ] Damage model
- [ ] World streaming, floating origin, LOD/HLOD
- [ ] Road graph, A* routing, GPS mini-map, voice nav
- [ ] AI traffic (lanes, IDM, signals, overtaking, pooling)
- [ ] Day/night, rain, fog, wet roads
- [ ] Save/load + cloud sync

### 🔹 Gameplay
- [ ] Job board & job generator
- [ ] Delivery & parking bays, result screen
- [ ] Fuel, fatigue, time, tolls, fines
- [ ] XP, levels, unlocks
- [ ] Garage, truck purchase, loans
- [ ] Hire AI drivers (passive income)
- [ ] Customization (lorry art, horns, lights, accessories)
- [ ] Daily missions & login rewards
- [ ] Tutorial

### 🔹 Content (Phase 1)
- [ ] Modular kits: highway, town, rural, industrial, toll, petrol bunk, tea kadai
- [ ] Map: Chennai–Vellore–Krishnagiri (NH48)
- [ ] Map: Krishnagiri–Salem–Namakkal–Karur–Dindigul–Madurai (NH44)
- [ ] Map: Salem–Erode–Coimbatore (NH544) + Tiruppur
- [ ] 4–6 trucks, 4 trailer types, 10+ cargo
- [ ] AI vehicle set (10–15 models)
- [ ] Bilingual signboards
- [ ] Engine/horn/ambience SFX, radio tracks, Tamil + English VO

### 🔹 Services & release
- [ ] Firebase (Auth, Remote Config, Analytics, Crashlytics, FCM)
- [ ] AdMob + UMP consent, rewarded/interstitial placements
- [ ] Unity IAP / Play Billing + server receipt validation
- [ ] Quality-tier auto-detection & settings menu
- [ ] Localization complete & proofread
- [ ] Device QA pass (6–8 devices) + Firebase Test Lab
- [ ] Play Console: closed test, listing (Tamil + English), data safety, rating, privacy policy
- [ ] Pre-registration & marketing assets (trailer, screenshots)
- [ ] Soft launch → KPI tuning → global launch

---

### ✅ First 7 days — start here
1. Install Unity 6 LTS + Android Build Support; create URP project; set up Git LFS.
2. Build a 2 km test road with a hairpin bend and a slope.
3. Import/prototype a truck with WheelColliders (or physics asset) → build APK → test on your cheapest phone.
4. Put Tamil strings (`கொடைக்கானல்`, `தூத்துக்குடி`) on screen → verify rendering on device.
5. Download an OSM extract of Tamil Nadu → open in QGIS → highlight NH44 Salem–Madurai.
6. Create the economy spreadsheet skeleton (cargo rates, fuel cost, truck prices).
7. Write the 1-page pitch and share with 10 potential players for feedback.

---

*Document version 1.0 — November 2026. Keep this as a living document: update after every milestone review.*
