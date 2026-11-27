using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Makeshift.Rendering
{
    /// <summary>
    /// Welds standing trees of every kind into a few meshes per square of ground, one per material, so a view of
    /// the forest costs a handful of draws instead of one per tree model. Each square has a near and a far version
    /// and swaps as a whole, so it can cost more triangles than <see cref="SceneryInstancer"/>, which picks the
    /// level tree by tree. Compare the two on the headset with the wrist menu's "Draw" button.
    ///
    /// Left out, and drawn as before: choppable trees (they change), anything with a rigidbody, mirrored models and
    /// meshes without Read/Write. The squares are built the first time this is switched on.
    /// </summary>
    public class ChunkedScenery : MonoBehaviour
    {
        private const string Root = "Trees";
        private const float CheckSeconds = 0.25f;
        private const float Hysteresis = 2f;

        [Tooltip("Side of one square of ground, in metres. Bigger squares: fewer draws, more triangles.")]
        [SerializeField] private float cell = 40f;

        private sealed class Chunk
        {
            public readonly List<Renderer>[] levels = { new List<Renderer>(), new List<Renderer>() };
            public readonly List<LODGroup> trees = new List<LODGroup>();
            public Vector2Int square;
            public Bounds bounds;
            public float near, far;
            public int shown = -2; // -2 not set yet, -1 hidden, 0 near, 1 far
        }

        private sealed class Piece
        {
            public readonly List<CombineInstance> parts = new List<CombineInstance>();
            public Renderer source;
            public int slot, vertices;
        }

        private readonly List<Chunk> chunks = new List<Chunk>();
        private readonly Dictionary<Renderer, bool> originals = new Dictionary<Renderer, bool>(); // was enabled
        private ToonLookSwitcher looks;
        private SceneryInstancer merger;
        private Camera head;
        private GameObject holder;
        private float timer;

        public bool On { get; private set; }

        public void Setup(ToonLookSwitcher switcher, SceneryInstancer instancer, Camera viewer)
        {
            looks = switcher;
            merger = instancer;
            head = viewer;
        }

        public void SetViewer(Camera viewer)
        {
            head = viewer;
            Reach();
        }

        public void SetOn(bool on)
        {
            if (On == on) return;
            On = on;
            if (on)
            {
                if (holder == null) Build();
                // The merger hands every renderer back before these are taken from it.
                if (merger != null) merger.Claim(originals.Keys);
                var keys = new List<Renderer>(originals.Keys);
                foreach (var r in keys)
                {
                    if (r == null) continue;
                    originals[r] = r.enabled;
                    r.enabled = false;
                }
                holder.SetActive(true);
                Reach();
                timer = 0f;
            }
            else
            {
                if (holder != null) holder.SetActive(false);
                foreach (var pair in originals)
                    if (pair.Key != null) pair.Key.enabled = pair.Value;
                if (merger != null) merger.Claim(null);
            }
        }

        private void Build()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            holder = new GameObject("ChunkedTrees");
            var squares = new Dictionary<Vector2Int, List<(LODGroup group, LOD[] lods)>>();
            foreach (var group in FindObjectsByType<LODGroup>())
            {
                if (group.transform.root.name != Root || !group.enabled) continue;
                var lods = group.GetLODs();
                // Trees with a far card (the extra forest) stay instanced: welding thousands of them would cost
                // hundreds of megabytes of mesh.
                if (lods.Length == 0 || lods.Length > 2 || !Usable(lods)) continue;
                var p = group.transform.position;
                var key = new Vector2Int(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.z / cell));
                if (!squares.TryGetValue(key, out var list)) squares[key] = list = new List<(LODGroup, LOD[])>();
                list.Add((group, lods));
            }

            int draws = 0;
            foreach (var square in squares)
            {
                var chunk = new Chunk { square = square.Key };
                foreach (var (group, lods) in square.Value)
                {
                    chunk.trees.Add(group);
                    foreach (var lod in lods)
                        foreach (var r in lod.renderers)
                            if (r != null) originals[r] = r.enabled;
                }
                WeldChunk(chunk);
                draws += chunk.levels[0].Count;
                chunks.Add(chunk);
            }
            Debug.Log($"[Makeshift] chunked trees: {originals.Count} renderers in {chunks.Count} squares, " +
                      $"{draws} near meshes, built in {clock.ElapsedMilliseconds} ms");
        }

        /// <summary>
        /// Takes one tree out of its square and welds that square again, for a tree about to change shape.
        /// </summary>
        public void Release(LODGroup tree)
        {
            foreach (var chunk in chunks)
            {
                if (!chunk.trees.Remove(tree)) continue;
                Clear(chunk);
                WeldChunk(chunk);
                chunk.shown = -2; // set the new meshes' visibility again
                Reach();
                return;
            }
        }

        private void WeldChunk(Chunk chunk)
        {
            bool any = false;
            for (int level = 0; level < 2; level++)
            {
                var pieces = new Dictionary<(Material, ShadowCastingMode), Piece>();
                foreach (var group in chunk.trees)
                {
                    var lods = group.GetLODs();
                    foreach (var renderer in lods[Mathf.Min(level, lods.Length - 1)].renderers)
                    {
                        if (!(renderer is MeshRenderer r)) continue;
                        var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                        var materials = r.sharedMaterials;
                        for (int s = 0; s < materials.Length && s < mesh.subMeshCount; s++)
                        {
                            if (materials[s] == null) continue;
                            var key = (materials[s], r.shadowCastingMode);
                            if (!pieces.TryGetValue(key, out var piece))
                                pieces[key] = piece = new Piece { source = r, slot = s };
                            piece.parts.Add(new CombineInstance { mesh = mesh, subMeshIndex = s, transform = r.localToWorldMatrix });
                            piece.vertices += mesh.vertexCount; // an upper bound: a submesh may use them all
                        }
                        if (!any) chunk.bounds = r.bounds;
                        else chunk.bounds.Encapsulate(r.bounds);
                        any = true;
                    }
                }
                foreach (var pair in pieces)
                    chunk.levels[level].Add(Weld(chunk.square, level, pair.Value, pair.Key.Item2));
            }
        }

        private static void Clear(Chunk chunk)
        {
            foreach (var level in chunk.levels)
            {
                foreach (var r in level)
                {
                    if (r == null) continue;
                    Destroy(r.GetComponent<MeshFilter>().sharedMesh);
                    Destroy(r.gameObject);
                }
                level.Clear();
            }
        }

        private static bool Usable(LOD[] lods)
        {
            foreach (var lod in lods)
                foreach (var renderer in lod.renderers)
                {
                    if (!(renderer is MeshRenderer r)) return false;
                    var filter = r.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) return false;
                    if (r.GetComponentInParent<Rigidbody>(true) != null) return false;
                    if (r.GetComponentInParent<MonoBehaviour>(true) != null) return false;
                    if (r.localToWorldMatrix.determinant < 0f) return false;
                }
            return true;
        }

        private Renderer Weld(Vector2Int square, int level, Piece piece, ShadowCastingMode shadows)
        {
            var mesh = new Mesh { name = $"Trees {square.x},{square.y} {(level == 0 ? "near" : "far")}" };
            if (piece.vertices > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.CombineMeshes(piece.parts.ToArray(), true, true);
            mesh.UploadMeshData(true); // no copy kept in main memory

            var go = new GameObject(mesh.name) { layer = piece.source.gameObject.layer };
            go.transform.SetParent(holder.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = shadows;
            r.receiveShadows = piece.source.receiveShadows;
            r.sharedMaterial = piece.source.sharedMaterials[piece.slot];
            // The shader buttons change the squares along with the trees they came from.
            if (looks != null) looks.Follow(piece.source, r, piece.slot, null, false);
            r.enabled = false;
            return r;
        }

        /// <summary>The distances at which squares swap to the far version and vanish, taken from the trees' LOD groups.</summary>
        public void Reach()
        {
            if (head == null) return;
            // How much of the view's height one metre of object fills at one metre's distance.
            float perMetre = QualitySettings.lodBias / (2f * Mathf.Tan(head.fieldOfView * 0.5f * Mathf.Deg2Rad));
            foreach (var chunk in chunks)
            {
                float near = 0f, far = 0f;
                foreach (var group in chunk.trees)
                {
                    if (group == null) continue;
                    var lods = group.GetLODs();
                    var scale = group.transform.lossyScale;
                    float size = group.size * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                    float first = lods[0].screenRelativeTransitionHeight;
                    float last = lods[lods.Length - 1].screenRelativeTransitionHeight;
                    near = Mathf.Max(near, first > 0f ? size * perMetre / first : float.MaxValue);
                    far = Mathf.Max(far, last > 0f ? size * perMetre / last : float.MaxValue);
                }
                chunk.near = near;
                chunk.far = far;
            }
            timer = 0f;
        }

        private void LateUpdate()
        {
            if (!On || head == null) return;
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = CheckSeconds;

            var eye = head.transform.position;
            foreach (var chunk in chunks)
            {
                float distance = Mathf.Sqrt(chunk.bounds.SqrDistance(eye));
                // A square swaps back only once the eye is clearly past the line, so it does not flicker.
                int want = distance < chunk.near + (chunk.shown == 0 ? Hysteresis : 0f) ? 0
                    : distance < chunk.far + (chunk.shown == 1 ? Hysteresis : 0f) ? 1 : -1;
                if (want == chunk.shown) continue;
                chunk.shown = want;
                for (int level = 0; level < 2; level++)
                    foreach (var r in chunk.levels[level])
                        r.enabled = level == want;
            }
        }

        private void OnDestroy()
        {
            foreach (var chunk in chunks) Clear(chunk);
            if (holder != null) Destroy(holder);
        }
    }
}
