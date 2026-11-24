using TNLorry.Traffic;
using TNLorry.Vehicle;
using UnityEngine;

namespace TNLorry.Gameplay
{
    /// <summary>
    /// The stop line at a signalised junction (section K.7). Crossing it while the signal is
    /// red issues the red-light fine; crossing on amber does not, same as reality.
    ///
    /// Place the trigger on the painted line, facing the direction of travel. `laneId` must
    /// match the LaneGraph lane this approach controls, so the signal knows which phase applies.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class SignalStopLine : MonoBehaviour
    {
        [SerializeField] TrafficSignal signal;
        [Tooltip("LaneGraph lane id for this approach. Must match the signal's approach list.")]
        [SerializeField] int laneId = -1;
        [Tooltip("Only fire when the truck is actually heading through the junction.")]
        [SerializeField] bool directional = true;

        void Awake()
        {
            GetComponent<Collider>().isTrigger = true;

            if (signal == null) signal = GetComponentInParent<TrafficSignal>();
            if (signal == null)
            {
                Debug.LogError("[SignalStopLine] No TrafficSignal assigned or found in parents.", this);
                enabled = false;
                return;
            }
            if (laneId < 0)
                Debug.LogWarning("[SignalStopLine] laneId is unset; this approach will be treated " +
                                 "as uncontrolled and will never fine the player.", this);
        }

        void OnTriggerEnter(Collider other)
        {
            var truck = other.GetComponentInParent<TruckController>();
            if (truck == null) return;

            if (directional && Vector3.Dot(truck.transform.forward, transform.forward) < 0.3f) return;

            var monitor = truck.GetComponentInChildren<TrafficRuleMonitor>();
            if (monitor == null) return;

            // The signal owns the phase; TrafficRuleMonitor owns the fine. Keeping that split
            // means signal timing changes never touch the fine logic.
            signal.ReportPlayerCrossing(laneId, monitor);
        }
    }
}
