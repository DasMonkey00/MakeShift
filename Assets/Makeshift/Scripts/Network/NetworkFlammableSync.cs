#if FUSION_WEAVER
using Fusion;
using Makeshift.Fire;
using UnityEngine;

namespace Makeshift.Network
{
    /// <summary>Burning state over Fusion Shared mode. Authority simulates, others mirror.</summary>
    [RequireComponent(typeof(Flammable), typeof(NetworkObject))]
    public class NetworkFlammableSync : NetworkBehaviour, INetRelayTarget
    {
        [Networked] public NetworkBool BurningNet { get; set; }

        private Flammable flammable;

        private void Awake() => flammable = GetComponent<Flammable>();

        public override void Spawned()
        {
            if (Object.HasStateAuthority) BurningNet = flammable.IsBurning;
        }

        public override void FixedUpdateNetwork()
        {
            if (Object.HasStateAuthority) BurningNet = flammable.IsBurning;
        }

        public override void Render()
        {
            if (!Object.HasStateAuthority) flammable.ApplyRemoteBurning(BurningNet);
        }

        public void SendCommand(int command, float arg) => RPC_Command(command, arg);
        public void SendHit(float amount, Vector3 point, Vector3 direction) { }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_Command(int command, float arg) =>
            flammable.ApplyCommand((FlammableCommand)command, arg);
    }
}
#endif
