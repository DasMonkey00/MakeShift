using System.Collections.Generic;
using UnityEngine;

namespace Makeshift.Fire
{
    /// <summary>
    /// Heats Flammable objects in range: campfires, torches, sparking batteries,
    /// an overloaded toaster. Also keeps nearby survivors warm and cooks food.
    /// Can be switched on and off by other systems.
    /// </summary>
    public class HeatSource : MonoBehaviour
    {
        public static readonly List<HeatSource> All = new List<HeatSource>();

        [Tooltip("Close range: cooks food. Also ignites Flammables unless Ignite Radius is set.")]
        [SerializeField] private float radius = 1f;
        [Tooltip("Range that sets things alight. 0 uses Radius. Keep it to the size of the visible fire.")]
        [SerializeField] private float igniteRadius = 0f;
        [Tooltip("Height of the flame above this object, where ignition is measured from.")]
        [SerializeField] private float igniteHeight = 0f;
        [SerializeField] private float heatPerSecond = 2f;
        [Tooltip("Wider range: survivors inside get warm.")]
        [SerializeField] private float warmthRadius = 4f;
        [SerializeField] private float warmthPerSecond = 6f;
        [SerializeField] private bool active = true;

        private static readonly Collider[] Hits = new Collider[32];

        public bool Active
        {
            get => active;
            set => active = value;
        }

        private void OnEnable() { All.Add(this); }
        private void OnDisable() { All.Remove(this); }

        /// <summary>Total close-range heat per second at a point (cooking, ignition).</summary>
        public static float HeatAt(Vector3 point)
        {
            float sum = 0f;
            foreach (var h in All)
                if (h.active && (h.transform.position - point).sqrMagnitude <= h.radius * h.radius)
                    sum += h.heatPerSecond;
            return sum;
        }

        /// <summary>Warmth per second a survivor standing at a point receives.</summary>
        public static float WarmthAt(Vector3 point)
        {
            float sum = 0f;
            foreach (var h in All)
                if (h.active && (h.transform.position - point).sqrMagnitude <= h.warmthRadius * h.warmthRadius)
                    sum += h.warmthPerSecond;
            return sum;
        }

        private void Update()
        {
            if (!active) return;
            int count = Physics.OverlapSphereNonAlloc(IgniteCentre, IgniteRange, Hits,
                ~0, QueryTriggerInteraction.Ignore);
            float amount = heatPerSecond * Time.deltaTime;
            for (int i = 0; i < count; i++)
            {
                var f = Hits[i].GetComponentInParent<Flammable>();
                if (f != null && f.transform != transform) f.ApplyHeat(amount);
            }
        }

        private Vector3 IgniteCentre => transform.position + Vector3.up * igniteHeight;
        private float IgniteRange => igniteRadius > 0f ? igniteRadius : radius;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0f, 0f, 0.8f);
            Gizmos.DrawWireSphere(IgniteCentre, IgniteRange);
            Gizmos.color = new Color(1f, 0.2f, 0f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, radius);
            Gizmos.color = new Color(1f, 0.8f, 0f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, warmthRadius);
        }
    }
}
