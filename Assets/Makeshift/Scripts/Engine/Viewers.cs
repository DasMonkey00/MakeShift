using UnityEngine;

namespace Makeshift.Engine
{
    /// <summary>
    /// The points the World Budget measures distance from: the player's head and both hands, set once per frame by
    /// <see cref="WorldBudget"/>. Shared by the physics bubble and the AI scheduler. Empty until a WorldBudget runs,
    /// in which case the budget systems do nothing and everything runs as before.
    /// </summary>
    public static class Viewers
    {
        public const int Max = 8;
        private static readonly Vector3[] points = new Vector3[Max];
        private static int count;

        public static int Count => count;

        public static void Set(Vector3[] source, int n)
        {
            count = Mathf.Clamp(n, 0, Max);
            for (int i = 0; i < count; i++) points[i] = source[i];
        }

        public static void Clear() => count = 0;

        /// <summary>Distance from a position to the nearest viewer point; float.MaxValue when there are none.</summary>
        public static float NearestDistance(Vector3 position)
        {
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
                best = Mathf.Min(best, Vector3.Distance(position, points[i]));
            return best;
        }
    }
}
