using UnityEngine;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// Session-only object IDs for cache keys and change checks. GetInstanceID is a compile error from Unity 6.6;
    /// GetEntityId works from 6.3. Never store these: they change between editor sessions.
    /// </summary>
    public static class LooksmithObjectIds
    {
        /// <summary>The full ID as text ("" for none): for keys that must never collide.</summary>
        public static string Get(Object target)
        {
            if (target == null)
                return "";
#if UNITY_6000_3_OR_NEWER
            return target.GetEntityId().ToString();
#else
            return target.GetInstanceID().ToString();
#endif
        }

        /// <summary>A hash of the ID (0 for none): for change checks, where a rare collision only costs a skipped update.</summary>
        public static int Hash(Object target)
        {
            if (target == null)
                return 0;
#if UNITY_6000_3_OR_NEWER
            return target.GetEntityId().GetHashCode();
#else
            return target.GetInstanceID();
#endif
        }
    }
}
