using System;
using System.Collections.Generic;
using UnityEngine;

namespace TNLorry.Navigation
{
    /// <summary>
    /// Road network as a directed graph: nodes are intersections/waypoints, edges are road
    /// segments carrying length, speed limit, toll and ghat flags (section K.4).
    ///
    /// Directed, not undirected, because one-way flyovers and divided highways are everywhere
    /// on NH44/NH48 — and because wrong-side detection (section K.7) needs edge direction.
    /// Authored by the road tool into a RoadGraphAsset; this is the runtime form.
    /// </summary>
    public class RoadGraph
    {
        public struct Node
        {
            public int     Id;
            public Vector3 Position;
            public bool    IsIntersection;
            public bool    HasSignal;
            public string  CityId;        // "" unless this node is a depot gate
        }

        [Flags]
        public enum EdgeFlags
        {
            None     = 0,
            Toll     = 1 << 0,
            Ghat     = 1 << 1,   // hairpins / steep grade -> pay premium, slower planning
            Urban    = 1 << 2,   // speed breakers, pedestrians
            Bridge   = 1 << 3,
            OneWay   = 1 << 4,
            Unpaved  = 1 << 5
        }

        public struct Edge
        {
            public int       From;
            public int       To;
            public float     LengthM;
            public float     SpeedLimitKmh;
            public int       Lanes;
            public EdgeFlags Flags;

            public bool Has(EdgeFlags f) => (Flags & f) != 0;

            /// <summary>Free-flow traversal time in seconds — the primary A* cost.</summary>
            public float TravelTimeSeconds => LengthM / Mathf.Max(5f, SpeedLimitKmh) * 3.6f;
        }

        readonly List<Node> _nodes = new List<Node>();
        readonly List<Edge> _edges = new List<Edge>();

        /// <summary>node id -> indices into _edges that start at that node.</summary>
        readonly Dictionary<int, List<int>> _outgoing = new Dictionary<int, List<int>>();
        readonly Dictionary<int, int> _nodeIndexById  = new Dictionary<int, int>();

        public int NodeCount => _nodes.Count;
        public int EdgeCount => _edges.Count;

        /// <summary>
        /// Seconds of extra cost charged per rupee of toll. 0 = ignore tolls entirely,
        /// higher values make the router detour around plazas (the "avoid tolls" setting).
        /// </summary>
        public float TollAversionSecondsPerRupee = 0.6f;

        /// <summary>Loaded trucks crawl up ghats, so planning should not treat them as fast roads.</summary>
        public float GhatTimePenaltyMultiplier = 1.6f;

        // ── Construction ────────────────────────────────────────────────────

        public void AddNode(Node node)
        {
            if (_nodeIndexById.ContainsKey(node.Id))
            {
                Debug.LogError("[RoadGraph] Duplicate node id " + node.Id);
                return;
            }
            _nodeIndexById[node.Id] = _nodes.Count;
            _nodes.Add(node);
        }

        public void AddEdge(Edge edge, bool bidirectional)
        {
            AddDirectedEdge(edge);
            if (!bidirectional) return;

            var reverse = edge;
            reverse.From = edge.To;
            reverse.To   = edge.From;
            reverse.Flags &= ~EdgeFlags.OneWay;
            AddDirectedEdge(reverse);
        }

        void AddDirectedEdge(Edge edge)
        {
            if (!_nodeIndexById.ContainsKey(edge.From) || !_nodeIndexById.ContainsKey(edge.To))
            {
                Debug.LogError("[RoadGraph] Edge " + edge.From + "->" + edge.To + " references a missing node.");
                return;
            }

            if (edge.LengthM <= 0f)
                edge.LengthM = Vector3.Distance(GetNode(edge.From).Position, GetNode(edge.To).Position);

            int index = _edges.Count;
            _edges.Add(edge);

            if (!_outgoing.TryGetValue(edge.From, out var list))
            {
                list = new List<int>(4);
                _outgoing[edge.From] = list;
            }
            list.Add(index);
        }

        public Node GetNode(int id)
            => _nodeIndexById.TryGetValue(id, out var i) ? _nodes[i] : default;

        public bool HasNode(int id) => _nodeIndexById.ContainsKey(id);

        public IReadOnlyList<int> OutgoingEdgeIndices(int nodeId)
            => _outgoing.TryGetValue(nodeId, out var list) ? (IReadOnlyList<int>)list : Array.Empty<int>();

        public Edge GetEdge(int edgeIndex) => _edges[edgeIndex];

        public void Clear()
        {
            _nodes.Clear();
            _edges.Clear();
            _outgoing.Clear();
            _nodeIndexById.Clear();
        }

        // ── Queries ─────────────────────────────────────────────────────────

        /// <summary>Nearest node to a world position. Linear scan — fine for a few thousand nodes.</summary>
        public int FindNearestNode(Vector3 position)
        {
            int best = -1;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < _nodes.Count; i++)
            {
                float sqr = (_nodes[i].Position - position).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = i; }
            }
            return best >= 0 ? _nodes[best].Id : -1;
        }

        /// <summary>
        /// Nearest road edge and the closest point on it, for wrong-side detection and for
        /// snapping the GPS marker to the road.
        /// </summary>
        public bool TryFindNearestEdge(Vector3 position, float maxDistance,
                                       out int edgeIndex, out Vector3 pointOnRoad, out Vector3 direction)
        {
            edgeIndex   = -1;
            pointOnRoad = position;
            direction   = Vector3.forward;

            float bestSqr = maxDistance * maxDistance;
            for (int i = 0; i < _edges.Count; i++)
            {
                var a = GetNode(_edges[i].From).Position;
                var b = GetNode(_edges[i].To).Position;
                var p = ClosestPointOnSegment(a, b, position);

                float sqr = (p - position).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                bestSqr     = sqr;
                edgeIndex   = i;
                pointOnRoad = p;
                direction   = (b - a).sqrMagnitude > 0.001f ? (b - a).normalized : Vector3.forward;
            }
            return edgeIndex >= 0;
        }

        static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            var ab = b - a;
            float lengthSqr = ab.sqrMagnitude;
            if (lengthSqr < 0.0001f) return a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / lengthSqr);
            return a + ab * t;
        }

        // ── A* routing (section K.4) ────────────────────────────────────────

        readonly Dictionary<int, float>  _gScore  = new Dictionary<int, float>();
        readonly Dictionary<int, int>    _cameFromEdge = new Dictionary<int, int>();
        readonly MinHeap                 _open    = new MinHeap();
        readonly HashSet<int>            _closed  = new HashSet<int>();

        /// <summary>
        /// Cost is travel time in seconds, plus toll aversion, plus a ghat penalty.
        /// The heuristic is straight-line distance divided by the fastest speed limit in the
        /// graph, which keeps it admissible (never overestimates) so A* stays optimal.
        /// </summary>
        public Route FindRoute(int startNodeId, int goalNodeId)
        {
            var route = new Route();
            if (!HasNode(startNodeId) || !HasNode(goalNodeId)) return route;
            if (startNodeId == goalNodeId) { route.IsValid = true; return route; }

            _gScore.Clear();
            _cameFromEdge.Clear();
            _closed.Clear();
            _open.Clear();

            var goalPos = GetNode(goalNodeId).Position;
            _gScore[startNodeId] = 0f;
            _open.Push(startNodeId, Heuristic(GetNode(startNodeId).Position, goalPos));

            while (_open.Count > 0)
            {
                int current = _open.Pop();
                if (current == goalNodeId) return Reconstruct(startNodeId, goalNodeId);
                if (!_closed.Add(current)) continue;

                float currentG = _gScore[current];

                foreach (int edgeIndex in OutgoingEdgeIndices(current))
                {
                    var edge = _edges[edgeIndex];
                    if (_closed.Contains(edge.To)) continue;

                    float stepCost = EdgeCost(edge);
                    float tentative = currentG + stepCost;

                    if (_gScore.TryGetValue(edge.To, out var existing) && tentative >= existing) continue;

                    _gScore[edge.To]       = tentative;
                    _cameFromEdge[edge.To] = edgeIndex;
                    _open.Push(edge.To, tentative + Heuristic(GetNode(edge.To).Position, goalPos));
                }
            }

            return route;   // unreachable
        }

        float EdgeCost(Edge edge)
        {
            float cost = edge.TravelTimeSeconds;
            if (edge.Has(EdgeFlags.Ghat))    cost *= GhatTimePenaltyMultiplier;
            if (edge.Has(EdgeFlags.Unpaved)) cost *= 1.4f;
            if (edge.Has(EdgeFlags.Toll))    cost += edge.LengthM / 1000f * 2.4f * TollAversionSecondsPerRupee;
            return cost;
        }

        float Heuristic(Vector3 from, Vector3 to)
        {
            const float fastestKmh = 100f;    // must be >= any speed limit in the graph
            return Vector3.Distance(from, to) / fastestKmh * 3.6f;
        }

        Route Reconstruct(int startNodeId, int goalNodeId)
        {
            var route = new Route { IsValid = true };
            var edges = new List<int>(64);

            int cursor = goalNodeId;
            int guard  = 0;
            while (cursor != startNodeId)
            {
                if (!_cameFromEdge.TryGetValue(cursor, out var edgeIndex)) { route.IsValid = false; return route; }
                edges.Add(edgeIndex);
                cursor = _edges[edgeIndex].From;

                if (++guard > _edges.Count + 1) { route.IsValid = false; return route; }  // cycle guard
            }
            edges.Reverse();

            route.EdgeIndices = edges;
            route.NodeIds     = new List<int>(edges.Count + 1) { startNodeId };

            foreach (int edgeIndex in edges)
            {
                var edge = _edges[edgeIndex];
                route.NodeIds.Add(edge.To);
                route.LengthKm      += edge.LengthM / 1000f;
                route.EstimatedSeconds += edge.TravelTimeSeconds;
                if (edge.Has(EdgeFlags.Ghat)) route.HasGhatSection = true;
                if (edge.Has(EdgeFlags.Toll)) route.TollSegmentKm  += edge.LengthM / 1000f;
            }

            return route;
        }

        /// <summary>A planned route: the edge sequence plus the figures the HUD and economy need.</summary>
        public class Route
        {
            public bool      IsValid;
            public List<int> NodeIds     = new List<int>();
            public List<int> EdgeIndices = new List<int>();
            public float     LengthKm;
            public float     EstimatedSeconds;
            public bool      HasGhatSection;
            public float     TollSegmentKm;

            public float EstimatedMinutes => EstimatedSeconds / 60f;
        }

        /// <summary>
        /// Binary min-heap keyed by f-score. A sorted list would be O(n) per insert and the
        /// open set gets large on a 400 km route, so the heap is worth the 60 lines.
        /// </summary>
        class MinHeap
        {
            readonly List<int>   _ids   = new List<int>(256);
            readonly List<float> _costs = new List<float>(256);

            public int Count => _ids.Count;

            public void Clear() { _ids.Clear(); _costs.Clear(); }

            public void Push(int id, float cost)
            {
                _ids.Add(id);
                _costs.Add(cost);

                int child = _ids.Count - 1;
                while (child > 0)
                {
                    int parent = (child - 1) / 2;
                    if (_costs[parent] <= _costs[child]) break;
                    Swap(parent, child);
                    child = parent;
                }
            }

            public int Pop()
            {
                int top = _ids[0];
                int last = _ids.Count - 1;

                _ids[0]   = _ids[last];
                _costs[0] = _costs[last];
                _ids.RemoveAt(last);
                _costs.RemoveAt(last);

                int parent = 0;
                while (true)
                {
                    int left = parent * 2 + 1, right = left + 1, smallest = parent;
                    if (left  < _ids.Count && _costs[left]  < _costs[smallest]) smallest = left;
                    if (right < _ids.Count && _costs[right] < _costs[smallest]) smallest = right;
                    if (smallest == parent) break;
                    Swap(parent, smallest);
                    parent = smallest;
                }
                return top;
            }

            void Swap(int a, int b)
            {
                (_ids[a], _ids[b])     = (_ids[b], _ids[a]);
                (_costs[a], _costs[b]) = (_costs[b], _costs[a]);
            }
        }
    }
}
