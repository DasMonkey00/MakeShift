using UnityEngine;

namespace Makeshift.Machines
{
    /// <summary>
    /// A poke-able button for greybox machines. Any hard enough knock (a hand, a thrown
    /// toaster) toggles the machine. Call Press() from a lever or UnityEvent as well.
    /// </summary>
    public class MachineSwitch : MonoBehaviour
    {
        [SerializeField] private Machine machine;
        [SerializeField] private float minImpulse = 0.4f;
        [SerializeField] private float cooldown = 0.6f;

        private float nextAllowed;

        public void Bind(Machine m) => machine = m;

        public void Press()
        {
            if (machine == null || Time.time < nextAllowed) return;
            nextAllowed = Time.time + cooldown;
            machine.Toggle();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.impulse.magnitude >= minImpulse) Press();
        }
    }
}
