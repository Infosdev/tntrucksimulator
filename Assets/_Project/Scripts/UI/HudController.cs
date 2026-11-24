using System.Collections.Generic;
using TMPro;
using TNLorry.Core;
using TNLorry.Gameplay;
using TNLorry.Navigation;
using TNLorry.Services;
using TNLorry.Utils;
using TNLorry.Vehicle;
using UnityEngine;
using UnityEngine.UI;

namespace TNLorry.UI
{
    /// <summary>
    /// In-game HUD (section O.2). Reads truck state directly (it is one object, polling is
    /// cheaper than events at 30 Hz) and listens on the event bus for the discrete things:
    /// money, fines, deliveries, weather.
    ///
    /// Text updates are throttled and change-guarded, because assigning TMP_Text.text forces a
    /// mesh rebuild — doing that on eight labels every frame is a measurable cost on a Mali-G52.
    /// </summary>
    public class HudController : MonoBehaviour
    {
        [Header("Top bar")]
        [SerializeField] TMP_Text moneyLabel;
        [SerializeField] TMP_Text levelLabel;
        [SerializeField] Image levelProgressFill;
        [SerializeField] TMP_Text clockLabel;
        [SerializeField] TMP_Text weatherLabel;

        [Header("Driving panel")]
        [SerializeField] TMP_Text speedLabel;
        [SerializeField] TMP_Text speedUnitLabel;
        [SerializeField] TMP_Text speedLimitLabel;
        [SerializeField] TMP_Text gearLabel;
        [SerializeField] Image rpmFill;
        [SerializeField] Image fuelFill;
        [SerializeField] Graphic overspeedIndicator;

        [Header("Navigation")]
        [SerializeField] TMP_Text navInstructionLabel;
        [SerializeField] TMP_Text navDistanceLabel;
        [SerializeField] TMP_Text etaLabel;
        [SerializeField] LineRenderer routeLine;

        [Header("Job")]
        [SerializeField] TMP_Text cargoLabel;
        [SerializeField] TMP_Text destinationLabel;
        [SerializeField] TMP_Text timeRemainingLabel;
        [SerializeField] Image cargoConditionFill;

        [Header("Warnings")]
        [SerializeField] GameObject lowFuelWarning;
        [SerializeField] GameObject damageWarning;
        [SerializeField] GameObject fatigueWarning;
        [SerializeField] GameObject jackknifeWarning;
        [SerializeField] CanvasGroup fatigueVignette;

        [Header("Toast")]
        [SerializeField] TMP_Text toastLabel;
        [SerializeField] CanvasGroup toastGroup;
        [SerializeField] float toastSeconds = 2.5f;

        [Header("Refs")]
        [SerializeField] TruckController truck;
        [SerializeField] FuelSystem fuel;
        [SerializeField] DamageSystem damage;
        [SerializeField] TrafficRuleMonitor rules;
        [SerializeField] TrailerCoupling coupling;
        [SerializeField] GpsService gps;
        [SerializeField] FatigueSystem fatigue;

        [Header("Refresh")]
        [Tooltip("Seconds between text refreshes. 0.1 is imperceptible and saves mesh rebuilds.")]
        [SerializeField] float textRefreshInterval = 0.1f;

        LocalizationService _loc;
        GameClock _clock;
        JobBoardService _jobs;
        ProgressionService _progression;
        EconomyService _economy;

        readonly List<Vector3> _routeBuffer = new List<Vector3>(128);
        float _refreshTimer;
        float _toastTimer;
        bool  _useMph;

        // Cached last-written strings, so we only touch TMP when the value actually changed.
        string _lastSpeed, _lastGear, _lastClock, _lastMoney, _lastNavDistance, _lastTimeRemaining;

        void Awake()
        {
            ServiceLocator.TryGet(out _loc);
            ServiceLocator.TryGet(out _jobs);
            ServiceLocator.TryGet(out _progression);
            ServiceLocator.TryGet(out _economy);
            _clock = FindFirstObjectByType<GameClock>();

            if (ServiceLocator.TryGet<SaveSystem>(out var save) && save.Data != null)
                _useMph = save.Data.settings.speedUnitsMph;

            if (speedUnitLabel != null) speedUnitLabel.text = _useMph ? "mph" : "km/h";

            HideAll();
        }

        void HideAll()
        {
            if (lowFuelWarning != null)    lowFuelWarning.SetActive(false);
            if (damageWarning != null)     damageWarning.SetActive(false);
            if (fatigueWarning != null)    fatigueWarning.SetActive(false);
            if (jackknifeWarning != null)  jackknifeWarning.SetActive(false);
            if (toastGroup != null)        toastGroup.alpha = 0f;
            if (fatigueVignette != null)   fatigueVignette.alpha = 0f;
        }

        void OnEnable()
        {
            EventBus.Subscribe<MoneyChangedEvent>(OnMoneyChanged);
            EventBus.Subscribe<XpChangedEvent>(OnXpChanged);
            EventBus.Subscribe<FineIssuedEvent>(OnFineIssued);
            EventBus.Subscribe<TollPaidEvent>(OnTollPaid);
            EventBus.Subscribe<WeatherChangedEvent>(OnWeatherChanged);
            EventBus.Subscribe<FuelChangedEvent>(OnFuelChanged);
            EventBus.Subscribe<DamageChangedEvent>(OnDamageChanged);
            EventBus.Subscribe<FatigueChangedEvent>(OnFatigueChanged);
            EventBus.Subscribe<DeliveryCompletedEvent>(OnDeliveryCompleted);

            if (fatigue != null) fatigue.OnBlink += OnFatigueBlink;
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<MoneyChangedEvent>(OnMoneyChanged);
            EventBus.Unsubscribe<XpChangedEvent>(OnXpChanged);
            EventBus.Unsubscribe<FineIssuedEvent>(OnFineIssued);
            EventBus.Unsubscribe<TollPaidEvent>(OnTollPaid);
            EventBus.Unsubscribe<WeatherChangedEvent>(OnWeatherChanged);
            EventBus.Unsubscribe<FuelChangedEvent>(OnFuelChanged);
            EventBus.Unsubscribe<DamageChangedEvent>(OnDamageChanged);
            EventBus.Unsubscribe<FatigueChangedEvent>(OnFatigueChanged);
            EventBus.Unsubscribe<DeliveryCompletedEvent>(OnDeliveryCompleted);

            if (fatigue != null) fatigue.OnBlink -= OnFatigueBlink;
        }

        void Start()
        {
            RefreshMoney();
            RefreshLevel();
            RefreshJobPanel();
        }

        void Update()
        {
            TickContinuous();

            _refreshTimer += Time.deltaTime;
            if (_refreshTimer >= textRefreshInterval)
            {
                _refreshTimer = 0f;
                TickThrottled();
            }

            TickToast(Time.deltaTime);
        }

        /// <summary>Per-frame: only the things that would visibly stutter if throttled.</summary>
        void TickContinuous()
        {
            if (truck == null) return;

            if (rpmFill != null) rpmFill.fillAmount = truck.Engine != null ? truck.Engine.Rpm01 : 0f;

            if (jackknifeWarning != null && coupling != null)
                jackknifeWarning.SetActive(coupling.IsJackknifing);
        }

        void TickThrottled()
        {
            if (truck != null)
            {
                float displaySpeed = _useMph ? truck.SpeedKmh * 0.621371f : truck.SpeedKmh;
                SetText(speedLabel, Mathf.RoundToInt(Mathf.Abs(displaySpeed)).ToString(), ref _lastSpeed);
                SetText(gearLabel, truck.Gearbox.GearLabel, ref _lastGear);
            }

            if (rules != null)
            {
                float limit = rules.CurrentSpeedLimitKmh;
                if (speedLimitLabel != null)
                    speedLimitLabel.text = Mathf.RoundToInt(_useMph ? limit * 0.621371f : limit).ToString();

                if (overspeedIndicator != null)
                    overspeedIndicator.enabled = truck != null && truck.SpeedKmh > limit + 5f;
            }

            if (fuel != null && fuelFill != null) fuelFill.fillAmount = fuel.Fraction;

            if (_clock != null) SetText(clockLabel, _clock.FormatClock(), ref _lastClock);

            RefreshNavigation();
            RefreshJobTimer();
        }

        void RefreshNavigation()
        {
            if (gps == null) return;

            if (navInstructionLabel != null && _loc != null)
                navInstructionLabel.text = _loc.Get(gps.NextInstructionKey);

            if (navDistanceLabel != null)
            {
                float distance = gps.DistanceToNextManoeuvre;
                string text = distance >= 1000f
                    ? (distance / 1000f).ToString("0.0") + " km"
                    : Mathf.RoundToInt(distance / 10f) * 10 + " m";
                SetText(navDistanceLabel, text, ref _lastNavDistance);
            }

            if (etaLabel != null && _loc != null && _clock != null)
            {
                float arrivalHour = _clock.HourOfDay + gps.RemainingMinutes / 60f;
                while (arrivalHour >= 24f) arrivalHour -= 24f;
                int hours = Mathf.FloorToInt(arrivalHour);
                int minutes = Mathf.FloorToInt((arrivalHour - hours) * 60f);
                etaLabel.text = _loc.GetFormat("hud.eta", hours.ToString("00") + ":" + minutes.ToString("00"));
            }

            if (routeLine != null)
            {
                gps.FillRoutePolyline(_routeBuffer);
                routeLine.positionCount = _routeBuffer.Count;
                routeLine.SetPositions(_routeBuffer.ToArray());
            }
        }

        void RefreshJobPanel()
        {
            var job = _jobs?.ActiveJob;
            bool hasJob = job != null;

            if (cargoLabel != null)
            {
                cargoLabel.gameObject.SetActive(hasJob);
                if (hasJob && _loc != null) cargoLabel.text = _loc.Get(job.Cargo.nameKey);
            }

            if (destinationLabel != null)
            {
                destinationLabel.gameObject.SetActive(hasJob);
                if (hasJob && _loc != null) destinationLabel.text = _loc.Get(job.DestinationNameKey);
            }
        }

        void RefreshJobTimer()
        {
            var job = _jobs?.ActiveJob;
            if (job == null || timeRemainingLabel == null) return;

            float elapsed = _clock != null ? _clock.JobElapsedMinutes : job.ElapsedMinutes;
            float remaining = job.TimeLimitMinutes - elapsed;

            string text = remaining >= 0f
                ? Mathf.FloorToInt(remaining / 60f) + "h " + Mathf.FloorToInt(remaining % 60f) + "m"
                : "-" + Mathf.FloorToInt(-remaining) + "m";

            SetText(timeRemainingLabel, text, ref _lastTimeRemaining);
            timeRemainingLabel.color = remaining < 15f ? new Color(1f, 0.4f, 0.3f) : Color.white;

            if (cargoConditionFill != null && damage != null)
                cargoConditionFill.fillAmount = 1f - damage.CargoDamage;
        }

        // ── Event handlers ──────────────────────────────────────────────────

        void OnMoneyChanged(MoneyChangedEvent e)
        {
            RefreshMoney();
            if (e.Delta < 0) return;
            ShowToast("+ " + FormatRupees(e.Delta));
        }

        void RefreshMoney()
        {
            if (moneyLabel == null || _economy == null) return;
            SetText(moneyLabel, FormatRupees(_economy.Balance), ref _lastMoney);
        }

        void OnXpChanged(XpChangedEvent e)
        {
            RefreshLevel();
            if (e.LeveledUp && _loc != null) ShowToast(_loc.GetFormat("result.level_up", e.Level));
        }

        void RefreshLevel()
        {
            if (_progression == null) return;
            if (levelLabel != null) levelLabel.text = "Lv " + _progression.Level;
            if (levelProgressFill != null) levelProgressFill.fillAmount = _progression.LevelProgress01;
        }

        void OnFineIssued(FineIssuedEvent e)
        {
            if (_loc == null) return;
            string key = "fine." + e.Violation switch
            {
                ViolationType.Overspeed           => "overspeed",
                ViolationType.RedLight            => "red_light",
                ViolationType.WrongSide           => "wrong_side",
                ViolationType.PedestrianCollision => "pedestrian",
                ViolationType.NoHeadlights        => "no_headlights",
                ViolationType.Overload            => "overload",
                _                                 => "collision"
            };
            ShowToast(_loc.Get(key) + "  - " + FormatRupees(e.Amount));
        }

        void OnTollPaid(TollPaidEvent e)
        {
            if (_loc != null) ShowToast(_loc.GetFormat("toll.paid", e.Amount));
        }

        void OnWeatherChanged(WeatherChangedEvent e)
        {
            if (weatherLabel != null) weatherLabel.text = e.Type.ToString();
        }

        void OnFuelChanged(FuelChangedEvent e)
        {
            bool low = e.CapacityLitres > 0f && e.Litres / e.CapacityLitres <= 0.15f;
            if (lowFuelWarning != null) lowFuelWarning.SetActive(low);
        }

        void OnDamageChanged(DamageChangedEvent e)
        {
            if (damageWarning != null) damageWarning.SetActive(e.EnginePercent >= 50f);
        }

        void OnFatigueChanged(FatigueChangedEvent e)
        {
            if (fatigueWarning != null) fatigueWarning.SetActive(e.Normalized >= 0.5f);
        }

        void OnFatigueBlink(float severity)
        {
            if (fatigueVignette == null) return;
            _blinkStrength = severity;
            _blinkTimer = 0.45f;
        }

        float _blinkStrength, _blinkTimer;

        void OnDeliveryCompleted(DeliveryCompletedEvent e)
        {
            RefreshJobPanel();
            if (_loc != null) ShowToast(_loc.Get("result.delivered"));
        }

        // ── Toast & vignette ────────────────────────────────────────────────

        public void ShowToast(string message)
        {
            if (toastLabel == null || toastGroup == null) return;
            toastLabel.text = message;
            toastGroup.alpha = 1f;
            _toastTimer = toastSeconds;
        }

        void TickToast(float dt)
        {
            if (toastGroup != null && _toastTimer > 0f)
            {
                _toastTimer -= dt;
                if (_toastTimer < 0.5f) toastGroup.alpha = Mathf.Clamp01(_toastTimer / 0.5f);
            }

            if (fatigueVignette != null && _blinkTimer > 0f)
            {
                _blinkTimer -= dt;
                // Eyelid close-and-open, so the blink reads as a blink rather than a fade.
                float phase = Mathf.Sin(Mathf.Clamp01(1f - _blinkTimer / 0.45f) * Mathf.PI);
                fatigueVignette.alpha = phase * _blinkStrength;
            }
        }

        // Indian digit grouping lives in Utils.CurrencyFormat so the HUD, result screen,
        // dealer and bank cannot drift apart on how money is written.
        static string FormatRupees(long amount) => CurrencyFormat.Rupees(amount);

        static void SetText(TMP_Text label, string value, ref string cache)
        {
            if (label == null || cache == value) return;
            cache = value;
            label.text = value;
        }
    }
}
