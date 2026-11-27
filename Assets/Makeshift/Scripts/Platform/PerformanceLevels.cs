using System.Collections;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Extensions.PerformanceSettings;

namespace Makeshift.Platform
{
    /// <summary>
    /// Tells the headset the game needs sustained processor and graphics speed. Without a request it picks its own
    /// clock levels, which can sit well below what the chip can hold. Needs the OpenXR feature
    /// "XR Performance Settings" on the platform; elsewhere this does nothing.
    /// </summary>
    public class PerformanceLevels : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            var go = new GameObject("PerformanceLevels");
            DontDestroyOnLoad(go);
            go.AddComponent<PerformanceLevels>();
        }

        private bool asked;

        private IEnumerator Start()
        {
            // The headset session can take a few seconds to start.
            for (float waited = 0f; DisplayRefresh.Running() == null && waited < 15f; waited += 0.5f)
                yield return new WaitForSecondsRealtime(0.5f);
            Ask();
        }

        // The headset may drop the request while the game is in the background.
        private void OnApplicationFocus(bool focused)
        {
            if (focused && asked) Ask();
        }

        private void Ask()
        {
            if (DisplayRefresh.Running() == null) return; // playing without a headset
            var feature = OpenXRSettings.Instance.GetFeature<XrPerformanceSettingsFeature>();
            if (feature == null || !feature.enabled)
            {
                Debug.LogWarning("[PERF] 'XR Performance Settings' is off for this platform; the headset picks its own clocks.");
                return;
            }
            asked = true;
            bool cpu = XrPerformanceSettingsFeature.SetPerformanceLevelHint(PerformanceDomain.Cpu, PerformanceLevelHint.SustainedHigh);
            bool gpu = XrPerformanceSettingsFeature.SetPerformanceLevelHint(PerformanceDomain.Gpu, PerformanceLevelHint.SustainedHigh);
            Debug.Log($"[PERF] asked for sustained high clocks: processor {(cpu ? "accepted" : "refused")}, graphics {(gpu ? "accepted" : "refused")}");
        }
    }
}
