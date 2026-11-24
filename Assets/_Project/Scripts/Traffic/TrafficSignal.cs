using System.Collections.Generic;
using TNLorry.Gameplay;
using TNLorry.Vehicle;
using UnityEngine;

namespace TNLorry.Traffic
{
    /// <summary>
    /// Fixed-cycle traffic signal (section M.2). Drives both AI stopping behaviour and the
    /// player's red-light fine.
    ///
    /// Fixed-time rather than sensor-actuated: it is predictable for the player, cheap, and
    /// matches how most junctions in Tamil Nadu towns actually run.
    /// </summary>
    public class TrafficSignal : MonoBehaviour
    {
        public enum Phase { Green, Amber, Red }

        [System.Serializable]
        public class Approach
        {
            [Tooltip("Lane ids this approach controls.")]
            public int[] laneIds = new int[0];
            [Tooltip("Green duration in seconds for this approach.")]
            public float greenSeconds = 18f;
            [Tooltip("Emissive renderers for this approach's lamps, in red/amber/green order.")]
            public Renderer redLamp;
            public Renderer amberLamp;
            public Renderer greenLamp;
        }

        [SerializeField] Approach[] approaches = new Approach[0];
        [Tooltip("Amber duration between phases.")]
        [SerializeField] float amberSeconds = 3f;
        [Tooltip("All-red clearance between approaches. Prevents AI from meeting in the middle.")]
        [SerializeField] float allRedSeconds = 1.5f;

        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        public int ActiveApproach { get; private set; }
        public Phase CurrentPhase { get; private set; } = Phase.Green;

        readonly Dictionary<int, int> _laneToApproach = new Dictionary<int, int>();
        float _timer;

        void Awake()
        {
            for (int i = 0; i < approaches.Length; i++)
            {
                if (approaches[i]?.laneIds == null) continue;
                foreach (int laneId in approaches[i].laneIds) _laneToApproach[laneId] = i;
            }

            if (approaches.Length == 0)
            {
                Debug.LogError("[TrafficSignal] No approaches configured.", this);
                enabled = false;
                return;
            }

            _timer = approaches[0].greenSeconds;
            RefreshLamps();
        }

        void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;

            switch (CurrentPhase)
            {
                case Phase.Green:
                    CurrentPhase = Phase.Amber;
                    _timer = amberSeconds;
                    break;

                case Phase.Amber:
                    CurrentPhase = Phase.Red;
                    _timer = allRedSeconds;
                    break;

                default:
                    ActiveApproach = (ActiveApproach + 1) % approaches.Length;
                    CurrentPhase = Phase.Green;
                    _timer = approaches[ActiveApproach].greenSeconds;
                    break;
            }

            RefreshLamps();
        }

        public bool IsGreenFor(int laneId)
        {
            if (!_laneToApproach.TryGetValue(laneId, out int approach)) return true;  // uncontrolled
            return approach == ActiveApproach && CurrentPhase == Phase.Green;
        }

        /// <summary>Amber counts as "do not enter" for AI but not for a fine — same as reality.</summary>
        public bool IsRedFor(int laneId)
        {
            if (!_laneToApproach.TryGetValue(laneId, out int approach)) return false;
            return approach != ActiveApproach || CurrentPhase == Phase.Red;
        }

        void RefreshLamps()
        {
            for (int i = 0; i < approaches.Length; i++)
            {
                var approach = approaches[i];
                bool isActive = i == ActiveApproach;

                bool green = isActive && CurrentPhase == Phase.Green;
                bool amber = isActive && CurrentPhase == Phase.Amber;
                bool red   = !green && !amber;

                SetLamp(approach.greenLamp, green, new Color(0.1f, 1f, 0.2f));
                SetLamp(approach.amberLamp, amber, new Color(1f, 0.65f, 0f));
                SetLamp(approach.redLamp,   red,   new Color(1f, 0.1f, 0.1f));
            }
        }

        static void SetLamp(Renderer lamp, bool on, Color color)
        {
            if (lamp == null) return;
            lamp.material.SetColor(EmissionColorId, on ? color * 2.5f : Color.black);
        }

        /// <summary>
        /// Called by the stop-line trigger on the player's approach. Keeps the fine logic in
        /// TrafficRuleMonitor and the signal state here.
        /// </summary>
        public void ReportPlayerCrossing(int laneId, TrafficRuleMonitor monitor)
        {
            if (monitor != null && IsRedFor(laneId)) monitor.OnRedLightCrossed();
        }
    }
}
