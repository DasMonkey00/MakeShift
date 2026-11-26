using Makeshift.Survival;
using PaleFernStudio.Looksmith;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Makeshift.Rendering
{
    /// <summary>
    /// Drives the Looksmith Sky from the world clock (DayNightCycle.Time01: 0 midnight, 0.25 dawn, 0.5 noon, 0.75 dusk).
    /// Blends the night, golden-hour and day presets, moves the sun and moon along their arc, and keeps some
    /// clouds in the daytime sky. The sky also drives the sun light, ambient light and fog.
    ///
    /// Keys:  ,  back one hour    .  forward one hour    7 sunrise    8 noon    9 sunset    0 midnight    P pause/resume the clock
    /// </summary>
    public class TimeOfDaySky : MonoBehaviour
    {
        [SerializeField] private LooksmithSky sky;
        [SerializeField] private DayNightCycle cycle;
        [SerializeField] private LooksmithSkyPreset night;
        [SerializeField] private LooksmithSkyPreset goldenHour;
        [SerializeField] private LooksmithSkyPreset day;
        [SerializeField, Range(0f, 1f)] private float startTime01 = 0.45f;
        [Tooltip("Cloud cover kept in the midday sky.")]
        [SerializeField, Range(0f, 1f)] private float daytimeClouds = 0.8f;
        [Tooltip("Peak sun height in degrees at noon.")]
        [SerializeField] private float noonElevation = 65f;
        [SerializeField] private bool hotkeys = true;

        private LooksmithSkyState nightState, goldenState, dayState;
        private float lastApplied = -1f;

        private void Start()
        {
            nightState = LooksmithSkyState.From(night);
            goldenState = LooksmithSkyState.From(goldenHour);
            dayState = LooksmithSkyState.From(day);
            if (cycle != null) cycle.SetTime01(startTime01);
            Refresh(true);
        }

        public void SetHour(float hour)
        {
            if (cycle == null) return;
            cycle.SetTime01(hour / 24f);
            Refresh(true);
        }

        public void AddHours(float hours)
        {
            if (cycle == null) return;
            cycle.SetTime01(cycle.Time01 + hours / 24f);
            Refresh(true);
        }

        public void TogglePause()
        {
            if (cycle != null) cycle.Paused = !cycle.Paused;
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var k = hotkeys ? Keyboard.current : null;
            if (k != null)
            {
                if (k.commaKey.wasPressedThisFrame) AddHours(-1f);
                if (k.periodKey.wasPressedThisFrame) AddHours(1f);
                if (k.digit7Key.wasPressedThisFrame) SetHour(6f);
                if (k.digit8Key.wasPressedThisFrame) SetHour(12f);
                if (k.digit9Key.wasPressedThisFrame) SetHour(18f);
                if (k.digit0Key.wasPressedThisFrame) SetHour(0f);
                if (k.pKey.wasPressedThisFrame) TogglePause();
            }
#endif
            Refresh(false);
        }

        private void Refresh(bool force)
        {
            if (sky == null || cycle == null || night == null || goldenHour == null || day == null) return;
            float t = cycle.Time01;
            // Re-pushing the sky is not free: do it a few times a second, not every frame.
            if (!force && Mathf.Abs(Mathf.DeltaAngle(t * 360f, lastApplied * 360f)) < 0.35f) return;
            lastApplied = t;

            // Keyframes around the clock: night, dawn (golden), day, dusk (golden), night.
            LooksmithSkyState s;
            if (t < 0.25f) s = LooksmithSkyState.Lerp(nightState, goldenState, Smooth(t / 0.25f));
            else if (t < 0.5f) s = LooksmithSkyState.Lerp(goldenState, dayState, Smooth((t - 0.25f) / 0.25f));
            else if (t < 0.75f) s = LooksmithSkyState.Lerp(dayState, goldenState, Smooth((t - 0.5f) / 0.25f));
            else s = LooksmithSkyState.Lerp(goldenState, nightState, Smooth((t - 0.75f) / 0.25f));

            // Sun arc: rises in the east (90) at dawn, south at noon, sets in the west (270).
            float angle = (t - 0.25f) * Mathf.PI * 2f;
            float elevation = Mathf.Sin(angle) * noonElevation;
            float azimuth = Mathf.Repeat(90f + (t - 0.25f) * 360f, 360f);
            s.sunElevation = elevation;
            s.sunAzimuth = azimuth;
            s.moonElevation = -elevation;
            s.moonAzimuth = Mathf.Repeat(azimuth + 180f, 360f);

            float daylight = Mathf.Clamp01(Mathf.Sin(angle) * 2f);
            s.cloudCover = Mathf.Lerp(s.cloudCover, Mathf.Max(s.cloudCover, daytimeClouds), daylight);
            sky.SetState(s);
        }

        private static float Smooth(float x) { return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x)); }
    }
}
