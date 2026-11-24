using System.Collections.Generic;
using TNLorry.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TNLorry.World
{
    /// <summary>
    /// Shifts the whole world back toward the origin when the player strays too far
    /// (section K.9). Unity's float precision degrades past ~5-10 km from (0,0,0) and the
    /// truck starts to jitter — on a Chennai-to-Madurai route that is unavoidable without this.
    ///
    /// Runs in FixedUpdate, not LateUpdate: moving Rigidbody transforms outside the physics
    /// step makes PhysX see teleports, which shows up as a one-frame suspension spasm on
    /// every vehicle in the scene.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class FloatingOrigin : MonoBehaviour
    {
        [SerializeField] Transform player;
        [Tooltip("Distance from origin that triggers a shift, in metres. Keep well under 10 km.")]
        [SerializeField] float threshold = 2000f;
        [Tooltip("Also shift particle systems. Costs a little; needed for rain and dust trails.")]
        [SerializeField] bool shiftParticles = true;

        /// <summary>
        /// Total offset applied since the session began. Anything that stores an absolute world
        /// position across a shift (saved player position, map markers) must add this back.
        /// </summary>
        public static Vector3 TotalOffset { get; private set; }

        /// <summary>Direct callback for systems that cannot afford the event bus in a hot path.</summary>
        public static event System.Action<Vector3> OnShift;

        readonly List<ParticleSystem.Particle> _particleBuffer = new List<ParticleSystem.Particle>(1024);

        void Awake()
        {
            if (player == null)
            {
                Debug.LogError("[FloatingOrigin] No player transform assigned; disabling.", this);
                enabled = false;
            }
        }

        void FixedUpdate()
        {
            var position = player.position;
            position.y = 0f;                 // never shift vertically; terrain height is absolute

            if (position.magnitude < threshold) return;

            Shift(position);
        }

        void Shift(Vector3 offset)
        {
            // Every loaded scene, not just the active one — tile scenes are additive.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    // Skip our own manager hierarchy and anything explicitly opted out, or the
                    // managers walk away from the world they are managing.
                    if (root.GetComponent<FloatingOriginExempt>() != null) continue;
                    root.transform.position -= offset;
                }
            }

            if (shiftParticles) ShiftParticles(offset);

            TotalOffset += offset;

            OnShift?.Invoke(offset);
            EventBus.Publish(new OriginShiftedEvent { Offset = offset });
        }

        void ShiftParticles(Vector3 offset)
        {
            // World-simulated particles keep absolute positions and would be left behind.
            foreach (var system in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                if (system.main.simulationSpace != ParticleSystemSimulationSpace.World) continue;

                int count = system.GetParticles(_particleBuffer, system.particleCount);
                if (count == 0) continue;

                for (int i = 0; i < count; i++)
                {
                    var particle = _particleBuffer[i];
                    particle.position -= offset;
                    _particleBuffer[i] = particle;
                }
                system.SetParticles(_particleBuffer, count);
            }
        }

        /// <summary>Converts a stored absolute world position into current scene space.</summary>
        public static Vector3 ToSceneSpace(Vector3 absolutePosition) => absolutePosition - TotalOffset;

        /// <summary>Converts a current scene position into a shift-independent absolute one, for saving.</summary>
        public static Vector3 ToAbsolute(Vector3 scenePosition) => scenePosition + TotalOffset;

        public static void ResetOffset() => TotalOffset = Vector3.zero;
    }
}
