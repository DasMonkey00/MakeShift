using UnityEngine;

namespace Makeshift.Settling
{
    /// <summary>
    /// Makes a loose prop come to rest on the ground. While it touches something and is moving slowly, its
    /// spin and drift bleed off quickly; once it has been nearly still for a moment it goes to sleep.
    /// Rounded things (balls, logs, stones) otherwise roll on with almost no resistance. Added by the builder to every prop.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PropSettle : MonoBehaviour
    {
        [Tooltip("Below this linear speed (m/s), while touching ground, the prop starts to settle.")]
        [SerializeField] private float slowSpeed = 0.35f;
        [Tooltip("Spin kept per physics step while settling (0..1). Lower stops rolling faster.")]
        [SerializeField, Range(0f, 1f)] private float spinKeep = 0.8f;
        [SerializeField] private float sleepAfterSeconds = 0.4f;

        private Rigidbody body;
        private int contacts;
        private float stillFor;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
        }

        private void OnCollisionEnter(Collision collision) { contacts++; }
        private void OnCollisionExit(Collision collision) { contacts = Mathf.Max(0, contacts - 1); }

        private void FixedUpdate()
        {
            if (body.isKinematic || body.IsSleeping()) return;
            if (contacts <= 0 || body.linearVelocity.magnitude > slowSpeed)
            {
                stillFor = 0f;
                return;
            }

            // Slow and touching: rolling dies out quickly.
            body.angularVelocity *= spinKeep;
            body.linearVelocity *= 0.9f;

            stillFor += Time.fixedDeltaTime;
            if (stillFor >= sleepAfterSeconds && body.angularVelocity.magnitude < 0.2f)
                body.Sleep();
        }
    }
}
