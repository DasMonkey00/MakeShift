using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Makeshift.EditorTools
{
    /// <summary>
    /// Builds the playground's effects (fire, smoke, sparks, water spray, splash) plus the torus mesh. Everything
    /// uses the "Fully Opaque Fire" asset's look: solid low-poly chunks that shrink away instead of fading, drawn
    /// with its unlit opaque shader. No transparency, so no overdraw (Quest budget).
    /// </summary>
    static class ParticleFactory
    {
        const string Dir = "Assets/Makeshift/Particles/";

        const string FireAsset = "Assets/RomanCHACORNAC/Fire/";

        static T FireAssetAt<T>(string relative) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(FireAsset + relative);
            if (asset == null) throw new FileNotFoundException("Fully Opaque Fire asset missing", FireAsset + relative);
            return asset;
        }

        /// <summary>The asset's octahedron, resized to 1 m so particle sizes mean what they say.</summary>
        static Mesh Chunk()
        {
            string path = Dir + "Chunk.asset";
            var made = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (made != null) return made;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(FireAsset + "Meshes/SM_Octahedron.fbx"))
            {
                if (!(o is Mesh source)) continue;
                var mesh = Object.Instantiate(source);
                mesh.name = "Chunk";
                Vector3 size = source.bounds.size;
                float k = 1f / Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                var points = mesh.vertices;
                for (int i = 0; i < points.Length; i++) points[i] = (points[i] - source.bounds.center) * k;
                mesh.vertices = points;
                mesh.RecalculateBounds();
                Directory.CreateDirectory(Dir);
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            throw new FileNotFoundException("Fully Opaque Fire mesh missing", FireAsset + "Meshes/SM_Octahedron.fbx");
        }

        public static Material ParticleMat(string name)
        {
            Directory.CreateDirectory(Dir);
            string path = Dir + name + ".mat";
            var shader = FireAssetAt<Shader>("Shaders/VFX_FullyOpaque.shader");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            m.shaderKeywords = new string[0];
            m.renderQueue = -1;
            // Sparks glow a little over their own colour; everything else shows it as is.
            float glow = name == "SparkParticle" ? 1.5f : 1f;
            m.SetColor("_BaseColor", new Color(glow, glow, glow, 1f));
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Draws a particle system as solid spinning chunks that shrink to nothing.</summary>
        public static void Style(ParticleSystem ps, string materialName)
        {
            bool smoke = materialName == "SmokeParticle";
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = Chunk();
            r.enableGPUInstancing = false; // mesh particles did not draw with instancing on this shader
            r.alignment = ParticleSystemRenderSpace.World;
            r.sharedMaterial = ParticleMat(materialName);
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;

            var main = ps.main;
            main.startRotation3D = false;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            var spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-2f, 2f);

            // Pop in, hold, shrink away: the opaque stand-in for fading out.
            var shrink = ps.sizeOverLifetime;
            shrink.enabled = true;
            var curve = smoke
                ? new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(0.45f, 1f), new Keyframe(1f, 0f))
                : new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0f));
            shrink.size = new ParticleSystem.MinMaxCurve(1f, curve);

            // Solid chunks need to be smaller than the old soft dots, which were drawn much wider than they looked.
            if (smoke) main.startSizeMultiplier = Mathf.Min(main.startSizeMultiplier, 0.07f);
        }

        public static Material LashMaterial()
        {
            Directory.CreateDirectory("Assets/Makeshift/Materials");
            string path = "Assets/Makeshift/Materials/VineLash.mat";
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            m.SetColor("_BaseColor", new Color(0.25f, 0.5f, 0.2f));
            m.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// A loosely wound coil of vine, drawn as the same round rope as a finished lashing: a few uneven turns with
        /// both ends trailing out. Rebuilt in place each time, so tuning it shows after the next scene build.
        /// </summary>
        public static Mesh VineCoil(float thickness = 0.0072f)
        {
            Directory.CreateDirectory("Assets/Makeshift/Meshes");
            const string path = "Assets/Makeshift/Meshes/VineCoil.asset";
            const float turns = 4.5f;
            const int steps = 120, sides = 6;

            Vector3 At(float t)
            {
                float a = t * turns * Mathf.PI * 2f;
                // The two ends peel away from the loops.
                float tail = Mathf.Pow(Mathf.Clamp01((0.07f - t) / 0.07f), 2f) + Mathf.Pow(Mathf.Clamp01((t - 0.93f) / 0.07f), 2f);
                float radius = 0.066f + 0.011f * Mathf.Sin(a * 0.73f + 1.3f) + 0.035f * tail;
                float x = Mathf.Cos(a) * radius + 0.009f * Mathf.Sin(a * 0.31f);
                float z = Mathf.Sin(a) * radius * 0.9f + 0.008f * Mathf.Cos(a * 0.43f);
                float y = Mathf.Lerp(-0.016f, 0.016f, t) + 0.008f * Mathf.Sin(a * 1.37f + 0.5f);
                return new Vector3(x, y, z);
            }

            var verts = new Vector3[(steps + 1) * sides + 2];
            var normals = new Vector3[verts.Length];
            var tris = new System.Collections.Generic.List<int>();
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector3 forward = (At(Mathf.Min(t + 0.002f, 1f)) - At(Mathf.Max(t - 0.002f, 0f))).normalized;
                Vector3 right = Vector3.Cross(forward, Mathf.Abs(forward.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                Vector3 top = Vector3.Cross(right, forward);
                for (int s = 0; s < sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides;
                    Vector3 n = right * Mathf.Cos(a) + top * Mathf.Sin(a);
                    verts[i * sides + s] = At(t) + n * thickness;
                    normals[i * sides + s] = n;
                }
            }
            for (int i = 0; i < steps; i++)
                for (int s = 0; s < sides; s++)
                {
                    int a = i * sides + s, b = i * sides + (s + 1) % sides, c = a + sides, d = b + sides;
                    tris.AddRange(new[] { a, c, b, b, c, d });
                }
            // Closed ends.
            int first = verts.Length - 2, last = verts.Length - 1;
            verts[first] = At(0f); normals[first] = (At(0f) - At(0.002f)).normalized;
            verts[last] = At(1f); normals[last] = (At(1f) - At(0.998f)).normalized;
            for (int s = 0; s < sides; s++)
            {
                tris.AddRange(new[] { first, s, (s + 1) % sides });
                tris.AddRange(new[] { last, steps * sides + (s + 1) % sides, steps * sides + s });
            }

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool isNew = mesh == null;
            if (isNew) mesh = new Mesh();
            else mesh.Clear();
            mesh.name = "VineCoil";
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateBounds();
            if (isNew) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        public static Mesh Torus(string name, float majorRadius, float tubeRadius, int segments = 16, int sides = 8)
        {
            Directory.CreateDirectory("Assets/Makeshift/Meshes");
            string path = "Assets/Makeshift/Meshes/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;

            var verts = new Vector3[(segments + 1) * (sides + 1)];
            var normals = new Vector3[verts.Length];
            var uvs = new Vector2[verts.Length];
            for (int i = 0; i <= segments; i++)
            {
                float a = (float)i / segments * Mathf.PI * 2f;
                Vector3 centre = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * majorRadius;
                for (int j = 0; j <= sides; j++)
                {
                    float b = (float)j / sides * Mathf.PI * 2f;
                    Vector3 n = new Vector3(Mathf.Cos(a) * Mathf.Cos(b), Mathf.Sin(b), Mathf.Sin(a) * Mathf.Cos(b));
                    int idx = i * (sides + 1) + j;
                    verts[idx] = centre + n * tubeRadius;
                    normals[idx] = n;
                    uvs[idx] = new Vector2((float)i / segments, (float)j / sides);
                }
            }
            var tris = new int[segments * sides * 6];
            int t = 0;
            for (int i = 0; i < segments; i++)
            {
                for (int j = 0; j < sides; j++)
                {
                    int a0 = i * (sides + 1) + j;
                    int a1 = (i + 1) * (sides + 1) + j;
                    tris[t++] = a0; tris[t++] = a1; tris[t++] = a0 + 1;
                    tris[t++] = a0 + 1; tris[t++] = a1; tris[t++] = a1 + 1;
                }
            }
            var mesh = new Mesh { name = name, vertices = verts, normals = normals, uv = uvs, triangles = tris };
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // ---------- systems ----------

        static ParticleSystem NewSystem(string name, Transform parent, Vector3 localPos, Quaternion localRot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return ps;
        }

        static Gradient Fade(Color from, Color mid, Color to, float peakAlpha)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(from, 0f), new GradientColorKey(mid, 0.45f), new GradientColorKey(to, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peakAlpha, 0.15f), new GradientAlphaKey(peakAlpha * 0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        /// <summary>A flame (size 1 = campfire, 0.3 = torch). Returned object starts inactive; the game toggles it.</summary>
        public static GameObject Flame(Transform parent, Vector3 localPos, float size, bool withSmoke, float baseRadius = -1f)
        {
            // The root keeps the old emitter orientation: FlameUpright and the smoke offset rely on it.
            var go = new GameObject("Flame");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            AddFire(go, size, baseRadius);
            AddGlow(go, size);

            // Flames always point up, even when the log under them is turned over.
            go.AddComponent<Makeshift.Fire.FlameUpright>();
            if (withSmoke) AddFireflies(go); // the asset's fire has no smoke; fireflies mark the campfire
            go.SetActive(false);
            return go;
        }

        /// <summary>The asset's fire is about 0.5 m tall at scale 1; this makes a size 1 flame a good campfire.</summary>
        public const float FireScale = 1.2f;

        /// <summary>The asset's warm light colour. Campfire.cs still drives the flicker and strength.</summary>
        public static readonly Color FireLightColor = new Color(1f, 0.54f, 0.2f);

        /// <summary>Puts the Fully Opaque Fire effect under a flame root.</summary>
        public static void AddFire(GameObject flame, float size, float baseRadius = -1f)
        {
            var fire = (GameObject)PrefabUtility.InstantiatePrefab(FireAssetAt<GameObject>("Prefabs/VFX_Fire.prefab"), flame.transform);
            fire.transform.localPosition = Vector3.zero;
            fire.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // cancels the root's -90
            // A wide base (a torch head) never gets a flame narrower than itself.
            fire.transform.localScale = Vector3.one * Mathf.Max(FireScale * size, baseRadius * 8f);
        }

        /// <summary>
        /// A few large, faint, soft puffs of warm light round the base of a flame, to fill it out. Additive and few,
        /// so the transparency stays cheap.
        /// </summary>
        public static void AddGlow(GameObject flame, float size)
        {
            var ps = NewSystem("Glow", flame.transform, new Vector3(0f, 0f, 0.24f * size), Quaternion.identity); // +Z is up
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 8;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.3f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f * size, 0.75f * size);
            main.startColor = new Color(1f, 0.5f, 0.12f, 0.22f);

            var emission = ps.emission;
            emission.rateOverTime = 5f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f * size;

            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.45f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;

            var material = AssetDatabase.LoadAssetAtPath<Material>(Dir + "FlameParticle.mat");
            if (material == null) throw new FileNotFoundException("Soft glow material missing", Dir + "FlameParticle.mat");
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            ps.Play();
        }

        /// <summary>The asset's drifting fireflies, on while the campfire burns.</summary>
        public static void AddFireflies(GameObject flame)
        {
            var flies = (GameObject)PrefabUtility.InstantiatePrefab(FireAssetAt<GameObject>("Prefabs/VFX_Fireflies.prefab"), flame.transform);
            flies.transform.localPosition = new Vector3(0f, 0f, 0.3f); // the root's +Z is up
            flies.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        /// <summary>The asset campfire's three logs, as the stand-in fuel pile.</summary>
        public static void StyleFuelPile(Transform pile)
        {
            for (int i = pile.childCount - 1; i >= 0; i--) Object.DestroyImmediate(pile.GetChild(i).gameObject);
            var campfire = Object.Instantiate(FireAssetAt<GameObject>("Prefabs/P_Campfire.prefab"));
            for (int i = campfire.transform.childCount - 1; i >= 0; i--)
            {
                var child = campfire.transform.GetChild(i);
                if (!child.name.StartsWith("Log_")) continue;
                Vector3 pos = child.localPosition;
                child.SetParent(pile, false);
                child.localPosition = pos * 1.7f;
                child.localScale *= 1.7f;
            }
            Object.DestroyImmediate(campfire);
        }

        /// <summary>
        /// Puffs of smoke rising from a point. seconds &gt; 0 runs that long per Play; 0 loops until stopped.
        /// </summary>
        public static ParticleSystem SmokeColumn(Transform parent, Vector3 localPos, float seconds, float size)
        {
            var ps = NewSystem("SmokeColumn", parent, localPos, Quaternion.Euler(-90f, 0f, 0f));
            var main = ps.main;
            main.loop = seconds <= 0f;
            if (seconds > 0f) main.duration = seconds;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 24;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.25f, size * 0.45f);
            main.startColor = Color.white;
            main.gravityModifier = -0.02f;

            var emission = ps.emission;
            emission.rateOverTime = 9f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = size * 0.35f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Fade(new Color(0.75f, 0.75f, 0.75f), new Color(0.6f, 0.6f, 0.62f), new Color(0.5f, 0.5f, 0.52f), 0.55f);

            var grow = ps.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.7f));

            Style(ps, "SmokeParticle");
            return ps;
        }

        /// <summary>A short burst system. Call Emit(n) on it. Sparks or splash.</summary>
        public static ParticleSystem Burst(Transform parent, Vector3 localPos, bool sparks)
        {
            var ps = NewSystem(sparks ? "Sparks" : "Splash", parent, localPos, Quaternion.Euler(-90f, 0f, 0f));
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;
            main.startLifetime = sparks ? new ParticleSystem.MinMaxCurve(0.2f, 0.4f) : new ParticleSystem.MinMaxCurve(0.4f, 0.6f);
            main.startSpeed = sparks ? new ParticleSystem.MinMaxCurve(0.8f, 2f) : new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
            main.startSize = sparks ? new ParticleSystem.MinMaxCurve(0.012f, 0.022f) : new ParticleSystem.MinMaxCurve(0.025f, 0.045f);
            main.startColor = sparks ? new Color(1f, 0.85f, 0.4f, 1f) : new Color(0.7f, 0.85f, 1f, 0.85f);
            main.gravityModifier = sparks ? 1f : 1.2f;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = sparks ? 60f : 30f;
            shape.radius = sparks ? 0.01f : 0.03f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            Style(ps, sparks ? "SparkParticle" : "WaterParticle");
            return ps;
        }

        /// <summary>A thin stream that arcs out along dir (parent-local) and falls under gravity.</summary>
        public static ParticleSystem Spray(Transform parent, Vector3 localPos, Vector3 dir)
        {
            var ps = NewSystem("WaterSpray", parent, localPos, Quaternion.LookRotation(dir));
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            // Globby, not dotty: fewer, bigger drops that stretch along their motion, pulled by gravity like a pour.
            main.maxParticles = 48;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 0.85f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.05f);
            main.startColor = new Color(0.6f, 0.82f, 1f, 0.85f);
            main.gravityModifier = 1.4f;

            var emission = ps.emission;
            emission.rateOverTime = 40f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 2f;
            shape.radius = 0.012f;

            Style(ps, "WaterParticle");
            return ps;
        }
    }
}
