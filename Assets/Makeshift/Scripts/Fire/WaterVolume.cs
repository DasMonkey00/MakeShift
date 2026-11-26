using Makeshift.Core;
using Makeshift.Machines;
using UnityEngine;

namespace Makeshift.Fire
{
    /// <summary>
    /// A trigger volume for streams, puddles and buckets. Puts out fires,
    /// cools machines, and pushes Floats objects up. Splashes when something drops in.
    /// Needs a Collider set to Is Trigger.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class WaterVolume : MonoBehaviour
    {
        [SerializeField] private float wetSeconds = 10f;
        [SerializeField] private float buoyancy = 12f;
        [Tooltip("Optional splash particles, emitted at the surface where something enters.")]
        [SerializeField] private ParticleSystem splash;

        private Collider volume;
        private float nextSplash;

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void Awake()
        {
            volume = GetComponent<Collider>();
            if (transform.parent == null) return;
            // The spout's stream lands on whatever is held under it instead of falling straight through.
            foreach (var spray in transform.parent.GetComponentsInChildren<ParticleSystem>(true))
                if (spray.name == "WaterSpray") Collide(spray);
        }

        private static void Collide(ParticleSystem spray)
        {
            var hit = spray.collision;
            hit.enabled = true;
            hit.type = ParticleSystemCollisionType.World;
            hit.mode = ParticleSystemCollisionMode.Collision3D;
            hit.quality = ParticleSystemCollisionQuality.High;
            hit.collidesWith = ~0;
            hit.enableDynamicColliders = true;
            hit.radiusScale = 0.4f;
            hit.dampen = 0.45f;
            hit.bounce = 0.15f;
            hit.lifetimeLoss = 0.45f;
        }

        private void OnTriggerEnter(Collider other)
        {
            var f = other.GetComponentInParent<Flammable>();
            if (f != null) f.Extinguish(wetSeconds);

            var m = other.GetComponentInParent<Machine>();
            if (m != null) m.Douse();

            var c = other.GetComponentInParent<Campfire>();
            if (c != null) c.Douse();

            if (splash != null && Time.time >= nextSplash)
            {
                nextSplash = Time.time + 0.15f;
                if (volume == null) volume = GetComponent<Collider>();
                Vector3 centre = other.bounds.center;
                splash.transform.position = new Vector3(centre.x, volume.bounds.max.y, centre.z);
                splash.Emit(10);
            }
        }

        private void OnTriggerStay(Collider other)
        {
            var rb = other.attachedRigidbody;
            if (rb == null || rb.isKinematic) return;
            if (TraitHolder.Has(other, Trait.Floats))
                rb.AddForce(Vector3.up * buoyancy, ForceMode.Acceleration);
        }
    }
}
