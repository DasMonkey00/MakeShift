using System;
using UnityEngine;

namespace Makeshift.Core
{
    /// <summary>
    /// The one place game code asks "may I change this?". Offline (no provider) everything is
    /// allowed, so the greybox and smoke tests behave exactly as before. The network layer
    /// installs a provider; gameplay scripts never reference Fusion directly.
    ///
    /// Pattern: public entry points call RouteOrRun first. With authority they run; without it
    /// the request is relayed to whoever has authority and the call returns. The mirror methods
    /// (ApplyRemote...) on each component are the only way remote state lands locally.
    /// </summary>
    public static class NetAuthority
    {
        /// <summary>True if this client may change the object's state. Null = offline = yes.</summary>
        public static Func<Component, bool> HasAuthority;

        /// <summary>Sends (component, command, argument) to the authority holder.</summary>
        public static Action<Component, int, float> Relay;

        /// <summary>Sends a hit to the authority holder: (target, amount, point, direction).</summary>
        public static Action<Component, float, Vector3, Vector3> RelayHit;

        public static bool Has(Component c) => HasAuthority == null || c == null || HasAuthority(c);

        /// <summary>Returns true when the caller should run the change itself.</summary>
        public static bool RouteOrRun(Component c, int command, float arg = 0f)
        {
            if (Has(c)) return true;
            Relay?.Invoke(c, command, arg);
            return false;
        }

        public static bool RouteOrRunHit(Component c, float amount, Vector3 point, Vector3 dir)
        {
            if (Has(c)) return true;
            RelayHit?.Invoke(c, amount, point, dir);
            return false;
        }

        /// <summary>For tests and scene teardown.</summary>
        public static void Reset()
        {
            HasAuthority = null;
            Relay = null;
            RelayHit = null;
        }
    }
}
