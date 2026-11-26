using UnityEngine;

namespace Makeshift.Machines
{
    /// <summary>
    /// Anything that can be bolted into a machine: a power drill, a toaster, a seatbelt,
    /// a doll's head. Reliability is how well it does the job it's being used for.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class MachinePart : MonoBehaviour
    {
        [Tooltip("Short id sockets can ask for, e.g. 'motor', 'battery', 'frame', 'toaster'.")]
        [SerializeField] private string partId = "junk";
        [Tooltip("0 = barely holds together, 1 = solid. Junk sits around 0.4 to 0.7.")]
        [SerializeField, Range(0f, 1f)] private float reliability = 0.6f;

        public string PartId => partId;
        public float Reliability => reliability;
        public Rigidbody Body { get; private set; }
        public PartSocket Socket { get; internal set; }
        public bool IsSocketed => Socket != null;

        private void Awake()
        {
            Body = GetComponent<Rigidbody>();
        }

        /// <summary>Wear and tear: bonks and backfires can knock reliability down.</summary>
        public void Damage(float amount)
        {
            reliability = Mathf.Clamp01(reliability - amount);
        }

        /// <summary>
        /// A seated part cannot be grabbed or pulled out. Ejecting (a backfire) gives the hand back its grip.
        /// </summary>
        public void SetSeated(bool seated)
        {
            foreach (var grab in GetComponents<Autohand.Grabbable>())
                grab.enabled = !seated;
        }
    }
}
