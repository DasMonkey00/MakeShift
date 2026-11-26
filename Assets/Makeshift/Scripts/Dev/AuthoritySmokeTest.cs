using System.Collections;
using Makeshift.Combat;
using Makeshift.Core;
using Makeshift.Fire;
using Makeshift.Machines;
using UnityEngine;

namespace Makeshift.Dev
{
    /// <summary>
    /// Headless check of the netcode seam, no Fusion needed: pretend we are not the authority and
    /// confirm state changes are relayed instead of run, mirrored state lands, and the proxy
    /// does not simulate.
    /// </summary>
    public class AuthoritySmokeTest : SmokeSuite
    {
        private bool authority = true;
        private int relayed;
        private int lastCommand = -1;
        private int hitsRelayed;

        public override IEnumerator Run()
        {
            NetAuthority.HasAuthority = c => authority;
            NetAuthority.Relay = (c, cmd, arg) => { relayed++; lastCommand = cmd; };
            NetAuthority.RelayHit = (c, amt, p, d) => hitsRelayed++;

            // ---- Damageable ----
            var dummy = new GameObject("AuthDummy").AddComponent<Damageable>();
            yield return null;
            float full = dummy.Health;
            authority = false;
            dummy.Hit(5f, Vector3.zero, Vector3.up);
            Check(dummy.Health == full && hitsRelayed == 1, "non-authority hit is relayed, not applied");
            int hitEvents = 0;
            dummy.onHit = new UnityEngine.Events.UnityEvent<float>();
            dummy.onHit.AddListener(_ => hitEvents++);
            dummy.ApplyRemoteHealth(full - 7f);
            Check(Mathf.Approximately(dummy.Health, full - 7f) && hitEvents == 1, "mirrored health lands and fires onHit");
            authority = true;
            dummy.Hit(3f, Vector3.zero, Vector3.up);
            Check(Mathf.Approximately(dummy.Health, full - 10f) && hitsRelayed == 1, "authority hit applies directly");

            // ---- Flammable ----
            var log = new GameObject("AuthLog").AddComponent<Flammable>();
            yield return null;
            authority = false;
            relayed = 0;
            log.Ignite();
            Check(!log.IsBurning && relayed == 1 && lastCommand == (int)FlammableCommand.Ignite,
                "non-authority Ignite is relayed, not applied");
            log.ApplyRemoteBurning(true);
            Check(log.IsBurning, "mirrored burning state lands");
            authority = true;
            log.Extinguish(1f);
            Check(!log.IsBurning, "authority Extinguish applies directly");
            authority = false;
            log.ApplyRemoteBurning(false);

            // ---- Machine ----
            var machine = new GameObject("AuthMachine").AddComponent<Machine>();
            yield return null;
            authority = false;
            relayed = 0;
            machine.TurnOn();
            Check(!machine.SwitchedOn && relayed == 1 && lastCommand == (int)MachineCommand.TurnOn,
                "non-authority TurnOn is relayed, not applied");
            machine.Bonk();
            Check(relayed == 2 && lastCommand == (int)MachineCommand.Bonk, "non-authority Bonk is relayed");

            MachineState seen = MachineState.Off;
            machine.onStateChanged = new UnityEngine.Events.UnityEvent<MachineState>();
            machine.onStateChanged.AddListener(s => seen = s);
            machine.ApplyRemoteState(MachineState.Jammed, 12f, true);
            Check(machine.State == MachineState.Jammed && seen == MachineState.Jammed && machine.SwitchedOn,
                "mirrored machine state lands and fires onStateChanged");
            yield return WaitGameSeconds(0.5f);
            Check(Mathf.Approximately(machine.Heat, 12f), "non-authority machine does not simulate (heat unchanged)");

            authority = true;
            yield return WaitGameSeconds(0.3f);
            Check(machine.Heat < 12f, "same machine simulates again once it holds authority (heat cools)");

            NetAuthority.Reset();
            Object.Destroy(dummy.gameObject);
            Object.Destroy(log.gameObject);
            Object.Destroy(machine.gameObject);
        }
    }
}
