using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Makeshift.Rendering
{
    /// <summary>
    /// Draws the still scenery (trees, grass, plants, rocks) itself: one instanced draw for every mesh and material,
    /// however many copies are in view, in place of one renderer per copy. It picks each copy's detail level from the
    /// same distances as its LODGroup, and hands everything back to Unity when switched off.
    ///
    /// Left to Unity: anything with a script or a rigidbody on it (a tree that can be felled), anything close to a
    /// point light (these draws get no extra lights), and mirrored models. There is no cross fade between detail
    /// levels here: they swap at once.
    ///
    /// Working out what is in view runs as a Burst job on another core (<see cref="ScenerySort"/>): it is asked for in
    /// one frame and drawn from the next. The view margin covers what the head turns in between.
    ///
    /// Call <see cref="MarkDirty"/> after changing the scenery's materials, shadows, LOD distances or which objects
    /// are active.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class SceneryInstancer : MonoBehaviour
    {
        private static readonly HashSet<string> Roots = new HashSet<string>
        {
            "Grass", "Trees", "Background Trees", "Plants", "Rocks", "Flowers", "Mushrooms", "Decals", "Props"
        };

        [SerializeField] private Camera head;
        [Tooltip("Scenery nearer than this to a point light stays a normal renderer, so the light still falls on it.")]
        [SerializeField] private float lightReach = 4.5f;
        [Tooltip("The lists of what is in view are rebuilt after moving or turning this far.")]
        [SerializeField] private float rebuildMetres = 0.5f;
        [SerializeField] private float rebuildDegrees = 5f;
        [Tooltip("Degrees added round the view, so nothing is missing at the edges between rebuilds.")]
        [SerializeField] private float viewMargin = 18f;

        private const int PerDraw = 500;

        private class Batch
        {
            public Mesh mesh;
            public int submesh;
            public RenderParams how;
            public int index;           // its place in the sort's lists
        }

        private class Part
        {
            public MeshRenderer renderer;
            public Mesh mesh;
            public Matrix4x4 matrix;
            public Bounds bounds;
            public bool wasEnabled;
            public bool shown;
            public readonly List<Batch> batches = new List<Batch>(2);
        }

        private class Item
        {
            public LODGroup group;
            public Part[][] levels;
            public float[] reach;     // each level is used nearer than this
            public Vector3 centre;
            public float radius;
            public bool merged;
            public bool casts;
            public int slot = -1;     // its place in the sort, while it is in it
        }

        private readonly List<Item> items = new List<Item>();
        private readonly Dictionary<(Mesh, int, Material, ShadowCastingMode, int), Batch> batches =
            new Dictionary<(Mesh, int, Material, ShadowCastingMode, int), Batch>();
        private readonly Plane[] planes = new Plane[6];
        private readonly List<Batch> ordered = new List<Batch>();
        private ScenerySort sort;
        private bool collected, dirty = true, on = true;
        [Tooltip("Which point lights stop nearby scenery from being merged.")]
        [SerializeField] private LightKeeping keeping = LightKeeping.Fire;
        private Vector3 builtAt;
        private Quaternion builtLooking;

        public bool On => on;
        /// <summary>Main thread time the last pass over the scenery cost, in milliseconds: asking and collecting.</summary>
        public float LastSortMs { get; private set; }
        private readonly System.Diagnostics.Stopwatch sortClock = new System.Diagnostics.Stopwatch();
        /// <summary>How many renderers are drawn by this component when it is on.</summary>
        public int Covered { get; private set; }

        public void SetViewer(Camera viewer)
        {
            head = viewer;
            dirty = true;
        }

        public void SetOn(bool value)
        {
            if (on == value) return;
            on = value;
            dirty = true;
            if (!on)
            {
                sort?.Forget();
                HandBack();
            }
        }

        public void MarkDirty() => dirty = true;

        private bool again;

        /// <summary>
        /// Stops drawing one tree because something else has taken it over. Far cheaper than <see cref="MarkDirty"/>,
        /// which reads all the scenery again and stalls the frame.
        /// </summary>
        public void Withdraw(LODGroup group)
        {
            foreach (var item in items)
            {
                if (item.group != group) continue;
                foreach (var level in item.levels)
                    foreach (var part in level)
                        part.shown = false;
                if (item.slot >= 0) sort?.Drop(item.slot);
                item.slot = -1;
                again = true; // the lists in hand still hold it
            }
        }

        /// <summary>
        /// Which point lights keep the scenery around them drawn one by one, so it stays lit by them. Merged scenery
        /// gets no point light. All = every point light (most draws); Fire = only lights that belong to a fire or
        /// torch; None = merge everything.
        /// </summary>
        public enum LightKeeping { All, Fire, None }

        public LightKeeping Keeping => keeping;

        public void SetKeeping(LightKeeping value)
        {
            if (keeping == value) return;
            keeping = value;
            Recollect();
        }

        private readonly HashSet<Renderer> claimed = new HashSet<Renderer>();

        /// <summary>
        /// Renderers someone else is drawing (<see cref="ChunkedScenery"/>); pass null to take them all back.
        /// Every renderer is handed back first, so the caller can switch the claimed ones off straight away.
        /// </summary>
        public void Claim(IEnumerable<Renderer> renderers)
        {
            claimed.Clear();
            if (renderers != null) claimed.UnionWith(renderers);
            Recollect();
        }

        /// <summary>Reads every LOD group again, for when their models have changed (the trees' LOD1 mode).</summary>
        public void Rebuild() => Recollect();

        private void Recollect()
        {
            sort?.Forget();
            if (on) HandBack(); // switched off, they were handed back already
            items.Clear();
            collected = false;
            dirty = true;
        }

        private bool Keeps(Light light)
        {
            if (keeping == LightKeeping.All) return true;
            if (keeping == LightKeeping.None) return false;
            return light.GetComponentInParent<Fire.Flammable>(true) != null
                   || light.GetComponentInParent<Fire.Campfire>(true) != null;
        }

        private void OnDisable()
        {
            sort?.Dispose();
            sort = null;
            HandBack();
            dirty = true;
        }

        private void HandBack()
        {
            foreach (var item in items)
                foreach (var level in item.levels)
                    foreach (var part in level)
                        if (part.renderer != null) part.renderer.enabled = part.wasEnabled;
        }

        // Collected on the first frame, after every other Start has added its LOD groups and chosen materials.
        private void LateUpdate()
        {
            if (!on || head == null) return;
            if (!collected) Collect();

            var eye = head.transform;
            bool moved = (eye.position - builtAt).sqrMagnitude > rebuildMetres * rebuildMetres
                         || Quaternion.Angle(eye.rotation, builtLooking) > rebuildDegrees;
            if (sort == null) sort = new ScenerySort();

            sortClock.Restart();
            bool collectedOne = sort.Collect();   // what was asked for last frame
            if (dirty)
            {
                Refresh();
                Pack();
                Rebuild(true);                    // the lists are new: no frame may go without them
            }
            else if (again) Rebuild(true);
            else if (moved) Rebuild(false);
            if (collectedOne || dirty || moved) LastSortMs = (float)sortClock.Elapsed.TotalMilliseconds;
            dirty = again = false;

            foreach (var batch in ordered)
            {
                if (!sort.Visible(batch.index, out var matrices, out int first, out int count, out var bounds)) continue;
                var how = batch.how;
                how.worldBounds = bounds;
                for (int start = 0; start < count; start += PerDraw)
                    Graphics.RenderMeshInstanced(how, batch.mesh, batch.submesh, matrices,
                        Mathf.Min(PerDraw, count - start), first + start);
            }
        }

        private void Collect()
        {
            collected = true;
            var lights = new List<Light>();
            foreach (var light in FindObjectsByType<Light>(FindObjectsInactive.Include))
                if (light.type == LightType.Point && Keeps(light)) lights.Add(light);

            var taken = new HashSet<MeshRenderer>();
            foreach (var group in FindObjectsByType<LODGroup>(FindObjectsInactive.Include))
            {
                if (!Roots.Contains(group.transform.root.name)) continue;
                var lods = group.GetLODs();
                var levels = new Part[lods.Length][];
                bool usable = lods.Length > 0;
                for (int i = 0; i < lods.Length; i++)
                {
                    var parts = new List<Part>();
                    foreach (var r in lods[i].renderers)
                    {
                        if (r == null) continue;
                        var part = r is MeshRenderer mesh ? MakePart(mesh, lights) : null;
                        if (part == null) usable = false;
                        else parts.Add(part);
                    }
                    levels[i] = parts.ToArray();
                }
                // All or nothing: a group half drawn here would double up or leave holes.
                foreach (var lod in lods)
                    foreach (var r in lod.renderers)
                        if (r is MeshRenderer mesh) taken.Add(mesh);
                if (usable) Add(group, levels);
            }

            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
            {
                if (taken.Contains(r) || !Roots.Contains(r.transform.root.name)) continue;
                var part = MakePart(r, lights);
                if (part != null) Add(null, new[] { new[] { part } });
            }
        }

        private Part MakePart(MeshRenderer r, List<Light> lights)
        {
            var filter = r.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return null;
            if (claimed.Contains(r) || r.GetComponentInParent<Rigidbody>(true) != null) return null;
            // Scripted objects may move or change, so they stay normal renderers. A choppable tree is the exception:
            // it stands still until the first bite, when it hides these renderers and marks the merge dirty.
            foreach (var script in r.GetComponentsInParent<MonoBehaviour>(true))
                if (!(script is Nature.ChoppableTree)) return null;
            var matrix = r.localToWorldMatrix;
            if (matrix.determinant < 0f) return null; // mirrored: would draw inside out
            var bounds = r.bounds;
            if (bounds.size == Vector3.zero) bounds = new Bounds(r.transform.position, Vector3.one * 2f);
            foreach (var light in lights)
            {
                float reach = Mathf.Min(light.range, lightReach);
                if (bounds.SqrDistance(light.transform.position) < reach * reach) return null;
            }
            return new Part { renderer = r, mesh = filter.sharedMesh, matrix = matrix, bounds = bounds, wasEnabled = r.enabled };
        }

        private void Add(LODGroup group, Part[][] levels)
        {
            var item = new Item { group = group, levels = levels, reach = new float[levels.Length] };
            bool any = false;
            var bounds = new Bounds();
            foreach (var level in levels)
                foreach (var part in level)
                {
                    if (!any) bounds = part.bounds;
                    else bounds.Encapsulate(part.bounds);
                    any = true;
                }
            if (!any) return;
            item.centre = group != null ? group.transform.TransformPoint(group.localReferencePoint) : bounds.center;
            item.radius = bounds.extents.magnitude + Vector3.Distance(bounds.center, item.centre);
            items.Add(item);
        }

        /// <summary>Reads materials, shadows, active state and LOD distances again.</summary>
        private void Refresh()
        {
            // How much of the view's height one metre of object fills at one metre's distance.
            float perMetre = QualitySettings.lodBias / (2f * Mathf.Tan(head.fieldOfView * 0.5f * Mathf.Deg2Rad));
            batches.Clear();
            ordered.Clear();
            Covered = 0;
            foreach (var item in items)
            {
                if (item.group != null && item.group.enabled)
                {
                    var lods = item.group.GetLODs();
                    var scale = item.group.transform.lossyScale;
                    float size = item.group.size * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                    for (int i = 0; i < item.reach.Length && i < lods.Length; i++)
                    {
                        float height = lods[i].screenRelativeTransitionHeight;
                        item.reach[i] = height > 0f ? size * perMetre / height : float.MaxValue;
                    }
                }
                else
                {
                    for (int i = 0; i < item.reach.Length; i++) item.reach[i] = i == 0 ? float.MaxValue : 0f;
                }

                item.merged = true;
                item.casts = false;
                foreach (var level in item.levels)
                    foreach (var part in level)
                    {
                        part.batches.Clear();
                        var r = part.renderer;
                        part.shown = r != null && part.wasEnabled && r.gameObject.activeInHierarchy && !r.forceRenderingOff;
                        if (r == null) continue;
                        var materials = r.sharedMaterials;
                        for (int s = 0; s < materials.Length && s < part.mesh.subMeshCount; s++)
                        {
                            var material = materials[s];
                            if (material == null) continue;
                            if (!material.enableInstancing) { item.merged = false; continue; }
                            item.casts |= r.shadowCastingMode != ShadowCastingMode.Off;
                            part.batches.Add(BatchFor(part.mesh, s, material, r.shadowCastingMode, r.gameObject.layer));
                        }
                    }

                foreach (var level in item.levels)
                    foreach (var part in level)
                        if (part.renderer != null)
                        {
                            part.renderer.enabled = !item.merged && part.wasEnabled;
                            if (item.merged) Covered++;
                        }
            }
        }

        private Batch BatchFor(Mesh mesh, int submesh, Material material, ShadowCastingMode shadows, int layer)
        {
            var key = (mesh, submesh, material, shadows, layer);
            if (batches.TryGetValue(key, out var batch)) return batch;
            batch = new Batch
            {
                index = ordered.Count,
                mesh = mesh,
                submesh = submesh,
                how = new RenderParams(material)
                {
                    layer = layer,
                    shadowCastingMode = shadows,
                    receiveShadows = true,
                    lightProbeUsage = LightProbeUsage.Off,
                    reflectionProbeUsage = ReflectionProbeUsage.Off
                }
            };
            batches.Add(key, batch);
            ordered.Add(batch);
            return batch;
        }

        /// <summary>Hands the sort what can be drawn at all. Hidden things are left out, so they cost nothing.</summary>
        private void Pack()
        {
            sort.Begin(ordered.Count);
            foreach (var item in items)
            {
                item.slot = -1;
                if (!item.merged) continue;
                bool any = false;
                foreach (var level in item.levels)
                    foreach (var part in level)
                        any |= part.shown && part.batches.Count > 0;
                if (!any) continue;

                item.slot = sort.Item(item.centre, item.radius, item.casts);
                for (int i = 0; i < item.levels.Length; i++)
                {
                    sort.Level(item.reach[i]);
                    foreach (var part in item.levels[i])
                    {
                        if (!part.shown || part.batches.Count == 0) continue;
                        sort.Part(part.matrix, part.bounds);
                        foreach (var batch in part.batches) sort.Uses(batch.index);
                    }
                }
            }
            sort.End();
        }

        private void Rebuild(bool now)
        {
            var eye = head.transform;
            builtAt = eye.position;
            builtLooking = eye.rotation;

            float fov = Mathf.Min(head.fieldOfView + viewMargin, 160f);
            var wide = Matrix4x4.Perspective(fov, head.aspect * 1.05f, head.nearClipPlane, head.farClipPlane);
            GeometryUtility.CalculateFrustumPlanes(wide * head.worldToCameraMatrix, planes);
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            float shadowReach = pipeline != null ? pipeline.shadowDistance : 25f;

            var sun = RenderSettings.sun;
            // Which way shadows run from the thing that casts them.
            Vector3 fall = sun != null && sun.shadows != LightShadows.None ? sun.transform.forward : Vector3.zero;
            if (fall == Vector3.zero) shadowReach = 0f;

            sort.Ask(builtAt, planes, fall, shadowReach, now);
        }
    }
}
