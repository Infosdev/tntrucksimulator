using System;
using UnityEngine;

namespace TNLorry.Navigation
{
    /// <summary>
    /// Authored, serialisable form of the road network (section L). The map tool writes this;
    /// at runtime BuildGraph() turns it into the RoadGraph used for routing.
    ///
    /// Kept separate from RoadGraph so the runtime structure can change (spatial hashing,
    /// burst-friendly arrays) without re-authoring content.
    /// </summary>
    [CreateAssetMenu(menuName = "TN/Road Graph", fileName = "RoadGraph_")]
    public class RoadGraphAsset : ScriptableObject
    {
        [Serializable]
        public class NodeEntry
        {
            public int     id;
            public Vector3 position;
            public bool    isIntersection;
            public bool    hasSignal;
            [Tooltip("Set only on depot gate nodes; must match CityData.id.")]
            public string  cityId = "";
        }

        [Serializable]
        public class EdgeEntry
        {
            public int   from;
            public int   to;
            [Tooltip("Leave 0 to auto-compute from node positions.")]
            public float lengthM;
            public float speedLimitKmh = 80f;
            public int   lanes = 2;
            public bool  toll;
            public bool  ghat;
            public bool  urban;
            public bool  bridge;
            public bool  unpaved;
            [Tooltip("Off for divided highways authored as two one-way carriageways.")]
            public bool  bidirectional = true;
        }

        [Tooltip("Human-readable label, e.g. NH44 Krishnagiri-Madurai.")]
        public string routeName = "";
        public NodeEntry[] nodes = new NodeEntry[0];
        public EdgeEntry[] edges = new EdgeEntry[0];

        public RoadGraph BuildGraph()
        {
            var graph = new RoadGraph();

            foreach (var n in nodes)
            {
                if (n == null) continue;
                graph.AddNode(new RoadGraph.Node
                {
                    Id             = n.id,
                    Position       = n.position,
                    IsIntersection = n.isIntersection,
                    HasSignal      = n.hasSignal,
                    CityId         = n.cityId
                });
            }

            foreach (var e in edges)
            {
                if (e == null) continue;

                var flags = RoadGraph.EdgeFlags.None;
                if (e.toll)    flags |= RoadGraph.EdgeFlags.Toll;
                if (e.ghat)    flags |= RoadGraph.EdgeFlags.Ghat;
                if (e.urban)   flags |= RoadGraph.EdgeFlags.Urban;
                if (e.bridge)  flags |= RoadGraph.EdgeFlags.Bridge;
                if (e.unpaved) flags |= RoadGraph.EdgeFlags.Unpaved;
                if (!e.bidirectional) flags |= RoadGraph.EdgeFlags.OneWay;

                graph.AddEdge(new RoadGraph.Edge
                {
                    From          = e.from,
                    To            = e.to,
                    LengthM       = e.lengthM,
                    SpeedLimitKmh = e.speedLimitKmh,
                    Lanes         = e.lanes,
                    Flags         = flags
                }, e.bidirectional);
            }

            return graph;
        }

        void OnValidate()
        {
            // Speed limits above the A* heuristic's assumed maximum break admissibility and
            // can make the router return a non-optimal path, so clamp at author time.
            foreach (var e in edges)
            {
                if (e == null) continue;
                if (e.speedLimitKmh > 100f)
                {
                    Debug.LogWarning(name + ": edge " + e.from + "->" + e.to +
                                     " speed limit clamped to 100 (A* heuristic ceiling).", this);
                    e.speedLimitKmh = 100f;
                }
                if (e.speedLimitKmh < 5f) e.speedLimitKmh = 5f;
                if (e.lanes < 1) e.lanes = 1;
            }
        }
    }
}
