using System.Collections;
using Makeshift.Audio;
using Makeshift.Core;
using UnityEngine;
using UnityEngine.Events;

namespace Makeshift.Combat
{
    /// <summary>
    /// A stone that cracks apart after a few good club hits (3 or 4, rolled per stone). Each hit knocks, the last one
    /// breaks it into pieces. Soft taps do not count.
    /// </summary>
    public class Breakable : MonoBehaviour
    {
        [SerializeField] private int minHits = 3;
        [SerializeField] private int maxHits = 4;
        [Tooltip("Closing speed (m/s) a hit needs to count.")]
        [SerializeField] private float minSpeed = 1.2f;
        [Tooltip("Hits closer together than this count once (a club bouncing on the stone).")]
        [SerializeField] private float cooldown = 0.3f;
        [SerializeField] private GameObject piecePrefab;
        [SerializeField] private int pieceCount = 3;
        [Tooltip("One of the pieces is this instead: a flat, sharp rock for an axe head.")]
        [SerializeField] private GameObject flatPrefab;

        [Header("Events")]
        public UnityEvent onBroken;

        public int Hits { get; private set; }
        public int HitsNeeded { get; private set; }

        private float nextHit;
        private bool broken;

        private void Awake()
        {
            HitsNeeded = Random.Range(minHits, maxHits + 1);
        }

        private void OnCollisionEnter(Collision collision)
        {
            // Glued into something else, it is part of that thing now and no longer cracks.
            if (GetComponent<Rigidbody>() == null) return;
            if (collision.collider.GetComponentInParent<StoneBreaker>() == null) return;
            if (collision.relativeVelocity.magnitude < minSpeed) return;
            Strike(collision.contactCount > 0 ? collision.GetContact(0).point : transform.position);
        }

        /// <summary>The one entry point for cracking hits.</summary>
        public void Strike(Vector3 point)
        {
            if (broken || Time.time < nextHit) return;
            nextHit = Time.time + cooldown;
            Hits++;
            if (Hits < HitsNeeded)
            {
                // Rises in loudness as the stone gives way.
                PlaceholderSfx.Play(SfxKind.Crack, point, 0.5f + 0.5f * Hits / HitsNeeded);
                return;
            }
            broken = true;
            PlaceholderSfx.Play(SfxKind.Break, point, 1f);
            StartCoroutine(Break());
        }

        private IEnumerator Break()
        {
            HandCheck.Release(GetComponent<Rigidbody>());
            yield return new WaitForFixedUpdate();

            // Solid parts off first, so the pieces are not thrown out of the old stone.
            foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = false;
            if (piecePrefab != null)
            {
                for (int i = 0; i < pieceCount; i++)
                {
                    float a = i * Mathf.PI * 2f / Mathf.Max(1, pieceCount);
                    var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    var piece = Instantiate(i == 0 && flatPrefab != null ? flatPrefab : piecePrefab, transform.position + outward * 0.05f + Vector3.up * 0.03f, Random.rotation);
                    if (piece.TryGetComponent(out Rigidbody rb))
                        rb.linearVelocity = outward * 0.7f + Vector3.up * 0.8f;
                }
            }
            onBroken?.Invoke();
            Destroy(gameObject);
        }
    }
}
