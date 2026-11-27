using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Makeshift.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

using Object = UnityEngine.Object;

namespace Makeshift.EditorTools
{
    /// <summary>
    /// Fills the empty ground of the ToonNature scene with random pines, in three tiers the wrist menu switches on
    /// one after another, to test how the game copes with distance and a growing forest. Each extra pine has a third
    /// detail level, the pack's flat tree card, for far away. The scatter is seeded, so every rebuild is the same.
    ///
    /// Kept clear: the camp and start, the road, steep ground, rocks, water and other trees.
    /// </summary>
    public static class ToonForestBuilder
    {
        const string Prefabs = "Assets/Toon Fantasy Nature/Prefabs/";
        const string CardMaterial = "Assets/Makeshift/ToonNature/Forest Tree Card.mat";
        static readonly int[] TierSizes = { 1000, 2000, 3000 };
        const int Seed = 20261126;
        const float Spacing = 4.5f;      // between extra trees and any other tree, metres
        const float CampClearing = 14f;
        const float EdgeMargin = 3f;
        const float Reach = 320f;        // metres from the camp; the terrain is larger than can be seen
        const float MaxSlope = 32f;

        /// <summary>Plants the extra forest under the scene's Trees root and returns the far card material.</summary>
        public static Material Add(Terrain terrain, Vector3 start, Vector3 camp, GameObject player, StringBuilder log)
        {
            var trees = GameObject.Find("Trees");
            if (terrain == null || trees == null)
            {
                log.AppendLine("extra forest: no terrain or no Trees root, skipped");
                return null;
            }

            var species = new List<(GameObject tree, GameObject card)>();
            for (int n = 1; n <= 5; n++)
            {
                var tree = AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}Vegetation/Trees/TFF_Pine_Tree_0{n}C.prefab");
                var card = AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}Decals/TFF_Board_Pine_Tree_0{n}A.prefab");
                if (tree != null && card != null) species.Add((tree, card));
            }
            if (species.Count == 0)
            {
                log.AppendLine("extra forest: pine prefabs not found, skipped");
                return null;
            }

            var taken = new Grid(Spacing);
            foreach (var g in Object.FindObjectsByType<LODGroup>().Where(g => g.name.Contains("Tree")))
                taken.Add(g.transform.position);
            var blocked = new List<Bounds>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>())
            {
                string root = r.transform.root.name;
                if (root == "Background Trees") taken.Add(r.bounds.center);
                bool water = r.sharedMaterial != null && r.sharedMaterial.shader.name.Contains("Water");
                if (root != "Rocks" && root != "Props" && !water) continue;
                var b = r.bounds;
                b.Expand(new Vector3(1.5f, 0f, 1.5f));
                blocked.Add(b);
            }

            var data = terrain.terrainData;
            var layers = data.terrainLayers;
            var roads = Enumerable.Range(0, layers.Length)
                .Where(i => layers[i] != null && layers[i].diffuseTexture != null && layers[i].diffuseTexture.name.Contains("Road"))
                .ToArray();
            var splat = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);

            var forest = new GameObject("ExtraForest").transform;
            forest.SetParent(trees.transform, false);
            var tiers = new GameObject[TierSizes.Length];
            for (int i = 0; i < tiers.Length; i++)
            {
                tiers[i] = new GameObject($"Tier {i + 1}");
                tiers[i].transform.SetParent(forest, false);
            }

            var random = new System.Random(Seed);
            float Next() => (float)random.NextDouble();
            Vector3 origin = terrain.transform.position;
            Vector3 size = data.size;
            int wanted = TierSizes.Sum(), planted = 0, tier = 0, inTier = 0;
            Material cardMaterial = null;

            for (int attempt = 0; attempt < wanted * 60 && planted < wanted; attempt++)
            {
                // A circle round the camp, thicker towards the middle where the player sees it.
                float radius = Reach * Mathf.Pow(Next(), 0.65f), angle = Next() * Mathf.PI * 2f;
                float x = camp.x + Mathf.Cos(angle) * radius - origin.x;
                float z = camp.z + Mathf.Sin(angle) * radius - origin.z;
                if (x < EdgeMargin || z < EdgeMargin || x > size.x - EdgeMargin || z > size.z - EdgeMargin) continue;
                float u = x / size.x, v = z / size.z;
                var p = new Vector3(origin.x + x, 0f, origin.z + z);
                p.y = origin.y + terrain.SampleHeight(p);

                if (Flat(p - start) < CampClearing || Flat(p - camp) < CampClearing) continue;
                if (data.GetSteepness(u, v) > MaxSlope) continue;
                int sx = Mathf.Min((int)(u * data.alphamapWidth), data.alphamapWidth - 1);
                int sz = Mathf.Min((int)(v * data.alphamapHeight), data.alphamapHeight - 1);
                if (roads.Sum(i => splat[sz, sx, i]) > 0.15f) continue;
                if (taken.Near(p)) continue;
                if (blocked.Any(b => p.x > b.min.x && p.x < b.max.x && p.z > b.min.z && p.z < b.max.z && p.y < b.max.y)) continue;

                var (treePrefab, cardPrefab) = species[random.Next(species.Count)];
                var tree = (GameObject)PrefabUtility.InstantiatePrefab(treePrefab, tiers[tier].transform);
                tree.transform.SetPositionAndRotation(p + Vector3.down * 0.15f, Quaternion.Euler(0f, Next() * 360f, 0f));
                tree.transform.localScale = treePrefab.transform.localScale * Mathf.Lerp(0.65f, 1.15f, Next());
                foreach (var t in tree.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
                if (!AddCard(tree, cardPrefab, ref cardMaterial))
                {
                    Object.DestroyImmediate(tree);
                    continue;
                }
                PrefabUtility.RecordPrefabInstancePropertyModifications(tree.transform);
                taken.Add(p);
                planted++;
                if (++inTier >= TierSizes[tier] && tier < tiers.Length - 1)
                {
                    tier++;
                    inTier = 0;
                }
            }

            ReplaceFlatTrees(log);

            var density = player.AddComponent<ForestDensity>();
            var so = new SerializedObject(density);
            var list = so.FindProperty("tiers");
            list.arraySize = tiers.Length;
            for (int i = 0; i < tiers.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = tiers[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            AddGrass(player, tiers, log);

            log.AppendLine($"extra forest: {planted} of {wanted} pines planted on {size.x:0} x {size.z:0} m of terrain, " +
                           $"tiers {string.Join(" + ", tiers.Select(t => t.transform.childCount.ToString()))}");
            return cardMaterial;
        }

        /// <summary>
        /// The pack scene fills the land beyond the start with trees that are only ever its flat card. Each becomes a
        /// normal pine of the same kind, place and height, with every detail level, so it can be walked up to, cut and
        /// blown down like the rest. Returns how many.
        /// </summary>
        public static int ReplaceFlatTrees(StringBuilder log)
        {
            var flat = GameObject.Find("Background Trees");
            var trees = GameObject.Find("Trees");
            var material = AssetDatabase.LoadAssetAtPath<Material>(CardMaterial);
            var terrain = Terrain.activeTerrain;
            if (flat == null || trees == null || terrain == null) return 0;

            var holder = new GameObject("Replaced background pines").transform;
            holder.SetParent(trees.transform, false);
            int made = 0;
            foreach (var renderer in flat.GetComponentsInChildren<MeshRenderer>(true))
            {
                // "TFF_Board_Pine_Tree_03A" is the card of pine 3.
                string name = renderer.GetComponent<MeshFilter>().sharedMesh.name;
                int at = name.IndexOf("Pine_Tree_0", StringComparison.Ordinal);
                if (at < 0) continue;
                char n = name[at + "Pine_Tree_0".Length];
                var treePrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}Vegetation/Trees/TFF_Pine_Tree_0{n}C.prefab");
                var cardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}Decals/TFF_Board_Pine_Tree_0{n}A.prefab");
                if (treePrefab == null || cardPrefab == null) continue;

                var tree = (GameObject)PrefabUtility.InstantiatePrefab(treePrefab, holder);
                Vector3 p = renderer.transform.position;
                p.y = terrain.transform.position.y + terrain.SampleHeight(p) - 0.15f;
                tree.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, renderer.transform.eulerAngles.y, 0f));
                var full = tree.GetComponentInChildren<LODGroup>().GetLODs()[0].renderers[0];
                float height = full.bounds.size.y;
                if (height > 0.01f) tree.transform.localScale *= renderer.bounds.size.y / height;
                foreach (var t in tree.GetComponentsInChildren<Transform>(true))
                {
                    t.gameObject.layer = 0;
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);
                }
                if (!AddCard(tree, cardPrefab, ref material))
                {
                    Object.DestroyImmediate(tree);
                    continue;
                }
                PrefabUtility.RecordPrefabInstancePropertyModifications(tree.transform);
                Object.DestroyImmediate(renderer.gameObject);
                made++;
            }
            log.AppendLine($"flat background trees replaced by normal pines: {made}");
            return made;
        }

        /// <summary>Grass round the extra trees, grown during play from one switched-off patch of each kind.</summary>
        static void AddGrass(GameObject player, GameObject[] tiers, StringBuilder log)
        {
            var holder = new GameObject("ForestGrassTemplates").transform;
            var templates = new List<Object>();
            foreach (var kind in new[] { "A", "B", "C" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}Vegetation/Plants/TFF_Grass_Patch_01{kind}.prefab");
                if (prefab == null) continue;
                var patch = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder);
                patch.transform.localPosition = Vector3.zero;
                foreach (var t in patch.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
                patch.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(patch);
                templates.Add(patch);
            }

            var grass = player.AddComponent<ForestGrass>();
            var so = new SerializedObject(grass);
            Fill(so.FindProperty("templates"), templates);
            Fill(so.FindProperty("tiers"), tiers.Select(t => (Object)t.transform).ToList());
            so.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine($"forest grass: {templates.Count} patch kinds");

            void Fill(SerializedProperty list, List<Object> values)
            {
                list.arraySize = values.Count;
                for (int i = 0; i < values.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        /// <summary>Adds the pack's flat card as a last detail level, sized to this tree.</summary>
        static bool AddCard(GameObject tree, GameObject cardPrefab, ref Material material)
        {
            var group = tree.GetComponentInChildren<LODGroup>();
            if (group == null) return false;
            var lods = group.GetLODs();
            if (lods.Length == 0 || lods[0].renderers.Length == 0 || lods[0].renderers[0] == null) return false;

            // Not a prefab instance, so the card can be reshaped and takes no overrides with it.
            var card = Object.Instantiate(cardPrefab, group.transform);
            card.name = "Far card";
            card.transform.localPosition = Vector3.zero;
            card.transform.localRotation = Quaternion.identity;
            card.transform.localScale = Vector3.one;
            var renderer = card.GetComponentInChildren<MeshRenderer>();
            if (renderer == null) return false;
            foreach (var t in card.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
            foreach (var c in card.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

            // Same height and foot as the full model.
            var full = lods[0].renderers[0].bounds;
            float height = renderer.bounds.size.y;
            if (height > 0.01f) card.transform.localScale = Vector3.one * (full.size.y / height);
            card.transform.position += Vector3.up * (full.min.y - renderer.bounds.min.y);

            if (material == null) material = CardCopy(renderer.sharedMaterial);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            var all = new LOD[lods.Length + 1];
            lods.CopyTo(all, 0);
            float last = Mathf.Max(lods[lods.Length - 1].screenRelativeTransitionHeight, 0.02f);
            all[lods.Length - 1].screenRelativeTransitionHeight = Mathf.Min(last * 4f,
                lods.Length > 1 ? lods[lods.Length - 2].screenRelativeTransitionHeight * 0.8f : 0.5f);
            all[lods.Length] = new LOD(0.01f, new Renderer[] { renderer });
            group.SetLODs(all);
            group.RecalculateBounds();
            PrefabUtility.RecordPrefabInstancePropertyModifications(group);
            return true;
        }

        /// <summary>
        /// The card shader draws one flat colour in the card's shape, and the pack's is a pale haze made for the far
        /// horizon. These stand much closer, so they get a copy in a dark pine colour; the fog still fades them.
        /// </summary>
        static Material CardCopy(Material source)
        {
            var copy = new Material(source);
            copy.SetColor("_ColorOverlay", new Color(0.24f, 0.25f, 0.17f, 1f));
            copy.enableInstancing = true;
            var existing = AssetDatabase.LoadAssetAtPath<Material>(CardMaterial);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(copy, CardMaterial);
                return copy;
            }
            existing.shader = copy.shader;
            existing.CopyPropertiesFromMaterial(copy);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        /// <summary>Remembers where trees stand, to keep new ones apart without comparing every pair.</summary>
        class Grid
        {
            readonly float spacing;
            readonly Dictionary<Vector2Int, List<Vector3>> cells = new Dictionary<Vector2Int, List<Vector3>>();

            public Grid(float spacing) => this.spacing = spacing;

            Vector2Int Cell(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / spacing), Mathf.FloorToInt(p.z / spacing));

            public void Add(Vector3 p)
            {
                if (!cells.TryGetValue(Cell(p), out var list)) cells[Cell(p)] = list = new List<Vector3>();
                list.Add(p);
            }

            public bool Near(Vector3 p)
            {
                var c = Cell(p);
                for (int x = -1; x <= 1; x++)
                    for (int z = -1; z <= 1; z++)
                        if (cells.TryGetValue(c + new Vector2Int(x, z), out var list)
                            && list.Any(o => Flat(o - p) < spacing)) return true;
                return false;
            }
        }
    }
}
