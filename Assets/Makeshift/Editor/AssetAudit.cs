using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Makeshift.EditorTools
{
    /// <summary>
    /// Read-only asset audit for the Quest 3S budget. Menu: Makeshift > Audit Assets (CSV).
    ///
    /// Writes to the Logs folder of the project:
    /// <list type="bullet">
    /// <item>AssetAudit_Meshes.csv: vertices, triangles, submeshes, readable, down-facing triangles near the base,
    /// screen size at 10 m, instances and materials in the active scene, duplicate groups.</item>
    /// <item>AssetAudit_Materials.csv: shader, alpha mode (opaque, cutout, blended), outline pass, instancing.</item>
    /// <item>AssetAudit_Textures.csv: size, mipmaps, max size, Android format.</item>
    /// <item>AssetAudit_Top30.txt: meshes ranked by instances times triangles, with the expected savings.</item>
    /// </list>
    /// Nothing in the project is changed. Back faces are not measured (needs a mesh read per face and a view
    /// direction); bottom faces are the down-facing triangles in the lowest tenth of the mesh.
    /// Meshes without Read/Write enabled report 0 bottom triangles and "no" in the readable column.
    /// </summary>
    public static class AssetAudit
    {
        static readonly string[] Roots = { "Assets/Makeshift", "Assets/Toon Fantasy Nature" };
        const string OutDir = "Logs";
        const float ReferenceMetres = 10f;
        const float ScreenHeightAt60DegFov = 1.1547005f; // 2 * tan(30 degrees): a 60 degree vertical field of view
        const float TinyPercent = 3f;

        class MeshRow
        {
            public string path, name;
            public int vertices, triangles, submeshes, instances, sceneMaterials, bottomTris, group;
            public bool readable;
            public float screenPercent;
            public string sceneShaders;
        }

        [MenuItem("Makeshift/Audit Assets (CSV)")]
        public static void Run()
        {
            try
            {
                Directory.CreateDirectory(OutDir);
                var usage = SceneUsage(out string sceneName);
                var meshes = AuditMeshes(usage);
                int materials = AuditMaterials();
                int textures = AuditTextures();
                WriteTop30(meshes, sceneName);
                string summary = $"asset audit: {meshes.Count} meshes, {materials} materials, {textures} textures, " +
                                 $"scene '{sceneName}' ({usage.Count} meshes in use). Output in {Path.GetFullPath(OutDir)}";
                Debug.Log(summary);
            }
            catch (Exception e)
            {
                Debug.LogError("asset audit failed: " + e);
            }
        }

        class SceneUse
        {
            public int instances;
            public readonly HashSet<string> shaders = new HashSet<string>();
            public readonly HashSet<Material> materials = new HashSet<Material>();
        }

        /// <summary>How often each mesh appears in the active scene, and with which materials.</summary>
        static Dictionary<Mesh, SceneUse> SceneUsage(out string sceneName)
        {
            var scene = SceneManager.GetActiveScene();
            sceneName = scene.name;
            var usage = new Dictionary<Mesh, SceneUse>();
            foreach (var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include))
            {
                var mesh = filter.sharedMesh;
                var renderer = filter.GetComponent<MeshRenderer>();
                if (mesh == null || renderer == null || !renderer.gameObject.scene.IsValid()) continue;
                if (!usage.TryGetValue(mesh, out var use)) usage[mesh] = use = new SceneUse();
                use.instances++;
                foreach (var m in renderer.sharedMaterials)
                {
                    if (m == null) continue;
                    use.materials.Add(m);
                    use.shaders.Add(m.shader.name);
                }
            }
            return usage;
        }

        static List<MeshRow> AuditMeshes(Dictionary<Mesh, SceneUse> usage)
        {
            var rows = new List<MeshRow>();
            foreach (var guid in AssetDatabase.FindAssets("t:Mesh", Roots))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null) continue;

                int tris = 0;
                for (int s = 0; s < mesh.subMeshCount; s++) tris += (int)(mesh.GetIndexCount(s) / 3);
                var row = new MeshRow
                {
                    path = path,
                    name = mesh.name,
                    vertices = mesh.vertexCount,
                    triangles = tris,
                    submeshes = mesh.subMeshCount,
                    readable = mesh.isReadable,
                    screenPercent = mesh.bounds.size.y / (ReferenceMetres * ScreenHeightAt60DegFov) * 100f,
                };
                if (usage.TryGetValue(mesh, out var use))
                {
                    row.instances = use.instances;
                    row.sceneMaterials = use.materials.Count;
                    row.sceneShaders = string.Join(" | ", use.shaders.OrderBy(s => s));
                }
                if (row.readable) row.bottomTris = BottomTris(mesh);
                rows.Add(row);
            }

            // Duplicates: same vertex count, triangle count and size. Usually one mesh under several material variants.
            var groups = new Dictionary<string, int>();
            foreach (var row in rows.OrderBy(r => r.path, StringComparer.Ordinal))
            {
                string key = $"{row.vertices}|{row.triangles}|{row.screenPercent:0.000}";
                if (!groups.TryGetValue(key, out int id)) groups[key] = id = groups.Count + 1;
                row.group = id;
            }
            var counts = rows.GroupBy(r => r.group).ToDictionary(g => g.Key, g => g.Count());
            foreach (var row in rows)
                if (counts[row.group] < 2) row.group = 0;

            Write("AssetAudit_Meshes.csv", new[]
            {
                "path,name,vertices,triangles,submeshes,readable,bottom_tris,screen_pct_at_10m,tiny,instances_in_scene,materials_in_scene,shaders_in_scene,duplicate_group"
            }.Concat(rows.Select(r => Line(r.path, r.name, r.vertices, r.triangles, r.submeshes, r.readable ? "yes" : "no",
                r.bottomTris, r.screenPercent.ToString("0.00", CultureInfo.InvariantCulture),
                r.screenPercent < TinyPercent ? "yes" : "no", r.instances, r.sceneMaterials, r.sceneShaders ?? "",
                r.group == 0 ? "" : r.group.ToString()))));
            return rows;
        }

        /// <summary>Down-facing triangles whose centre sits in the lowest tenth of the mesh: the faces nobody sees.</summary>
        static int BottomTris(Mesh mesh)
        {
            float height = mesh.bounds.size.y;
            if (height <= 0f) return 0;
            float low = mesh.bounds.min.y + 0.1f * height;
            var v = mesh.vertices;
            int count = 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var t = mesh.GetTriangles(s);
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                    Vector3 n = Vector3.Cross(b - a, c - a);
                    if (n.sqrMagnitude < 1e-12f) continue;
                    n.Normalize();
                    if (n.y > -0.9f) continue;
                    if ((a.y + b.y + c.y) / 3f > low) continue;
                    count++;
                }
            }
            return count;
        }

        static int AuditMaterials()
        {
            var lines = new List<string> { "path,name,shader,alpha,outline_pass,instancing,queue,textures" };
            var found = AssetDatabase.FindAssets("t:Material", Roots);
            foreach (var guid in found)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null || m.shader == null) continue;
                string shader = m.shader.name;
                int textures = 0;
                var props = m.shader.GetPropertyCount();
                for (int i = 0; i < props; i++)
                    if (m.shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Texture) textures++;
                lines.Add(Line(path, m.name, shader, AlphaMode(m), shader.Contains("Outline") ? "yes" : "no",
                    m.enableInstancing ? "yes" : "no", m.renderQueue, textures));
            }
            Write("AssetAudit_Materials.csv", lines);
            return found.Length;
        }

        /// <summary>Opaque, cutout or blended, from the URP surface property or the common shader conventions.</summary>
        static string AlphaMode(Material m)
        {
            if (m.HasProperty("_Surface"))
            {
                if (m.GetFloat("_Surface") < 0.5f) return m.HasProperty("_AlphaClip") && m.GetFloat("_AlphaClip") > 0.5f ? "cutout" : "opaque";
                return "blended";
            }
            if (m.HasProperty("_Mode"))
            {
                switch ((int)m.GetFloat("_Mode"))
                {
                    case 0: return "opaque";
                    case 1: return "cutout";
                    default: return "blended";
                }
            }
            // The queue first: a shader can be called "Cutout" and still blend (the pack's billboard shader does).
            if (m.renderQueue >= (int)RenderQueue.Transparent - 1) return "blended";
            if (m.renderQueue >= (int)RenderQueue.AlphaTest || m.IsKeywordEnabled("_ALPHATEST_ON")) return "cutout";
            return "opaque";
        }

        static int AuditTextures()
        {
            var lines = new List<string> { "path,name,width,height,max_size,mipmaps,readable,android_format,compression" };
            var found = AssetDatabase.FindAssets("t:Texture2D", Roots);
            foreach (var guid in found)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                importer.GetSourceTextureWidthAndHeight(out int width, out int height);
                var android = importer.GetPlatformTextureSettings("Android");
                lines.Add(Line(path, Path.GetFileNameWithoutExtension(path), width, height, importer.maxTextureSize,
                    importer.mipmapEnabled ? "yes" : "no", importer.isReadable ? "yes" : "no",
                    android.overridden ? android.format.ToString() : "default",
                    importer.textureCompression.ToString()));
            }
            Write("AssetAudit_Textures.csv", lines);
            return found.Length;
        }

        static void WriteTop30(List<MeshRow> meshes, string sceneName)
        {
            var ranked = meshes
                .Where(r => r.triangles > 0)
                .OrderByDescending(r => (float)Math.Max(1, r.instances) * r.triangles)
                .Take(30)
                .ToList();

            var text = new System.Text.StringBuilder();
            text.AppendLine($"Asset audit top 30 (scene '{sceneName}'). Score = instances (at least 1) x triangles.");
            text.AppendLine("Savings: merging or instancing saves (instances - 1) draw calls per material; stripping bottom faces saves the bottom triangles per instance.");
            text.AppendLine();
            int rank = 0;
            foreach (var r in ranked)
            {
                rank++;
                var savings = new List<string>();
                if (r.instances > 1) savings.Add($"draws -{r.instances - 1} if merged");
                if (r.readable && r.bottomTris > 0) savings.Add($"tris -{r.bottomTris * Math.Max(1, r.instances)} if bottom stripped");
                else if (!r.readable) savings.Add("read/write off: bottom faces not measured");
                if (r.screenPercent < TinyPercent) savings.Add($"tiny: under {TinyPercent}% screen at 10 m, cull it");
                if (r.group != 0) savings.Add($"duplicate group {r.group}");
                text.AppendLine($"{rank,2}. {r.name}  [{r.instances} in scene, {r.triangles} tris, {r.vertices} verts, " +
                                $"{r.submeshes} submeshes]  {string.Join("; ", savings)}");
                text.AppendLine($"      {r.path}");
            }
            File.WriteAllText(Path.Combine(OutDir, "AssetAudit_Top30.txt"), text.ToString());
        }

        static string Line(params object[] values) =>
            string.Join(",", values.Select(v => Cell(Convert.ToString(v, CultureInfo.InvariantCulture) ?? "")));

        static string Cell(string s) =>
            s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

        static void Write(string file, IEnumerable<string> lines) =>
            File.WriteAllLines(Path.Combine(OutDir, file), lines);
    }
}
