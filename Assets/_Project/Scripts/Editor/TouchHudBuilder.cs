using TMPro;
using TNLorry.Gameplay;
using TNLorry.InputSystem;
using TNLorry.UI;
using TNLorry.Vehicle;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TNLorry.EditorTools
{
    /// <summary>
    /// Generates the on-screen driving controls and a working HUD (sections K.2, O.2, O.3).
    ///
    /// This is the piece that makes a phone build actually playable: every other system was
    /// reachable with a keyboard, but a phone has no keyboard, so without these buttons the
    /// truck sits there.
    ///
    /// The wiring uses <see cref="UnityEventTools"/> rather than
    /// <c>callback.AddListener(() =&gt; ...)</c>. A runtime lambda is a *non-persistent*
    /// listener: it works if you add it while the game is running and vanishes the moment the
    /// scene is serialised. Only persistent listeners survive into the saved scene and the
    /// build, which is the whole point of generating this from an editor script.
    ///
    /// Pedals and steering need press-and-hold, which <c>Button.onClick</c> cannot express
    /// (it fires on release), so those go through <see cref="EventTrigger"/> PointerDown /
    /// PointerUp instead. Tap-once controls — gear, lights, camera — use onClick.
    /// </summary>
    public static class TouchHudBuilder
    {
        // Reference resolution. Landscape 16:9; the CanvasScaler handles everything else.
        const float RefWidth = 1920f;
        const float RefHeight = 1080f;

        // Section O.3: every button at least 48dp. At this reference height that is ~130 px,
        // so these sizes clear the minimum with room for a gloved or imprecise thumb.
        const float PedalWidth = 210f;
        const float PedalHeight = 230f;
        const float SteerSize = 215f;
        const float SmallButton = 130f;

        static readonly Color PanelTint   = new Color(0.04f, 0.05f, 0.07f, 0.55f);
        static readonly Color SteerTint   = new Color(0.15f, 0.35f, 0.55f, 0.80f);
        static readonly Color ThrottleTint = new Color(0.15f, 0.50f, 0.22f, 0.85f);
        static readonly Color BrakeTint   = new Color(0.58f, 0.15f, 0.13f, 0.85f);
        static readonly Color NeutralTint = new Color(0.20f, 0.22f, 0.26f, 0.80f);
        static readonly Color AccentTint  = new Color(0.85f, 0.62f, 0.15f, 0.85f);

        static TMP_FontAsset _font;

        /// <summary>
        /// Builds the canvas. Pass the components it should drive; any of them may be null and
        /// the corresponding controls are simply skipped.
        /// </summary>
        public static GameObject Build(TouchInputSource touch, TruckController truck,
                                       FuelSystem fuel, TrafficRuleMonitor rules,
                                       TruckLights lights, CameraRig camera)
        {
            ResolveFont();

            // Buttons mode, because that is what we are about to build. The player can switch
            // to wheel, tilt or slider in Settings; this is just the default that matches.
            if (touch != null)
            {
                var touchSo = new SerializedObject(touch);
                touchSo.FindProperty("mode").enumValueIndex = (int)SteeringMode.Buttons;
                touchSo.ApplyModifiedProperties();
            }

            var canvasObject = new GameObject("TouchHUD");
            // The canvas must not be dragged away by a floating-origin shift (section K.9).
            canvasObject.AddComponent<World.FloatingOriginExempt>();

            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Driving controls must sit above anything else that gets added later.
            canvas.sortingOrder = 100;

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
            // 0.5 match: on a very wide phone the controls neither shrink to nothing nor
            // overflow the screen.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasObject.AddComponent<GraphicRaycaster>();

            // Controls live inside the safe area; the notch in landscape sits exactly where
            // the steering buttons go.
            var safeArea = CreateChild(canvasObject.transform, "SafeArea");
            Stretch(safeArea);
            safeArea.gameObject.AddComponent<SafeAreaFitter>();

            BuildSteering(safeArea, touch);
            BuildPedals(safeArea, touch);
            BuildGearAndAux(safeArea, touch);
            BuildBottomStrip(safeArea, touch);

            var hud = BuildReadouts(safeArea, canvasObject);
            WireHud(hud, truck, fuel, rules);

            EnsureEventSystem();

            if (lights == null || camera == null)
                Debug.Log("[TouchHudBuilder] No TruckLights/CameraRig passed; those buttons " +
                          "are wired to the input source only, which is correct — the router " +
                          "forwards them.");

            return canvasObject;
        }

        // ── Control clusters ───────────────────────────────────────────────

        static void BuildSteering(RectTransform parent, TouchInputSource touch)
        {
            var cluster = CreateChild(parent, "SteeringCluster");
            Anchor(cluster, new Vector2(0f, 0f), new Vector2(40f, 40f),
                   new Vector2(SteerSize * 2f + 24f, SteerSize));

            var left = CreateHoldButton(cluster, "SteerLeft", "◀", SteerTint,
                                        new Vector2(0f, 0f), new Vector2(SteerSize, SteerSize));
            var right = CreateHoldButton(cluster, "SteerRight", "▶", SteerTint,
                                         new Vector2(SteerSize + 24f, 0f), new Vector2(SteerSize, SteerSize));

            if (touch == null) return;

            // Held = integrate toward lock; released = self-centre. That is what turns two
            // binary buttons into proportional steering.
            BindHold(left, new UnityAction(touch.OnSteerLeftDown), new UnityAction(touch.OnSteerButtonUp));
            BindHold(right, new UnityAction(touch.OnSteerRightDown), new UnityAction(touch.OnSteerButtonUp));
        }

        static void BuildPedals(RectTransform parent, TouchInputSource touch)
        {
            var cluster = CreateChild(parent, "PedalCluster");
            AnchorRight(cluster, new Vector2(-40f, 40f), new Vector2(PedalWidth * 2f + 24f, PedalHeight));

            var brake = CreateHoldButton(cluster, "Brake", "BRAKE", BrakeTint,
                                         new Vector2(0f, 0f), new Vector2(PedalWidth, PedalHeight));
            var throttle = CreateHoldButton(cluster, "Throttle", "GO", ThrottleTint,
                                            new Vector2(PedalWidth + 24f, 0f),
                                            new Vector2(PedalWidth, PedalHeight));

            if (touch == null) return;

            BindHoldBool(brake, touch.SetBrakeHeld);
            BindHoldBool(throttle, touch.SetThrottleHeld);
        }

        static void BuildGearAndAux(RectTransform parent, TouchInputSource touch)
        {
            var cluster = CreateChild(parent, "GearCluster");
            AnchorRight(cluster, new Vector2(-40f, PedalHeight + 60f),
                        new Vector2(SmallButton * 2f + 16f, SmallButton * 2f + 16f));

            var up = CreateTapButton(cluster, "GearUp", "+", NeutralTint,
                                     new Vector2(SmallButton + 16f, SmallButton + 16f),
                                     new Vector2(SmallButton, SmallButton));
            var down = CreateTapButton(cluster, "GearDown", "−", NeutralTint,
                                       new Vector2(SmallButton + 16f, 0f),
                                       new Vector2(SmallButton, SmallButton));

            // Handbrake and retarder are holds, not taps: a retarder you have to keep pressed
            // on a ghat descent is the correct feel, and a tap-toggle handbrake causes rollaways.
            var handbrake = CreateHoldButton(cluster, "HandBrake", "P", AccentTint,
                                             new Vector2(0f, SmallButton + 16f),
                                             new Vector2(SmallButton, SmallButton));
            var retarder = CreateHoldButton(cluster, "Retarder", "RET", AccentTint,
                                            new Vector2(0f, 0f), new Vector2(SmallButton, SmallButton));

            if (touch == null) return;

            BindTap(up, new UnityAction(touch.PressShiftUp));
            BindTap(down, new UnityAction(touch.PressShiftDown));
            BindHoldBool(handbrake, touch.SetHandBrakeHeld);
            BindHoldBool(retarder, touch.SetRetarderHeld);
        }

        static void BuildBottomStrip(RectTransform parent, TouchInputSource touch)
        {
            var strip = CreateChild(parent, "AuxStrip");
            Anchor(strip, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(SmallButton * 5f + 64f, SmallButton));
            strip.anchorMin = new Vector2(0.5f, 0f);
            strip.anchorMax = new Vector2(0.5f, 0f);
            strip.pivot = new Vector2(0.5f, 0f);
            strip.anchoredPosition = new Vector2(0f, 40f);

            float step = SmallButton + 16f;
            float startX = -(step * 2f);

            // Horn is a hold: a long press becomes the air horn (section K.2), which is half
            // the reason anyone drives a decorated lorry.
            var horn = CreateHoldButton(strip, "Horn", "HORN", AccentTint,
                                        Vector2.zero, new Vector2(SmallButton, SmallButton));
            CentreAt(horn, startX);

            var lights = CreateTapButton(strip, "Lights", "LGT", NeutralTint,
                                         Vector2.zero, new Vector2(SmallButton, SmallButton));
            CentreAt(lights, startX + step);

            var hazard = CreateTapButton(strip, "Hazard", "HAZ", NeutralTint,
                                         Vector2.zero, new Vector2(SmallButton, SmallButton));
            CentreAt(hazard, startX + step * 2f);

            var indicator = CreateTapButton(strip, "IndicateLeft", "◀|", NeutralTint,
                                            Vector2.zero, new Vector2(SmallButton, SmallButton));
            CentreAt(indicator, startX + step * 3f);

            var cam = CreateTapButton(strip, "Camera", "CAM", NeutralTint,
                                      Vector2.zero, new Vector2(SmallButton, SmallButton));
            CentreAt(cam, startX + step * 4f);

            if (touch == null) return;

            BindHoldBool(horn, touch.SetHornHeld);
            BindTap(lights, new UnityAction(touch.PressLights));
            BindTap(hazard, new UnityAction(touch.PressHazard));
            BindTap(indicator, new UnityAction(touch.PressIndicateLeft));
            BindTap(cam, new UnityAction(touch.PressCamera));
        }

        // ── Readouts ───────────────────────────────────────────────────────

        static HudController BuildReadouts(RectTransform parent, GameObject canvasObject)
        {
            // Top-left: money and level (section O.2).
            var topLeft = CreatePanel(parent, "TopLeft", PanelTint);
            Anchor(topLeft, new Vector2(0f, 1f), new Vector2(40f, -24f), new Vector2(340f, 92f));
            topLeft.anchorMin = new Vector2(0f, 1f);
            topLeft.anchorMax = new Vector2(0f, 1f);
            topLeft.pivot = new Vector2(0f, 1f);
            topLeft.anchoredPosition = new Vector2(40f, -24f);

            var money = CreateLabel(topLeft, "Money", "₹0", 40f, TextAlignmentOptions.MidlineLeft);
            Fill(money, new Vector4(16f, 44f, 16f, 6f));

            var level = CreateLabel(topLeft, "Level", "Lv 1", 26f, TextAlignmentOptions.MidlineLeft);
            Fill(level, new Vector4(16f, 8f, 150f, 50f));

            var levelFill = CreateFillBar(topLeft, "LevelProgress", new Color(0.3f, 0.75f, 0.95f, 0.9f));
            Fill(levelFill, new Vector4(150f, 14f, 16f, 54f));

            // Top-right: clock and weather.
            var topRight = CreatePanel(parent, "TopRight", PanelTint);
            topRight.anchorMin = new Vector2(1f, 1f);
            topRight.anchorMax = new Vector2(1f, 1f);
            topRight.pivot = new Vector2(1f, 1f);
            topRight.anchoredPosition = new Vector2(-40f, -24f);
            topRight.sizeDelta = new Vector2(260f, 92f);

            var clock = CreateLabel(topRight, "Clock", "08:00", 40f, TextAlignmentOptions.MidlineRight);
            Fill(clock, new Vector4(16f, 44f, 16f, 6f));

            var weather = CreateLabel(topRight, "Weather", "Clear", 24f, TextAlignmentOptions.MidlineRight);
            Fill(weather, new Vector4(16f, 8f, 16f, 50f));

            // Right side, above the gear cluster: the driving instruments.
            var instruments = CreatePanel(parent, "Instruments", PanelTint);
            instruments.anchorMin = new Vector2(1f, 0f);
            instruments.anchorMax = new Vector2(1f, 0f);
            instruments.pivot = new Vector2(1f, 0f);
            instruments.anchoredPosition = new Vector2(-40f, PedalHeight + SmallButton * 2f + 100f);
            instruments.sizeDelta = new Vector2(300f, 220f);

            var speed = CreateLabel(instruments, "Speed", "0", 86f, TextAlignmentOptions.Center);
            Fill(speed, new Vector4(10f, 92f, 110f, 10f));

            var speedUnit = CreateLabel(instruments, "SpeedUnit", "km/h", 24f, TextAlignmentOptions.Center);
            Fill(speedUnit, new Vector4(10f, 64f, 110f, 128f));

            var limit = CreateLabel(instruments, "SpeedLimit", "80", 40f, TextAlignmentOptions.Center);
            Fill(limit, new Vector4(200f, 120f, 12f, 16f));
            limit.GetComponent<TextMeshProUGUI>().color = new Color(1f, 0.85f, 0.3f);

            var gear = CreateLabel(instruments, "Gear", "N", 48f, TextAlignmentOptions.Center);
            Fill(gear, new Vector4(200f, 40f, 12f, 96f));

            var rpm = CreateFillBar(instruments, "Rpm", new Color(0.95f, 0.45f, 0.2f, 0.95f));
            Fill(rpm, new Vector4(14f, 40f, 100f, 164f));

            var fuelBar = CreateFillBar(instruments, "Fuel", new Color(0.3f, 0.8f, 0.45f, 0.95f));
            Fill(fuelBar, new Vector4(14f, 12f, 100f, 192f));

            var overspeed = CreatePanel(instruments, "OverspeedFlag", new Color(1f, 0.15f, 0.15f, 0.8f));
            Fill(overspeed, new Vector4(200f, 96f, 12f, 108f));
            overspeed.GetComponent<Image>().enabled = false;

            // Toast, top centre, above everything.
            var toast = CreatePanel(parent, "Toast", new Color(0.04f, 0.05f, 0.07f, 0.85f));
            toast.anchorMin = new Vector2(0.5f, 1f);
            toast.anchorMax = new Vector2(0.5f, 1f);
            toast.pivot = new Vector2(0.5f, 1f);
            toast.anchoredPosition = new Vector2(0f, -24f);
            toast.sizeDelta = new Vector2(760f, 76f);

            var toastGroup = toast.gameObject.AddComponent<CanvasGroup>();
            toastGroup.alpha = 0f;
            toastGroup.interactable = false;
            toastGroup.blocksRaycasts = false;

            var toastLabel = CreateLabel(toast, "ToastLabel", "", 34f, TextAlignmentOptions.Center);
            Fill(toastLabel, Vector4.zero);

            // HudController lives on the canvas root so it is found with the UI, not the truck.
            var hud = canvasObject.AddComponent<HudController>();
            var so = new SerializedObject(hud);
            Assign(so, "moneyLabel", money.GetComponent<TextMeshProUGUI>());
            Assign(so, "levelLabel", level.GetComponent<TextMeshProUGUI>());
            Assign(so, "levelProgressFill", levelFill.GetComponent<Image>());
            Assign(so, "clockLabel", clock.GetComponent<TextMeshProUGUI>());
            Assign(so, "weatherLabel", weather.GetComponent<TextMeshProUGUI>());
            Assign(so, "speedLabel", speed.GetComponent<TextMeshProUGUI>());
            Assign(so, "speedUnitLabel", speedUnit.GetComponent<TextMeshProUGUI>());
            Assign(so, "speedLimitLabel", limit.GetComponent<TextMeshProUGUI>());
            Assign(so, "gearLabel", gear.GetComponent<TextMeshProUGUI>());
            Assign(so, "rpmFill", rpm.GetComponent<Image>());
            Assign(so, "fuelFill", fuelBar.GetComponent<Image>());
            Assign(so, "overspeedIndicator", overspeed.GetComponent<Image>());
            Assign(so, "toastLabel", toastLabel.GetComponent<TextMeshProUGUI>());
            Assign(so, "toastGroup", toastGroup);
            so.ApplyModifiedProperties();

            return hud;
        }

        static void WireHud(HudController hud, TruckController truck, FuelSystem fuel,
                            TrafficRuleMonitor rules)
        {
            var so = new SerializedObject(hud);
            Assign(so, "truck", truck);
            Assign(so, "fuel", fuel);
            Assign(so, "rules", rules);
            if (truck != null) Assign(so, "damage", truck.GetComponent<DamageSystem>());
            so.ApplyModifiedProperties();
        }

        // ── Event wiring ───────────────────────────────────────────────────

        /// <summary>Press-and-release pair for a void/void control, e.g. steering buttons.</summary>
        static void BindHold(GameObject target, UnityAction onDown, UnityAction onUp)
        {
            var trigger = target.GetComponent<EventTrigger>() ?? target.AddComponent<EventTrigger>();

            var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            UnityEventTools.AddVoidPersistentListener(down.callback, onDown);
            trigger.triggers.Add(down);

            var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
            UnityEventTools.AddVoidPersistentListener(up.callback, onUp);
            trigger.triggers.Add(up);

            // PointerExit as well: dragging a thumb off a pedal must release it, or the truck
            // keeps accelerating after the finger has left the button.
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            UnityEventTools.AddVoidPersistentListener(exit.callback, onUp);
            trigger.triggers.Add(exit);

            EditorUtility.SetDirty(trigger);
        }

        /// <summary>Press-and-release pair for a bool setter, e.g. SetThrottleHeld.</summary>
        static void BindHoldBool(GameObject target, UnityAction<bool> setter)
        {
            var trigger = target.GetComponent<EventTrigger>() ?? target.AddComponent<EventTrigger>();

            var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            UnityEventTools.AddBoolPersistentListener(down.callback, setter, true);
            trigger.triggers.Add(down);

            var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
            UnityEventTools.AddBoolPersistentListener(up.callback, setter, false);
            trigger.triggers.Add(up);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            UnityEventTools.AddBoolPersistentListener(exit.callback, setter, false);
            trigger.triggers.Add(exit);

            EditorUtility.SetDirty(trigger);
        }

        static void BindTap(GameObject target, UnityAction action)
        {
            var button = target.GetComponent<Button>();
            if (button == null) return;

            UnityEventTools.AddVoidPersistentListener(button.onClick, action);
            EditorUtility.SetDirty(button);
        }

        // ── Primitives ─────────────────────────────────────────────────────

        static GameObject CreateHoldButton(RectTransform parent, string name, string caption,
                                           Color tint, Vector2 position, Vector2 size)
        {
            var button = CreateButtonObject(parent, name, caption, tint, position, size);
            return button;
        }

        static GameObject CreateTapButton(RectTransform parent, string name, string caption,
                                          Color tint, Vector2 position, Vector2 size)
        {
            return CreateButtonObject(parent, name, caption, tint, position, size);
        }

        static GameObject CreateButtonObject(RectTransform parent, string name, string caption,
                                             Color tint, Vector2 position, Vector2 size)
        {
            var rect = CreateChild(parent, name);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var image = rect.gameObject.AddComponent<Image>();
            image.color = tint;

            // Button is here for the press tint only; the behaviour comes from EventTrigger or
            // a persistent onClick added by the caller.
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            var colors = button.colors;
            colors.pressedColor = new Color(tint.r * 1.5f, tint.g * 1.5f, tint.b * 1.5f, 1f);
            colors.fadeDuration = 0.05f;
            button.colors = colors;

            var label = CreateLabel(rect, "Label", caption, size.y * 0.3f, TextAlignmentOptions.Center);
            Fill(label, Vector4.zero);

            return rect.gameObject;
        }

        static RectTransform CreatePanel(RectTransform parent, string name, Color tint)
        {
            var rect = CreateChild(parent, name);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = tint;
            image.raycastTarget = false;     // readouts must never eat a pedal press
            return rect;
        }

        static RectTransform CreateFillBar(RectTransform parent, string name, Color tint)
        {
            var rect = CreateChild(parent, name);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = tint;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillAmount = 0.5f;
            image.raycastTarget = false;
            return rect;
        }

        static RectTransform CreateLabel(RectTransform parent, string name, string text,
                                         float fontSize, TextAlignmentOptions alignment)
        {
            var rect = CreateChild(parent, name);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();

            label.text = text;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = Color.white;
            label.raycastTarget = false;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;

            if (_font != null) label.font = _font;

            return rect;
        }

        static RectTransform CreateChild(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>Stretches to the parent with per-edge insets (left, bottom, right, top).</summary>
        static void Fill(RectTransform rect, Vector4 insets)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(insets.x, insets.y);
            rect.offsetMax = new Vector2(-insets.z, -insets.w);
        }

        static void Anchor(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        static void AnchorRight(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        static void CentreAt(GameObject button, float offsetX)
        {
            var rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(offsetX, 0f);
        }

        static void Assign(SerializedObject so, string field, Object value)
        {
            var property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogWarning("[TouchHudBuilder] HudController has no serialized field '" +
                                 field + "'; skipping.");
                return;
            }
            property.objectReferenceValue = value;
        }

        static void ResolveFont()
        {
            _font = TMP_Settings.defaultFontAsset;

            if (_font == null)
                Debug.LogWarning("[TouchHudBuilder] No default TMP font asset. Run " +
                                 "Window > TextMeshPro > Import TMP Essential Resources, then " +
                                 "rebuild the scenes. Labels will not render until you do.\n" +
                                 "NOTE (section P.2): the default font cannot shape Tamil. A " +
                                 "Noto Sans Tamil asset plus an Indic-shaping plugin is still " +
                                 "required before any Tamil string renders correctly on device.");
        }

        /// <summary>
        /// Without an EventSystem in the scene, no UI input is dispatched at all and every
        /// button is inert — the single easiest way to ship a dead HUD.
        /// </summary>
        static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;

            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();

            // StandaloneInputModule needs legacy input enabled. PlayableSceneBuilder's
            // "Apply Android Build Settings" sets Active Input Handling to Both, which this
            // and the runtime's UnityEngine.Input calls both depend on.
            go.AddComponent<StandaloneInputModule>();
        }
    }
}
