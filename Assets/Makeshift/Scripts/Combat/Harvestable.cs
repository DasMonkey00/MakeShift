using Makeshift.Core;
using UnityEngine;
using UnityEngine.Events;

namespace Makeshift.Combat
{
    /// <summary>
    /// A bush, tree, crate or carcass that drops loot when hit. Any hard hit works; the
    /// preferred tool trait just works better (use it wrong: a rock beats a bush slowly).
    /// </summary>
    public class Harvestable : MonoBehaviour
    {
        [SerializeField] private GameObject dropPrefab;
        [SerializeField] private int dropCount = 3;
        [Tooltip("Total damage needed to harvest everything.")]
        [SerializeField] private float strengthToBreak = 6f;
        [SerializeField] private Trait preferredTool = Trait.None;
        [SerializeField] private float preferredToolMultiplier = 2f;
        [SerializeField] private float popOutSpeed = 1.5f;
        [SerializeField] private bool destroyWhenDepleted = true;
        [Tooltip("Carcasses: only yields loot once the Damageable on this object is dead.")]
        [SerializeField] private bool onlyWhenDead = false;

        [Header("Events")]
        public UnityEvent onDropped;
        public UnityEvent onDepleted;

        public int DropsSpawned { get; private set; }
        public bool IsDepleted => DropsSpawned >= dropCount;

        private float progress;

        /// <summary>The one entry point for harvesting hits.</summary>
        public void Strike(float damage, Trait tool, Vector3 point)
        {
            if (IsDepleted || damage <= 0f) return;
            if (onlyWhenDead)
            {
                var d = GetComponent<Damageable>();
                if (d != null && !d.IsDead) return;
            }
            if (preferredTool != Trait.None && (tool & preferredTool) != 0) damage *= preferredToolMultiplier;

            progress += damage;
            float perDrop = strengthToBreak / Mathf.Max(1, dropCount);
            while (progress >= perDrop && !IsDepleted)
            {
                progress -= perDrop;
                Drop(point);
            }

            if (IsDepleted)
            {
                onDepleted?.Invoke();
                if (destroyWhenDepleted) Destroy(gameObject);
            }
        }

        private void Drop(Vector3 point)
        {
            DropsSpawned++;
            if (dropPrefab != null)
            {
                var item = Instantiate(dropPrefab, point + Vector3.up * 0.15f, Random.rotation);
                var rb = item.GetComponent<Rigidbody>();
                if (rb != null)
                    rb.AddForce((Vector3.up + Random.insideUnitSphere * 0.5f) * popOutSpeed, ForceMode.VelocityChange);
            }
            onDropped?.Invoke();
        }
    }
}
