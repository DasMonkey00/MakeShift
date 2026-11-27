using UnityEngine;

namespace Makeshift.Engine
{
    /// <summary>
    /// Time slicing for AI. An actor near a player runs every frame; one further away runs at a lower rate, and
    /// one far away runs rarely. Each actor keeps its own accumulated time, so movement stays at the same speed
    /// however often it runs (a far creature takes bigger steps, not slower ones).
    ///
    /// With no viewers set (no <see cref="WorldBudget"/> in the scene) or tiers switched off, every actor runs
    /// every frame, as before.
    /// </summary>
    public static class AiScheduler
    {
        public static bool Enabled = true;
        public static float NearRange = 15f;
        public static float MidRange = 35f;
        public static float MidPeriod = 0.1f;
        public static float FarPeriod = 0.5f;

        public static void Configure(BudgetProfile profile)
        {
            if (profile == null) return;
            Enabled = profile.aiTiersOn;
            NearRange = profile.nearRange;
            MidRange = Mathf.Max(profile.midRange, NearRange);
            MidPeriod = Mathf.Max(0.01f, profile.midPeriod);
            FarPeriod = Mathf.Max(MidPeriod, profile.farPeriod);
        }

        /// <summary>Seconds between runs for an actor at this position; 0 means every frame.</summary>
        public static float PeriodAt(Vector3 position)
        {
            if (!Enabled || Viewers.Count == 0) return 0f;
            float distance = Viewers.NearestDistance(position);
            if (distance <= NearRange) return 0f;
            return distance <= MidRange ? MidPeriod : FarPeriod;
        }

        /// <summary>
        /// Call once per frame from an actor's Update. Returns true when the actor should run; <paramref name="step"/>
        /// is then the time since its last run, to use instead of Time.deltaTime.
        /// </summary>
        public static bool Due(Vector3 position, ref float accumulated, float deltaTime, out float step)
        {
            accumulated += deltaTime;
            float period = PeriodAt(position);
            if (accumulated < period)
            {
                step = 0f;
                return false;
            }
            step = accumulated;
            accumulated = 0f;
            return true;
        }
    }
}
