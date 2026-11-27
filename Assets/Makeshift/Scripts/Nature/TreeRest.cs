using UnityEngine;

namespace Makeshift.Nature
{
    /// <summary>
    /// Where a felled pine ends up: flat on the ground beside its stump, following the slope. However the fall went,
    /// every tree finishes here; one held up by a rock or another tree slides off to a clear line.
    /// </summary>
    public static class TreeRest
    {
        public static float Ground(Vector3 at, float otherwise)
        {
            var terrain = Terrain.activeTerrain;
            return terrain != null ? terrain.SampleHeight(at) + terrain.transform.position.y : otherwise;
        }

        /// <summary>
        /// The lying pose for a tree falling along <paramref name="flat"/>: where its butt rests, and how far it has
        /// turned from upright, in degrees (90 is level; less uphill, more downhill).
        /// </summary>
        public static void Lie(Vector3 hinge, Vector3 flat, float length, float radius, out Vector3 butt, out float angle)
        {
            butt = hinge + flat * (radius + 0.3f);
            butt.y = Ground(butt, hinge.y - 1f) + radius * 0.85f;

            // The steepest line up from the butt that any part of the ground reaches: the trunk rests on that point.
            float rise = -80f;
            for (float along = 0.12f; along <= 1.001f; along += 0.08f)
            {
                float out_ = length * along;
                Vector3 p = butt + flat * out_;
                // Branches crush under the weight, so only the trunk holds it off the ground, thinning to the tip.
                float clear = Mathf.Lerp(radius * 0.85f, 0.08f, along);
                float needed = Ground(p, butt.y - 1f) + clear - butt.y;
                rise = Mathf.Max(rise, Mathf.Atan2(needed, out_) * Mathf.Rad2Deg);
            }
            angle = 90f - rise;
        }
    }
}
