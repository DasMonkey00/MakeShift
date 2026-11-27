using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Makeshift.Rendering
{
    /// <summary>
    /// All the grass and flowers: the patches the scene was built with, and random ones round the extra forest's trees
    /// (worked out once at the start, seeded, so always the same). There are no grass objects in play. Places are kept
    /// in squares of ground, a Burst job picks the ones to draw, and they are drawn instanced.
    ///
    /// Nothing pops: near patches are drawn whole, further ones with the blades that stay, and Scenery.shader sinks the
    /// other blades away before the change and shrinks whole patches into the ground far out, each at its own distance.
    /// A shader set without that fade (the pack's toon ones) draws whole patches to a shorter distance instead.
    ///
    /// Follows the "Extra forest" tiers, the grass percentage and LOD buttons, and the shader set in use.
    /// </summary>
    public class ForestGrass : MonoBehaviour
    {
        [Tooltip("Grass patch models with LOD groups, switched off. Their renderers carry the materials in use.")]
        [SerializeField] private GameObject[] templates;
        [Tooltip("The forest tiers; grass grows round the trees of the tiers that are on.")]
        [SerializeField] private Transform[] tiers;
        [SerializeField] private int patchesPerTree = 24;
        [SerializeField] private Vector2 spread = new Vector2(0.6f, 6f); // metres from the trunk

        private const float Cell = 8f;
        private const int Seed = 4107;
        private const byte Always = 255;          // a patch the scene was built with, in no forest tier
        private const int NearRoom = 2500;        // most patches of one kind drawn whole
        private const int FarRoom = 6000;         // and thinned
        private const int PerDraw = 500;
        private const float PlainReach = 30f;     // for shader sets that cannot fade

        // Blades start sinking, have all gone, nearest and furthest distance a whole patch fades at; per LOD button.
        private static readonly float4[] Distances =
        {
            new float4(10f, 17f, 40f, 80f), // Pack
            new float4(7f, 12f, 30f, 60f),  // Mid
            new float4(5f, 9f, 22f, 42f)    // Short
        };
        private static readonly string[] SceneRoots = { "Grass", "Flowers" };

        private struct Spot
        {
            public Matrix4x4 matrix;
            public float yaw, scale;
            public byte kind, tier, order; // order thins evenly with the grass percentage
        }

        private struct Square
        {
            public float3 centre;
            public int first, count;
        }

        private sealed class Kind
        {
            public Renderer renderer;   // wears the material in use
            public Mesh near, far;
            public Matrix4x4 local;     // where the model sits inside a forest patch
        }

        private readonly Dictionary<Vector2Int, List<Spot>> squares = new Dictionary<Vector2Int, List<Spot>>();
        private readonly List<Kind> kinds = new List<Kind>();
        private readonly Dictionary<Mesh, (Mesh near, Mesh far)> meshes = new Dictionary<Mesh, (Mesh, Mesh)>();
        private int templated; // kinds the forest is planted from: the first ones
        private ForestDensity forest;
        private ToonLookSwitcher looks;
        private DetailPresets detail;
        private Camera head;
        private Vector3 builtAt, builtLooking;
        private int builtForest = -1, builtDetail = -1;
        private float builtShare = -1f;
        private bool builtSoft;

        private NativeArray<Spot> spots;
        private NativeArray<Square> cells;
        private Results drawn, filling;
        private JobHandle handle;
        private bool running, ready;

        /// <summary>Patches in the world, and how many are being drawn.</summary>
        public int Planted { get; private set; }
        public int Shown { get; private set; }

        public void SetViewer(Camera viewer)
        {
            head = viewer;
            builtForest = -1;
        }

        private void Start()
        {
            forest = GetComponent<ForestDensity>();
            looks = GetComponent<ToonLookSwitcher>();
            detail = GetComponent<DetailPresets>();
            if (head == null) head = Camera.main;
            ReadTemplates();
            Plant();
            Adopt();
            Pack();
        }

        private void ReadTemplates()
        {
            if (templates == null) return;
            foreach (var template in templates)
            {
                if (template == null) continue;
                var group = template.GetComponentInChildren<LODGroup>(true);
                if (group == null || !Closest(group, out var r, out var mesh)) continue;
                var (near, far) = MeshesFor(mesh);
                kinds.Add(new Kind
                {
                    renderer = r,
                    near = near,
                    far = far,
                    // Where the model sits inside its patch, the patch's own scale included.
                    local = Matrix4x4.Scale(template.transform.localScale)
                            * template.transform.worldToLocalMatrix * r.transform.localToWorldMatrix
                });
            }
            templated = kinds.Count;
        }

        private static bool Closest(LODGroup group, out Renderer renderer, out Mesh mesh)
        {
            renderer = null;
            mesh = null;
            var lods = group.GetLODs();
            if (lods.Length == 0 || lods[0].renderers.Length == 0 || lods[0].renderers[0] == null) return false;
            renderer = lods[0].renderers[0];
            var filter = renderer.GetComponent<MeshFilter>();
            mesh = filter != null ? filter.sharedMesh : null;
            return mesh != null;
        }

        private (Mesh, Mesh) MeshesFor(Mesh source)
        {
            if (!meshes.TryGetValue(source, out var pair))
            {
                GrassMeshes.Make(source, out var near, out var far);
                meshes[source] = pair = (near, far);
            }
            return pair;
        }

        // Takes over the scene's own grass and flowers and switches their objects off.
        private void Adopt()
        {
            var found = new Dictionary<(Mesh, Material), int>();
            foreach (var name in SceneRoots)
            {
                var root = GameObject.Find(name);
                if (root == null) continue;
                foreach (var group in root.GetComponentsInChildren<LODGroup>())
                {
                    if (!Closest(group, out var r, out var mesh)) continue;
                    var key = (mesh, r.sharedMaterial);
                    if (!found.TryGetValue(key, out int kind))
                    {
                        if (kinds.Count >= Always) continue;
                        var (near, far) = MeshesFor(mesh);
                        found[key] = kind = kinds.Count;
                        kinds.Add(new Kind { renderer = r, near = near, far = far, local = Matrix4x4.identity });
                    }
                    File(new Spot
                    {
                        matrix = r.localToWorldMatrix, kind = (byte)kind, tier = Always,
                        order = (byte)(Planted * 37 % 100)
                    });
                }
                root.SetActive(false);
            }
        }

        private void File(Spot spot)
        {
            var key = new Vector2Int(Mathf.FloorToInt(spot.matrix.m03 / Cell), Mathf.FloorToInt(spot.matrix.m23 / Cell));
            if (!squares.TryGetValue(key, out var list)) squares[key] = list = new List<Spot>();
            list.Add(spot);
            Planted++;
        }

        // Lays everything out in one array, square after square, for the job.
        private void Pack()
        {
            if (Planted == 0) return;
            spots = new NativeArray<Spot>(Planted, Allocator.Persistent);
            cells = new NativeArray<Square>(squares.Count, Allocator.Persistent);
            int next = 0, c = 0;
            foreach (var pair in squares)
            {
                float height = 0f;
                int first = next;
                foreach (var spot in pair.Value)
                {
                    spots[next++] = spot;
                    height += spot.matrix.m13;
                }
                cells[c++] = new Square
                {
                    centre = new float3((pair.Key.x + 0.5f) * Cell, height / pair.Value.Count, (pair.Key.y + 0.5f) * Cell),
                    first = first, count = pair.Value.Count
                };
            }
            squares.Clear();
            drawn = new Results(kinds.Count);
            filling = new Results(kinds.Count);
        }

        private void Plant()
        {
            var terrain = Terrain.activeTerrain;
            if (terrain == null || tiers == null || templated == 0) return;

            var data = terrain.terrainData;
            var layers = data.terrainLayers;
            var roads = new List<int>();
            for (int i = 0; i < layers.Length; i++)
                if (layers[i] != null && layers[i].diffuseTexture != null && layers[i].diffuseTexture.name.Contains("Road"))
                    roads.Add(i);
            var splat = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
            // Rocks filed by the squares they cover, so a patch is only tested against the ones near it.
            var rocks = new Dictionary<Vector2Int, List<Bounds>>();
            foreach (var r in FindObjectsByType<MeshRenderer>())
            {
                if (r.transform.root.name != "Rocks") continue;
                var b = r.bounds;
                for (int x = Mathf.FloorToInt(b.min.x / Cell); x <= Mathf.FloorToInt(b.max.x / Cell); x++)
                    for (int z = Mathf.FloorToInt(b.min.z / Cell); z <= Mathf.FloorToInt(b.max.z / Cell); z++)
                    {
                        var at = new Vector2Int(x, z);
                        if (!rocks.TryGetValue(at, out var near)) rocks[at] = near = new List<Bounds>();
                        near.Add(b);
                    }
            }

            var random = new System.Random(Seed);
            float Next() => (float)random.NextDouble();
            Vector3 origin = terrain.transform.position;
            for (int t = 0; t < tiers.Length; t++)
            {
                if (tiers[t] == null) continue;
                foreach (Transform tree in tiers[t])
                {
                    Vector3 trunk = tree.position;
                    for (int i = 0; i < patchesPerTree; i++)
                    {
                        float angle = Next() * Mathf.PI * 2f;
                        float out_ = Mathf.Lerp(spread.x, spread.y, Mathf.Sqrt(Next()));
                        var p = new Vector3(trunk.x + Mathf.Cos(angle) * out_, 0f, trunk.z + Mathf.Sin(angle) * out_);
                        var spot = new Spot
                        {
                            yaw = Next() * 360f,
                            scale = Mathf.Lerp(0.85f, 1.35f, Next()),
                            kind = (byte)random.Next(templated),
                            tier = (byte)t,
                            order = (byte)random.Next(100)
                        };
                        float u = (p.x - origin.x) / data.size.x, v = (p.z - origin.z) / data.size.z;
                        if (u <= 0f || v <= 0f || u >= 1f || v >= 1f || data.GetSteepness(u, v) > 35f) continue;
                        int sx = Mathf.Min((int)(u * data.alphamapWidth), data.alphamapWidth - 1);
                        int sz = Mathf.Min((int)(v * data.alphamapHeight), data.alphamapHeight - 1);
                        float road = 0f;
                        foreach (int layer in roads) road += splat[sz, sx, layer];
                        if (road > 0.25f) continue;
                        p.y = origin.y + terrain.SampleHeight(p) - 0.03f;
                        var key = new Vector2Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell));
                        bool underRock = false;
                        if (rocks.TryGetValue(key, out var near))
                            foreach (var b in near)
                                if (p.x > b.min.x && p.x < b.max.x && p.z > b.min.z && p.z < b.max.z && p.y < b.center.y)
                                {
                                    underRock = true;
                                    break;
                                }
                        if (underRock) continue;
                        spot.matrix = Matrix4x4.TRS(p, Quaternion.Euler(0f, spot.yaw, 0f), Vector3.one * spot.scale)
                                      * kinds[spot.kind].local;
                        File(spot);
                    }
                }
            }
            Debug.Log($"[Makeshift] forest grass: {Planted} patches round the extra trees");
        }

        private void LateUpdate()
        {
            if (head == null || !spots.IsCreated) return;
            int level = forest != null ? forest.Level : (tiers != null ? tiers.Length : 0);
            float share = looks != null ? looks.GrassShare : 1f;
            int preset = detail != null ? Mathf.Clamp(detail.Current, 0, Distances.Length - 1) : 1;
            var material = kinds[0].renderer != null ? kinds[0].renderer.sharedMaterial : null;
            bool soft = material != null && material.HasProperty("_GrassFade");

            var eye = head.transform;
            Shader.SetGlobalVector("_MkGrassFade", soft ? (Vector4)Distances[preset] : Vector4.zero);
            Shader.SetGlobalVector("_MkGrassEye", eye.position);

            if (running)
            {
                handle.Complete();
                running = false;
                (drawn, filling) = (filling, drawn);
                ready = true;
            }

            bool changed = level != builtForest || preset != builtDetail || !Mathf.Approximately(share, builtShare)
                           || soft != builtSoft;
            bool moved = (eye.position - builtAt).sqrMagnitude > 0.5f || Vector3.Angle(eye.forward, builtLooking) > 8f;
            if (changed || moved)
            {
                builtForest = level;
                builtDetail = preset;
                builtShare = share;
                builtSoft = soft;
                builtAt = eye.position;
                builtLooking = eye.forward;
                Gather(eye, level, share, soft ? Distances[preset] : float4.zero, soft, changed || !ready);
            }
            if (ready) Draw(eye.position);
        }

        private void Gather(Transform eye, int level, float share, float4 distances, bool soft, bool now)
        {
            var job = new Job
            {
                spots = spots, cells = cells,
                eye = eye.position,
                looking = math.normalizesafe(new float2(eye.forward.x, eye.forward.z)),
                // Looking steeply down, everything round the feet is in view.
                narrow = math.abs(eye.forward.y) < 0.75f,
                level = level, keep = (int)math.round(share * 100f),
                // A margin for what the head moves between two passes.
                swap = soft ? distances.y + 1.5f : float.MaxValue,
                farMin = soft ? distances.z : PlainReach, farMax = soft ? distances.w : PlainReach,
                matrices = filling.matrices, counts = filling.counts
            };
            running = true;
            if (now)
            {
                job.Run();
                running = false;
                (drawn, filling) = (filling, drawn);
                ready = true;
            }
            else
            {
                handle = job.Schedule();
                JobHandle.ScheduleBatchedJobs();
            }
        }

        private void Draw(Vector3 eye)
        {
            Shown = 0;
            for (int k = 0; k < kinds.Count; k++)
            {
                var kind = kinds[k];
                if (kind.renderer == null) continue;
                var how = new RenderParams(kind.renderer.sharedMaterial)
                {
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = true,
                    worldBounds = new Bounds(eye, Vector3.one * 180f),
                    lightProbeUsage = LightProbeUsage.Off,
                    reflectionProbeUsage = ReflectionProbeUsage.Off
                };
                for (int far = 0; far < 2; far++)
                {
                    int slot = k * 2 + far, count = drawn.counts[slot], first = slot * Room;
                    Shown += count;
                    for (int start = 0; start < count; start += PerDraw)
                        Graphics.RenderMeshInstanced(how, far == 0 ? kind.near : kind.far, 0, drawn.matrices,
                            Mathf.Min(PerDraw, count - start), first + start);
                }
            }
        }

        private void OnDestroy()
        {
            if (running) handle.Complete();
            running = false;
            Shader.SetGlobalVector("_MkGrassFade", Vector4.zero);
            if (spots.IsCreated) spots.Dispose();
            if (cells.IsCreated) cells.Dispose();
            drawn.Dispose();
            filling.Dispose();
        }

        private const int Room = FarRoom > NearRoom ? FarRoom : NearRoom;

        private struct Results
        {
            public NativeArray<Matrix4x4> matrices; // Room places for each kind, near then far
            public NativeArray<int> counts;

            public Results(int kinds)
            {
                matrices = new NativeArray<Matrix4x4>(kinds * 2 * Room, Allocator.Persistent);
                counts = new NativeArray<int>(kinds * 2, Allocator.Persistent);
            }

            public void Dispose()
            {
                if (matrices.IsCreated) matrices.Dispose();
                if (counts.IsCreated) counts.Dispose();
            }
        }

        [BurstCompile]
        private struct Job : IJob
        {
            [ReadOnly] public NativeArray<Spot> spots;
            [ReadOnly] public NativeArray<Square> cells;
            public float3 eye;
            public float2 looking;
            public bool narrow;
            public int level, keep;
            public float swap, farMin, farMax;

            public NativeArray<Matrix4x4> matrices;
            public NativeArray<int> counts;

            public void Execute()
            {
                for (int i = 0; i < counts.Length; i++) counts[i] = 0;
                const float around = Cell * 0.75f; // from a square's middle to its corner, a little over
                for (int c = 0; c < cells.Length; c++)
                {
                    var cell = cells[c];
                    float2 to = cell.centre.xz - eye.xz;
                    float distance = math.length(to);
                    if (distance - around > farMax) continue;
                    // Behind the head: outside about 75 degrees of where it looks.
                    if (narrow && distance > around * 2f && math.dot(to, looking) < distance * 0.26f - around) continue;

                    for (int i = cell.first; i < cell.first + cell.count; i++)
                    {
                        var spot = spots[i];
                        if (spot.order >= keep || (spot.tier != Always && spot.tier >= level)) continue;
                        var at = new float2(spot.matrix.m03, spot.matrix.m23);
                        float far = math.distance(at, eye.xz);
                        if (far > math.lerp(farMin, farMax, Rank(at)) + 0.5f) continue;
                        int slot = spot.kind * 2 + (far < swap ? 0 : 1);
                        int n = counts[slot];
                        if (n >= ((slot & 1) == 0 ? NearRoom : FarRoom)) continue;
                        matrices[slot * Room + n] = spot.matrix;
                        counts[slot] = n + 1;
                    }
                }
            }

            // Matches MkRank in Scenery.shader.
            private static float Rank(float2 at)
            {
                uint x = (uint)(int)math.floor(at.x * 16f), y = (uint)(int)math.floor(at.y * 16f);
                uint h = (x * 73856093u) ^ (y * 19349663u);
                return ((h >> 8) & 1023u) / 1023f;
            }
        }
    }
}
