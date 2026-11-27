using System;
using System.Collections.Generic;
using Makeshift.Engine;
using Makeshift.Fire;
using Makeshift.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Makeshift.Rendering
{
    /// <summary>
    /// Look testing for the Toon Fantasy Nature scene: swaps the foliage and prop shaders, switches lighting
    /// presets, and turns the expensive extras on and off, all from the wrist menu in the headset.
    ///
    /// Keys (editor and PC): M menu, 1-5 shader set, L next lighting, P post effects, O shadows,
    /// F fog, G grass amount, K particles,
    /// T storm, B LOD distances, I draw mode.
    /// </summary>
    public class ToonLookSwitcher : MonoBehaviour, IWristMenuPage
    {
        [Serializable]
        public class ShaderSet
        {
            public string name;
            [Tooltip("One replacement per entry in Source Materials, same order.")]
            public Material[] materials;
        }

        [Serializable]
        public class LightPreset
        {
            public string name;
            public VolumeProfile profile;
            public Material skybox;
            public Color sunColor = Color.white;
            public float sunIntensity = 1.4f;
            public Quaternion sunRotation = Quaternion.identity;
            public Color fogColor = Color.gray;
            public FogMode fogMode = FogMode.ExponentialSquared;
            public float fogDensity = 0.01f;
            public float fogStart = 50f;
            public float fogEnd = 150f;
            public Color ambientSky = Color.white;
            public Color ambientEquator = Color.gray;
            public Color ambientGround = Color.black;
        }

        [SerializeField] private Material[] sourceMaterials;
        [SerializeField] private ShaderSet[] shaderSets;

        /// <summary>Shader sets the wrist menu offers, by name. URP + wind is the default.</summary>
        private static readonly string[] MenuShaderSets = { "URP + wind", "URP Unlit" };
        [SerializeField] private LightPreset[] lightPresets;
        [SerializeField] private int startLighting;
        [SerializeField] private Light sun;
        [SerializeField] private Volume volume;
        [SerializeField] private Camera head;
        [SerializeField] private Transform spawnPoint;
        [Tooltip("Grass and flower objects, thinned out by the Grass button.")]
        [SerializeField] private GameObject[] grass;
        [SerializeField] private GameObject[] particles;
        [Header("Camp")]
        [Tooltip("Lit at the start and kept fed, so its glow can be compared across looks.")]
        [SerializeField] private Campfire campfire;
        [Tooltip("Torches that burn from the start.")]
        [SerializeField] private Flammable[] torches;

        [SerializeField] private StormController storm;
        [SerializeField] private DetailPresets detail;
        [Tooltip("World budget: physics bubble, AI tiers, shadow distance and MSAA switches. Set by the builder.")]
        [SerializeField] private WorldBudget budget;
        [Tooltip("The billboard tree materials (alpha clipped) and the old blended shader to compare them with.")]
        [SerializeField] private Material[] billboards;
        [SerializeField] private Shader blendedBillboardShader;
        private Shader[] cutoutShaders; // set while the blended comparison is showing
        [Tooltip("Shader set used from the start. 3 = our URP shader with the pack's wind (Makeshift/Scenery).")]
        [SerializeField] private int startShader = 3;

        /// <summary>The lighting preset in use, the fair weather base a storm fades from.</summary>
        public LightPreset CurrentPreset =>
            lightPresets != null && lightPresets.Length > 0 ? lightPresets[lighting] : null;

        public void ReapplyLighting() => ApplyLighting(lighting);

        private static readonly int[] GrassPercent = { 100, 50, 25, 0 };

        private readonly List<Renderer> renderers = new List<Renderer>();
        private readonly List<Material[]> originals = new List<Material[]>();

        private int shader;
        private int lighting;
        private int grassStep;
        private bool post;
        private bool shadows = true;
        private bool fog = true;
        private bool particlesOn = true;
        private DrawMode drawMode = DrawMode.Merged;
        private MaterialPropertyBlock instanceBlock;
        private SceneryInstancer merger;
        private ChunkedScenery chunks;

        private void Start()
        {
            // Post processing costs a full-screen pass, so it starts off on the headset.
            post = Application.platform != RuntimePlatform.Android;
            CollectRenderers();
            ApplyShader(startShader);
            merger = GetComponent<SceneryInstancer>();
            if (merger == null) merger = gameObject.AddComponent<SceneryInstancer>();
            merger.SetViewer(head);
            if (detail != null) detail.Applied += merger.MarkDirty;
            chunks = GetComponent<ChunkedScenery>();
            if (chunks == null) chunks = gameObject.AddComponent<ChunkedScenery>();
            chunks.Setup(this, merger, head);
            if (detail != null) detail.Applied += chunks.Reach;
            SetDrawMode(drawMode);
            ApplyLighting(startLighting);
            SetFire(true);
        }

        /// <summary>How the scenery is handed to the graphics chip.</summary>
        public enum DrawMode
        {
            /// <summary>Unity's own batching (SRP batcher): one draw for every object.</summary>
            Standard,
            /// <summary>Unity's GPU instancing, which merges some neighbours.</summary>
            Instanced,
            /// <summary>The <see cref="SceneryInstancer"/>: one draw for every mesh and material in view.</summary>
            Merged,
            /// <summary>Merged, with the trees welded into squares of ground by <see cref="ChunkedScenery"/>.</summary>
            Chunked
        }

        public DrawMode Drawing => drawMode;
        /// <summary>The share of grass the menu's grass button leaves on, 0 to 1.</summary>
        public float GrassShare => GrassPercent[grassStep] / 100f;

        public void SetDrawMode(DrawMode mode)
        {
            drawMode = mode;
            // The SRP batcher takes priority over a material's "GPU Instancing" tick, so ticking it does nothing
            // alone. A property block takes a renderer off the batcher; objects sharing a mesh then draw together.
            if (instanceBlock == null)
            {
                instanceBlock = new MaterialPropertyBlock();
                instanceBlock.SetFloat("_MkInstanced", 1f); // unused by the shaders; the block itself is the point
            }
            foreach (var r in renderers)
                if (r != null) r.SetPropertyBlock(mode == DrawMode.Standard || unblocked.Contains(r) ? null : instanceBlock);
            if (merger != null)
            {
                merger.SetOn(mode == DrawMode.Merged || mode == DrawMode.Chunked);
                merger.MarkDirty();
            }
            if (chunks != null) chunks.SetOn(mode == DrawMode.Chunked);
        }

        public void CycleDrawMode() => SetDrawMode((DrawMode)(((int)drawMode + 1) % 4));

        /// <summary>Takes one tree out of the merged drawing: it now draws itself.</summary>
        public void Withdraw(LODGroup group)
        {
            if (merger != null) merger.Withdraw(group);
        }

        /// <summary>Tell the merged drawing that scenery changed.</summary>
        public void SceneryChanged()
        {
            if (merger != null) merger.MarkDirty();
        }

        public void ToggleFire() => SetFire(campfire == null || !campfire.IsLit);

        private void SetFire(bool on)
        {
            if (campfire != null)
            {
                if (on) { campfire.AddFuel(120f); campfire.Light(); }
                else campfire.Douse();
            }
            if (torches == null) return;
            foreach (var t in torches)
            {
                if (t == null) continue;
                if (on) t.Ignite(); else t.Extinguish(0.1f);
            }
        }

        private void CollectRenderers()
        {
            var known = new HashSet<Material>(sourceMaterials);
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            {
                var mats = r.sharedMaterials;
                if (!Array.Exists(mats, m => m != null && known.Contains(m))) continue;
                renderers.Add(r);
                originals.Add(mats);
            }
        }

        /// <summary>
        /// Takes on a renderer made during play from part of a scenery model, so the shader buttons change it too.
        /// With <paramref name="slot"/> set, the copy wears only that material of the source, followed by
        /// <paramref name="extra"/>.
        /// </summary>
        public void Follow(Renderer source, Renderer copy, int slot = -1, Material extra = null, bool instanced = true)
        {
            int i = renderers.IndexOf(source);
            if (i < 0) return;
            var authored = originals[i];
            var current = source.sharedMaterials;
            if (slot >= 0)
            {
                authored = extra != null ? new[] { authored[slot], extra } : new[] { authored[slot] };
                current = extra != null ? new[] { current[slot], extra } : new[] { current[slot] };
            }
            renderers.Add(copy);
            originals.Add(authored);
            copy.sharedMaterials = current;
            // A one-off mesh has nothing to instance with, so it is better left to the SRP batcher.
            if (!instanced) unblocked.Add(copy);
            copy.SetPropertyBlock(drawMode == DrawMode.Standard || !instanced ? null : instanceBlock);
        }

        private readonly HashSet<Renderer> unblocked = new HashSet<Renderer>();

        public void ApplyShader(int index)
        {
            if (shaderSets == null || index < 0 || index >= shaderSets.Length) return;
            shader = index;
            var map = new Dictionary<Material, Material>();
            var replacements = shaderSets[index].materials;
            for (int i = 0; i < sourceMaterials.Length && i < replacements.Length; i++)
                if (sourceMaterials[i] != null && replacements[i] != null) map[sourceMaterials[i]] = replacements[i];

            for (int i = 0; i < renderers.Count; i++)
            {
                if (renderers[i] == null) continue;
                var mats = (Material[])originals[i].Clone();
                for (int m = 0; m < mats.Length; m++)
                    if (mats[m] != null && map.TryGetValue(mats[m], out var swap)) mats[m] = swap;
                renderers[i].sharedMaterials = mats;
            }
            SceneryChanged();
        }

        public void ApplyLighting(int index)
        {
            if (lightPresets == null || lightPresets.Length == 0) return;
            lighting = Mathf.Clamp(index, 0, lightPresets.Length - 1);
            var p = lightPresets[lighting];

            if (sun != null)
            {
                sun.color = p.sunColor;
                sun.intensity = p.sunIntensity;
                sun.transform.rotation = p.sunRotation;
                RenderSettings.sun = sun;
            }
            RenderSettings.skybox = p.skybox;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = p.ambientSky;
            RenderSettings.ambientEquatorColor = p.ambientEquator;
            RenderSettings.ambientGroundColor = p.ambientGround;
            RenderSettings.fogColor = p.fogColor;
            RenderSettings.fogMode = p.fogMode;
            RenderSettings.fogDensity = p.fogDensity;
            RenderSettings.fogStartDistance = p.fogStart;
            RenderSettings.fogEndDistance = p.fogEnd;
            if (volume != null) volume.sharedProfile = p.profile;
            ApplyToggles();
            DynamicGI.UpdateEnvironment();
        }

        private void ApplyToggles()
        {
            RenderSettings.fog = fog;
            if (sun != null) sun.shadows = shadows ? Makeshift.Engine.WorldBudget.SunShadows : LightShadows.None;
            if (volume != null) volume.enabled = post;
            if (head != null) head.GetUniversalAdditionalCameraData().renderPostProcessing = post;
        }

        public void TogglePost() { post = !post; ApplyToggles(); }
        public void ToggleShadows()
        {
            shadows = !shadows;
            if (budget != null) budget.SetShadowsOn(shadows);
            ApplyToggles();
        }

        /// <summary>The wrist menu's shadow button: Off, 10, 15, 25, 50 m.</summary>
        public void CycleShadowDistance()
        {
            if (budget == null)
            {
                ToggleShadows();
                return;
            }
            budget.CycleShadows();
            shadows = budget.ShadowMetres > 0f;
            ApplyToggles();
        }
        public void ToggleFog() { fog = !fog; ApplyToggles(); }

        public void CycleGrass()
        {
            grassStep = (grassStep + 1) % GrassPercent.Length;
            int percent = GrassPercent[grassStep];
            // Even thinning: object i stays when the running total crosses a whole number.
            for (int i = 0; i < grass.Length; i++)
                if (grass[i] != null) grass[i].SetActive((i + 1) * percent / 100 > i * percent / 100);
            SceneryChanged();
        }

        public void ToggleParticles()
        {
            particlesOn = !particlesOn;
            foreach (var p in particles)
                if (p != null) p.SetActive(particlesOn);
        }

        public void BuildMenu(WristMenu menu)
        {
            // Rows 0-2: shader sets. Rows 3-4: lighting. Rows 5-7: extras.
            // Only the shader sets still under test get buttons; the others stay in the scene data (indices unchanged).
            int shown = 0;
            for (int i = 0; i < shaderSets.Length && shown < 2; i++)
            {
                if (Array.IndexOf(MenuShaderSets, shaderSets[i].name) < 0) continue;
                int index = i;
                menu.AddPageButton(0, shown++, 2, () => ApplyShader(index),
                    () => shaderSets[index].name, () => shader == index);
            }
            menu.AddPageButton(1, 0, 1, CycleDrawMode, () => "Draw: " + drawMode,
                () => drawMode == DrawMode.Merged || drawMode == DrawMode.Chunked);
            for (int i = 0; i < lightPresets.Length; i++)
            {
                int index = i;
                menu.AddPageButton(3 + i / 3, i % 3, 3, () => ApplyLighting(index),
                    () => lightPresets[index].name, () => lighting == index);
            }
            menu.AddPageButton(5, 0, 2, TogglePost, () => "Post FX: " + OnOff(post), () => post);
            menu.AddPageButton(5, 1, 2, CycleShadowDistance, () => "Shadows: " + (budget != null ? budget.ShadowLabel : OnOff(shadows)), () => shadows);
            menu.AddPageButton(6, 0, 2, ToggleFog, () => "Fog: " + OnOff(fog), () => fog);
            menu.AddPageButton(6, 1, 2, ToggleParticles, () => "Particles: " + OnOff(particlesOn), () => particlesOn);
            menu.AddPageButton(7, 0, 3, CycleGrass, () => "Grass " + GrassPercent[grassStep] + "%", () => grassStep == 0);
            menu.AddPageButton(7, 1, 3, CycleDetail, () => "LOD: " + (detail != null ? detail.CurrentName : "-"),
                () => detail != null && detail.Current != 0);
            menu.AddPageButton(7, 2, 3, () => menu.Teleport(spawnPoint), () => "To start");
            menu.AddPageButton(8, 0, 2, ToggleFire, () => "Fire: " + OnOff(campfire != null && campfire.IsLit),
                () => campfire != null && campfire.IsLit);
            menu.AddPageButton(8, 1, 2, menu.ToggleHud, () => "Stats in view: " + OnOff(menu.HudVisible), () => menu.HudVisible);
            menu.AddPageButton(9, 0, 3, ToggleStorm, () => "STORM: " + OnOff(storm != null && storm.Active),
                () => storm != null && storm.Active);
            menu.AddPageButton(9, 1, 3, ToggleSoftShadows,
                () => "Soft shad: " + OnOff(budget != null && budget.SoftShadowsOn),
                () => budget != null && budget.SoftShadowsOn);
            menu.AddPageButton(9, 2, 3, ToggleCascades,
                () => "Cascades: " + (budget != null ? budget.CascadeLabel : "-"),
                () => budget != null && budget.CascadesSplit);

            // Rows 10-11: world budget (the panel is taller to hold them).
            menu.AddPageButton(10, 0, 2, CycleCap, () => "Bubble: " + (budget != null ? budget.CapLabel : "-"),
                () => budget != null && budget.Bubble.On);
            menu.AddPageButton(10, 1, 2, ToggleAi, () => "AI tiers: " + OnOff(budget != null && budget.AiOn),
                () => budget != null && budget.AiOn);
            menu.AddPageButton(11, 0, 2, CycleMsaa, () => "MSAA: " + (budget != null ? budget.MsaaLabel : "-"));
            menu.AddPageButton(11, 1, 2, ToggleBillboards, () => "Billboards: " + (cutoutShaders == null ? "CUTOUT" : "BLEND"),
                () => cutoutShaders == null);

            // Rows 12-13: scenery, headset and gameplay cost switches.
            menu.AddPageButton(12, 0, 2, CycleGlow,
                () => "Merge lights: " + (merger != null ? merger.Keeping.ToString().ToUpperInvariant() : "-"),
                () => merger != null && merger.Keeping != SceneryInstancer.LightKeeping.All);
            menu.AddPageButton(12, 1, 2, CycleFoveation,
                () => "Foveation: " + (budget != null ? budget.FoveationLabel : "-"), () => budget != null && budget.Foveated);
            menu.AddPageButton(13, 0, 2, ToggleWind, () => "Wind: " + OnOff(wind), () => wind);
            menu.AddPageButton(13, 1, 2, ToggleCutting, () => "Tree cutting: " + OnOff(Nature.ChoppableTree.CuttingOn),
                () => Nature.ChoppableTree.CuttingOn);
            menu.AddPageButton(14, 0, 2, CycleForest,
                () => "Extra forest: " + (Forest != null ? Forest.Label : "-"), () => Forest != null && Forest.Level > 0);
            menu.AddPageButton(14, 1, 2, ToggleTreesLod1,
                () => "Trees LOD1: " + OnOff(detail != null && detail.TreesOnLod1), () => detail != null && detail.TreesOnLod1);
        }

        private ForestDensity forest;
        private ForestDensity Forest => forest != null ? forest : forest = GetComponent<ForestDensity>();

        private void CycleForest() { if (Forest != null) Forest.Cycle(); }

        private bool wind = true;

        private void CycleGlow()
        {
            if (merger != null) merger.SetKeeping((SceneryInstancer.LightKeeping)(((int)merger.Keeping + 1) % 3));
        }

        private void CycleFoveation() { if (budget != null) budget.CycleFoveation(); }

        private void ToggleWind()
        {
            wind = !wind;
            Shader.SetGlobalFloat("_MkWindOff", wind ? 0f : 1f);
        }

        private void ToggleCutting() => Nature.ChoppableTree.CuttingOn = !Nature.ChoppableTree.CuttingOn;

        /// <summary>Swaps the billboard trees between alpha clipped (cheap) and the pack's old blended look.</summary>
        private void ToggleBillboards()
        {
            if (cutoutShaders != null)
            {
                RestoreBillboards();
                return;
            }
            if (billboards == null || blendedBillboardShader == null) return;
            cutoutShaders = new Shader[billboards.Length];
            for (int i = 0; i < billboards.Length; i++)
            {
                cutoutShaders[i] = billboards[i].shader;
                billboards[i].shader = blendedBillboardShader;
            }
        }

        // The materials are assets, so the swap has to be undone or it would stay after play mode.
        private void RestoreBillboards()
        {
            if (cutoutShaders == null) return;
            for (int i = 0; i < billboards.Length; i++) billboards[i].shader = cutoutShaders[i];
            cutoutShaders = null;
        }

        private void OnDisable()
        {
            RestoreBillboards();
            Shader.SetGlobalFloat("_MkWindOff", 0f);
            Nature.ChoppableTree.CuttingOn = true;
        }

        private void CycleCap() { if (budget != null) budget.CycleCap(); }
        private void ToggleAi() { if (budget != null) budget.ToggleAi(); }
        private void CycleMsaa() { if (budget != null) budget.CycleMsaa(); }

        private void ToggleSoftShadows() { if (budget != null) budget.ToggleSoftShadows(); }
        private void ToggleCascades() { if (budget != null) budget.ToggleCascades(); }

        public void ToggleStorm()
        {
            if (storm != null) storm.Toggle();
        }

        public void CycleDetail()
        {
            if (detail != null) detail.Cycle();
        }

        // While on, trees never use their closest model: the second one shows at every distance.
        private void ToggleTreesLod1()
        {
            if (detail == null) return;
            detail.SetTreesOnLod1(!detail.TreesOnLod1);
            if (merger != null) merger.Rebuild(); // the merged scenery still holds the old models
        }

        private static string OnOff(bool on) => on ? "ON" : "OFF";

        private void Update()
        {
            // A lit campfire never runs out in this scene.
            if (campfire != null && campfire.IsLit && campfire.Fuel01 < 0.6f) campfire.AddFuel(40f);

#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null) return;
            if (k.digit1Key.wasPressedThisFrame) ApplyShader(0);
            if (k.digit2Key.wasPressedThisFrame) ApplyShader(1);
            if (k.digit3Key.wasPressedThisFrame) ApplyShader(2);
            if (k.digit4Key.wasPressedThisFrame) ApplyShader(3);
            if (k.digit5Key.wasPressedThisFrame) ApplyShader(4);
            if (k.lKey.wasPressedThisFrame && lightPresets.Length > 0) ApplyLighting((lighting + 1) % lightPresets.Length);
            if (k.pKey.wasPressedThisFrame) TogglePost();
            if (k.oKey.wasPressedThisFrame) ToggleShadows();
            if (k.fKey.wasPressedThisFrame) ToggleFog();
            if (k.gKey.wasPressedThisFrame) CycleGrass();
            if (k.kKey.wasPressedThisFrame) ToggleParticles();
            if (k.tKey.wasPressedThisFrame) ToggleStorm();
            if (k.bKey.wasPressedThisFrame) CycleDetail();
            if (k.iKey.wasPressedThisFrame) CycleDrawMode();
#endif
        }
    }
}
