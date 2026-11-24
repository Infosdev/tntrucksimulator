using UnityEngine;

namespace TNLorry.Traffic
{
    /// <summary>
    /// A streamed tile's share of the lane network (section K.10). Put one on each
    /// Tile_X_Y scene root alongside its authored lane waypoints.
    ///
    /// Registers on enable and unregisters on disable, so the lane network grows and shrinks
    /// with the loaded tile set and AI traffic can drive across tile boundaries.
    ///
    /// Lane ids must be globally unique. The convention the map tool should follow is
    /// `tileIndex * 1000 + localLaneIndex`, because `nextLaneIds` on a lane at the edge of one
    /// tile points into a lane owned by the next one.
    /// </summary>
    public class LaneSet : MonoBehaviour
    {
        [SerializeField] LaneGraph.Lane[] lanes = new LaneGraph.Lane[0];

        public LaneGraph.Lane[] Lanes => lanes;
        public bool IsRegistered { get; private set; }

        void OnEnable()
        {
            var graph = LaneGraph.Instance;
            if (graph == null)
            {
                // Expected only if a tile scene is opened on its own; in the normal flow
                // 10_World_Core and its LaneGraph are always loaded first.
                Debug.LogWarning("[LaneSet] No LaneGraph in the scene yet; " + name +
                                 " will carry no traffic.", this);
                return;
            }

            graph.AddLanes(lanes, this);
            IsRegistered = true;
        }

        void OnDisable()
        {
            if (!IsRegistered) return;

            // Instance may already be gone when the whole scene is tearing down, which is fine:
            // nothing is left to drive on these lanes either.
            LaneGraph.Instance?.RemoveLanes(lanes);
            IsRegistered = false;
        }

        void OnDrawGizmosSelected()
        {
            if (lanes == null) return;
            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.8f);

            foreach (var lane in lanes)
            {
                if (lane?.waypoints == null) continue;
                for (int i = 1; i < lane.waypoints.Length; i++)
                {
                    if (lane.waypoints[i - 1] == null || lane.waypoints[i] == null) continue;
                    Gizmos.DrawLine(lane.waypoints[i - 1].position, lane.waypoints[i].position);
                }
            }
        }
    }
}
