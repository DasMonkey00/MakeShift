using System;
using System.Collections.Generic;
using UnityEngine;

namespace Makeshift.Nature
{
    /// <summary>
    /// Makes every pine in the scene choppable without any of them costing anything until someone swings an axe at
    /// it. The first swing near a trunk turns that one tree into a <see cref="ChoppableTree"/>. One of these per
    /// scene holds what the trees share: readable copies of the tree models, the cut wood, chips and leaves.
    /// </summary>
    public class TreeCutting : MonoBehaviour
    {
        /// <summary>A tree model the pack ships, with the copy the game may read.</summary>
        [Serializable]
        public struct Model
        {
            public Mesh drawn;
            public Mesh readable;
            [Tooltip("Which submesh is bark.")]
            public int bark;
        }

        [SerializeField] private Model[] models;
        [SerializeField] private Material woodMaterial;
        [SerializeField] private Material chipMaterial;
        [SerializeField] private Material leafMaterial;
        [SerializeField] private Mesh leafMesh;

        private static readonly Collider[] Found = new Collider[32];
        private readonly Dictionary<Mesh, Model> known = new Dictionary<Mesh, Model>();

        public static TreeCutting Instance { get; private set; }
        /// <summary>How many trees have been prepared in play.</summary>
        public int Prepared { get; private set; }

        // The further model of each pine, given the closest one's limbs.
        private readonly Dictionary<Mesh, Mesh> fuller = new Dictionary<Mesh, Mesh>();

        private void Awake()
        {
            if (models == null) return;
            foreach (var m in models)
                if (m.drawn != null && m.readable != null) known[m.drawn] = m;
            GiveLimbs();
        }

        /// <summary>The model to cut for a readable pine mesh: with its limbs, if it is a further model that had none.</summary>
        public Mesh Fuller(Mesh readable) => fuller.TryGetValue(readable, out var full) ? full : readable;

        // The pack's second model of a pine keeps every spray of needles but drops all the limbs, so they appeared out
        // of nowhere when the closest model came in. It is replaced by its own needles on the closest model's wood.
        private void GiveLimbs()
        {
            var made = new Dictionary<Mesh, Mesh>();
            foreach (var group in FindObjectsByType<LODGroup>(FindObjectsInactive.Include))
            {
                var lods = group.GetLODs();
                if (lods.Length < 2 || lods[0].renderers.Length == 0 || lods[1].renderers.Length == 0) continue;
                if (!(lods[0].renderers[0] is MeshRenderer near) || !(lods[1].renderers[0] is MeshRenderer far)) continue;
                var nearFilter = near.GetComponent<MeshFilter>();
                var farFilter = far.GetComponent<MeshFilter>();
                if (nearFilter == null || farFilter == null || farFilter.sharedMesh == null) continue;
                var second = farFilter.sharedMesh;
                if (!made.TryGetValue(second, out var full))
                {
                    if (nearFilter.sharedMesh == null || !known.TryGetValue(nearFilter.sharedMesh, out var a)
                        || !known.TryGetValue(second, out var b) || a.bark != b.bark || b.readable.subMeshCount != 2) continue;
                    full = WithLimbs(a.readable, b.readable, a.bark);
                    made[second] = full;
                    fuller[b.readable] = full;
                    known[full] = new Model { drawn = full, readable = full, bark = b.bark };
                }
                farFilter.sharedMesh = full;
            }
        }

        private static Mesh WithLimbs(Mesh near, Mesh far, int bark)
        {
            var parts = new CombineInstance[2];
            for (int s = 0; s < 2; s++) parts[s] = new CombineInstance { mesh = s == bark ? near : far, subMeshIndex = s };
            var mesh = new Mesh { name = far.name + " with limbs" };
            mesh.CombineMeshes(parts, false, false);
            return mesh;
        }

        private void OnEnable() => Instance = this;

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        public Material Wood => woodMaterial;
        public Material Leaf => leafMaterial;
        public Mesh LeafMesh => leafMesh;

        /// <summary>The readable copy of a pine model the pack draws. False for any other mesh.</summary>
        public bool TryModel(Mesh drawn, out Model model) => known.TryGetValue(drawn, out model);

        private TreeBlast blast;

        private void Start()
        {
            blast = gameObject.AddComponent<TreeBlast>();
            blast.Setup(this);
        }

        /// <summary>
        /// Brings down every pine whose trunk is within <paramref name="radius"/> metres, falling away from the middle:
        /// the one way a blast fells trees. Returns how many.
        /// </summary>
        public int Blast(Vector3 centre, float radius)
        {
            if (!ChoppableTree.CuttingOn || blast == null) return 0;
            var near = new List<(float far, LODGroup group)>();
            var seen = new HashSet<LODGroup>();
            foreach (var hit in Physics.OverlapCapsule(centre + Vector3.down * radius, centre + Vector3.up * radius, radius,
                         ~0, QueryTriggerInteraction.Ignore))
            {
                var group = hit.GetComponentInParent<LODGroup>();
                if (group == null || !group.enabled || !seen.Add(group)) continue;
                Vector3 to = group.transform.position - centre;
                float far = new Vector2(to.x, to.z).magnitude;
                if (far <= radius) near.Add((far, group));
            }
            near.Sort((a, b) => a.far.CompareTo(b.far));

            int count = 0;
            foreach (var (_, group) in near)
            {
                if (group.TryGetComponent(out ChoppableTree tree))
                {
                    if (tree.Felled) continue;
                    if (tree.Hits > 0)
                    {
                        tree.Fell(); // already carved: it finishes the way a chopped tree does
                        count++;
                        continue;
                    }
                    tree.Retire();
                }
                if (blast.Break(group, centre)) count++;
            }
            return count;
        }

        /// <summary>
        /// Prepares any pine whose trunk collider lies within <paramref name="radius"/> of the path an axe head took.
        /// </summary>
        public void PrepareNear(Vector3 from, Vector3 to, float radius)
        {
            if (!ChoppableTree.CuttingOn) return;
            int count = Physics.OverlapCapsuleNonAlloc(from, to, radius, Found, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var group = Found[i].GetComponentInParent<LODGroup>();
                if (group != null) Prepare(group);
            }
        }

        /// <summary>The tree's chopping component, added now if this is a pine that has none. Null for anything else.</summary>
        public ChoppableTree Prepare(LODGroup group)
        {
            var tree = group.GetComponent<ChoppableTree>();
            if (tree != null) return tree;

            // The two full models, closest first. A third, flat far level takes no part in cutting.
            var lods = group.GetLODs();
            var renderers = new List<MeshRenderer>();
            var meshes = new List<Mesh>();
            int bark = 0;
            for (int i = 0; i < lods.Length && i < 2; i++)
            {
                if (lods[i].renderers.Length == 0 || !(lods[i].renderers[0] is MeshRenderer r)) break;
                var filter = r.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || !known.TryGetValue(filter.sharedMesh, out var model)) break;
                if (i == 0) bark = model.bark;
                renderers.Add(r);
                meshes.Add(model.readable);
            }
            if (renderers.Count == 0) return null;

            tree = group.gameObject.AddComponent<ChoppableTree>();
            tree.Configure(group, renderers.ToArray(), meshes.ToArray(), bark,
                woodMaterial, chipMaterial, leafMaterial, leafMesh);
            Prepared++;
            return tree;
        }
    }
}
