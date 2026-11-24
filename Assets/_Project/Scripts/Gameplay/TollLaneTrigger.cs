using TNLorry.Vehicle;
using UnityEngine;

namespace TNLorry.Gameplay
{
    /// <summary>
    /// Put on each lane's trigger volume. Keeps TollPlaza free of collider plumbing so one
    /// plaza prefab can carry any number of lanes (section K.8).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class TollLaneTrigger : MonoBehaviour
    {
        [SerializeField] TollPlaza plaza;
        [SerializeField] TollLane lane = TollLane.Fastag;

        void Awake()
        {
            if (plaza == null) plaza = GetComponentInParent<TollPlaza>();
            if (plaza == null)
            {
                Debug.LogError("[TollLaneTrigger] No TollPlaza found in parents.", this);
                enabled = false;
                return;
            }
            GetComponent<Collider>().isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            var truck = other.GetComponentInParent<TruckController>();
            if (truck != null) plaza.EnterLane(lane, truck);
        }

        void OnTriggerExit(Collider other)
        {
            if (other.GetComponentInParent<TruckController>() != null) plaza.ExitLane();
        }
    }
}
