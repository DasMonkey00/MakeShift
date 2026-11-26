using Makeshift.Core;
using Makeshift.Fire;
using UnityEngine;

namespace Makeshift.Crafting
{
    /// <summary>
    /// Flint throws sparks when it is struck hard against something Heavy (a stone, a log).
    /// Sparks light a fuelled campfire and anything flammable right next to the strike. This is how a fire gets
    /// started from nothing. Any object can have this: use it wrong.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class SparkStriker : MonoBehaviour
    {
        [SerializeField] private ParticleSystem sparks;
        [Tooltip("Collision impulse needed to throw sparks.")]
        // Flint is light (0.2 kg), so a 0.6 threshold meant only a hard tip strike sparked. Lowered so the whole edge works.
        [SerializeField] private float minImpulse = 0.15f;
        [SerializeField] private float igniteRadius = 0.3f;
        [Tooltip("Heat pushed into flammables at the strike. Higher than any ignition threshold = lights instantly.")]
        [SerializeField] private float strikeHeat = 10f;
        [SerializeField] private float cooldown = 0.25f;

        private float next;
        private static readonly Collider[] Hits = new Collider[16];

        private void OnCollisionEnter(Collision collision)
        {
            if (Time.time < next || collision.impulse.magnitude < minImpulse) return;
            if (!TraitHolder.Has(collision.collider, Trait.Heavy)) return;
            next = Time.time + cooldown;

            Vector3 p = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
            Makeshift.Audio.PlaceholderSfx.Play(Makeshift.Audio.SfxKind.Flint, p, 0.7f);
            if (sparks != null)
            {
                sparks.transform.position = p;
                sparks.Emit(12);
            }

            int count = Physics.OverlapSphereNonAlloc(p, igniteRadius, Hits, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var f = Hits[i].GetComponentInParent<Flammable>();
                if (f != null) f.ApplyHeat(strikeHeat);
                var fire = Hits[i].GetComponentInParent<Campfire>();
                if (fire != null) fire.Strike();
            }
        }
    }
}
