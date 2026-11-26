using System;

namespace Makeshift.Platform
{
    /// <summary>
    /// The only door game code uses for store features. Implementations: Offline (editor),
    /// Meta Platform SDK (Quest) and Steamworks (PC), picked per build with scripting defines.
    /// An invite carries a Photon session name; matchmaking stays shared.
    /// </summary>
    public interface IPlatformServices
    {
        string PlatformName { get; }
        string UserName { get; }
        bool IsSignedIn { get; }

        /// <summary>Checks the player owns the game. Offline always passes.</summary>
        void CheckEntitlement(Action<bool> onResult);

        /// <summary>Sets what friends see ("Hunting in the Jungle"). Optional on a platform.</summary>
        void SetRichPresence(string status, string photonSessionName);

        void UnlockAchievement(string id);

        /// <summary>Raised when the player accepts an invite; the argument is the Photon session name.</summary>
        event Action<string> InviteAccepted;
    }
}
