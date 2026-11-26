using Makeshift.Core;
using UnityEngine;
using UnityEngine.Events;

namespace Makeshift.Fire
{
    public enum FlammableCommand { Ignite, Extinguish }

    /// <summary>
    /// Anything with the Burns trait. Builds up heat from nearby fire, ignites,
    /// spreads heat to neighbours while burning, then burns out.
    /// </summary>
    [RequireComponent(typeof(TraitHolder))]
    public class Flammable : MonoBehaviour
    {
        [Header("Ignition")]
        [Tooltip("Heat needed to catch fire. Dry grass is low, a log is high.")]
        [SerializeField] private float ignitionHeat = 3f;
        [Tooltip("Heat lost per second when nothing is heating it.")]
        [SerializeField] private float coolingPerSecond = 0.5f;

        [Header("Burning")]
        [SerializeField] private float burnDuration = 20f;
        [Tooltip("How far from the flame heat reaches. Keep it close to the size of the visible fire.")]
        [SerializeField] private float spreadRadius = 0.45f;
        [SerializeField] private float spreadHeatPerSecond = 1.5f;
        [SerializeField] private bool destroyWhenBurntOut = false;
        [SerializeField] private GameObject fireEffect;

        [Header("Events")]
        public UnityEvent onIgnite;
        public UnityEvent onExtinguish;
        public UnityEvent onBurntOut;

        public bool IsBurning { get; private set; }
        public float Heat { get; private set; }
        public bool IsWet { get; private set; }

        private float burnTimeLeft;
        private float wetTimeLeft;
        private static readonly Collider[] Hits = new Collider[32];

        private void Awake()
        {
            if (fireEffect != null) fireEffect.SetActive(false);
        }

        /// <summary>Called by heat sources every frame they touch this object.</summary>
        public void ApplyHeat(float amount)
        {
            if (contained || IsBurning || IsWet || amount <= 0f) return;
            Heat += amount;
            if (Heat >= ignitionHeat) Ignite();
        }

        public void Ignite()
        {
            if (contained || IsBurning || IsWet) return;
            if (!NetAuthority.RouteOrRun(this, (int)FlammableCommand.Ignite)) return;
            IsBurning = true;
            burnTimeLeft = burnDuration;
            if (fireEffect != null) fireEffect.SetActive(true);
            onIgnite?.Invoke();
        }

        public void Extinguish(float wetSeconds = 10f)
        {
            if (!NetAuthority.RouteOrRun(this, (int)FlammableCommand.Extinguish, wetSeconds)) return;
            IsWet = true;
            wetTimeLeft = wetSeconds;
            Heat = 0f;
            if (!IsBurning) return;
            IsBurning = false;
            if (fireEffect != null) fireEffect.SetActive(false);
            onExtinguish?.Invoke();
        }

        private bool contained;

        /// <summary>
        /// This thing is now fuel inside a fire (a log fixed in the campfire). Its own flame goes out and it neither
        /// catches nor spreads fire; the campfire does that.
        /// </summary>
        public void Contain()
        {
            contained = true;
            IsBurning = false;
            Heat = 0f;
            if (fireEffect != null) fireEffect.SetActive(false);
        }

        /// <summary>Runs a command relayed from another client. Call on the authority only.</summary>
        public void ApplyCommand(FlammableCommand command, float arg)
        {
            if (command == FlammableCommand.Ignite) Ignite();
            else Extinguish(arg > 0f ? arg : 10f);
        }

        /// <summary>Mirror path for non-authority clients; fires the same events.</summary>
        public void ApplyRemoteBurning(bool burning)
        {
            if (burning == IsBurning) return;
            IsBurning = burning;
            if (fireEffect != null) fireEffect.SetActive(burning);
            if (burning) onIgnite?.Invoke(); else onExtinguish?.Invoke();
        }

        private void Update()
        {
            if (!NetAuthority.Has(this)) return;
            float dt = Time.deltaTime;

            if (IsWet)
            {
                wetTimeLeft -= dt;
                if (wetTimeLeft <= 0f) IsWet = false;
            }

            if (!IsBurning)
            {
                Heat = Mathf.Max(0f, Heat - coolingPerSecond * dt);
                return;
            }

            SpreadHeat(spreadHeatPerSecond * dt);

            burnTimeLeft -= dt;
            if (burnTimeLeft <= 0f) BurnOut();
        }

        private void SpreadHeat(float amount)
        {
            // Centred on the flame, not the object's middle: a torch burns at its head.
            Vector3 centre = fireEffect != null ? fireEffect.transform.position : transform.position;
            int count = Physics.OverlapSphereNonAlloc(centre, spreadRadius, Hits,
                ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var other = Hits[i].GetComponentInParent<Flammable>();
                if (other != null && other != this) other.ApplyHeat(amount);
            }
        }

        private void BurnOut()
        {
            IsBurning = false;
            if (fireEffect != null) fireEffect.SetActive(false);
            onBurntOut?.Invoke();
            if (destroyWhenBurntOut) Destroy(gameObject);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f);
            Gizmos.DrawWireSphere(fireEffect != null ? fireEffect.transform.position : transform.position, spreadRadius);
        }
    }
}
