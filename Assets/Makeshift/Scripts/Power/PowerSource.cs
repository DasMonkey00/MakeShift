using Makeshift.Core;
using UnityEngine;
using UnityEngine.Events;

namespace Makeshift.Power
{
    /// <summary>
    /// Anything with the Powers trait: a car battery, a bike generator, a hamster wheel.
    /// Holds charge and hands it out when something draws on it.
    /// </summary>
    [RequireComponent(typeof(TraitHolder))]
    public class PowerSource : MonoBehaviour
    {
        [SerializeField] private float capacity = 100f;
        [SerializeField] private float startCharge = 100f;
        [Tooltip("Most power it can give per second, however much is asked for.")]
        [SerializeField] private float maxOutputPerSecond = 10f;
        [Tooltip("Generators: never run out (charge is ignored).")]
        [SerializeField] private bool infinite = false;

        [Header("Events")]
        public UnityEvent onDepleted;

        public float Charge { get; private set; }
        public float Capacity => capacity;
        public float Charge01 => infinite ? 1f : (capacity > 0f ? Charge / capacity : 0f);
        public bool IsEmpty => !infinite && Charge <= 0f;

        private void Awake()
        {
            Charge = Mathf.Clamp(startCharge, 0f, capacity);
        }

        /// <summary>
        /// Ask for power this frame. Returns how much was actually supplied.
        /// </summary>
        public float Draw(float requestedPerSecond, float deltaTime)
        {
            float wanted = Mathf.Min(requestedPerSecond, maxOutputPerSecond) * deltaTime;
            if (infinite) return wanted;
            if (Charge <= 0f) return 0f;

            float given = Mathf.Min(wanted, Charge);
            Charge -= given;
            if (Charge <= 0f)
            {
                Charge = 0f;
                onDepleted?.Invoke();
            }
            return given;
        }

        /// <summary>Cranks, pedals and wheels call this to add charge.</summary>
        public void AddCharge(float amount)
        {
            Charge = Mathf.Clamp(Charge + amount, 0f, capacity);
        }
    }
}
