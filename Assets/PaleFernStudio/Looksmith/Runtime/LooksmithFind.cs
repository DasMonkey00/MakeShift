using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// Scene searches that compile on every supported editor: Unity 6.6 dropped FindObjectsSortMode from
    /// FindObjectsByType, which 6.3 to 6.5 still require. Unsorted, like the calls these replace.
    /// </summary>
    public static class LooksmithFind
    {
        public static T[] All<T>(FindObjectsInactive inactive = FindObjectsInactive.Exclude) where T : Object
        {
#if UNITY_6000_6_OR_NEWER
            return Object.FindObjectsByType<T>(inactive);
#else
            return Object.FindObjectsByType<T>(inactive, FindObjectsSortMode.None);
#endif
        }

        public static Object[] All(Type type, FindObjectsInactive inactive = FindObjectsInactive.Exclude)
        {
#if UNITY_6000_6_OR_NEWER
            return Object.FindObjectsByType(type, inactive);
#else
            return Object.FindObjectsByType(type, inactive, FindObjectsSortMode.None);
#endif
        }
    }
}
