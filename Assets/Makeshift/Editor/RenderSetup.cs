using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Makeshift.EditorTools
{
    /// <summary>
    /// One-time render pipeline setup: two URP assets (a lean one for Quest, a richer one for PC),
    /// assigned per quality level, plus conversion of old Built-in "Standard" materials.
    /// Safe to run again: it reuses assets that exist.
    /// </summary>
    public static class RenderSetup
    {
        const string Dir = "Assets/Makeshift/Rendering";
        public const string QuestAssetPath = Dir + "/Makeshift_URP_Quest.asset";
        public const string PcAssetPath = Dir + "/Makeshift_URP_PC.asset";

        [MenuItem("Makeshift/Setup URP (Quest + PC assets)")]
        public static void SetupMenu() { Setup(); }

        public static void SetupBatch()
        {
            Setup();
            EditorApplication.Exit(0);
        }

        public static void Setup()
        {
            Directory.CreateDirectory(Dir);
            var quest = MakeAsset(QuestAssetPath, true);
            var pc = MakeAsset(PcAssetPath, false);

            GraphicsSettings.defaultRenderPipeline = pc;
            int levels = QualitySettings.names.Length;
            int original = QualitySettings.GetQualityLevel();
            for (int i = 0; i < levels; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                bool isQuest = QualitySettings.names[i].Contains("Quest");
                QualitySettings.renderPipeline = isQuest ? quest : pc;
                Debug.Log($"[SETUP] quality '{QualitySettings.names[i]}' -> {(isQuest ? "Quest" : "PC")} URP asset");
            }
            QualitySettings.SetQualityLevel(original, false);

            ConvertStandardMaterials(null);
            AssetDatabase.SaveAssets();
            Debug.Log("[SETUP] URP setup done");
        }

        static UniversalRenderPipelineAsset MakeAsset(string path, bool quest)
        {
            string rendererPath = path.Replace(".asset", "_Renderer.asset");
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (asset == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, rendererPath);
                asset = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(asset, path);
            }

            // Settings are reapplied every run so the numbers here stay the source of truth.
            asset.msaaSampleCount = 4;
            var so = new SerializedObject(asset);
            Set(so, "m_SupportsHDR", quest ? 0 : 1);
            Set(so, "m_RequireDepthTexture", 0);
            Set(so, "m_RequireOpaqueTexture", 0);
            Set(so, "m_SupportsTerrainHoles", 0);
            Set(so, "m_SupportsDynamicBatching", 0); // removed from URP; ticked, it only logs an error
            // Quest-friendly shadows: one hard-edged cascade of the sun, 1024 map, 25 m. No extra-light shadows, no soft.
            Set(so, "m_MainLightShadowsSupported", 1);
            // Quest bumped 1024 -> 2048 (one step up). Needs an on-device check at 90 fps; drop back to 1024 if it costs frames.
            Set(so, "m_MainLightShadowmapResolution", 2048);
            Set(so, "m_SoftShadowsSupported", quest ? 0 : 1);
            Set(so, "m_ShadowCascadeCount", quest ? 1 : 3);
            Set(so, "m_ShadowDistance", quest ? 25f : 60f);
            Set(so, "m_AdditionalLightsPerObjectLimit", quest ? 2 : 4);
            Set(so, "m_AdditionalLightShadowsSupported", 0);
            so.ApplyModifiedPropertiesWithoutUndo();

            // Forward on both (Forward+ can come later for PC; one path keeps testing honest).
            if (rendererData != null)
            {
                var rso = new SerializedObject(rendererData);
                Set(rso, "m_RenderingMode", 0);
                Set(rso, "m_DepthPrimingMode", 0);
                rso.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(rendererData);
            }

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }

        static void Set(SerializedObject so, string name, int v)
        {
            var p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning($"[SETUP] URP asset has no field {name}"); return; }
            if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = v != 0; else p.intValue = v;
        }

        static void Set(SerializedObject so, string name, float v)
        {
            var p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning($"[SETUP] URP asset has no field {name}"); return; }
            p.floatValue = v;
        }

        // ---------- materials ----------

        /// <summary>The shader builders give new surfaces.</summary>
        public static Shader StyledShader() => Shader.Find("Universal Render Pipeline/Lit");

        /// <summary>Moves Standard materials to URP Lit (or the given shader). Returns how many changed.</summary>
        public static int ConvertStandardMaterials(Shader target, string folder = "Assets")
        {
            target = target != null ? target : Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            int converted = 0;
            var unknown = new HashSet<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith("Assets/Photon")) continue;
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null || m.shader == null) continue;
                string n = m.shader.name;
                if (n == "Standard" || n == "Standard (Specular setup)")
                {
                    ConvertOne(m, target);
                    converted++;
                }
                else if (n == "Unlit/Color" || n == "Unlit/Texture")
                {
                    ConvertOne(m, unlit);
                    converted++;
                }
                else if (n == "Hidden/InternalErrorShader" || n.StartsWith("Legacy Shaders/") || n.StartsWith("Mobile/"))
                {
                    ConvertOne(m, target);
                    converted++;
                }
                else if (!n.StartsWith("Universal Render Pipeline/") && !n.StartsWith("Shader Graphs/") &&
                         !n.StartsWith("Sprites/") && !n.StartsWith("UI/") && !n.StartsWith("TextMeshPro/"))
                    unknown.Add(n + "  (" + path + ")");
            }
            foreach (var u in unknown) Debug.Log("[SETUP] left alone (custom shader): " + u);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SETUP] converted {converted} materials");
            return converted;
        }

        /// <summary>Changes a material's shader and carries over color, textures and basics.</summary>
        public static void ConvertOne(Material m, Shader target)
        {
            Color color = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
            Texture main = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
            Texture bump = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
            Texture occ = m.HasProperty("_OcclusionMap") ? m.GetTexture("_OcclusionMap") : null;
            float metallic = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : 0f;
            float smooth = m.HasProperty("_Glossiness") ? m.GetFloat("_Glossiness") : 0.4f;
            bool emissive = m.IsKeywordEnabled("_EMISSION");
            Color emission = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
            Texture emissionMap = m.HasProperty("_EmissionMap") ? m.GetTexture("_EmissionMap") : null;
            float mode = m.HasProperty("_Mode") ? m.GetFloat("_Mode") : 0f; // 0 opaque, 1 cutout, 2 fade, 3 transparent
            float cutoff = m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0.5f;
            Vector2 tile = m.HasProperty("_MainTex") ? m.GetTextureScale("_MainTex") : Vector2.one;
            Vector2 offs = m.HasProperty("_MainTex") ? m.GetTextureOffset("_MainTex") : Vector2.zero;

            m.shader = target;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", main); m.SetTextureScale("_BaseMap", tile); m.SetTextureOffset("_BaseMap", offs); }
            if (m.HasProperty("_BumpMap") && bump != null) { m.SetTexture("_BumpMap", bump); m.EnableKeyword("_NORMALMAP"); }
            if (m.HasProperty("_OcclusionMap") && occ != null) { m.SetTexture("_OcclusionMap", occ); m.EnableKeyword("_OCCLUSIONMAP"); }
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (emissive && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission);
                if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", emissionMap);
            }
            if (mode >= 2f && m.HasProperty("_Surface"))
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)RenderQueue.Transparent;
                m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            else if (mode == 1f && m.HasProperty("_AlphaClip"))
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", cutoff);
                m.EnableKeyword("_ALPHATEST_ON");
            }
            EditorUtility.SetDirty(m);
        }
    }
}
