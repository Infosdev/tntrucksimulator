using TNLorry.Vehicle;
using UnityEngine;

namespace TNLorry.Gameplay
{
    /// <summary>
    /// A speed camera or check-post (section K.7). This is what makes overspeeding a risk at
    /// specific places rather than a constant tax — the one design decision that keeps the
    /// fine system from feeling like harassment.
    ///
    /// Place the trigger across the carriageway at the camera gantry. Set postedLimitKmh to
    /// match the signboard the player can actually read, or leave it 0 to use the road graph's
    /// limit for whatever edge they are on.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class SpeedCamera : MonoBehaviour
    {
        [Tooltip("Posted limit in km/h. 0 = use the limit of the road segment the truck is on.")]
        [SerializeField] float postedLimitKmh;
        [Tooltip("Only trigger when the truck is heading the right way through the gantry.")]
        [SerializeField] bool directional = true;

        void Awake() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerEnter(Collider other)
        {
            var truck = other.GetComponentInParent<TruckController>();
            if (truck == null) return;

            // Without this, a camera fires again when the player reverses back through it.
            if (directional && Vector3.Dot(truck.transform.forward, transform.forward) < 0.3f) return;

            var monitor = truck.GetComponentInChildren<TrafficRuleMonitor>();
            if (monitor == null) return;

            monitor.OnSpeedCameraPassed(postedLimitKmh);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.9f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 8f);
        }
    }
}
