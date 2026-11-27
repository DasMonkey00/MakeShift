using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

namespace Makeshift.Engine
{
    /// <summary>
    /// Entry point of the World Budget. Put one in each scene with a <see cref="BudgetProfile"/>. It:
    /// <list type="bullet">
    /// <item>publishes the head and hand points to <see cref="Viewers"/> (the physics bubble and AI tiers measure from them),</item>
    /// <item>keeps the physics bubble and rigidbody cap in step with the profile,</item>
    /// <item>sets terrain draw settings and the URP shadow distance, cascades and MSAA.</item>
    /// </list>
    /// The wrist menu changes the live values through the Cycle and Set methods. URP asset values are put back when
    /// the budget is disabled, so nothing is left changed in the asset after play mode.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class WorldBudget : MonoBehaviour
    {
        [SerializeField] private BudgetProfile profile;
        [Tooltip("Points the budget measures from: the head and both hands. Falls back to the main camera.")]
        [SerializeField] private Transform[] viewers;

        private readonly Vector3[] points = new Vector3[Viewers.Max];
        private PhysicsBubble bubble;
        private bool urpCaptured;
        private float originalShadowDistance;
        private int originalCascades, originalMsaa, originalShadowRes;
        private int capStep, msaaStep;
        private float shadowMetres, lastShadowMetres;

        public BudgetProfile Profile => profile;
        public PhysicsBubble Bubble => bubble;
        public bool AiOn => AiScheduler.Enabled;

        private void Awake()
        {
            if (profile == null) profile = ScriptableObject.CreateInstance<BudgetProfile>();
            bubble = GetComponent<PhysicsBubble>();
            if (bubble == null) bubble = gameObject.AddComponent<PhysicsBubble>();
            CaptureUrp();
            Apply();
        }

        private void OnDisable()
        {
            Viewers.Clear();
            if (bubble != null) bubble.SetOn(false);
            RestoreUrp();
        }

        // The headset display starts a moment after the scene, so the starting foveation waits for it.
        private bool foveationSet;

        private void Update()
        {
            if (!foveationSet) StartFoveation();

            int n = 0;
            if (viewers != null)
            {
                foreach (var t in viewers)
                {
                    if (t == null || n >= points.Length) continue;
                    points[n++] = t.position;
                }
            }
            if (n == 0 && Camera.main != null)
                points[n++] = Camera.main.transform.position;
            Viewers.Set(points, n);
        }

        /// <summary>Put every setting back to the profile.</summary>
        public void Apply()
        {
            AiScheduler.Configure(profile);
            bubble.Configure(profile);
            capStep = IndexNearest(BudgetProfile.CapPresets, profile.bubbleOn ? profile.rigidbodyCap : 0);
            msaaStep = IndexNearest(BudgetProfile.MsaaPresets, profile.msaaSamples);
            ApplyTerrain();
            // Quest starts on hard shadows (cheapest); the wrist menu switches to soft for testing.
            SetSoftShadows(!Application.isMobilePlatform);
            var urp = Urp;
            if (Application.isMobilePlatform || profile.applyToDesktop)
            {
                SetShadowDistance(BudgetProfile.ShadowPresets[IndexNearest(BudgetProfile.ShadowPresets, profile.shadowDistance)]);
                SetMsaa(BudgetProfile.MsaaPresets[msaaStep]);
            }
            else if (urp != null)
            {
                // Desktop keeps the PC asset; the menu shows what it actually has.
                shadowMetres = lastShadowMetres = urp.shadowDistance;
                msaaStep = IndexNearest(BudgetProfile.MsaaPresets, urp.msaaSampleCount);
            }
        }

        // ---- Terrain -------------------------------------------------------------------------------------------

        /// <summary>Instanced terrain draws its patches together; the heightmap error and detail distance cut patches and grass.</summary>
        public void ApplyTerrain()
        {
            foreach (var terrain in Terrain.activeTerrains)
            {
                terrain.drawInstanced = true;
                terrain.heightmapPixelError = profile.terrainPixelError;
                terrain.detailObjectDistance = profile.terrainDetailDistance;
            }
        }

        // ---- Shadows and MSAA on the active URP asset ----------------------------------------------------------

        private static UniversalRenderPipelineAsset Urp => GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

        private void CaptureUrp()
        {
            var urp = Urp;
            if (urp == null || urpCaptured) return;
            originalShadowDistance = urp.shadowDistance;
            originalCascades = urp.shadowCascadeCount;
            originalMsaa = urp.msaaSampleCount;
            originalShadowRes = urp.mainLightShadowmapResolution;
            originalCascade2Split = urp.cascade2Split;
            urpCaptured = true;
        }

        private void RestoreUrp()
        {
            var urp = Urp;
            if (urp == null || !urpCaptured) return;
            urp.shadowDistance = originalShadowDistance;
            urp.shadowCascadeCount = originalCascades;
            urp.msaaSampleCount = originalMsaa;
            urp.mainLightShadowmapResolution = originalShadowRes;
            urp.cascade2Split = originalCascade2Split;
            urpCaptured = false;
        }

        /// <summary>Shadow distance in metres; 0 turns real-time shadows off for the frame budget.</summary>
        public void SetShadowDistance(float metres)
        {
            shadowMetres = Mathf.Max(0f, metres);
            if (shadowMetres > 0f) lastShadowMetres = shadowMetres;
            var urp = Urp;
            if (urp == null) return;
            CaptureUrp();
            urp.shadowDistance = shadowMetres;
            ApplyCascades(urp);
        }

        // ---- Near shadow cascade (testing) ---------------------------------------------------------------------

        /// <summary>Metres the sharp near cascade covers when the split is on.</summary>
        private const float NearCascadeMetres = 5f;

        private bool splitCascades;
        private float originalCascade2Split;

        /// <summary>
        /// Two cascades: a sharp shadow map for the first few metres around the player and the normal one beyond.
        /// URP shares one shadow map between cascades, so the size doubles too (each cascade keeps the old size).
        /// Off puts back the profile's cascade count and the asset's own size.
        /// </summary>
        public void ToggleCascades()
        {
            splitCascades = !splitCascades;
            var urp = Urp;
            if (urp == null) return;
            CaptureUrp();
            ApplyCascades(urp);
        }

        public bool CascadesSplit => splitCascades;

        public string CascadeLabel => splitCascades ? "2 (near " + NearCascadeMetres + " m)" : "1";

        private void ApplyCascades(UniversalRenderPipelineAsset urp)
        {
            if (splitCascades)
            {
                urp.shadowCascadeCount = 2;
                // The split is a fraction of the shadow distance; keep the near cascade at about 5 m.
                float distance = Mathf.Max(urp.shadowDistance, 0.01f);
                urp.cascade2Split = Mathf.Clamp(NearCascadeMetres / distance, 0.1f, 0.6f);
                urp.mainLightShadowmapResolution = Mathf.Min(originalShadowRes * 2, 4096);
            }
            else
            {
                // Desktop keeps the PC asset's own cascades unless the profile is applied there too.
                urp.shadowCascadeCount = Application.isMobilePlatform || profile.applyToDesktop
                    ? Mathf.Clamp(profile.shadowCascades, 1, 4) : originalCascades;
                urp.cascade2Split = originalCascade2Split;
                urp.mainLightShadowmapResolution = originalShadowRes;
            }
        }

        /// <summary>Off keeps the last distance; on brings it back (the near preset if there was none).</summary>
        public void SetShadowsOn(bool on)
        {
            if (!on) SetShadowDistance(0f);
            else if (shadowMetres <= 0f)
                SetShadowDistance(lastShadowMetres > 0f ? lastShadowMetres : BudgetProfile.ShadowPresets[1]);
        }

        public void SetMsaa(int samples)
        {
            var urp = Urp;
            if (urp == null) return;
            CaptureUrp();
            urp.msaaSampleCount = samples;
        }

        // ---- Fixed foveated rendering ----------------------------------------------------------------------------

        private static readonly float[] FoveationLevels = { 0f, 0.34f, 0.67f, 1f };
        private static readonly string[] FoveationNames = { "OFF", "LOW", "MED", "HIGH" };
        private readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        private int foveationStep;

        /// <summary>The menu's label; "n/a" without a headset display.</summary>
        public string FoveationLabel
        {
            get
            {
                SubsystemManager.GetSubsystems(displays);
                return displays.Count == 0 ? "n/a" : FoveationNames[foveationStep];
            }
        }

        public bool Foveated => foveationStep > 0;

        private void StartFoveation()
        {
            SubsystemManager.GetSubsystems(displays);
            foreach (var display in displays)
            {
                if (!display.running) continue;
                foveationStep = Mathf.Clamp(profile.foveation, 0, FoveationLevels.Length - 1);
                SetFoveation(FoveationLevels[foveationStep]);
                foveationSet = true;
                return;
            }
        }

        /// <summary>
        /// Fixed foveated rendering: Off, Low, Medium, High. Needs the OpenXR Foveated Rendering feature on the
        /// build target; without it the headset ignores the level.
        /// </summary>
        public void CycleFoveation()
        {
            foveationStep = (foveationStep + 1) % FoveationLevels.Length;
            SetFoveation(FoveationLevels[foveationStep]);
        }

        private void SetFoveation(float level)
        {
            SubsystemManager.GetSubsystems(displays);
            foreach (var display in displays) display.foveatedRenderingLevel = level;
        }

        // ---- Wrist menu cycles ---------------------------------------------------------------------------------

        /// <summary>Shadow distance cycles Off, 10, 15, 25, 50 m. Returns the new distance.</summary>
        public float CycleShadows()
        {
            int current = IndexNearest(BudgetProfile.ShadowPresets, shadowMetres);
            int next = (current + 1) % BudgetProfile.ShadowPresets.Length;
            SetShadowDistance(BudgetProfile.ShadowPresets[next]);
            return shadowMetres;
        }

        public float ShadowMetres => shadowMetres;
        public string ShadowLabel => ShadowLabelFor(shadowMetres);

        /// <summary>Rigidbody cap cycles Off, 20, 40, 60.</summary>
        public void CycleCap()
        {
            capStep = (capStep + 1) % BudgetProfile.CapPresets.Length;
            bubble.SetCap(BudgetProfile.CapPresets[capStep]);
        }

        public string CapLabel => BudgetProfile.CapPresets[capStep] == 0 ? "OFF" : BudgetProfile.CapPresets[capStep].ToString();

        public void ToggleAi() => AiScheduler.Enabled = !AiScheduler.Enabled;

        /// <summary>MSAA cycles 1x, 2x, 4x.</summary>
        public void CycleMsaa()
        {
            msaaStep = (msaaStep + 1) % BudgetProfile.MsaaPresets.Length;
            SetMsaa(BudgetProfile.MsaaPresets[msaaStep]);
        }

        public string MsaaLabel => BudgetProfile.MsaaPresets[msaaStep] + "x";

        /// <summary>
        /// Sun shadow map size: toggles between the asset's own size (1024 on Quest) and one step higher, for testing.
        /// </summary>
        public void ToggleShadowResolution()
        {
            var urp = Urp;
            if (urp == null) return;
            CaptureUrp();
            int higher = Mathf.Min(originalShadowRes * 2, 4096);
            urp.mainLightShadowmapResolution = urp.mainLightShadowmapResolution == originalShadowRes ? higher : originalShadowRes;
        }

        public bool ShadowResolutionRaised => urpCaptured && Urp != null && Urp.mainLightShadowmapResolution != originalShadowRes;

        public string ShadowResolutionLabel => Urp != null ? Urp.mainLightShadowmapResolution.ToString() : "-";

        /// <summary>
        /// Shadow type the sun uses whenever its shadows are on (Hard or Soft). Other scripts that switch the
        /// sun's shadows back on read this, so the menu choice sticks.
        /// </summary>
        public static LightShadows SunShadows { get; private set; } = LightShadows.Soft;

        /// <summary>
        /// Soft (filtered) or hard sun shadows. Soft needs "Soft Shadows" on in the URP asset, or the shader
        /// variants are stripped from the build and the sun stays hard. Quality comes from the asset (Low on Quest).
        /// </summary>
        public void SetSoftShadows(bool soft)
        {
            SunShadows = soft ? LightShadows.Soft : LightShadows.Hard;
            var sun = RenderSettings.sun;
            if (sun != null && sun.shadows != LightShadows.None) sun.shadows = SunShadows;
        }

        public void ToggleSoftShadows() => SetSoftShadows(SunShadows != LightShadows.Soft);

        public bool SoftShadowsOn => SunShadows == LightShadows.Soft;

        private static string ShadowLabelFor(float metres) => metres <= 0f ? "OFF" : metres + " m";

        private static int IndexNearest(int[] presets, int value)
        {
            int best = 0;
            for (int i = 1; i < presets.Length; i++)
                if (Mathf.Abs(presets[i] - value) < Mathf.Abs(presets[best] - value)) best = i;
            return best;
        }

        private static int IndexNearest(float[] presets, float value)
        {
            int best = 0;
            for (int i = 1; i < presets.Length; i++)
                if (Mathf.Abs(presets[i] - value) < Mathf.Abs(presets[best] - value)) best = i;
            return best;
        }
    }
}
