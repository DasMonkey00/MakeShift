using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Makeshift.Rendering
{
    /// <summary>
    /// Scatters flat grass clumps over a square area. Clumps are merged into one mesh per chunk at start, so
    /// the whole field is a handful of draw calls and still culls by chunk (cheap enough for Quest).
    /// Wind sway comes from the grass material.
    /// </summary>
    public class GrassField : MonoBehaviour
    {
        [SerializeField] private Mesh clump;
        [SerializeField] private Material material;
        [SerializeField] private float halfSize = 28f;
        [SerializeField] private float chunkSize = 8f;
        [Tooltip("Clumps per square metre.")]
        [SerializeField] private float density = 1.2f;
        [SerializeField] private Vector2 scaleRange = new Vector2(0.8f, 1.4f);
        [Tooltip("Keep this radius around the origin clear (spawn point and style board).")]
        [SerializeField] private float clearRadius = 0f;
        [SerializeField] private int seed = 7;
        [Tooltip("Density multiplier on Android (Quest). 140 triangles per clump adds up fast in stereo.")]
        [SerializeField, Range(0.1f, 1f)] private float questDensityScale = 0.35f;
        [Tooltip("Chunks farther than this from the camera are switched off. Quest uses the smaller value.")]
        [SerializeField] private float drawDistancePc = 40f;
        [SerializeField] private float drawDistanceQuest = 16f;

        /// <summary>Circles where no grass grows (a campfire pit, a path). Filled by objects in Awake, read at Start.</summary>
        private static readonly List<(Vector3 centre, float radius)> exclusions = new List<(Vector3, float)>();

        public static void Exclude(Vector3 centre, float radius)
        {
            exclusions.Add((centre, radius));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetExclusions()
        {
            exclusions.Clear();
        }

        private readonly List<Renderer> chunkRenderers = new List<Renderer>();
        private float nextCull;
        private float drawDistance;

        private bool IsExcluded(float x, float z)
        {
            foreach (var e in exclusions)
            {
                float dx = x - e.centre.x, dz = z - e.centre.z;
                if (dx * dx + dz * dz < e.radius * e.radius) return true;
            }
            return false;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextCull || chunkRenderers.Count == 0) return;
            nextCull = Time.unscaledTime + 0.25f;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 p = cam.transform.position;
            float limit = drawDistance + chunkSize * 0.75f; // chunk centre distance plus half a chunk diagonal
            float limitSqr = limit * limit;
            foreach (var r in chunkRenderers)
            {
                Vector3 d = r.transform.position - p;
                d.y = 0f;
                r.enabled = d.sqrMagnitude < limitSqr;
            }
        }

        private void Start()
        {
            if (clump == null || material == null) return;
            bool quest = Application.platform == RuntimePlatform.Android;
            drawDistance = quest ? drawDistanceQuest : drawDistancePc;
            var rng = new System.Random(seed);
            int chunks = Mathf.CeilToInt(halfSize * 2f / chunkSize);
            float start = -halfSize;
            int perChunk = Mathf.RoundToInt(chunkSize * chunkSize * density * (quest ? questDensityScale : 1f));
            int total = 0;

            for (int cx = 0; cx < chunks; cx++)
            {
                for (int cz = 0; cz < chunks; cz++)
                {
                    var combine = new List<CombineInstance>(perChunk);
                    var center = new Vector3(start + (cx + 0.5f) * chunkSize, 0f, start + (cz + 0.5f) * chunkSize);
                    for (int i = 0; i < perChunk; i++)
                    {
                        float x = start + cx * chunkSize + (float)rng.NextDouble() * chunkSize;
                        float z = start + cz * chunkSize + (float)rng.NextDouble() * chunkSize;
                        if (clearRadius > 0f && x * x + z * z < clearRadius * clearRadius) continue;
                        if (IsExcluded(x, z)) continue;
                        float s = Mathf.Lerp(scaleRange.x, scaleRange.y, (float)rng.NextDouble());
                        var m = Matrix4x4.TRS(new Vector3(x, 0f, z) - center,
                            Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), Vector3.one * s);
                        combine.Add(new CombineInstance { mesh = clump, transform = m });
                    }
                    if (combine.Count == 0) continue;

                    var mesh = new Mesh { name = $"Grass {cx},{cz}", indexFormat = IndexFormat.UInt32 };
                    mesh.CombineMeshes(combine.ToArray(), true, true);
                    mesh.UploadMeshData(true);

                    var go = new GameObject($"GrassChunk {cx},{cz}");
                    go.transform.SetParent(transform, false);
                    go.transform.position = center;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = material;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    chunkRenderers.Add(r);
                    total += combine.Count;
                }
            }
            Debug.Log($"[GRASS] {total} clumps in {transform.childCount} chunks");
        }
    }
}
