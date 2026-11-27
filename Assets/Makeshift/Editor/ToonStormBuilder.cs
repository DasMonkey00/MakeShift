using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Makeshift.Fire;
using Makeshift.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Makeshift.EditorTools
{
    /// <summary>Adds the storm (cloud dome, rain, blown leaves and its controller) to the look test scene.</summary>
    public static class ToonStormBuilder
    {
        const string Dir = "Assets/Makeshift/ToonNature/Storm";

        public static void Add(GameObject player, ToonLookSwitcher looks, Light sun, Camera head, Campfire campfire,
            StringBuilder log)
        {
            Directory.CreateDirectory(Dir);
            AssetDatabase.Refresh();

            // Everything the scene already emits, sorted by how weather should treat it.
            var existing = Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include)
                .Where(p => p.GetComponentInParent<FlameUpright>(true) == null).ToArray();
            var leaves = existing.Where(p => p.name.Contains("Falling_Leaves")).OrderBy(p => p.name).ToList();
            var fair = existing.Where(p => p.name.Contains("Firefl") || p.name.Contains("Sun_Shaft")).OrderBy(p => p.name).ToList();

            var haze = existing.Where(p => p.name.Contains("Fog")).OrderBy(p => p.name).ToList();

            var root = new GameObject("Storm");

            var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dome.name = "StormClouds";
            Object.DestroyImmediate(dome.GetComponent<Collider>());
            dome.transform.SetParent(root.transform, false);
            dome.transform.localScale = Vector3.one * 700f;
            var domeRenderer = dome.GetComponent<MeshRenderer>();
            domeRenderer.sharedMaterial = SkyMaterial();
            domeRenderer.shadowCastingMode = ShadowCastingMode.Off;
            domeRenderer.receiveShadows = false;
            domeRenderer.lightProbeUsage = LightProbeUsage.Off;
            domeRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            domeRenderer.enabled = false;

            var rain = Rain(root.transform);

            ParticleSystem blown = null;
            if (leaves.Count > 0) blown = BlownLeaves(root.transform, leaves[0]);

            var storm = root.AddComponent<StormController>();
            Set(storm, "looks", looks);
            Set(storm, "sun", sun);
            Set(storm, "head", head);
            Set(storm, "cloudDome", domeRenderer);
            Set(storm, "rain", rain);
            Set(storm, "blownLeaves", blown);
            Set(storm, "campfire", campfire);
            SetList(storm, "leaves", leaves);
            SetList(storm, "fairWeather", fair);
            SetList(storm, "haze", haze);
            Set(looks, "storm", storm);
            log.AppendLine($"storm: {leaves.Count} leaf systems, {fair.Count} fair weather systems");
        }

        static ParticleSystem Rain(Transform parent)
        {
            var go = new GameObject("Rain");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 0.75f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.009f, 0.016f);
            main.startColor = new Color(0.75f, 0.82f, 0.9f, 0.4f);
            main.maxParticles = 900;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(16f, 1f, 16f);

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = 0f;
            velocity.y = -13f;
            velocity.z = 0f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.026f;
            renderer.lengthScale = 1f;
            renderer.maxParticleSize = 0.012f;
            renderer.sharedMaterial = RainMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        /// <summary>A copy of one of the pack's leaf emitters, reshaped to sweep past the player.</summary>
        static ParticleSystem BlownLeaves(Transform parent, ParticleSystem source)
        {
            var go = Object.Instantiate(source.gameObject, parent);
            go.name = "BlownLeaves";
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var ps = go.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.prewarm = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 3.5f;
            main.maxParticles = 90;
            main.gravityModifier = 0.02f;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = Vector3.zero;
            shape.rotation = Vector3.zero;
            shape.scale = new Vector3(5f, 2.5f, 12f);
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = 5f;
            velocity.y = -0.4f;
            velocity.z = 1.7f;
            return ps;
        }

        static Material SkyMaterial()
        {
            string path = Dir + "/StormSky.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var shader = Shader.Find("Makeshift/StormSky");
                if (shader == null) throw new FileNotFoundException("Makeshift/StormSky shader missing");
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetTexture("_Noise", CloudNoise());
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material RainMaterial()
        {
            string path = Dir + "/Rain.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            m.SetTexture("_BaseMap", RainStreak());
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.SetShaderPassEnabled("DepthOnly", false);
            m.renderQueue = (int)RenderQueue.Transparent;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Texture2D RainStreak()
        {
            string path = Dir + "/RainStreak.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;
            const int w = 16, h = 64;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float across = 1f - Mathf.Abs((x + 0.5f) / w * 2f - 1f);
                    float along = Mathf.Sin(Mathf.PI * (y + 0.5f) / h);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, across * across * along));
                }
            t.Apply(true, true);
            AssetDatabase.CreateAsset(t, path);
            return t;
        }

        /// <summary>Tiling layered noise for the cloud shader.</summary>
        static Texture2D CloudNoise()
        {
            string path = Dir + "/CloudNoise.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;
            const int size = 256;
            var rng = new System.Random(5);
            var values = new float[size * size];
            float weight = 0.5f;
            for (int cells = 4; cells <= 64; cells *= 2)
            {
                var grid = new float[cells * cells];
                for (int i = 0; i < grid.Length; i++) grid[i] = (float)rng.NextDouble();
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float gx = x / (float)size * cells, gy = y / (float)size * cells;
                        int x0 = (int)gx, y0 = (int)gy, x1 = (x0 + 1) % cells, y1 = (y0 + 1) % cells;
                        float fx = Mathf.SmoothStep(0f, 1f, gx - x0), fy = Mathf.SmoothStep(0f, 1f, gy - y0);
                        float low = Mathf.Lerp(grid[y0 * cells + x0], grid[y0 * cells + x1], fx);
                        float high = Mathf.Lerp(grid[y1 * cells + x0], grid[y1 * cells + x1], fx);
                        values[y * size + x] += weight * Mathf.Lerp(low, high, fy);
                    }
                weight *= 0.55f;
            }
            float min = values.Min(), max = values.Max();
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Repeat };
            var pixels = values.Select(v => { float n = (v - min) / (max - min); return new Color(n, n, n, 1f); }).ToArray();
            t.SetPixels(pixels);
            t.Apply(true, true);
            AssetDatabase.CreateAsset(t, path);
            return t;
        }

        static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetList(Object target, string field, List<ParticleSystem> values)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            prop.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
