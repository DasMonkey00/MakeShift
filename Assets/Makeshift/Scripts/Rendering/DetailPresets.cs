using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Makeshift.Rendering
{
    /// <summary>
    /// Detail distances for the Toon Fantasy Nature scenery, in metres: where each kind of object steps down to a
    /// simpler model and where it stops being drawn. Small things without LODs in the pack get a cut-off distance too,
    /// and small plants stop casting shadows. "Pack" puts everything back as the pack authored it, to compare.
    ///
    /// Tune the distances in <see cref="Presets"/>.
    /// </summary>
    public class DetailPresets : MonoBehaviour
    {
        /// <summary>FarTree: a tree with a third level, the flat card used far away.</summary>
        private enum Kind { Grass, Flower, Plant, Tree, Small, Rock, FarTree }

        private class Preset
        {
            public string name;
            /// <summary>Per kind: the distance where each model hands over to the next; the last is where it vanishes.</summary>
            public Dictionary<Kind, float[]> distances;
        }

        private static readonly Preset[] Presets =
        {
            new Preset { name = "Pack" },
            new Preset
            {
                name = "Mid",
                distances = new Dictionary<Kind, float[]>
                {
                    { Kind.Grass, new[] { 4f, 7f, 11f, 18f } },
                    { Kind.Flower, new[] { 8f, 16f, 25f } },
                    { Kind.Plant, new[] { 9f, 32f } },
                    { Kind.Tree, new[] { 25f, 120f } },
                    { Kind.FarTree, new[] { 25f, 55f, 260f } },
                    { Kind.Small, new[] { 28f } },
                    { Kind.Rock, new[] { 20f, 40f, 90f } }
                }
            },
            new Preset
            {
                name = "Short",
                distances = new Dictionary<Kind, float[]>
                {
                    { Kind.Grass, new[] { 3f, 5f, 7.5f, 12f } },
                    { Kind.Flower, new[] { 6f, 11f, 17f } },
                    { Kind.Plant, new[] { 6f, 20f } },
                    { Kind.Tree, new[] { 18f, 75f } },
                    { Kind.FarTree, new[] { 18f, 40f, 170f } },
                    { Kind.Small, new[] { 17f } },
                    { Kind.Rock, new[] { 14f, 28f, 65f } }
                }
            }
        };

        [SerializeField] private Camera head;
        [SerializeField] private int startPreset = 1;

        private class Item
        {
            public LODGroup group;
            public Kind kind;
            public LOD[] authored;   // null when we added the group ourselves
            public Renderer[] renderers;
            public ShadowCastingMode[] shadows;
        }

        private readonly List<Item> items = new List<Item>();
        private float appliedFov;
        private float nextCheck;

        public int Current { get; private set; }
        /// <summary>While on, trees drop their closest model and always draw their second one.</summary>
        public bool TreesOnLod1 { get; private set; }
        /// <summary>Raised after the distances change.</summary>
        public event Action Applied;
        public string CurrentName => Presets[Current].name;

        private void Start()
        {
            Collect();
            Apply(startPreset);
        }

        public void Cycle() => Apply((Current + 1) % Presets.Length);

        /// <summary>Turns the trees' second-model-only mode on or off, and applies it straight away.</summary>
        public void SetTreesOnLod1(bool on)
        {
            TreesOnLod1 = on;
            Apply(Current);
        }

        // The models a group should have. In the trees-on-LOD1 mode a tree loses its closest model, so its second
        // one is the closest and is used at every distance.
        private LOD[] Wanted(Item item, LOD[] full)
        {
            bool tree = item.kind == Kind.Tree || item.kind == Kind.FarTree;
            if (!TreesOnLod1 || !tree || full.Length < 2) return (LOD[])full.Clone();
            var kept = new LOD[full.Length - 1];
            Array.Copy(full, 1, kept, 0, kept.Length);
            return kept;
        }

        // A model taken out of its LOD group is no longer switched by it, so Unity (and the merged scenery, which picks
        // it up as a loose mesh) would draw it at every distance. Hide the dropped closest model while the mode is on.
        private void HideDropped(Item item)
        {
            bool tree = item.kind == Kind.Tree || item.kind == Kind.FarTree;
            var full = item.authored;
            if (!tree || full == null || full.Length < 2) return;
            bool hide = TreesOnLod1;
            foreach (var r in full[0].renderers)
            {
                if (r == null) continue;
                bool alsoLater = false;
                for (int i = 1; i < full.Length && !alsoLater; i++)
                    alsoLater = Array.IndexOf(full[i].renderers, r) >= 0;
                if (!alsoLater) r.forceRenderingOff = hide;
            }
        }

        /// <summary>The camera whose field of view the distances are worked out for.</summary>
        public void SetViewer(Camera viewer)
        {
            head = viewer;
            Apply(Current);
        }

        private void Update()
        {
            // The headset sets the camera's field of view a moment after the start.
            if (head == null || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 1f;
            if (Mathf.Abs(head.fieldOfView - appliedFov) > 2f) Apply(Current);
        }

        private void Collect()
        {
            var taken = new HashSet<Renderer>();
            foreach (var group in FindObjectsByType<LODGroup>(FindObjectsInactive.Include))
            {
                string n = group.name;
                Kind kind = n.Contains("Grass_Patch") ? Kind.Grass
                    : n.Contains("Flower") ? Kind.Flower
                    : n.Contains("Tree") ? Kind.Tree
                    : n.Contains("Rock") ? Kind.Rock
                    : Kind.Plant;
                var lods = group.GetLODs();
                if (kind == Kind.Tree && lods.Length > 2) kind = Kind.FarTree;
                var all = new List<Renderer>();
                foreach (var lod in lods)
                    foreach (var r in lod.renderers)
                        if (r != null) { all.Add(r); taken.Add(r); }
                Add(group, kind, lods, all);
            }

            // Loose small scenery the pack gives no LODs. Big rocks, tree trunks and far tree cards stay as they are.
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
            {
                if (taken.Contains(r)) continue;
                string root = r.transform.root.name;
                bool small = root == "Plants" || root == "Mushrooms" || root == "Decals" || root == "Props"
                             || (root == "Rocks" && r.name.Contains("Small"));
                if (!small || r.GetComponent<LODGroup>() != null) continue;
                var group = r.gameObject.AddComponent<LODGroup>();
                group.SetLODs(new[] { new LOD(0.001f, new Renderer[] { r }) });
                group.RecalculateBounds();
                group.enabled = false;
                Add(group, Kind.Small, null, new List<Renderer> { r });
            }
        }

        private void Add(LODGroup group, Kind kind, LOD[] authored, List<Renderer> renderers)
        {
            var item = new Item { group = group, kind = kind, authored = authored, renderers = renderers.ToArray() };
            item.shadows = Array.ConvertAll(item.renderers, r => r.shadowCastingMode);
            items.Add(item);
        }

        public void Apply(int index)
        {
            Current = Mathf.Clamp(index, 0, Presets.Length - 1);
            var preset = Presets[Current];
            appliedFov = head != null ? head.fieldOfView : 96f;
            // How much of the view's height one metre of object fills at one metre's distance.
            float perMetre = QualitySettings.lodBias / (2f * Mathf.Tan(appliedFov * 0.5f * Mathf.Deg2Rad));

            foreach (var item in items)
            {
                if (item.group == null) continue;
                HideDropped(item);
                bool pack = preset.distances == null;

                // Small greenery's shadows cost a draw each and hardly show.
                bool noShadow = !pack && (item.kind == Kind.Plant || item.kind == Kind.Flower || item.kind == Kind.Small);
                for (int i = 0; i < item.renderers.Length; i++)
                    if (item.renderers[i] != null)
                        item.renderers[i].shadowCastingMode = noShadow ? ShadowCastingMode.Off : item.shadows[i];

                if (item.authored == null)
                {
                    item.group.enabled = !pack;
                    if (pack) continue;
                }
                else if (pack)
                {
                    item.group.SetLODs(Wanted(item, item.authored));
                    continue;
                }

                var scale = item.group.transform.lossyScale;
                float size = item.group.size * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                var distances = preset.distances[item.kind];
                var full = item.authored ?? item.group.GetLODs();
                var lods = Wanted(item, full);
                int dropped = full.Length - lods.Length; // models taken off the front (the closest one in LOD1 mode)
                float previous = 1f;
                for (int i = 0; i < lods.Length; i++)
                {
                    // Each model keeps the distance it had in the full set, so LOD1 still ends where it always did.
                    // With fewer models than distances, the last model lasts to the vanishing distance.
                    float d = i == lods.Length - 1 ? distances[distances.Length - 1] : distances[Mathf.Min(i + dropped, distances.Length - 1)];
                    // The closest model may switch off at any size of view, so a tall tree is not held to 98% of the
                    // view (which pushed its switch in and made it pop). Later models must still step down in order.
                    float cap = i == 0 ? 1f : previous * 0.98f;
                    float height = Mathf.Min(size * perMetre / d, cap);
                    lods[i].screenRelativeTransitionHeight = height;
                    previous = height;
                }
                item.group.SetLODs(lods);
            }
            Applied?.Invoke();
        }
    }
}
