using UnityEngine;

namespace TNLorry.World
{
    /// <summary>
    /// Marker: put this on a scene root that must NOT be moved by a floating-origin shift —
    /// the manager hierarchy, the UI canvas root, the audio listener rig, persistent singletons.
    ///
    /// Note that the player truck IS shifted, by its own Rigidbody-aware handler in
    /// TruckController, so do not exempt it here.
    /// </summary>
    public class FloatingOriginExempt : MonoBehaviour { }
}
