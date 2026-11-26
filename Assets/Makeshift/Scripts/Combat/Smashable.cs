using Makeshift.Audio;
using Makeshift.Core;
using Makeshift.Rendering;
using UnityEngine;

namespace Makeshift.Combat
{
    /// <summary>Something soft (a berry) that a club squashes into a splat of bits.</summary>
    public class Smashable : MonoBehaviour
    {
        [SerializeField] private float minSpeed = 0.6f;
        [SerializeField] private Color splat = new Color(0.7f, 0.1f, 0.3f);
        [Tooltip("Safe for this long after appearing, so the hit that knocks a berry off the bush does not also squash it.")]
        [SerializeField] private float graceSeconds = 0.6f;

        private float safeUntil;
        private bool smashed;

        private void Start() { safeUntil = Time.time + graceSeconds; }

        private void OnCollisionEnter(Collision collision)
        {
            if (smashed || Time.time < safeUntil) return;
            if (collision.collider.GetComponentInParent<StoneBreaker>() == null) return;
            if (collision.relativeVelocity.magnitude < minSpeed) return;
            smashed = true;
            PlaceholderSfx.Play(SfxKind.Squish, transform.position, 0.8f);
            Crumbs.Burst(transform.position, splat, 10);
            HandCheck.Release(GetComponent<Rigidbody>());
            Destroy(gameObject, 0.02f);
        }
    }
}
