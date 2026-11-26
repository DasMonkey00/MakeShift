using System;
using UnityEngine;

namespace Makeshift.Platform
{
    /// <summary>Editor / offline stand-in. Real Meta and Steam versions come later.</summary>
    public class OfflinePlatformServices : IPlatformServices
    {
        public string PlatformName => "Offline";
        public string UserName => "Survivor";
        public bool IsSignedIn => true;

#pragma warning disable CS0067 // never raised offline
        public event Action<string> InviteAccepted;
#pragma warning restore CS0067

        public void CheckEntitlement(Action<bool> onResult) => onResult?.Invoke(true);

        public void SetRichPresence(string status, string photonSessionName)
            => Debug.Log($"[Platform] presence: {status} ({photonSessionName})");

        public void UnlockAchievement(string id) => Debug.Log($"[Platform] achievement: {id}");
    }

    /// <summary>Game code calls Platforms.Current. Add MAKESHIFT_META / MAKESHIFT_STEAM branches later.</summary>
    public static class Platforms
    {
        private static IPlatformServices current;

        public static IPlatformServices Current
        {
            get
            {
                if (current == null)
                {
#if MAKESHIFT_META
                    // current = new MetaPlatformServices();
#elif MAKESHIFT_STEAM
                    // current = new SteamPlatformServices();
#endif
                    current ??= new OfflinePlatformServices();
                }
                return current;
            }
        }
    }
}
