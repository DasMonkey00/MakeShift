#if FUSION_WEAVER
using Fusion;
using Makeshift.Combat;
using UnityEngine;

namespace Makeshift.Network
{
    /// <summary>
    /// Health over Fusion Shared mode. A hit on a non-authority client is relayed to the
    /// authority, which applies it; everyone mirrors the resulting health.
    /// (Player-vs-player hits: the attacker resolves and sends the hit; the victim's own client
    /// holds authority over their Damageable and plays the reaction.)
    /// </summary>
    [RequireComponent(typeof(Damageable), typeof(NetworkObject))]
    public class NetworkDamageableSync : NetworkBehaviour, INetRelayTarget
    {
        [Networked] public float HealthNet { get; set; }

        private Damageable damageable;

        private void Awake() => damageable = GetComponent<Damageable>();

        public override void Spawned()
        {
            if (Object.HasStateAuthority) HealthNet = damageable.Health;
        }

        public override void FixedUpdateNetwork()
        {
            if (Object.HasStateAuthority) HealthNet = damageable.Health;
        }

        public override void Render()
        {
            if (!Object.HasStateAuthority) damageable.ApplyRemoteHealth(HealthNet);
        }

        public void SendCommand(int command, float arg) { }
        public void SendHit(float amount, Vector3 point, Vector3 direction) => RPC_Hit(amount, point, direction);

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_Hit(float amount, Vector3 point, Vector3 direction) =>
            damageable.Hit(amount, point, direction);
    }
}
#endif
