using Makeshift.Combat;
using Makeshift.Fire;
using UnityEngine;
using UnityEngine.Events;

namespace Makeshift.Survival
{
    /// <summary>
    /// A survivor's needs: hunger and warmth (both 100 = fine, 0 = in trouble).
    /// Warmth drains in the cold and refills near a lit HeatSource. At zero of either,
    /// the survivor takes damage if it has a Damageable. Put on the player rig root.
    /// </summary>
    public class Survivor : MonoBehaviour
    {
        [SerializeField] private float maxNeed = 100f;
        [SerializeField] private float hungerLossPerSecond = 0.15f;
        [SerializeField] private float warmthLossPerSecond = 0.4f;
        [Tooltip("Extra warmth loss at night.")]
        [SerializeField] private float nightColdMultiplier = 2f;
        [SerializeField] private float troubleDamage = 2f;
        [SerializeField] private float troubleDamageInterval = 1f;

        [Header("Events")]
        public UnityEvent onStarving;
        public UnityEvent onFreezing;

        public float Hunger { get; private set; }
        public float Warmth { get; private set; }
        public float MaxNeed => maxNeed;
        /// <summary>Test switch: hunger and warmth stop changing and trouble damage stops.</summary>
        public bool NeedsFrozen { get; set; }

        public void Refill() { Hunger = Warmth = maxNeed; }
        public bool IsStarving => Hunger <= 0f;
        public bool IsFreezing => Warmth <= 0f;

        /// <summary>Set by DayNightCycle so nights are colder.</summary>
        public static bool NightFalling;

        private Damageable health;
        private float damageTimer;

        private void Awake()
        {
            health = GetComponent<Damageable>();
            Hunger = Warmth = maxNeed;
        }

        public void SetNeeds(float hunger, float warmth)
        {
            Hunger = Mathf.Clamp(hunger, 0f, maxNeed);
            Warmth = Mathf.Clamp(warmth, 0f, maxNeed);
        }

        public void Eat(Edible food)
        {
            if (food == null) return;
            Hunger = Mathf.Min(maxNeed, Hunger + food.Nutrition);
            var at = food.transform.position;
            var bits = food.GetComponent<Cookable>() != null ? new Color(0.55f, 0.28f, 0.18f) : new Color(0.7f, 0.1f, 0.3f);
            Makeshift.Audio.PlaceholderSfx.Play(Makeshift.Audio.SfxKind.Chomp, at, 1f);
            Makeshift.Rendering.Crumbs.Burst(at, bits, 9, 0.7f);
            Makeshift.Core.HandCheck.Release(food.Body);
            Destroy(food.gameObject);
        }

        private void Update()
        {
            if (NeedsFrozen) return;
            float dt = Time.deltaTime;
            bool wasStarving = IsStarving, wasFreezing = IsFreezing;

            Hunger = Mathf.Max(0f, Hunger - hungerLossPerSecond * dt);

            float loss = warmthLossPerSecond * (NightFalling ? nightColdMultiplier : 1f);
            float gain = HeatSource.WarmthAt(transform.position);
            Warmth = Mathf.Clamp(Warmth + (gain - loss) * dt, 0f, maxNeed);

            if (IsStarving && !wasStarving) onStarving?.Invoke();
            if (IsFreezing && !wasFreezing) onFreezing?.Invoke();

            if ((IsStarving || IsFreezing) && health != null)
            {
                damageTimer += dt;
                if (damageTimer >= troubleDamageInterval)
                {
                    damageTimer = 0f;
                    health.Hit(troubleDamage, transform.position, Vector3.up);
                }
            }
            else damageTimer = 0f;
        }
    }
}
