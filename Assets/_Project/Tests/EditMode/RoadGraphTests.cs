using NUnit.Framework;
using TNLorry.Navigation;
using UnityEngine;

namespace TNLorry.Tests
{
    public class RoadGraphTests
    {
        static RoadGraph.Node Node(int id, float x, float z) => new RoadGraph.Node
        {
            Id = id,
            Position = new Vector3(x, 0f, z),
            IsIntersection = true
        };

        static RoadGraph.Edge Edge(int from, int to, float lengthM, float speedKmh = 80f,
                                  RoadGraph.EdgeFlags flags = RoadGraph.EdgeFlags.None)
            => new RoadGraph.Edge
            {
                From = from, To = to, LengthM = lengthM, SpeedLimitKmh = speedKmh, Lanes = 2, Flags = flags
            };

        /// <summary>
        /// A -- B -- C in a line, plus a long detour A -- D -- C.
        /// Straight route is 2000 m, detour is 6000 m.
        /// </summary>
        static RoadGraph Diamond()
        {
            var graph = new RoadGraph();
            graph.AddNode(Node(0, 0f, 0f));        // A
            graph.AddNode(Node(1, 0f, 1000f));     // B
            graph.AddNode(Node(2, 0f, 2000f));     // C
            graph.AddNode(Node(3, 2500f, 1000f));  // D, off to the side

            graph.AddEdge(Edge(0, 1, 1000f), true);
            graph.AddEdge(Edge(1, 2, 1000f), true);
            graph.AddEdge(Edge(0, 3, 3000f), true);
            graph.AddEdge(Edge(3, 2, 3000f), true);
            return graph;
        }

        [Test]
        public void BidirectionalEdgeIsTraversableBothWays()
        {
            var graph = Diamond();

            Assert.IsTrue(graph.FindRoute(0, 2).IsValid);
            Assert.IsTrue(graph.FindRoute(2, 0).IsValid);
        }

        [Test]
        public void OneWayEdgeIsNotTraversableBackwards()
        {
            var graph = new RoadGraph();
            graph.AddNode(Node(0, 0f, 0f));
            graph.AddNode(Node(1, 0f, 1000f));
            graph.AddEdge(Edge(0, 1, 1000f, flags: RoadGraph.EdgeFlags.OneWay), bidirectional: false);

            Assert.IsTrue(graph.FindRoute(0, 1).IsValid, "Forward along a one-way must work.");
            Assert.IsFalse(graph.FindRoute(1, 0).IsValid,
                "A one-way flyover must not be routable backwards — that is the point of a directed graph.");
        }

        [Test]
        public void FindsTheShortestOfTwoRoutes()
        {
            var graph = Diamond();
            var route = graph.FindRoute(0, 2);

            Assert.IsTrue(route.IsValid);
            Assert.AreEqual(2f, route.LengthKm, 0.01f, "Must take the 2 km straight line, not the 6 km detour.");
            Assert.AreEqual(3, route.NodeIds.Count, "A -> B -> C is three nodes.");
            Assert.AreEqual(1, route.NodeIds[1], "The middle node must be B.");
        }

        [Test]
        public void SameNodeRouteIsValidAndEmpty()
        {
            var route = Diamond().FindRoute(1, 1);

            Assert.IsTrue(route.IsValid);
            Assert.AreEqual(0f, route.LengthKm);
            Assert.IsEmpty(route.EdgeIndices);
        }

        [Test]
        public void UnreachableNodeReturnsAnInvalidRoute()
        {
            var graph = Diamond();
            graph.AddNode(Node(99, 50000f, 50000f));   // island, no edges

            Assert.IsFalse(graph.FindRoute(0, 99).IsValid);
        }

        [Test]
        public void MissingNodeReturnsAnInvalidRouteRatherThanThrowing()
        {
            var graph = Diamond();

            Assert.IsFalse(graph.FindRoute(0, 1234).IsValid);
            Assert.IsFalse(graph.FindRoute(1234, 0).IsValid);
        }

        [Test]
        public void PrefersTheFasterRouteEvenWhenItIsLonger()
        {
            // Two ways from A to C: a short slow road and a longer fast one. Cost is time,
            // so the motorway should win — this is what makes the router feel sane on NH44.
            var graph = new RoadGraph();
            graph.AddNode(Node(0, 0f, 0f));
            graph.AddNode(Node(1, 500f, 1000f));    // slow village road midpoint
            graph.AddNode(Node(2, 0f, 2000f));
            graph.AddNode(Node(3, 1200f, 1000f));   // motorway midpoint

            graph.AddEdge(Edge(0, 1, 1100f, speedKmh: 25f), true);   // 2200 m at 25 km/h = 317 s
            graph.AddEdge(Edge(1, 2, 1100f, speedKmh: 25f), true);
            graph.AddEdge(Edge(0, 3, 1600f, speedKmh: 100f), true);  // 3200 m at 100 km/h = 115 s
            graph.AddEdge(Edge(3, 2, 1600f, speedKmh: 100f), true);

            var route = graph.FindRoute(0, 2);

            Assert.IsTrue(route.IsValid);
            Assert.AreEqual(3, route.NodeIds[1], "Must route via the fast road even though it is longer.");
            Assert.Greater(route.LengthKm, 3f);
        }

        [Test]
        public void TollAversionCanPushTheRouteOntoAFreeRoad()
        {
            // Equal-length roads, one tolled. With aversion off the tolled road's higher speed
            // wins; with aversion high the free road should be chosen instead.
            RoadGraph Build(float aversion)
            {
                var g = new RoadGraph { TollAversionSecondsPerRupee = aversion };
                g.AddNode(Node(0, 0f, 0f));
                g.AddNode(Node(1, 800f, 1000f));    // free road
                g.AddNode(Node(2, 0f, 2000f));
                g.AddNode(Node(3, -800f, 1000f));   // tolled road
                g.AddEdge(Edge(0, 1, 1300f, 70f), true);
                g.AddEdge(Edge(1, 2, 1300f, 70f), true);
                g.AddEdge(Edge(0, 3, 1300f, 90f, RoadGraph.EdgeFlags.Toll), true);
                g.AddEdge(Edge(3, 2, 1300f, 90f, RoadGraph.EdgeFlags.Toll), true);
                return g;
            }

            Assert.AreEqual(3, Build(0f).FindRoute(0, 2).NodeIds[1],
                "With no toll aversion the faster tolled road should win.");

            Assert.AreEqual(1, Build(40f).FindRoute(0, 2).NodeIds[1],
                "With heavy toll aversion the free road should win.");
        }

        [Test]
        public void GhatSectionsAreFlaggedAndPenalised()
        {
            var graph = new RoadGraph();
            graph.AddNode(Node(0, 0f, 0f));
            graph.AddNode(Node(1, 0f, 1000f));
            graph.AddEdge(Edge(0, 1, 1000f, 40f, RoadGraph.EdgeFlags.Ghat), true);

            var route = graph.FindRoute(0, 1);

            Assert.IsTrue(route.IsValid);
            Assert.IsTrue(route.HasGhatSection, "The economy's ghat premium depends on this flag.");
        }

        [Test]
        public void TollSegmentLengthIsAccumulated()
        {
            var graph = new RoadGraph { TollAversionSecondsPerRupee = 0f };
            graph.AddNode(Node(0, 0f, 0f));
            graph.AddNode(Node(1, 0f, 1000f));
            graph.AddNode(Node(2, 0f, 2000f));
            graph.AddEdge(Edge(0, 1, 1000f, 80f, RoadGraph.EdgeFlags.Toll), true);
            graph.AddEdge(Edge(1, 2, 1000f, 80f), true);

            var route = graph.FindRoute(0, 2);

            Assert.AreEqual(1f, route.TollSegmentKm, 0.01f, "Only the tolled kilometre should be billed.");
            Assert.AreEqual(2f, route.LengthKm, 0.01f);
        }

        [Test]
        public void EdgeLengthIsDerivedFromGeometryWhenLeftAtZero()
        {
            var graph = new RoadGraph();
            graph.AddNode(Node(0, 0f, 0f));
            graph.AddNode(Node(1, 300f, 400f));     // 500 m away
            graph.AddEdge(Edge(0, 1, 0f), true);

            Assert.AreEqual(0.5f, graph.FindRoute(0, 1).LengthKm, 0.01f);
        }

        [Test]
        public void FindNearestNodePicksTheClosest()
        {
            var graph = Diamond();

            Assert.AreEqual(1, graph.FindNearestNode(new Vector3(10f, 0f, 950f)));
            Assert.AreEqual(0, graph.FindNearestNode(new Vector3(-50f, 0f, -50f)));
            Assert.AreEqual(-1, new RoadGraph().FindNearestNode(Vector3.zero), "An empty graph has no nearest node.");
        }

        [Test]
        public void NearestEdgeGivesThePointOnTheRoadAndItsDirection()
        {
            var graph = new RoadGraph();
            graph.AddNode(Node(0, 0f, 0f));
            graph.AddNode(Node(1, 0f, 1000f));
            graph.AddEdge(Edge(0, 1, 1000f), bidirectional: false);

            // Standing 20 m to the side of the midpoint.
            bool found = graph.TryFindNearestEdge(new Vector3(20f, 0f, 500f), 50f,
                                                  out int edgeIndex, out var point, out var direction);

            Assert.IsTrue(found);
            Assert.AreEqual(0, edgeIndex);
            Assert.AreEqual(0f, point.x, 0.01f, "The snapped point must be on the road centre line.");
            Assert.AreEqual(500f, point.z, 0.01f);
            Assert.AreEqual(Vector3.forward, direction, "Direction drives wrong-side detection.");
        }

        [Test]
        public void NearestEdgeRespectsTheSearchRadius()
        {
            var graph = new RoadGraph();
            graph.AddNode(Node(0, 0f, 0f));
            graph.AddNode(Node(1, 0f, 1000f));
            graph.AddEdge(Edge(0, 1, 1000f), true);

            Assert.IsFalse(graph.TryFindNearestEdge(new Vector3(500f, 0f, 500f), 50f, out _, out _, out _),
                "Well off-road must report no nearby edge, so off-route detection fires.");
        }

        [Test]
        public void RoutesAcrossALongChainWithoutStackOrCycleProblems()
        {
            // 300 nodes in a line: exercises the binary heap and the cycle guard on a route
            // the length of a real Chennai-to-Madurai haul.
            var graph = new RoadGraph();
            const int count = 300;

            for (int i = 0; i < count; i++) graph.AddNode(Node(i, 0f, i * 500f));
            for (int i = 1; i < count; i++) graph.AddEdge(Edge(i - 1, i, 500f), true);

            var route = graph.FindRoute(0, count - 1);

            Assert.IsTrue(route.IsValid);
            Assert.AreEqual(count, route.NodeIds.Count);
            Assert.AreEqual((count - 1) * 0.5f, route.LengthKm, 0.01f);
            Assert.Greater(route.EstimatedMinutes, 0f);
        }

        [Test]
        public void ClearEmptiesTheGraph()
        {
            var graph = Diamond();
            Assert.Greater(graph.NodeCount, 0);

            graph.Clear();

            Assert.AreEqual(0, graph.NodeCount);
            Assert.AreEqual(0, graph.EdgeCount);
            Assert.IsFalse(graph.HasNode(0));
        }
    }
}
