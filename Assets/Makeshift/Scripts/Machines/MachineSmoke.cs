using UnityEngine;

namespace Makeshift.Machines
{
    /// <summary>Smoke while a machine runs (or sits overheated). It works, badly.</summary>
    public class MachineSmoke : MonoBehaviour
    {
        [SerializeField] private Machine machine;
        [SerializeField] private ParticleSystem smoke;

        private void Update()
        {
            if (machine == null || smoke == null) return;
            bool on = machine.State == MachineState.Running || machine.State == MachineState.Sputtering
                || machine.State == MachineState.Overheated;
            if (on && !smoke.isEmitting) smoke.Play();
            else if (!on && smoke.isEmitting) smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}
