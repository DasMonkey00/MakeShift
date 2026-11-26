#if FUSION_WEAVER
using Fusion;
using Makeshift.Machines;
using UnityEngine;

namespace Makeshift.Network
{
    /// <summary>
    /// Machine state over Fusion Shared mode. The authority (the grabber, else the master client)
    /// simulates and writes; everyone else mirrors it through Machine.ApplyRemoteState.
    /// </summary>
    [RequireComponent(typeof(Machine), typeof(NetworkObject))]
    public class NetworkMachineSync : NetworkBehaviour, INetRelayTarget
    {
        [Networked] public byte StateNet { get; set; }
        [Networked] public float HeatNet { get; set; }
        [Networked] public NetworkBool SwitchedOnNet { get; set; }

        private Machine machine;

        private void Awake() => machine = GetComponent<Machine>();

        public override void Spawned()
        {
            if (Object.HasStateAuthority) Publish();
        }

        public override void FixedUpdateNetwork()
        {
            if (Object.HasStateAuthority) Publish();
        }

        public override void Render()
        {
            if (!Object.HasStateAuthority)
                machine.ApplyRemoteState((MachineState)StateNet, HeatNet, SwitchedOnNet);
        }

        private void Publish()
        {
            StateNet = (byte)machine.State;
            HeatNet = machine.Heat;
            SwitchedOnNet = machine.SwitchedOn;
        }

        public void SendCommand(int command, float arg) => RPC_Command(command);
        public void SendHit(float amount, Vector3 point, Vector3 direction) { }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_Command(int command) => machine.ApplyCommand((MachineCommand)command);
    }
}
#endif
