using System.Collections.Generic;
using TNLorry.Core;
using TNLorry.Services;
using UnityEngine;

namespace TNLorry.Navigation
{
    public enum NavInstruction { Straight, TurnLeft, TurnRight, SlightLeft, SlightRight, Exit, Arrive, Reroute }

    /// <summary>
    /// Route following and turn-by-turn voice navigation (section K.4).
    ///
    /// Announcements are distance-triggered, not time-triggered, and each one fires once per
    /// manoeuvre: "turn left in 500 metres" repeating every frame is the fastest way to make a
    /// player mute the game.
    /// </summary>
    public class GpsService : MonoBehaviour
    {
        [Header("Announcement distances (metres)")]
        [SerializeField] float farAnnounceDistance = 500f;
        [SerializeField] float nearAnnounceDistance = 120f;
        [SerializeField] float immediateAnnounceDistance = 35f;

        [Header("Thresholds")]
        [Tooltip("Degrees of heading change that counts as a turn rather than a bend.")]
        [SerializeField] float turnAngleThreshold = 35f;
        [Tooltip("Degrees for a slight turn / fork.")]
        [SerializeField] float slightAngleThreshold = 15f;
        [Tooltip("Metres off-route before rerouting.")]
        [SerializeField] float offRouteDistance = 60f;
        [Tooltip("Seconds off-route before rerouting, so a lane change does not trigger it.")]
        [SerializeField] float offRouteGraceSeconds = 4f;

        [Header("Voice")]
        [SerializeField] AudioSource voiceSource;
        [Tooltip("Clip ids are resolved as Audio/VO_<lang>/<clipId>.")]
        [SerializeField] bool voiceEnabled = true;

        [Header("Refs")]
        [SerializeField] Transform player;

        public RoadGraph.Route ActiveRoute { get; private set; }
        public float RemainingKm { get; private set; }
        public float RemainingMinutes { get; private set; }
        public NavInstruction NextInstruction { get; private set; } = NavInstruction.Straight;
        public float DistanceToNextManoeuvre { get; private set; }
        /// <summary>Localization key for the HUD's instruction banner.</summary>
        public string NextInstructionKey { get; private set; } = "nav.continue_straight";

        RoadGraph _roads;
        LocalizationService _loc;

        int _nextNodeIndex;
        float _offRouteTimer;
        readonly HashSet<string> _announced = new HashSet<string>();
        int _destinationNodeId = -1;

        void Awake()
        {
            ServiceLocator.TryGet<RoadGraph>(out _roads);
            ServiceLocator.TryGet<LocalizationService>(out _loc);

            if (player == null)
            {
                Debug.LogError("[GpsService] No player transform assigned; disabling.", this);
                enabled = false;
            }
        }

        /// <summary>Plans a route to a destination road node and starts guidance.</summary>
        public bool SetDestination(int destinationNodeId)
        {
            if (_roads == null) return false;

            _destinationNodeId = destinationNodeId;
            int startNode = _roads.FindNearestNode(player.position);
            if (startNode < 0) return false;

            var route = _roads.FindRoute(startNode, destinationNodeId);
            if (!route.IsValid)
            {
                Debug.LogWarning("[GpsService] No route from node " + startNode + " to " + destinationNodeId);
                return false;
            }

            ActiveRoute = route;
            _nextNodeIndex = 1;
            _announced.Clear();
            _offRouteTimer = 0f;
            return true;
        }

        public void ClearRoute()
        {
            ActiveRoute = null;
            _destinationNodeId = -1;
            NextInstruction = NavInstruction.Straight;
            NextInstructionKey = "nav.continue_straight";
            RemainingKm = RemainingMinutes = 0f;
        }

        void Update()
        {
            if (ActiveRoute == null || !ActiveRoute.IsValid || _roads == null) return;

            AdvanceAlongRoute();
            UpdateRemaining();
            UpdateInstruction();
            CheckOffRoute(Time.deltaTime);
        }

        void AdvanceAlongRoute()
        {
            if (_nextNodeIndex >= ActiveRoute.NodeIds.Count) return;

            var nextNode = _roads.GetNode(ActiveRoute.NodeIds[_nextNodeIndex]);
            float distance = HorizontalDistance(player.position, nextNode.Position);

            // 25 m, not 2 m: a truck is long, the node sits at the junction centre, and
            // clipping the apex should still count as having passed it.
            if (distance < 25f)
            {
                _nextNodeIndex++;
                if (_nextNodeIndex >= ActiveRoute.NodeIds.Count) Arrive();
            }
        }

        void UpdateRemaining()
        {
            RemainingKm = 0f;
            RemainingMinutes = 0f;

            // Distance to the next node, then the whole remaining edge chain.
            if (_nextNodeIndex < ActiveRoute.NodeIds.Count)
            {
                var nextNode = _roads.GetNode(ActiveRoute.NodeIds[_nextNodeIndex]);
                RemainingKm += HorizontalDistance(player.position, nextNode.Position) / 1000f;
            }

            for (int i = _nextNodeIndex; i < ActiveRoute.EdgeIndices.Count; i++)
            {
                var edge = _roads.GetEdge(ActiveRoute.EdgeIndices[i]);
                RemainingKm += edge.LengthM / 1000f;
                RemainingMinutes += edge.TravelTimeSeconds / 60f;
            }
        }

        void UpdateInstruction()
        {
            if (_nextNodeIndex >= ActiveRoute.NodeIds.Count)
            {
                NextInstruction = NavInstruction.Arrive;
                NextInstructionKey = "nav.destination_near";
                return;
            }

            var manoeuvreNode = _roads.GetNode(ActiveRoute.NodeIds[_nextNodeIndex]);
            DistanceToNextManoeuvre = HorizontalDistance(player.position, manoeuvreNode.Position);

            NextInstruction = ClassifyManoeuvre(_nextNodeIndex);
            NextInstructionKey = KeyFor(NextInstruction, DistanceToNextManoeuvre);

            Announce();
        }

        /// <summary>
        /// Compares the heading of the edge arriving at a node with the edge leaving it.
        /// Works on the planned route, so it is immune to where the player is actually pointing.
        /// </summary>
        NavInstruction ClassifyManoeuvre(int nodeIndex)
        {
            if (nodeIndex < 1 || nodeIndex >= ActiveRoute.NodeIds.Count - 1) return NavInstruction.Arrive;

            var previous = _roads.GetNode(ActiveRoute.NodeIds[nodeIndex - 1]).Position;
            var current  = _roads.GetNode(ActiveRoute.NodeIds[nodeIndex]).Position;
            var next     = _roads.GetNode(ActiveRoute.NodeIds[nodeIndex + 1]).Position;

            var incoming = current - previous;
            var outgoing = next - current;
            incoming.y = outgoing.y = 0f;

            if (incoming.sqrMagnitude < 0.01f || outgoing.sqrMagnitude < 0.01f) return NavInstruction.Straight;

            float angle = Vector3.SignedAngle(incoming.normalized, outgoing.normalized, Vector3.up);
            float magnitude = Mathf.Abs(angle);

            if (magnitude < slightAngleThreshold) return NavInstruction.Straight;

            if (magnitude < turnAngleThreshold)
                return angle > 0f ? NavInstruction.SlightRight : NavInstruction.SlightLeft;

            return angle > 0f ? NavInstruction.TurnRight : NavInstruction.TurnLeft;
        }

        string KeyFor(NavInstruction instruction, float distance)
        {
            bool immediate = distance <= nearAnnounceDistance;

            return instruction switch
            {
                NavInstruction.TurnLeft    => immediate ? "nav.turn_left_now"  : "nav.turn_left_500",
                NavInstruction.TurnRight   => immediate ? "nav.turn_right_now" : "nav.turn_right_500",
                NavInstruction.SlightLeft  => "nav.turn_left_500",
                NavInstruction.SlightRight => "nav.turn_right_500",
                NavInstruction.Exit        => "nav.take_exit",
                NavInstruction.Arrive      => "nav.destination_near",
                NavInstruction.Reroute     => "nav.rerouting",
                _                          => "nav.continue_straight"
            };
        }

        void Announce()
        {
            if (!voiceEnabled || NextInstruction == NavInstruction.Straight) return;

            // One announcement per (manoeuvre, distance band), so each turn is called at most
            // three times: far, near, immediate.
            string band =
                DistanceToNextManoeuvre <= immediateAnnounceDistance ? "immediate" :
                DistanceToNextManoeuvre <= nearAnnounceDistance      ? "near" :
                DistanceToNextManoeuvre <= farAnnounceDistance       ? "far" : null;

            if (band == null) return;

            string token = _nextNodeIndex + ":" + band;
            if (!_announced.Add(token)) return;

            PlayVoice(NextInstructionKey);
        }

        void PlayVoice(string key)
        {
            if (voiceSource == null) return;

            // Clips live per language so the Tamil and English VO sets stay independent
            // (section P.3). A missing clip is silent, not an error — VO lands late in production.
            string language = _loc != null ? _loc.LanguageCode : LocalizationService.English;
            string path = "Audio/VO_" + (language == LocalizationService.Tamil ? "Tamil" : "English") + "/" + key;

            var clip = Resources.Load<AudioClip>(path);
            if (clip == null) return;

            voiceSource.PlayOneShot(clip);
        }

        void CheckOffRoute(float dt)
        {
            if (!_roads.TryFindNearestEdge(player.position, offRouteDistance * 2f,
                                           out int edgeIndex, out var pointOnRoad, out _))
            {
                _offRouteTimer += dt;
            }
            else
            {
                bool onPlannedEdge = ActiveRoute.EdgeIndices.Contains(edgeIndex);
                float lateral = HorizontalDistance(player.position, pointOnRoad);

                if (onPlannedEdge && lateral < offRouteDistance) _offRouteTimer = 0f;
                else _offRouteTimer += dt;
            }

            if (_offRouteTimer < offRouteGraceSeconds) return;

            _offRouteTimer = 0f;
            if (_destinationNodeId < 0) return;

            NextInstruction = NavInstruction.Reroute;
            PlayVoice("nav.rerouting");
            SetDestination(_destinationNodeId);
        }

        void Arrive()
        {
            NextInstruction = NavInstruction.Arrive;
            NextInstructionKey = "nav.destination_near";
            PlayVoice("nav.destination_near");
        }

        static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>Route polyline for the minimap line renderer.</summary>
        public void FillRoutePolyline(List<Vector3> buffer)
        {
            buffer.Clear();
            if (ActiveRoute == null || _roads == null) return;

            for (int i = Mathf.Max(0, _nextNodeIndex - 1); i < ActiveRoute.NodeIds.Count; i++)
                buffer.Add(_roads.GetNode(ActiveRoute.NodeIds[i]).Position);
        }
    }
}
