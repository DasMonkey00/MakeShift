// Plugs Fusion state authority into Makeshift.Core.NetAuthority.
#if FUSION_WEAVER
using System.Collections.Generic;
using Fusion;
using Makeshift.Core;
using UnityEngine;

namespace Makeshift.Network
{
    /// <summary>
    /// Installed at startup. Gameplay code asks NetAuthority; this answers from the object's
    /// NetworkObject. Objects without a spawned NetworkObject count as local (offline play,
    /// scenery, and anything not meant to be synced), so nothing breaks without a runner.
    /// </summary>
    public static class FusionAuthorityBridge
    {
        private static readonly Dictionary<Component, NetworkObject> Cache = new Dictionary<Component, NetworkObject>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Install()
        {
            Cache.Clear();
            NetAuthority.HasAuthority = HasAuthority;
            NetAuthority.Relay = Relay;
            NetAuthority.RelayHit = RelayHit;
        }

        private static NetworkObject Find(Component c)
        {
            if (Cache.TryGetValue(c, out var no) && no != null) return no;
            no = c.GetComponentInParent<NetworkObject>();
            if (no != null) Cache[c] = no;
            return no;
        }

        private static bool HasAuthority(Component c)
        {
            var no = Find(c);
            if (no == null || !no.IsValid) return true;
            return no.HasStateAuthority;
        }

        private static void Relay(Component c, int command, float arg)
        {
            var target = c.GetComponentInParent<INetRelayTarget>();
            if (target != null) target.SendCommand(command, arg);
            else Debug.LogWarning($"[Net] No relay target for command {command} on {c.name}", c);
        }

        private static void RelayHit(Component c, float amount, Vector3 point, Vector3 dir)
        {
            var target = c.GetComponentInParent<INetRelayTarget>();
            if (target != null) target.SendHit(amount, point, dir);
            else Debug.LogWarning($"[Net] No relay target for hit on {c.name}", c);
        }
    }
}
#endif
