using UnityEngine;

namespace Makeshift.Network
{
    /// <summary>
    /// Implemented by the network sync component next to a gameplay component. Lets the
    /// authority bridge send a command to whoever holds state authority without gameplay
    /// code knowing about Fusion.
    /// </summary>
    public interface INetRelayTarget
    {
        void SendCommand(int command, float arg);
        void SendHit(float amount, Vector3 point, Vector3 direction);
    }
}
