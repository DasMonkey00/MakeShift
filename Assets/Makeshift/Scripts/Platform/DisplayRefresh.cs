using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Meta;

namespace Makeshift.Platform
{
    /// <summary>
    /// Asks the headset to run at <see cref="TargetHz"/> (90 Hz by default), in every scene. Without a request a Quest
    /// app runs at 72 Hz, whatever the streaming app on the PC is set to. Needs the OpenXR feature
    /// "Meta Quest: Display Utilities" on the platform. In VR the headset rate is the fps cap:
    /// Application.targetFrameRate is ignored while an XR display runs.
    /// </summary>
    public class DisplayRefresh : MonoBehaviour
    {
        public const float DefaultHz = 90f;

        /// <summary>The rates the wrist menu offers.</summary>
        public static readonly float[] Choices = { 72f, 90f, 120f };

        /// <summary>The rate we ask for. Change it with <see cref="Request"/>.</summary>
        public static float TargetHz { get; private set; } = DefaultHz;

        private static readonly List<XRDisplaySubsystem> Displays = new List<XRDisplaySubsystem>();
        private static DisplayRefresh instance;
        private Coroutine applying;

        /// <summary>The rate the headset reports, or 0 when there is no headset or it does not say.</summary>
        public static float CurrentHz
        {
            get
            {
                var display = Running();
                return display != null && display.TryGetDisplayRefreshRate(out float hz) ? hz : 0f;
            }
        }

        /// <summary>The running XR display, or null.</summary>
        public static XRDisplaySubsystem Running()
        {
            SubsystemManager.GetSubsystems(Displays);
            foreach (var d in Displays)
                if (d.running) return d;
            return null;
        }

        /// <summary>Asks the headset for a new rate. Rates the headset does not offer are logged and skipped.</summary>
        public static void Request(float hz)
        {
            TargetHz = hz;
            if (instance == null) return; // Start will pick it up
            if (instance.applying != null) instance.StopCoroutine(instance.applying);
            instance.applying = instance.StartCoroutine(instance.Apply(0f));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            var go = new GameObject("DisplayRefresh");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<DisplayRefresh>();
        }

        private void Start()
        {
            // The headset session can take a few seconds to start.
            applying = StartCoroutine(Apply(15f));
        }

        private IEnumerator Apply(float waitForHeadset)
        {
            XRDisplaySubsystem display = Running();
            for (float waited = 0f; display == null && waited < waitForHeadset; waited += 0.5f)
            {
                yield return new WaitForSecondsRealtime(0.5f);
                display = Running();
            }
            if (display == null) { applying = null; yield break; } // playing without a headset

            var feature = OpenXRSettings.Instance.GetFeature<DisplayUtilitiesFeature>();
            if (feature == null || !feature.enabled)
            {
                Debug.LogWarning("[REFRESH] 'Meta Quest: Display Utilities' is off for this platform; the headset keeps its default rate.");
                applying = null;
                yield break;
            }

            // The runtime may ignore a request made before the app has focus, so check and ask again.
            float target = TargetHz;
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                if (Mathf.Abs(CurrentHz - target) < 0.5f) break;
                if (!display.TryGetSupportedDisplayRefreshRates(Allocator.Temp, out var rates)) break;
                var list = string.Join(", ", rates.ToArray());
                bool offered = false;
                foreach (var r in rates) offered |= Mathf.Abs(r - target) < 0.5f;
                rates.Dispose();
                bool sent = offered && display.TryRequestDisplayRefreshRate(target);
                Debug.Log($"[REFRESH] attempt {attempt}: want {target:0} Hz, headset at {CurrentHz:0} Hz, offers {list}; request {(sent ? "sent" : "not sent")}");
                if (!offered) break;
                yield return new WaitForSecondsRealtime(2f);
            }
            Debug.Log($"[REFRESH] headset now reports {CurrentHz:0} Hz");
            applying = null;
        }
    }
}
