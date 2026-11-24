using System.Collections.Generic;
using TNLorry.Data;
using TNLorry.Gameplay;
using TNLorry.InputSystem;
using TNLorry.Navigation;
using TNLorry.Traffic;
using TNLorry.Vehicle;
using TNLorry.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TNLorry.EditorTools
{
    /// <summary>
    /// Generates a complete, buildable, playable scene set out of primitives — no art, no
    /// modelling, no audio required.
    ///
    /// Why this exists: the project is all systems and no scenes, so there is nothing to build
    /// into an APK. Rather than wait on the art pipeline, this wires every system together with
    /// cubes and cylinders so the game can be put on a phone *today* and the two questions that
    /// actually matter — does the truck feel right, does Tamil text render — can be answered.
    ///
    /// Run <c>TN Lorry &gt; Build Playable Test Scenes</c> after seeding content. It produces:
    ///   00_Bootstrap.unity   — service init, hands off to the world
    ///   10_World_Core.unity  — ground, a test road with a hairpin and a slope, a primitive
    ///                          6x4 lorry, depot bay, fuel bunk, rest point, traffic, weather
    /// and registers both in Build Settings so File &gt; Build does the right thing.
    ///
    /// Everything it makes is disposable. Once real art lands, delete the generated scenes.
    /// </summary>
    public static class PlayableSceneBuilder
    {
        const string SceneDir   = "Assets/_Project/Scenes";
        const string BootstrapScene = SceneDir + "/00_Bootstrap.unity";
        const string WorldScene     = SceneDir + "/10_World_Core.unity";
        const string DataRoot   = "Assets/_Project/Data";
        const string MaterialDir = "Assets/_Project/Art/Materials/Generated";

        [MenuItem("TN Lorry/Build Playable Test Scenes", priority = 20)]
        public static void BuildAll()
        {
            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataRoot + "/GameDatabase.asset");
            var economy  = AssetDatabase.LoadAssetAtPath<EconomyConfig>(DataRoot + "/Economy/EconomyConfig.asset");
            var roads    = AssetDatabase.LoadAssetAtPath<RoadGraphAsset>(
                               DataRoot + "/Routes/RoadGraph_Phase1_CentralCorridor.asset");

            if (database == null || economy == null || roads == null)
            {
                EditorUtility.DisplayDialog("Seed content first",
                    "Run TN Lorry > Seed Phase 1 Content before building the test scenes.\n\n" +
                    "Missing: " +
                    (database == null ? "GameDatabase " : "") +
                    (economy == null ? "EconomyConfig " : "") +
                    (roads == null ? "RoadGraph" : ""),
                    "OK");
                return;
            }

            EnsureFolder(MaterialDir);

            BuildWorldScene(database, economy, roads);
            BuildBootstrapScene(economy);
            RegisterBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[PlayableSceneBuilder] Built " + BootstrapScene + " and " + WorldScene +
                      ". Both are in Build Settings. File > Build Settings > Android > Build.");
        }

        // ── Bootstrap scene ────────────────────────────────────────────────

        static void BuildBootstrapScene(EconomyConfig economy)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("Bootstrap");
            var bootstrap = root.AddComponent<Bootstrap>();

            var so = new SerializedObject(bootstrap);
            so.FindProperty("economyConfig").objectReferenceValue = economy;
            so.FindProperty("mainMenuScene").stringValue = "10_World_Core";   // skip the menu for now
            so.FindProperty("minSplashSeconds").floatValue = 0.2f;
            so.ApplyModifiedProperties();

            // A camera so the splash frame is not a black screen on device.
            var camera = new GameObject("SplashCamera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.07f, 0.09f, 0.12f);

            EnsureFolder(SceneDir);
            EditorSceneManager.SaveScene(scene, BootstrapScene);
        }

        // ── World scene ────────────────────────────────────────────────────

        static void BuildWorldScene(GameDatabase database, EconomyConfig economy, RoadGraphAsset roads)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var managers = new GameObject("--- Managers ---");
            managers.AddComponent<FloatingOriginExempt>();

            var world = new GameObject("--- World ---");

            BuildGround(world.transform);
            var roadPoints = BuildTestRoad(world.transform);

            var truck = BuildPrimitiveTruck(database);
            truck.transform.position = roadPoints[0] + Vector3.up * 1.2f;

            var clock    = Add<GameClock>(managers, ("config", economy));
            var sun      = BuildSunAndLighting(managers.transform, clock);
            var weather  = BuildWeather(managers, truck);
            var lanes    = managers.AddComponent<LaneGraph>();
            var traffic  = BuildTraffic(managers, truck, lanes);

            Add<FloatingOrigin>(managers, ("player", truck.transform));
            Add<FatigueSystem>(managers, ("config", economy), ("truck", truck.GetComponent<TruckController>()));
            Add<CompanyTicker>(managers, ("clock", clock));

            var installer = managers.AddComponent<WorldInstaller>();
            var installerSo = new SerializedObject(installer);
            installerSo.FindProperty("database").objectReferenceValue = database;
            installerSo.FindProperty("roadGraphAsset").objectReferenceValue = roads;
            installerSo.FindProperty("economyConfig").objectReferenceValue = economy;
            installerSo.FindProperty("playerTruck").objectReferenceValue = truck.transform;
            installerSo.ApplyModifiedProperties();

            BuildRoadsideServices(world.transform, roadPoints, database);

            // Unused locals are intentional: holding the references documents what was wired.
            if (sun == null || weather == null || traffic == null)
                Debug.LogWarning("[PlayableSceneBuilder] Part of the world rig failed to build.");

            EnsureFolder(SceneDir);
            EditorSceneManager.SaveScene(scene, WorldScene);
        }

        static void BuildGround(Transform parent)
        {
            // One big collider rather than terrain: terrain needs authored heightmaps and this
            // is a test bed, not a map.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.SetParent(parent, false);
            ground.transform.localScale = new Vector3(2000f, 1f, 2000f);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.GetComponent<Renderer>().sharedMaterial = SolidMaterial("Ground", new Color(0.33f, 0.36f, 0.28f));
        }

        /// <summary>
        /// A 2 km test road with a hairpin and a slope — exactly the week-1 prototype the guide
        /// asks for, because those two features are where truck physics either works or does not.
        /// </summary>
        static List<Vector3> BuildTestRoad(Transform parent)
        {
            var points = new List<Vector3>();

            // Straight away from the start, so there is room to get up to speed.
            for (int i = 0; i <= 20; i++) points.Add(new Vector3(0f, 0f, i * 30f));

            // A climbing hairpin: 180 degrees over a 40 m radius, rising 25 m.
            var centre = new Vector3(-40f, 0f, 600f);
            for (int i = 1; i <= 18; i++)
            {
                float angle = i * 10f * Mathf.Deg2Rad;
                points.Add(centre + new Vector3(Mathf.Cos(angle) * 40f, i * 25f / 18f, Mathf.Sin(angle) * 40f));
            }

            // A long descending straight back, to test engine braking and the retarder.
            var last = points[points.Count - 1];
            for (int i = 1; i <= 20; i++)
                points.Add(last + new Vector3(0f, -25f * i / 20f, -i * 30f));

            var road = new GameObject("TestRoad");
            road.transform.SetParent(parent, false);
            var material = SolidMaterial("Road", new Color(0.16f, 0.16f, 0.17f));

            for (int i = 1; i < points.Count; i++)
            {
                var a = points[i - 1];
                var b = points[i];

                var segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
                segment.name = "Segment_" + i;
                segment.transform.SetParent(road.transform, false);
                segment.transform.position = (a + b) * 0.5f;
                segment.transform.rotation = Quaternion.LookRotation((b - a).normalized, Vector3.up);
                segment.transform.localScale = new Vector3(9f, 0.4f, Vector3.Distance(a, b) + 0.5f);
                segment.GetComponent<Renderer>().sharedMaterial = material;
            }

            return points;
        }

        static Light BuildSunAndLighting(Transform parent, GameClock clock)
        {
            var sunObject = new GameObject("Sun");
            sunObject.transform.SetParent(parent, false);

            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Soft;
            sunObject.transform.rotation = Quaternion.Euler(50f, 30f, 0f);

            var cycle = parent.gameObject.AddComponent<DayNightCycle>();
            var so = new SerializedObject(cycle);
            so.FindProperty("sun").objectReferenceValue = sun;

            // Gradients cannot be left empty or the sky goes black at runtime.
            SetGradient(so.FindProperty("sunColorOverDay"),
                        new Color(0.1f, 0.12f, 0.2f), new Color(1f, 0.95f, 0.85f));
            SetGradient(so.FindProperty("ambientColorOverDay"),
                        new Color(0.05f, 0.06f, 0.1f), new Color(0.55f, 0.58f, 0.62f));
            SetGradient(so.FindProperty("fogColorOverDay"),
                        new Color(0.08f, 0.09f, 0.12f), new Color(0.7f, 0.74f, 0.8f));
            so.ApplyModifiedProperties();

            return sun;
        }

        /// <summary>Night-to-noon-to-night ramp, so the generated cycle looks deliberate.</summary>
        static void SetGradient(SerializedProperty property, Color night, Color day)
        {
            if (property == null) return;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(night, 0f),
                    new GradientColorKey(day,   0.5f),
                    new GradientColorKey(night, 1f)
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });

            property.gradientValue = gradient;
        }

        static WeatherSystem BuildWeather(GameObject managers, GameObject truck)
        {
            return Add<WeatherSystem>(managers, ("playerTruck", truck.GetComponent<TruckController>()));
        }

        static TrafficManager BuildTraffic(GameObject managers, GameObject truck, LaneGraph lanes)
        {
            // No AI vehicle prefabs exist yet, so the manager is wired but its mix is empty —
            // it will log that nothing can spawn, which is correct and harmless.
            return Add<TrafficManager>(managers,
                ("player", truck.transform),
                ("laneGraph", lanes));
        }

        // ── Primitive truck ────────────────────────────────────────────────

        /// <summary>
        /// A box-and-cylinders 6x4 lorry with real WheelColliders and the full component stack.
        /// It looks like nothing, but it drives with the same physics the shipping truck will.
        /// </summary>
        static GameObject BuildPrimitiveTruck(GameDatabase database)
        {
            var truckData = database.GetTruck("kaveri_k1212") ??
                            (database.trucks.Length > 0 ? database.trucks[0] : null);

            if (truckData == null)
            {
                Debug.LogError("[PlayableSceneBuilder] No TruckData in the database; cannot build a truck.");
                return new GameObject("PlayerTruck (no data)");
            }

            var root = new GameObject("PlayerTruck");

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(2.5f, 2.2f, 8f);
            body.transform.localPosition = new Vector3(0f, 1.3f, 0f);
            body.GetComponent<Renderer>().sharedMaterial = SolidMaterial("TruckBody", new Color(0.11f, 0.42f, 0.65f));
            // The box collider would fight the WheelColliders; the body is visual only.
            Object.DestroyImmediate(body.GetComponent<BoxCollider>());

            var cab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cab.name = "Cab";
            cab.transform.SetParent(root.transform, false);
            cab.transform.localScale = new Vector3(2.5f, 1.4f, 2.2f);
            cab.transform.localPosition = new Vector3(0f, 2.8f, 2.6f);
            cab.GetComponent<Renderer>().sharedMaterial = SolidMaterial("TruckCab", new Color(0.85f, 0.78f, 0.2f));
            Object.DestroyImmediate(cab.GetComponent<BoxCollider>());

            // The one collider the physics actually uses.
            var hull = root.AddComponent<BoxCollider>();
            hull.center = new Vector3(0f, 1.4f, 0f);
            hull.size = new Vector3(2.4f, 2f, 7.8f);

            var rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.mass = truckData.massKg;
            rigidbody.centerOfMass = truckData.centerOfMass;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // Three axles: steered front, two driven rear — a 6x4.
            var axlePositions = new[] { 2.8f, -2.0f, -3.2f };
            var wheels = new List<WheelCollider>();
            var meshes = new List<Transform>();

            for (int axle = 0; axle < axlePositions.Length; axle++)
            {
                for (int side = 0; side < 2; side++)
                {
                    float x = side == 0 ? -1.1f : 1.1f;
                    var position = new Vector3(x, 0.55f, axlePositions[axle]);

                    var wheelObject = new GameObject("Wheel_" + axle + "_" + (side == 0 ? "L" : "R"));
                    wheelObject.transform.SetParent(root.transform, false);
                    wheelObject.transform.localPosition = position;

                    var collider = wheelObject.AddComponent<WheelCollider>();
                    collider.radius = 0.55f;
                    collider.suspensionDistance = 0.25f;
                    collider.wheelDampingRate = 1f;
                    collider.forceAppPointDistance = 0.1f;

                    // Sprung mass must reflect a loaded truck. Unity's default assumes a car,
                    // and the suspension bottoms out instantly under 9 tonnes.
                    collider.mass = 120f;
                    var spring = collider.suspensionSpring;
                    spring.spring = 160000f;
                    spring.damper = 9000f;
                    spring.targetPosition = 0.4f;
                    collider.suspensionSpring = spring;

                    var forward = collider.forwardFriction;
                    forward.extremumSlip = 0.4f; forward.extremumValue = 1f;
                    forward.asymptoteSlip = 0.8f; forward.asymptoteValue = 0.75f;
                    forward.stiffness = 1.6f;
                    collider.forwardFriction = forward;

                    var sideways = collider.sidewaysFriction;
                    sideways.extremumSlip = 0.3f; sideways.extremumValue = 1f;
                    sideways.asymptoteSlip = 0.6f; sideways.asymptoteValue = 0.8f;
                    sideways.stiffness = 1.8f;
                    collider.sidewaysFriction = sideways;

                    wheels.Add(collider);

                    var mesh = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    mesh.name = wheelObject.name + "_Mesh";
                    mesh.transform.SetParent(root.transform, false);
                    mesh.transform.localScale = new Vector3(1.1f, 0.18f, 1.1f);
                    mesh.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    mesh.GetComponent<Renderer>().sharedMaterial =
                        SolidMaterial("Tyre", new Color(0.09f, 0.09f, 0.1f));
                    Object.DestroyImmediate(mesh.GetComponent<CapsuleCollider>());

                    meshes.Add(mesh.transform);
                }
            }

            WireTruckController(root, truckData, wheels, meshes);

            var fuel = root.AddComponent<FuelSystem>();
            root.AddComponent<DamageSystem>();
            root.AddComponent<OdometerTracker>();

            var rules = root.AddComponent<TrafficRuleMonitor>();
            var rulesSo = new SerializedObject(rules);
            rulesSo.FindProperty("truck").objectReferenceValue = root.GetComponent<TruckController>();
            rulesSo.ApplyModifiedProperties();

            var camera = BuildCameraRig(root);
            var touch = WireInput(root, camera);

            // On-screen controls, so the build is playable on a phone and not just with a
            // keyboard or a Bluetooth pad.
            TouchHudBuilder.Build(touch, root.GetComponent<TruckController>(), fuel, rules,
                                  root.GetComponent<TruckLights>(), camera);

            if (fuel == null) Debug.LogWarning("[PlayableSceneBuilder] FuelSystem failed to attach.");
            return root;
        }

        static void WireTruckController(GameObject root, TruckData data,
                                        List<WheelCollider> wheels, List<Transform> meshes)
        {
            var controller = root.AddComponent<TruckController>();
            var so = new SerializedObject(controller);

            so.FindProperty("data").objectReferenceValue = data;

            var axles = so.FindProperty("axles");
            axles.arraySize = 3;

            for (int i = 0; i < 3; i++)
            {
                var axle = axles.GetArrayElementAtIndex(i);
                axle.FindPropertyRelative("left").objectReferenceValue = wheels[i * 2];
                axle.FindPropertyRelative("right").objectReferenceValue = wheels[i * 2 + 1];
                axle.FindPropertyRelative("isSteer").boolValue = i == 0;
                axle.FindPropertyRelative("isDrive").boolValue = i > 0;
                // Rear bias: braking all axles equally on a rigid lorry locks the fronts first.
                axle.FindPropertyRelative("brakeBias").floatValue = i == 0 ? 0.7f : 1f;
                axle.FindPropertyRelative("isLiftable").boolValue = false;
            }

            var meshArray = so.FindProperty("wheelMeshes");
            meshArray.arraySize = meshes.Count;
            for (int i = 0; i < meshes.Count; i++)
                meshArray.GetArrayElementAtIndex(i).objectReferenceValue = meshes[i];

            so.ApplyModifiedProperties();
        }

        static CameraRig BuildCameraRig(GameObject truck)
        {
            var rigObject = new GameObject("CameraRig");
            rigObject.transform.SetParent(truck.transform, false);

            var cameraObject = new GameObject("MainCamera");
            cameraObject.transform.SetParent(rigObject.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.farClipPlane = 800f;
            cameraObject.AddComponent<AudioListener>();

            var rig = rigObject.AddComponent<CameraRig>();
            var so = new SerializedObject(rig);
            so.FindProperty("targetCamera").objectReferenceValue = camera;
            so.FindProperty("truck").objectReferenceValue = truck.GetComponent<TruckController>();

            var views = so.FindProperty("views");
            views.arraySize = 2;

            var chase = views.GetArrayElementAtIndex(0);
            chase.FindPropertyRelative("view").enumValueIndex = (int)CameraView.Chase;
            chase.FindPropertyRelative("anchor").objectReferenceValue = truck.transform;
            chase.FindPropertyRelative("localOffset").vector3Value = new Vector3(0f, 4.5f, -11f);
            chase.FindPropertyRelative("localEuler").vector3Value = new Vector3(8f, 0f, 0f);
            chase.FindPropertyRelative("fieldOfView").floatValue = 62f;
            chase.FindPropertyRelative("followLag").floatValue = 0.25f;
            chase.FindPropertyRelative("steerLookDegrees").floatValue = 4f;

            var cab = views.GetArrayElementAtIndex(1);
            cab.FindPropertyRelative("view").enumValueIndex = (int)CameraView.Cab;
            cab.FindPropertyRelative("anchor").objectReferenceValue = truck.transform;
            cab.FindPropertyRelative("localOffset").vector3Value = new Vector3(-0.75f, 3.1f, 2.4f);
            cab.FindPropertyRelative("localEuler").vector3Value = Vector3.zero;
            cab.FindPropertyRelative("fieldOfView").floatValue = 68f;
            cab.FindPropertyRelative("followLag").floatValue = 0f;    // cab views must be rigid

            so.ApplyModifiedProperties();
            return rig;
        }

        /// <summary>Returns the touch source so the HUD builder can wire its buttons to it.</summary>
        static TouchInputSource WireInput(GameObject truck, CameraRig camera)
        {
            var keyboard = truck.AddComponent<KeyboardInputSource>();
            var keyboardSo = new SerializedObject(keyboard);
            // On a phone with no HUD built yet, the keyboard source is the only way to drive,
            // so allow it on device too. Turn this off once touch controls exist.
            keyboardSo.FindProperty("enableOnDevice").boolValue = true;
            keyboardSo.ApplyModifiedProperties();

            var touch = truck.AddComponent<TouchInputSource>();

            var router = truck.AddComponent<TruckInputRouter>();
            var so = new SerializedObject(router);

            var sources = so.FindProperty("sourceBehaviours");
            sources.arraySize = 2;
            sources.GetArrayElementAtIndex(0).objectReferenceValue = keyboard;
            sources.GetArrayElementAtIndex(1).objectReferenceValue = touch;

            so.FindProperty("cameraRig").objectReferenceValue = camera;
            so.ApplyModifiedProperties();

            return touch;
        }

        // ── Roadside services ──────────────────────────────────────────────

        static void BuildRoadsideServices(Transform parent, List<Vector3> roadPoints, GameDatabase database)
        {
            var services = new GameObject("--- Services ---");
            services.transform.SetParent(parent, false);

            // Fuel bunk, a short way along the straight.
            var bunk = BuildTriggerPad(services.transform, "FuelStation",
                                       roadPoints[5] + new Vector3(10f, 0f, 0f),
                                       new Vector3(8f, 4f, 14f), new Color(0.9f, 0.5f, 0.1f));
            bunk.AddComponent<FuelStation>();

            // Rest point, a dhaba further along.
            var dhaba = BuildTriggerPad(services.transform, "RestPoint",
                                        roadPoints[12] + new Vector3(-11f, 0f, 0f),
                                        new Vector3(8f, 4f, 10f), new Color(0.2f, 0.7f, 0.35f));
            dhaba.AddComponent<RestPoint>();

            // Speed camera across the straight.
            var cameraPad = BuildTriggerPad(services.transform, "SpeedCamera",
                                            roadPoints[9], new Vector3(10f, 5f, 1.5f),
                                            new Color(0.9f, 0.2f, 0.2f));
            cameraPad.AddComponent<SpeedCamera>();

            // Delivery bay at the far end of the loop, tied to Chennai so the seeded jobs that
            // start there can actually be completed in this test scene.
            var bayPosition = roadPoints[roadPoints.Count - 2] + new Vector3(14f, 0f, 0f);
            var bay = BuildTriggerPad(services.transform, "DeliveryBay", bayPosition,
                                      new Vector3(12f, 5f, 18f), new Color(0.25f, 0.5f, 0.9f));

            var centre = new GameObject("BayCentre");
            centre.transform.SetParent(bay.transform, false);
            centre.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

            var deliveryBay = bay.AddComponent<DeliveryBay>();
            var so = new SerializedObject(deliveryBay);
            so.FindProperty("cityId").stringValue = database.cities.Length > 0 ? database.cities[0].id : "chennai";
            so.FindProperty("bayVolume").objectReferenceValue = bay.GetComponent<Collider>();
            so.FindProperty("bayCentre").objectReferenceValue = centre.transform;
            so.ApplyModifiedProperties();
        }

        static GameObject BuildTriggerPad(Transform parent, string name, Vector3 position,
                                          Vector3 size, Color color)
        {
            var pad = new GameObject(name);
            pad.transform.SetParent(parent, false);
            pad.transform.position = position + Vector3.up * (size.y * 0.5f);

            var collider = pad.AddComponent<BoxCollider>();
            collider.size = size;
            collider.isTrigger = true;

            // A visible marker, since there is no art to indicate what this is.
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "Marker";
            marker.transform.SetParent(pad.transform, false);
            marker.transform.localScale = new Vector3(size.x, 0.2f, size.z);
            marker.transform.localPosition = new Vector3(0f, -size.y * 0.5f + 0.1f, 0f);
            marker.GetComponent<Renderer>().sharedMaterial = SolidMaterial(name + "Pad", color);
            Object.DestroyImmediate(marker.GetComponent<BoxCollider>());

            return pad;
        }

        // ── Helpers ────────────────────────────────────────────────────────

        /// <summary>
        /// Adds a component and sets private [SerializeField] fields by name.
        ///
        /// SerializedObject is the only legitimate way to do this from an editor script —
        /// the fields are private by design, and making them public just to let a generator
        /// touch them would weaken the runtime API permanently.
        /// </summary>
        static T Add<T>(GameObject target, params (string field, Object value)[] assignments)
            where T : Component
        {
            var component = target.AddComponent<T>();
            if (assignments == null || assignments.Length == 0) return component;

            var so = new SerializedObject(component);
            foreach (var (field, value) in assignments)
            {
                var property = so.FindProperty(field);
                if (property == null)
                {
                    Debug.LogWarning("[PlayableSceneBuilder] " + typeof(T).Name +
                                     " has no serialized field '" + field + "'; skipping.");
                    continue;
                }
                property.objectReferenceValue = value;
            }
            so.ApplyModifiedProperties();
            return component;
        }

        static readonly Dictionary<string, Material> MaterialCache = new Dictionary<string, Material>();

        /// <summary>
        /// A flat URP Lit material, cached as an asset so the generated scenes do not each
        /// carry their own copies.
        /// </summary>
        static Material SolidMaterial(string name, Color color)
        {
            if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;

            string path = MaterialDir + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                MaterialCache[name] = existing;
                return existing;
            }

            // URP Lit when the package is present, the built-in fallback when it is not, so
            // this works whether or not URP has finished importing.
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name };

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.15f);

            AssetDatabase.CreateAsset(material, path);
            MaterialCache[name] = material;
            return material;
        }

        static void EnsureFolder(string path)
        {
            if (!System.IO.Directory.Exists(path))
            {
                System.IO.Directory.CreateDirectory(path);
                AssetDatabase.Refresh();
            }
        }

        /// <summary>
        /// Sets Active Input Handling to "Both".
        ///
        /// Required, not cosmetic: the Input System package is in the manifest, but the runtime
        /// reads <c>UnityEngine.Input</c> (KeyboardInputSource, and TouchInputSource's tilt),
        /// and the generated HUD uses StandaloneInputModule. With handling set to the new
        /// system only, every one of those goes silently dead.
        ///
        /// There is no public API for this, so it goes through the serialised project settings.
        /// Unity needs a restart to pick it up.
        /// </summary>
        static void EnableLegacyInput()
        {
            const int both = 2;   // 0 = old, 1 = new, 2 = both

            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0)
            {
                Debug.LogWarning("[PlayableSceneBuilder] Could not open ProjectSettings.asset; set " +
                                 "Player > Active Input Handling to 'Both' by hand.");
                return;
            }

            var so = new SerializedObject(assets[0]);
            var property = so.FindProperty("activeInputHandler");

            if (property == null)
            {
                Debug.LogWarning("[PlayableSceneBuilder] No 'activeInputHandler' property in this " +
                                 "Unity version; set Player > Active Input Handling to 'Both' by hand.");
                return;
            }

            if (property.intValue == both) return;

            property.intValue = both;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            Debug.LogWarning("[PlayableSceneBuilder] Active Input Handling set to 'Both'. " +
                             "Unity must be restarted for this to take effect — until then the " +
                             "on-screen controls will not respond.");
        }

        static void RegisterBuildSettings()
        {
            // Bootstrap must be index 0 — it is the entry point.
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BootstrapScene, true),
                new EditorBuildSettingsScene(WorldScene, true)
            };
        }

        /// <summary>
        /// Applies the Android player settings the README lists, so a build does not fail on
        /// defaults nobody remembered to change.
        /// </summary>
        [MenuItem("TN Lorry/Apply Android Build Settings", priority = 21)]
        public static void ApplyAndroidSettings()
        {
            PlayerSettings.companyName = "TN Lorry";
            PlayerSettings.productName = "TN Lorry Simulator";

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel28;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Android, ApiCompatibilityLevel.NET_Unity_4_8);

            PlayerSettings.colorSpace = ColorSpace.Linear;
            Time.fixedDeltaTime = 0.02f;

            EnableLegacyInput();

            Debug.Log("[PlayableSceneBuilder] Android player settings applied " +
                      "(IL2CPP, ARM64, API 28+, landscape, linear colour, input handling Both).");
        }
    }
}
