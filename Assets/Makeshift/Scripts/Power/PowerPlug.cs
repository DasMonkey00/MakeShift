using UnityEngine;

namespace Makeshift.Power
{
    /// <summary>
    /// The loose end of a cable (jumper-cable clamp, plug, bare wire).
    /// Point it at its PowerSource. Touch it to a PowerPort to connect.
    /// Needs a trigger Collider on this object.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class PowerPlug : MonoBehaviour
    {
        [SerializeField] private PowerSource source;

        public PowerSource Source => source;
        public PowerPort ConnectedPort { get; internal set; }

        public void SetSource(PowerSource s) => source = s;
    }
}
