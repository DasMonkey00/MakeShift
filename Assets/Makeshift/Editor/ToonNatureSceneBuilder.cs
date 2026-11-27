using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autohand;
using Makeshift.Audio;
using Makeshift.Engine;
using Makeshift.Integration;
using Makeshift.Rendering;
using Makeshift.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Makeshift.EditorTools
{
    /// <summary>
    /// Builds Assets/Scenes/ToonNature.unity from the Toon Fantasy Nature demo scene Pines_2 (the lightest of the
    /// eight), adds the VR player and the look switcher. Running it again rebuilds the scene from the demo.
    /// </summary>
    public static class ToonNatureSceneBuilder
    {
        const string Pack = "Assets/Toon Fantasy Nature/";
        const string SourceScene = Pack + "Scenes/Demo_Scene_Pines_2.unity";
        const string ScenePath = "Assets/Scenes/ToonNature.unity";
        const string VariantDir = "Assets/Makeshift/ToonNature";
        const string BudgetPath = "Assets/Makeshift/Settings/BudgetProfile.asset";
        const string UiMaterial = "Assets/Makeshift/Materials/WristUI.mat";
        const float ClearingRadius = 3f;
        const float TerrainPixelError = 5f;

        [MenuItem("Makeshift/Build Toon Nature Look Test Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Debug.Log(BuildScene());
        }

        public static string BuildScene()
        {
            var log = new StringBuilder();
            FixFoliageMipmaps(log);

            AssetDatabase.DeleteAsset(ScenePath);
            if (!AssetDatabase.CopyAsset(SourceScene, ScenePath)) throw new IOException("could not copy " + SourceScene);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // The demo's own camera goes; its view point becomes the player's start.
            var demoCamera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).First();
            Vector3 viewPoint = demoCamera.transform.position;
            float yaw = demoCamera.transform.eulerAngles.y;
            Object.DestroyImmediate(demoCamera.gameObject);

            var sun = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Light>(true))
                .First(l => l.type == LightType.Directional);

            // The pack's layer numbers mean something else here (its terrain lands on HandPlayer, which the player
            // falls through), so all scenery goes on Default.
            int relayered = 0;
            foreach (var t in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)))
            {
                if (t.gameObject.layer < 6) continue;
                t.gameObject.layer = 0;
                PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);
                relayered++;
            }
            log.AppendLine($"moved {relayered} scenery objects to the Default layer");

            Physics.SyncTransforms();
            Vector3 start = viewPoint;
            var terrain = Object.FindAnyObjectByType<Terrain>();
            float ground = terrain != null ? terrain.SampleHeight(start) + terrain.transform.position.y : 0f;
            start.y = Physics.Raycast(new Vector3(start.x, viewPoint.y + 1f, start.z), Vector3.down, out var hit, 200f,
                ~0, QueryTriggerInteraction.Ignore) ? Mathf.Max(hit.point.y, ground) : ground;
            start.y += 0.05f;
            log.AppendLine($"demo view {viewPoint}, player start {start}, yaw {yaw:0}");

            var player = TemplateCommon.AddPlayer(start, Quaternion.Euler(0f, yaw, 0f));
            var ahp = player.GetComponentInChildren<AutoHandPlayer>(true);
            var head = player.GetComponentInChildren<Camera>(true);
            var spawn = new GameObject("TP_Spawn").transform;
            spawn.SetPositionAndRotation(start, player.transform.rotation);

            var jump = player.AddComponent<PlayerJump>();
            Set(jump, "player", ahp);
            player.AddComponent<HandShadowsOff>();

            var volumeGo = new GameObject("LookVolume");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;

            var menu = player.AddComponent<WristMenu>();
            Set(menu, "player", ahp);
            Set(menu, "leftHand", ahp.handLeft);
            Set(menu, "rightHand", ahp.handRight);
            Set(menu, "head", head);
            Set(menu, "sun", sun);
            Set(menu, "surfaceMaterial", UiSurface());

            var switcher = player.AddComponent<ToonLookSwitcher>();
            Set(switcher, "sun", sun);
            Set(switcher, "volume", volume);
            Set(switcher, "head", head);
            Set(switcher, "spawnPoint", spawn);

            // World budget: physics bubble, AI tiers, shadow distance and MSAA, measured from the head and both hands.
            var budget = player.AddComponent<WorldBudget>();
            Set(budget, "profile", BudgetAsset());
            SetList(budget, "viewers", new List<Object> { head.transform, ahp.handLeft.transform, ahp.handRight.transform });
            Set(switcher, "budget", budget);

            var all = scene.GetRootGameObjects().Where(r => r != player)
                .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();

            var grass = new List<Object>();
            var particles = new List<Object>();
            var sources = new List<Material>();
            int grassShadowsOff = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == player) continue;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null || sources.Contains(m)) continue;
                        string s = m.shader.name;
                        if (s.StartsWith("Toon/TFF_") && s != "Toon/TFF_ToonWater" && s != "Toon/TFF_ToonFire") sources.Add(m);
                    }
                    if (r.sharedMaterials.Any(m => m != null && m.shader.name == "Toon/TFF_CustomGrass"))
                    {
                        // Grass shadows are a large cost for very little picture.
                        r.shadowCastingMode = ShadowCastingMode.Off;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(r);
                        grassShadowsOff++;
                        var item = PrefabUtility.GetOutermostPrefabInstanceRoot(r.gameObject) ?? r.gameObject;
                        if (!grass.Contains(item)) grass.Add(item);
                    }
                }
                foreach (var p in root.GetComponentsInChildren<ParticleSystem>(true))
                    if (p.transform.parent == null || p.transform.parent.GetComponentInParent<ParticleSystem>() == null)
                        particles.Add(p.gameObject);
            }
            // A clearing round the start and the camp, so grass covers neither the menu nor the fire.
            Vector3 campCentre = start + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * GreyboxSceneBuilder.ToonCampDistance;
            bool Near(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude <= ClearingRadius;
            // The trees the player can cut down, each with a smaller clearing so the sticks in front of it show.
            var chopTrees = GreyboxSceneBuilder.PickChopTrees(start, yaw);
            // Random pines on the empty ground, after the choppable ones are picked so none of these is chosen.
            var treeCard = ToonForestBuilder.Add(terrain, start, campCentre, player, log);
            if (treeCard != null && !sources.Contains(treeCard)) sources.Add(treeCard);
            GreyboxSceneBuilder.AddTreeCutting(player, log);
            var treeFronts = chopTrees.Select(t => t.position + Vector3.ProjectOnPlane(start - t.position, Vector3.up).normalized).ToArray();
            int cleared = grass.RemoveAll(o =>
            {
                var go = (GameObject)o;
                if (!Near(go.transform.position, start) && !Near(go.transform.position, campCentre)
                    && treeFronts.All(f => Vector3.ProjectOnPlane(go.transform.position - f, Vector3.up).magnitude > 1.7f)) return false;
                go.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(go);
                return true;
            });
            log.AppendLine($"cleared {cleared} grass items around the start");
            SetList(switcher, "grass", grass);
            SetList(switcher, "particles", particles);
            SetList(switcher, "sourceMaterials", sources.Cast<Object>().ToList());

            // The pack's own old style particles would draw in one eye only in the headset.
            int converted = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var r in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
                {
                    var m = r.sharedMaterial;
                    if (m == null || !m.shader.name.StartsWith("Legacy Shaders/")) continue;
                    r.sharedMaterial = GreyboxSceneBuilder.UrpParticle(m);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(r);
                    converted++;
                }
            log.AppendLine($"converted {converted} old style particle renderers to URP");

            // Instanced terrain draws its patches together (about 125 fewer draws in view); saved in the scene so
            // that a build includes the shader variants it needs.
            if (terrain != null)
            {
                terrain.drawInstanced = true;
                terrain.heightmapPixelError = TerrainPixelError;
            }

            float Ground(Vector3 p)
            {
                float height = terrain != null ? terrain.SampleHeight(p) + terrain.transform.position.y : start.y;
                return height;
            }
            var campfire = GreyboxSceneBuilder.AddToonCamp(start, yaw, Ground, out var torches);
            Set(switcher, "campfire", campfire);
            SetList(switcher, "torches", torches.Cast<Object>().ToList());
            GreyboxSceneBuilder.AddToonCrafting(start, yaw, Ground, chopTrees, log);
            GreyboxSceneBuilder.AddGrenadeRack(GameObject.Find("CampTable").transform);
            ToonStormBuilder.Add(player, switcher, sun, head, campfire, log);

            var detail = player.AddComponent<DetailPresets>();
            Set(detail, "head", head);
            Set(switcher, "detail", detail);

            player.AddComponent<PerfStats>();
            var steps = player.AddComponent<Footsteps>();
            Set(steps, "player", ahp);
            PlayerSettings.enableFrameTimingStats = true; // CPU and GPU frame times in the stats card
            log.AppendLine($"audio listeners {Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include).Length}");
            log.AppendLine($"objects {all.Length}, grass items {grass.Count} (shadows off on {grassShadowsOff} renderers), " +
                           $"particle systems {particles.Count}, swappable materials {sources.Count}: " +
                           string.Join(", ", sources.Select(m => m.name)));

            WriteShaderSets(switcher, sources);
            SetList(switcher, "billboards", sources.Where(m => m.shader.name == "Toon/TFF_Board_Cutout").Cast<Object>().ToList());
            Set(switcher, "blendedBillboardShader", Shader.Find("Makeshift/Board Blended"));
            int dusk = WriteLightPresets(switcher);

            // Save the scene looking like its start preset, so edit mode matches play mode.
            var so = new SerializedObject(switcher);
            so.FindProperty("startLighting").intValue = dusk;
            so.ApplyModifiedPropertiesWithoutUndo();
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Pack + "Post Processing/TFF_Dusk_1.asset");
            RenderSettings.sun = sun;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            log.AppendLine("saved " + ScenePath);
            return log.ToString();
        }

        /// <summary>The shared World Budget profile. Created once; edit the asset to change the defaults.</summary>
        static BudgetProfile BudgetAsset()
        {
            var asset = AssetDatabase.LoadAssetAtPath<BudgetProfile>(BudgetPath);
            if (asset != null) return asset;
            if (!AssetDatabase.IsValidFolder("Assets/Makeshift/Settings"))
                AssetDatabase.CreateFolder("Assets/Makeshift", "Settings");
            asset = ScriptableObject.CreateInstance<BudgetProfile>();
            AssetDatabase.CreateAsset(asset, BudgetPath);
            return asset;
        }

        /// <summary>The pack ships foliage textures without mipmaps, which shimmers badly in VR and costs bandwidth.</summary>
        static void FixFoliageMipmaps(StringBuilder log)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Pack + "Textures" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileName(path);
                // Billboards are alpha clipped, so their mipmaps must keep coverage or distant trees thin out.
                bool billboard = name.StartsWith("TFF_Atlas_Billboards_");
                bool foliage = billboard || name.Contains("_Patch_") || name == "TFF_Atlas_Vegetation_1A_D.tga";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (!foliage || importer == null || (importer.mipmapEnabled && (!billboard || importer.mipMapsPreserveCoverage))) continue;
                importer.mipmapEnabled = true;
                importer.mipMapsPreserveCoverage = true;
                importer.alphaTestReferenceValue = 0.5f;
                importer.SaveAndReimport();
                log.AppendLine("mipmaps on: " + name);
            }
        }

        static void WriteShaderSets(ToonLookSwitcher switcher, List<Material> sources)
        {
            Directory.CreateDirectory(VariantDir);
            AssetDatabase.Refresh();
            var names = new[] { "Toon (pack)", "Toon no outline", "URP Lit", "URP + wind", "URP Unlit" };
            var sets = new List<Material[]>
            {
                sources.ToArray(),
                sources.Select(NoOutline).ToArray(),
                sources.Select(m => UrpCopy(m, "Lit")).ToArray(),
                sources.Select(m => WindCopy(m, sources)).ToArray(),
                sources.Select(m => UrpCopy(m, "Unlit")).ToArray()
            };

            // Instancing variants are only built into an app when some material asks for them.
            foreach (var m in sets.SelectMany(set => set).Distinct())
            {
                if (m.enableInstancing) continue;
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
            }

            var so = new SerializedObject(switcher);
            var prop = so.FindProperty("shaderSets");
            prop.arraySize = sets.Count;
            for (int i = 0; i < sets.Count; i++)
            {
                var element = prop.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("name").stringValue = names[i];
                var mats = element.FindPropertyRelative("materials");
                mats.arraySize = sets[i].Length;
                for (int m = 0; m < sets[i].Length; m++)
                    mats.GetArrayElementAtIndex(m).objectReferenceValue = sets[i][m];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Material NoOutline(Material source)
        {
            if (source.shader.name != "Toon/TFF_CustomToonOutline") return source;
            var copy = new Material(source) { shader = Shader.Find("Toon/TFF_CustomToon") };
            return Save(copy, source.name + " NoOutline");
        }

        static Material UrpCopy(Material source, string kind)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/" + kind));
            Texture tex = null;
            foreach (var property in new[] { "_TextureSample", "_TextureSample0", "_MainTex" })
                if (tex == null && source.HasProperty(property)) tex = source.GetTexture(property);
            m.SetTexture("_BaseMap", tex);

            // The grass shader multiplies a pale texture by two colours, bottom to top; use their mix.
            var tint = Color.white;
            if (source.shader.name == "Toon/TFF_CustomGrass")
                tint = Color.Lerp(source.GetColor("_Color1"), source.GetColor("_Color2"), 0.5f);
            tint.a = 1f;
            m.SetColor("_BaseColor", tint);

            if (source.shader.name != "Toon/TFF_CustomToon" && source.shader.name != "Toon/TFF_CustomToonOutline")
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.5f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
            if (kind == "Lit")
            {
                m.SetFloat("_SpecularHighlights", 0f);
                m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                m.SetFloat("_EnvironmentReflections", 0f);
                m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            }
            m.enableInstancing = source.enableInstancing;
            return Save(m, source.name + " " + kind.Replace(" ", ""));
        }

        /// <summary>Our URP shader wearing a pack material's texture, colours and wind.</summary>
        static Material WindCopy(Material source, List<Material> sources)
        {
            var m = new Material(Shader.Find("Makeshift/Scenery"));
            Texture tex = null;
            foreach (var property in new[] { "_TextureSample", "_TextureSample0", "_MainTex" })
                if (tex == null && source.HasProperty(property)) tex = source.GetTexture(property);
            m.SetTexture("_BaseMap", tex);

            string shader = source.shader.name;
            bool grass = shader == "Toon/TFF_CustomGrass";
            bool leaves = shader == "Toon/TFF_CustomToonVegetation";
            bool wood = shader == "Toon/TFF_CustomToon" || shader == "Toon/TFF_CustomToonOutline";
            bool card = !grass && !leaves && !wood;

            void Switch(string property, string keyword, bool on)
            {
                m.SetFloat(property, on ? 1f : 0f);
                if (on) m.EnableKeyword(keyword);
                else m.DisableKeyword(keyword);
            }

            Switch("_AlphaClip", "_ALPHATEST_ON", !wood);
            m.SetFloat("_Cutoff", 0.5f);
            m.SetFloat("_Cull", wood ? (float)CullMode.Back : (float)CullMode.Off);
            m.SetFloat("_Wrap", wood ? 0.25f : grass ? 0f : 0.6f);
            if (!wood)
            {
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }

            Switch("_GrassFade", "_GRASS", grass);
            Switch("_Wind", "_WIND", grass || leaves);
            if (grass)
            {
                m.SetColor("_BaseColor", Opaque(source.GetColor("_Color2")));
                m.SetColor("_TopColor", Opaque(source.GetColor("_Color1")));
                m.SetFloat("_GradientLevel", source.GetFloat("_Color1Level"));
            }
            if (grass || leaves)
            {
                m.SetTexture("_WindNoiseTexture", source.GetTexture("_WindNoiseTexture"));
                float speed = grass ? source.GetFloat("_WindSpeed") : 1f;
                float scale = grass ? Mathf.Lerp(0.01f, 0.35f, source.GetFloat("_WindNoiseSize")) : 0.1f;
                m.SetVector("_WindParams", new Vector4(scale, source.GetFloat("_WindScroll") * 0.3f * speed,
                    source.GetFloat("_WindJitter") * 0.5f * speed, grass ? 1f : 2f));
            }

            // A card is a white shape, painted one flat colour: the pine needles' own average, lit the same way as the
            // needles, so a tree keeps its colour when it changes to the card. Fog gives the far ones their haze.
            Switch("_FlatColor", "_FLAT_COLOR", card);
            if (card)
            {
                var needles = sources.FirstOrDefault(x => x.shader.name == "Toon/TFF_CustomToonVegetation");
                if (needles != null) m.SetColor("_BaseColor", Average(needles.GetTexture("_TextureSample")));
            }

            m.enableInstancing = true;
            return Save(m, source.name + " Wind");
        }

        static Color Opaque(Color c) => new Color(c.r, c.g, c.b, 1f);

        // The average colour of a texture's solid texels (read through a small copy: the asset need not be readable).
        static Color Average(Texture texture)
        {
            var small = RenderTexture.GetTemporary(128, 128, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(texture, small);
            var before = RenderTexture.active;
            RenderTexture.active = small;
            var copy = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
            RenderTexture.active = before;
            RenderTexture.ReleaseTemporary(small);
            Vector3 sum = Vector3.zero;
            float weight = 0f;
            foreach (var p in copy.GetPixels())
            {
                if (p.a < 0.5f) continue;
                sum += new Vector3(p.r, p.g, p.b);
                weight++;
            }
            Object.DestroyImmediate(copy);
            if (weight == 0f) return new Color(0.2f, 0.25f, 0.15f);
            sum /= weight;
            return new Color(sum.x, sum.y, sum.z, 1f);
        }

        /// <summary>Rebuilds the URP + wind set in the open scene and starts the scene on it.</summary>
        public static string ApplyWindShaderSet()
        {
            var switcher = Object.FindAnyObjectByType<ToonLookSwitcher>();
            var so = new SerializedObject(switcher);
            var source = so.FindProperty("sourceMaterials");
            var sources = new List<Material>();
            for (int i = 0; i < source.arraySize; i++)
                sources.Add(source.GetArrayElementAtIndex(i).objectReferenceValue as Material);
            Directory.CreateDirectory(VariantDir);
            var set = so.FindProperty("shaderSets").GetArrayElementAtIndex(3);
            set.FindPropertyRelative("name").stringValue = "URP + wind";
            var materials = set.FindPropertyRelative("materials");
            materials.arraySize = sources.Count;
            for (int i = 0; i < sources.Count; i++)
                materials.GetArrayElementAtIndex(i).objectReferenceValue = WindCopy(sources[i], sources);
            so.FindProperty("startShader").intValue = 3;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(switcher.gameObject.scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(switcher.gameObject.scene);
            return $"{sources.Count} materials";
        }

        static Material Save(Material m, string name)
        {
            string path = $"{VariantDir}/{name}.mat";
            m.name = name;
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(m, path);
                return m;
            }
            EditorUtility.CopySerialized(m, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(m);
            return existing;
        }

        struct Preset
        {
            public string name, profile, sky;
            public Color sun, fog, ambSky, ambEquator, ambGround;
            public float intensity, density, start, end;
            public Quaternion rotation;
            public FogMode mode;
        }

        /// <summary>Values copied from the six demo scenes that use each post processing profile.</summary>
        static int WriteLightPresets(ToonLookSwitcher switcher)
        {
            var equator = new Color(0.708f, 0.582f, 0.484f);
            var ground = new Color(0.311f, 0.310f, 0.453f);
            var presets = new[]
            {
                new Preset { name = "Day 1", profile = "Day_1", sky = "Day", sun = Color.white, intensity = 1.4f,
                    rotation = new Quaternion(-0.36658683f, -0.71372926f, 0.57566947f, 0.1575099f),
                    fog = new Color(0.933f, 0.863f, 0.490f), mode = FogMode.Linear, density = 0.01f, start = 85f, end = 350f,
                    ambSky = Color.white, ambEquator = equator, ambGround = ground },
                new Preset { name = "Day 2", profile = "Day_2", sky = "Day", sun = Color.white, intensity = 1.4f,
                    rotation = new Quaternion(-0.6346048f, -0.5275884f, 0.2127777f, -0.5231184f),
                    fog = new Color(0.281f, 0.530f, 0.670f), mode = FogMode.ExponentialSquared, density = 0.005f, start = 85f, end = 150f,
                    ambSky = Color.white, ambEquator = equator, ambGround = ground },
                new Preset { name = "Day 3", profile = "Day_3", sky = "Day", sun = Color.white, intensity = 1.4f,
                    rotation = new Quaternion(0.5652223f, 0.3476637f, -0.6702713f, 0.33225015f),
                    fog = new Color(0.435f, 0.699f, 0.788f), mode = FogMode.ExponentialSquared, density = 0.005f, start = 50f, end = 120f,
                    ambSky = Color.white, ambEquator = equator, ambGround = ground },
                new Preset { name = "Day 4", profile = "Day_4", sky = "Day", sun = Color.white, intensity = 1.4f,
                    rotation = new Quaternion(0.4760192f, 0.008630635f, 0.16627793f, 0.8635293f),
                    fog = new Color(0.643f, 0.414f, 0.349f), mode = FogMode.ExponentialSquared, density = 0.004f, start = 85f, end = 150f,
                    ambSky = new Color(1f, 0.816f, 0.637f), ambEquator = new Color(1f, 0.710f, 0.665f), ambGround = new Color(0f, 0.386f, 1f) },
                new Preset { name = "Dusk", profile = "Dusk_1", sky = "Sunset", sun = Color.white, intensity = 1.4f,
                    rotation = new Quaternion(0.01615897f, -0.86969566f, 0.4681722f, -0.15550901f),
                    fog = new Color(0.745f, 0.598f, 0.418f), mode = FogMode.ExponentialSquared, density = 0.015f, start = 85f, end = 150f,
                    ambSky = Color.white, ambEquator = new Color(0.594f, 0.594f, 0.594f), ambGround = new Color(0.165f, 0.084f, 0.021f) },
                new Preset { name = "Night", profile = "Night_1", sky = "Night", sun = new Color(0.517f, 0.646f, 0.906f), intensity = 1.6f,
                    rotation = new Quaternion(0.6621247f, -0.48695698f, 0.53913665f, 0.18383561f),
                    fog = new Color(0.095f, 0.219f, 0.519f), mode = FogMode.ExponentialSquared, density = 0.0025f, start = 20f, end = 185f,
                    ambSky = new Color(0.175f, 0.153f, 0.349f), ambEquator = new Color(0.437f, 0.376f, 0.820f), ambGround = new Color(0f, 0.283f, 0.491f) },
            };

            var so = new SerializedObject(switcher);
            var prop = so.FindProperty("lightPresets");
            prop.arraySize = presets.Length;
            for (int i = 0; i < presets.Length; i++)
            {
                var p = presets[i];
                var e = prop.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("name").stringValue = p.name;
                e.FindPropertyRelative("profile").objectReferenceValue =
                    Load<VolumeProfile>(Pack + $"Post Processing/TFF_{p.profile}.asset");
                e.FindPropertyRelative("skybox").objectReferenceValue =
                    Load<Material>(Pack + $"Skyboxes/TFF_Skybox_{p.sky}_01A.mat");
                e.FindPropertyRelative("sunColor").colorValue = p.sun;
                e.FindPropertyRelative("sunIntensity").floatValue = p.intensity;
                e.FindPropertyRelative("sunRotation").quaternionValue = p.rotation;
                e.FindPropertyRelative("fogColor").colorValue = p.fog;
                // enumValueIndex is the position in the list; FogMode starts at 1.
                e.FindPropertyRelative("fogMode").intValue = (int)p.mode;
                e.FindPropertyRelative("fogDensity").floatValue = p.density;
                e.FindPropertyRelative("fogStart").floatValue = p.start;
                e.FindPropertyRelative("fogEnd").floatValue = p.end;
                e.FindPropertyRelative("ambientSky").colorValue = p.ambSky;
                e.FindPropertyRelative("ambientEquator").colorValue = p.ambEquator;
                e.FindPropertyRelative("ambientGround").colorValue = p.ambGround;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return 4; // Dusk, the look Pines_2 was authored with.
        }

        static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new FileNotFoundException("missing " + path);
            return asset;
        }

        static Material UiSurface()
        {
            var ui = AssetDatabase.LoadAssetAtPath<Material>(UiMaterial);
            if (ui != null) return ui;
            Directory.CreateDirectory(Path.GetDirectoryName(UiMaterial));
            ui = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            AssetDatabase.CreateAsset(ui, UiMaterial);
            return ui;
        }

        static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetList(Object target, string field, List<Object> values)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            prop.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
