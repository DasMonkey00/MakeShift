using UnityEngine;
using UnityEngine.Events;

namespace Makeshift.Survival
{
    /// <summary>
    /// A day/night clock. Time01: 0 = midnight, 0.25 = dawn, 0.5 = noon, 0.75 = dusk.
    /// Night is before dawn and after dusk. Turns the sun and makes nights colder.
    /// </summary>
    public class DayNightCycle : MonoBehaviour
    {
        [SerializeField] private float dayLengthSeconds = 240f;
        [SerializeField, Range(0f, 1f)] private float startTime01 = 0.35f;
        [SerializeField] private Light sun;

        [Header("Events")]
        public UnityEvent onNightFalls;
        public UnityEvent onDawn;

        public float Time01 { get; private set; }
        public bool IsNight => Time01 < 0.25f || Time01 >= 0.75f;

        private bool wasNight;

        private void Awake()
        {
            Time01 = startTime01;
            wasNight = IsNight;
            Survivor.NightFalling = wasNight;
            ApplySun();
        }

        public void SetTime01(float t) { Time01 = Mathf.Repeat(t, 1f); ApplySun(); }
        public void SetDayLength(float seconds) { dayLengthSeconds = Mathf.Max(0.1f, seconds); }

        /// <summary>Freezes the clock (the time-of-day keys use this).</summary>
        public bool Paused { get; set; }

        private void Update()
        {
            if (!Paused) Time01 = Mathf.Repeat(Time01 + Time.deltaTime / dayLengthSeconds, 1f);
            ApplySun();

            bool night = IsNight;
            if (night == wasNight) return;
            wasNight = night;
            Survivor.NightFalling = night;
            if (night) onNightFalls?.Invoke(); else onDawn?.Invoke();
        }

        private void ApplySun()
        {
            if (sun == null) return;
            sun.transform.rotation = Quaternion.Euler(Time01 * 360f - 90f, -30f, 0f);
            float elevation = Mathf.Sin((Time01 - 0.25f) * Mathf.PI * 2f);
            sun.intensity = Mathf.Clamp01(elevation * 1.5f + 0.1f);
        }
    }
}
