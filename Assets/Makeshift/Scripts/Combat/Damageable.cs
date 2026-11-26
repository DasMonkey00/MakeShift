using Makeshift.Core;
using UnityEngine;
using UnityEngine.Events;

namespace Makeshift.Combat
{
    /// <summary>
    /// Anything that can be bonked: creatures, tribe members, crates. All damage goes through
    /// Hit() so authority checks (attacker resolves, victim reacts) can be added in one place.
    /// </summary>
    public class Damageable : MonoBehaviour
    {
        [SerializeField] private float maxHealth = 20f;
        [Tooltip("Shove applied to our own rigidbody per point of damage, if it is not kinematic.")]
        [SerializeField] private float knockbackPerDamage = 0.2f;

        [Header("Events")]
        public UnityEvent<float> onHit;
        public UnityEvent onDied;

        public float Health { get; private set; }
        public float MaxHealth => maxHealth;
        public bool IsDead { get; private set; }
        /// <summary>Which way the latest hit pushed. Zero for damage mirrored from the network.</summary>
        public Vector3 LastHitDirection { get; private set; }

        private void Awake()
        {
            Health = maxHealth;
        }

        public void Hit(float amount, Vector3 point, Vector3 direction)
        {
            if (IsDead || amount <= 0f) return;
            // Attacker resolves the hit; if we are not the authority the hit is relayed to it.
            if (!NetAuthority.RouteOrRunHit(this, amount, point, direction)) return;
            Health = Mathf.Max(0f, Health - amount);
            LastHitDirection = direction;

            var rb = GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
                rb.AddForceAtPosition(direction.normalized * (amount * knockbackPerDamage), point, ForceMode.Impulse);

            onHit?.Invoke(amount);
            if (Health <= 0f)
            {
                IsDead = true;
                onDied?.Invoke();
            }
        }

        /// <summary>Mirror path for non-authority clients: takes the authority's health value.</summary>
        public void ApplyRemoteHealth(float health)
        {
            if (IsDead) return;
            float lost = Health - health;
            Health = Mathf.Clamp(health, 0f, maxHealth);
            if (lost > 0f) onHit?.Invoke(lost);
            if (Health <= 0f) { IsDead = true; onDied?.Invoke(); }
        }

        public void Heal(float amount)
        {
            if (!IsDead) Health = Mathf.Min(maxHealth, Health + amount);
        }
    }
}
