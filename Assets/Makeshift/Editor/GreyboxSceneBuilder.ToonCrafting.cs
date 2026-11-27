using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Makeshift.Nature;
using Makeshift.Settling;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Makeshift.EditorTools
{
    /// <summary>
    /// The ToonNature crafting corner and the tree that can be cut down: a bench with glue, vines, stones and the club
    /// that cracks them, sticks lying in front of each cuttable tree to point it out, and those trees' own setup.
    /// </summary>
    public static partial class GreyboxSceneBuilder
    {
        const string TreeDir = "Assets/Makeshift/ToonNature/Trees";
        const string ToonPack = "Assets/Toon Fantasy Nature/";

        const int ChopTreeCount = 4;
        const float ChopTreeSpacing = 3f;

        /// <summary>
        /// The pines that can be cut: first the nearest one ahead of the player at spawn, then the next nearest in
        /// view or to the sides, far enough apart to tell their marker sticks apart.
        /// </summary>
        public static Transform[] PickChopTrees(Vector3 start, float yaw)
        {
            Vector3 ahead = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            var pines = Object.FindObjectsByType<LODGroup>(FindObjectsInactive.Exclude)
                .Where(g => g.name.Contains("Pine_Tree") && !g.name.Contains("Board"))
                .Select(g => (g.transform, flat: Vector3.ProjectOnPlane(g.transform.position - start, Vector3.up)))
                .Where(t => t.flat.magnitude > 3f) // not on top of the camp
                .OrderBy(t => t.flat.magnitude)
                .ToArray();
            var first = pines.FirstOrDefault(t => Vector3.Angle(t.flat, ahead) < 45f).transform;
            if (first == null) throw new InvalidOperationException("no pine tree in front of the player start");

            var chosen = new List<Transform> { first };
            foreach (var (tree, flat) in pines)
            {
                if (chosen.Count >= ChopTreeCount) break;
                if (Vector3.Angle(flat, ahead) > 100f) continue;
                if (chosen.Any(c => Vector3.ProjectOnPlane(c.position - tree.position, Vector3.up).magnitude < ChopTreeSpacing)) continue;
                chosen.Add(tree);
            }
            return chosen.ToArray();
        }

        public static void AddToonCrafting(Vector3 start, float yaw, Func<Vector3, float> ground, Transform[] trees, StringBuilder log)
        {
            var turn = Quaternion.Euler(0f, yaw, 0f);
            Vector3 OnGround(Vector3 p) { p.y = ground(p); return p; }

            // The bench stands at the player's left hand, long side towards them.
            Vector3 bench = OnGround(start + turn * new Vector3(-1.2f, 0f, 0.05f));
            var benchTurn = turn * Quaternion.Euler(0f, -90f, 0f);
            MakeTable("CraftBench", bench, new Vector2(0.9f, 0.6f), "camptable").transform.rotation = benchTurn;
            // x along the bench (positive = away from the player's feet, forward), z towards its far edge.
            Vector3 On(float x, float z, float lift) => bench + benchTurn * new Vector3(x, 0f, z) + Vector3.up * lift;
            MakeGlueTub(On(0.31f, 0f, 0.82f));
            for (int i = 0; i < 3; i++) MakeVine(On(0.09f, -0.17f + i * 0.17f, 0.8f));
            for (int i = 0; i < 3; i++) MakeStone(On(-0.12f, -0.15f + i * 0.15f, 0.85f));
            // Stones only crack under the club, and a cracked stone is where the axe head comes from.
            MakeClub(On(-0.32f, 0f, 0.82f)).transform.rotation = benchTurn * Quaternion.Euler(90f, 0f, 0f);

            // Sticks on the ground in front of each tree, on the player's side: (sideways, out from the trunk, turn).
            var pile = new[] { (-0.24f, 0.95f, 15f), (0.02f, 1.12f, -50f), (0.25f, 0.9f, 70f), (0.1f, 0.75f, 5f) };
            var pair = new[] { (0f, 0.95f, 45f), (0.03f, 0.95f, -45f) };
            for (int n = 0; n < trees.Length; n++)
            {
                var tree = trees[n];
                Vector3 toPlayer = Vector3.ProjectOnPlane(start - tree.position, Vector3.up).normalized;
                Vector3 across = Vector3.Cross(Vector3.up, toPlayer);
                var spots = n == 0 ? pile : pair;
                for (int i = 0; i < spots.Length; i++)
                {
                    var (side, out_, angle) = spots[i];
                    Vector3 at = OnGround(tree.position + toPlayer * out_ + across * side) + Vector3.up * (0.06f + 0.04f * i);
                    var stick = MakeStick(at);
                    stick.transform.rotation = Quaternion.LookRotation(toPlayer) * Quaternion.Euler(0f, angle, 0f);
                    stick.AddComponent<RestUntilTouched>(); // stays put on the slope until picked up
                }
                SetUpChopTree(tree, log);
                log.AppendLine($"tree to cut: {tree.name} at {tree.position}, " +
                               $"{Vector3.ProjectOnPlane(tree.position - start, Vector3.up).magnitude:0.0} m from the start");
            }
            log.AppendLine($"crafting bench at {bench}");
        }

        static void SetUpChopTree(Transform tree, StringBuilder log)
        {
            var group = tree.GetComponent<LODGroup>();
            var renderers = group.GetLODs().SelectMany(l => l.renderers.Take(1)).OfType<MeshRenderer>().ToArray();
            var materials = renderers[0].sharedMaterials;
            int bark = Array.FindIndex(materials, m => !m.name.Contains("Vegetation"));
            if (bark < 0) throw new InvalidOperationException("no bark material on " + tree.name);

            // The tree changes shape in play, so it cannot be baked into a static batch.
            foreach (var t in tree.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
                PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);
            }

            Directory.CreateDirectory(TreeDir);
            AssetDatabase.Refresh();
            var meshes = renderers.Select(r =>
            {
                var source = r.GetComponent<MeshFilter>().sharedMesh;
                return Readable(source, $"{TreeDir}/{source.name}.asset");
            }).ToArray();

            var (wood, chip, leaf, leafMesh) = TreeParts();

            var choppable = tree.gameObject.AddComponent<ChoppableTree>();
            var so = new SerializedObject(choppable);
            so.FindProperty("lodGroup").objectReferenceValue = group;
            Fill(so.FindProperty("renderers"), renderers);
            Fill(so.FindProperty("meshes"), meshes);
            so.FindProperty("trunkSubmesh").intValue = bark;
            so.FindProperty("woodMaterial").objectReferenceValue = wood;
            so.FindProperty("chipMaterial").objectReferenceValue = chip;
            so.FindProperty("leafMaterial").objectReferenceValue = leaf;
            so.FindProperty("leafMesh").objectReferenceValue = leafMesh;
            so.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine($"choppable tree: {renderers.Length} detail levels, {meshes[0].vertexCount} vertices, bark submesh {bark}");
        }

        /// <summary>
        /// Gives the scene a <see cref="TreeCutting"/>, which makes every pine in it choppable once an axe swings at
        /// it: readable copies of each pine model in use, and the shared wood, chips and leaves.
        /// </summary>
        public static void AddTreeCutting(GameObject host, StringBuilder log)
        {
            Directory.CreateDirectory(TreeDir);
            AssetDatabase.Refresh();
            var models = new List<(Mesh drawn, Mesh readable, int bark)>();
            int trees = 0;
            foreach (var group in Object.FindObjectsByType<LODGroup>(FindObjectsInactive.Include))
            {
                if (!group.name.Contains("Pine_Tree") || group.name.Contains("Board")) continue;
                var lods = group.GetLODs();
                for (int i = 0; i < lods.Length && i < 2; i++)
                {
                    if (lods[i].renderers.Length == 0 || !(lods[i].renderers[0] is MeshRenderer r)) break;
                    var source = r.GetComponent<MeshFilter>().sharedMesh;
                    int bark = Array.FindIndex(r.sharedMaterials, m => m != null && !m.name.Contains("Vegetation"));
                    if (i == 0 && bark < 0) break;
                    if (i == 0) trees++;
                    if (models.Any(m => m.drawn == source)) continue;
                    models.Add((source, Readable(source, $"{TreeDir}/{source.name}.asset"), Mathf.Max(bark, 0)));
                }
            }

            var (wood, chip, leaf, leafMesh) = TreeParts();
            var cutting = host.AddComponent<TreeCutting>();
            var so = new SerializedObject(cutting);
            var list = so.FindProperty("models");
            list.arraySize = models.Count;
            for (int i = 0; i < models.Count; i++)
            {
                var element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("drawn").objectReferenceValue = models[i].drawn;
                element.FindPropertyRelative("readable").objectReferenceValue = models[i].readable;
                element.FindPropertyRelative("bark").intValue = models[i].bark;
            }
            so.FindProperty("woodMaterial").objectReferenceValue = wood;
            so.FindProperty("chipMaterial").objectReferenceValue = chip;
            so.FindProperty("leafMaterial").objectReferenceValue = leaf;
            so.FindProperty("leafMesh").objectReferenceValue = leafMesh;
            so.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine($"tree cutting: {trees} pines choppable on demand, {models.Count} tree models copied");
        }

        /// <summary>What every cut tree shares: the pale cut wood, the chips and the falling leaves.</summary>
        static (Material wood, Material chip, Material leaf, Mesh leafMesh) TreeParts()
        {
            var wood = ToonWood();
            var chip = Lit("WoodChip", "Universal Render Pipeline/Particles/Unlit", new Color(0.72f, 0.53f, 0.3f));
            var leaf = Lit("FallingLeaf", "Universal Render Pipeline/Particles/Unlit", new Color(0.85f, 0.9f, 0.8f));
            leaf.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ToonPack + "Textures/TFF_Atlas_Vegetation_1A_D.tga"));
            leaf.SetFloat("_AlphaClip", 1f);
            leaf.SetFloat("_Cutoff", 0.5f);
            leaf.SetFloat("_Cull", 0f);
            leaf.EnableKeyword("_ALPHATEST_ON");
            leaf.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            EditorUtility.SetDirty(leaf);

            var leafSource = AssetDatabase.LoadAllAssetsAtPath(ToonPack + "Models/TFF_Leaf_01A.fbx").OfType<Mesh>().First();
            var leafMesh = Readable(leafSource, $"{TreeDir}/{leafSource.name}.asset");
            return (wood, chip, leaf, leafMesh);
        }

        /// <summary>
        /// The bare wood of a cut, in the same URP shader as the bark beside it. It takes no shadows: on the flat,
        /// steep faces of a cut they broke up into blotches.
        /// </summary>
        static Material ToonWood()
        {
            const string path = TreeDir + "/Wood.mat";
            var look = new Material(Shader.Find("Makeshift/Scenery")) { name = "Wood" };
            look.SetTexture("_BaseMap", WoodTexture());
            look.SetFloat("_Wrap", 0.35f);
            look.SetFloat("_Shadows", 0f);
            look.enableInstancing = true;
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(look, path);
                return look;
            }
            existing.shader = look.shader;
            existing.CopyPropertiesFromMaterial(look);
            Object.DestroyImmediate(look);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        /// <summary>
        /// Painted to sit with the pack's flat style: soft growth rings in two tans, a darker heart and
        /// a couple of splits. No speckle or hair lines.
        /// </summary>
        static Texture2D WoodTexture()
        {
            const string path = TreeDir + "/WoodGrain.png";
            const int size = 256;
            // Muted: the toon light warms and brightens these a good deal.
            var pale = new Color(0.68f, 0.54f, 0.38f);
            var ring = new Color(0.60f, 0.45f, 0.31f);
            var heart = new Color(0.52f, 0.37f, 0.25f);

            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size - 0.5f, v = (y + 0.5f) / size - 0.5f;
                    float radius = Mathf.Sqrt(u * u + v * v)
                                   + (Mathf.PerlinNoise(u * 3.5f + 11f, v * 3.5f + 3f) - 0.5f) * 0.05f;
                    // About seven rings from the heart to the bark, closer together towards the outside.
                    float within = Mathf.Repeat(Mathf.Pow(Mathf.Max(0f, radius) * 2f, 1.25f) * 7f, 1f);
                    float band = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.52f, 0.66f, within))
                                 * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.98f, 0.84f, within));
                    var colour = Color.Lerp(pale, ring, band);
                    colour = Color.Lerp(heart, colour, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.035f, 0.065f, radius)));

                    // Three wedge-shaped splits running in from the outside.
                    float angle = Mathf.Atan2(v, u) * Mathf.Rad2Deg;
                    foreach (var split in new[] { 25f, 147f, -88f })
                    {
                        float off = Mathf.Abs(Mathf.DeltaAngle(angle, split));
                        float wide = 4f * Mathf.InverseLerp(0.12f, 0.5f, radius);
                        if (off < wide) colour = Color.Lerp(colour, heart, 0.7f);
                    }
                    colour.a = 1f;
                    pixels[y * size + x] = colour;
                }

            var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
            texture.SetPixels(pixels);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Mirror;
            importer.maxTextureSize = size;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void Fill(SerializedProperty list, Object[] values)
        {
            list.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        /// <summary>A copy of an imported mesh the game may read, leaving the pack's import settings alone.</summary>
        static Mesh Readable(Mesh source, string path)
        {
            var copy = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool isNew = copy == null;
            if (isNew) copy = new Mesh();
            else copy.Clear();
            copy.name = Path.GetFileNameWithoutExtension(path);
            copy.indexFormat = source.indexFormat;
            copy.vertices = source.vertices;
            copy.normals = source.normals;
            copy.tangents = source.tangents;
            copy.colors = source.colors;
            for (int c = 0; c < 4; c++)
            {
                var uv = new System.Collections.Generic.List<Vector4>();
                source.GetUVs(c, uv);
                if (uv.Count == source.vertexCount) copy.SetUVs(c, uv);
            }
            copy.subMeshCount = source.subMeshCount;
            for (int s = 0; s < source.subMeshCount; s++) copy.SetTriangles(source.GetTriangles(s), s);
            copy.RecalculateBounds();
            if (isNew) AssetDatabase.CreateAsset(copy, path);
            else EditorUtility.SetDirty(copy);
            return copy;
        }

        static Material Lit(string name, string shaderName, Color color)
        {
            string path = $"{TreeDir}/{name}.mat";
            var shader = Shader.Find(shaderName);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
