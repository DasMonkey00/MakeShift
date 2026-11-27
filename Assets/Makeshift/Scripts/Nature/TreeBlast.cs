using System.Collections;
using System.Collections.Generic;
using Makeshift.Audio;
using Makeshift.Rendering;
using UnityEngine;

namespace Makeshift.Nature
{
    /// <summary>
    /// Brings down many pines at once, as a blast does. They look cut: a stump with torn wood and the rest of the tree
    /// lying on the ground. Chopping a tree carves it one bite at a time, far too slowly for dozens together, so here
    /// every tree of one kind shares the same broken pieces, made once. A few trees break each frame, nearest first, and
    /// each falls by simple turning about its foot until it meets the terrain.
    /// </summary>
    public class TreeBlast : MonoBehaviour
    {
        private const float CutHeight = 1f;   // in the model's own metres, so it scales with the tree
        private const float BandHalf = 0.42f;
        private const float Splinter = 0f; // TEST: flat caps to see if the splinter spikes cause the seam
        private const int PerFrame = 3;

        /// <summary>The broken pieces every tree of one kind shares.</summary>
        private class Kind
        {
            public Mesh[] stump, top;          // by detail level
            public Mesh stumpCut, crownCut;    // closest level only
            public Vector3 middle;             // trunk centre at the cut, model space
            public float radius, height, spread;
        }

        private struct Falling
        {
            public Transform crown;
            public Vector3 about, hinge, butt;
            public float rest, wait, time, takes, radius, length;
        }

        private readonly Dictionary<Mesh, Kind> kinds = new Dictionary<Mesh, Kind>();
        private readonly Queue<(LODGroup group, Vector3 from)> queue = new Queue<(LODGroup, Vector3)>();
        private readonly HashSet<LODGroup> taken = new HashSet<LODGroup>();
        private readonly List<Falling> falling = new List<Falling>();
        private TreeCutting cutting;
        private ToonLookSwitcher looks;
        private ChunkedScenery chunks;
        private ParticleSystem leaves;

        /// <summary>How many trees have been brought down this way.</summary>
        public int Broken { get; private set; }
        /// <summary>Trees still waiting to break or still on their way down.</summary>
        public int Busy => queue.Count + falling.Count;

        public void Setup(TreeCutting owner) => cutting = owner;

        // The pieces of each kind are made one a frame at the start, so the first blast does not stall.
        private IEnumerator Start()
        {
            yield return new WaitForSeconds(1.5f);
            foreach (var group in FindObjectsByType<LODGroup>())
            {
                if (group == null || !Read(group, out var renderers, out var meshes, out int bark)) continue;
                var drawn = renderers[0].GetComponent<MeshFilter>().sharedMesh;
                if (kinds.ContainsKey(drawn)) continue;
                Make(drawn, meshes, bark);
                yield return null;
            }
        }

        /// <summary>Queues one standing pine to break, falling away from <paramref name="from"/>.</summary>
        public bool Break(LODGroup group, Vector3 from)
        {
            if (group == null || !group.enabled || taken.Contains(group) || !Read(group, out _, out _, out _)) return false;
            taken.Add(group);
            queue.Enqueue((group, from));
            return true;
        }

        // The two full models of a pine and their readable copies. False for anything that is not a pine.
        private bool Read(LODGroup group, out List<MeshRenderer> renderers, out List<Mesh> meshes, out int bark)
        {
            renderers = new List<MeshRenderer>(2);
            meshes = new List<Mesh>(2);
            bark = 0;
            var lods = group.GetLODs();
            for (int i = 0; i < lods.Length && i < 2; i++)
            {
                if (lods[i].renderers.Length == 0 || !(lods[i].renderers[0] is MeshRenderer r)) break;
                var filter = r.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || !cutting.TryModel(filter.sharedMesh, out var model)) break;
                if (i == 0) bark = model.bark;
                renderers.Add(r);
                meshes.Add(model.readable);
            }
            return renderers.Count > 0;
        }

        private Kind Make(Mesh drawn, List<Mesh> meshes, int bark)
        {
            var kind = new Kind { stump = new Mesh[meshes.Count], top = new Mesh[meshes.Count] };
            float c = CutHeight;
            var low = TreeMesh.Profile(meshes[0], bark, c - BandHalf, 1.2f);
            var high = TreeMesh.Profile(meshes[0], bark, c + BandHalf, 1.2f);
            kind.middle = Vector3.Lerp(low.centre, high.centre, 0.5f);
            kind.radius = (low.MeanRadius + high.MeanRadius) * 0.5f;
            var bounds = meshes[0].bounds;
            kind.height = bounds.max.y - c;
            kind.spread = Mathf.Min(bounds.extents.x, bounds.extents.z);
            var notch = new TreeMesh.Notch
            {
                height = c, side = Vector3.right, depth = (low.MeanRadius + high.MeanRadius) * 0.6f, halfHeight = 0.27f
            };
            for (int i = 0; i < meshes.Count; i++)
            {
                // The coarser models have no torn piece: they are simply cut through.
                kind.stump[i] = TreeMesh.Slice(meshes[i], bark, i == 0 ? c - BandHalf : c, c, false);
                kind.top[i] = TreeMesh.Slice(meshes[i], bark, i == 0 ? c + BandHalf : c, c, true);
            }
            kind.stumpCut = TreeMesh.Section(meshes[0], bark, low, high, c - BandHalf, c, notch, true, false, Splinter);
            kind.crownCut = TreeMesh.Section(meshes[0], bark, low, high, c, c + BandHalf, notch, false, true, Splinter);
            kinds[drawn] = kind;
            return kind;
        }

        private void Update()
        {
            for (int i = 0; i < PerFrame && queue.Count > 0; i++)
            {
                var (group, from) = queue.Dequeue();
                if (group != null && group.enabled) BreakNow(group, from);
            }
            Fall(Time.deltaTime);
        }

        private void BreakNow(LODGroup group, Vector3 from)
        {
            if (!Read(group, out var renderers, out var meshes, out int bark)) return;
            var drawn = renderers[0].GetComponent<MeshFilter>().sharedMesh;
            if (!kinds.TryGetValue(drawn, out var kind)) kind = Make(drawn, meshes, bark);
            if (looks == null) looks = FindAnyObjectByType<ToonLookSwitcher>();
            if (chunks == null) chunks = FindAnyObjectByType<ChunkedScenery>();

            var tree = group.transform;
            var model = renderers[0].transform;
            float scale = model.lossyScale.y;
            Vector3 hinge = model.TransformPoint(kind.middle);
            float radius = kind.radius * scale, length = kind.height * scale;

            var stump = new GameObject(group.name + " stump").transform;
            stump.SetPositionAndRotation(tree.position, tree.rotation);
            stump.localScale = tree.lossyScale;
            stump.SetParent(tree.parent, true);
            stump.gameObject.AddComponent<LiveScenery>();

            var crown = new GameObject(group.name + " crown").transform;
            crown.position = hinge;
            var upper = new GameObject("Model").transform;
            upper.SetPositionAndRotation(tree.position, tree.rotation);
            upper.localScale = tree.lossyScale;
            upper.SetParent(crown, true);

            var authored = group.GetLODs();
            var stumpLods = new LOD[renderers.Count];
            var crownLods = new LOD[renderers.Count];
            for (int i = 0; i < renderers.Count; i++)
            {
                var source = renderers[i];
                var forStump = new List<Renderer> { Piece(stump, source, kind.stump[i], bark, false) };
                var forCrown = new List<Renderer> { Piece(upper, source, kind.top[i], bark, false) };
                // Every level gets the cut face: a coarser level without it leaves the stump open on top, so the cut
                // vanished as the tree changed level.
                forStump.Add(Piece(stump, source, kind.stumpCut, bark, true));
                forCrown.Add(Piece(upper, source, kind.crownCut, bark, true));
                int level = i == renderers.Count - 1 ? authored.Length - 1 : Mathf.Min(i, authored.Length - 1);
                float transition = authored[level].screenRelativeTransitionHeight;
                stumpLods[i] = new LOD(transition, forStump.ToArray());
                crownLods[i] = new LOD(transition, forCrown.ToArray());
            }
            Group(stump.gameObject, stumpLods, group.size);
            Group(upper.gameObject, crownLods, group.size);

            // The standing tree goes.
            if (chunks != null) chunks.Release(group);
            if (looks != null) looks.Withdraw(group);
            group.enabled = false;
            foreach (var lod in authored)
                foreach (var r in lod.renderers)
                    if (r != null) r.gameObject.SetActive(false);
            foreach (var col in group.GetComponentsInChildren<Collider>()) col.enabled = false;

            float ground = TreeRest.Ground(tree.position, tree.position.y);
            var foot = stump.gameObject.AddComponent<CapsuleCollider>();
            foot.radius = radius / Mathf.Max(0.01f, stump.lossyScale.y);
            foot.height = Mathf.Max(foot.radius * 2f, (hinge.y - ground) / Mathf.Max(0.01f, stump.lossyScale.y));
            foot.center = stump.InverseTransformPoint(new Vector3(hinge.x, (hinge.y + ground) * 0.5f, hinge.z));

            Vector3 away = Vector3.ProjectOnPlane(hinge - from, Vector3.up);
            away = away.sqrMagnitude > 0.01f ? away.normalized : Quaternion.Euler(0f, Random.value * 360f, 0f) * Vector3.forward;
            away = ClearOfRocks(hinge, away, length, radius);
            var about = Vector3.Cross(Vector3.up, away);
            TreeRest.Lie(hinge, away, length, radius, out var butt, out float rest);
            falling.Add(new Falling
            {
                crown = crown, about = about, radius = radius, length = length, hinge = hinge, butt = butt,
                rest = rest,
                wait = Vector3.Distance(from, hinge) / 60f + Random.value * 0.15f,
                takes = 1.3f + length * 0.045f + Random.value * 0.4f
            });
            Shed(hinge + Vector3.up * length * 0.5f, kind.spread * scale);
            if (Broken % 4 == 0) PlaceholderSfx.Play(SfxKind.Snap, hinge, 1f, 12f);
            Broken++;
        }

        private MeshRenderer Piece(Transform parent, MeshRenderer source, Mesh mesh, int bark, bool cut)
        {
            var go = new GameObject(cut ? "Cut" : "Piece");
            go.layer = source.gameObject.layer;
            go.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            go.transform.localScale = source.transform.lossyScale; // world size, set while it has no parent
            go.transform.SetParent(parent, true);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = cut ? new[] { source.sharedMaterials[bark], cutting.Wood } : source.sharedMaterials;
            r.shadowCastingMode = source.shadowCastingMode;
            r.receiveShadows = source.receiveShadows;
            if (looks != null) looks.Follow(source, r, cut ? bark : -1, cut ? cutting.Wood : null);
            return r;
        }

        private static void Group(GameObject go, LOD[] lods, float size)
        {
            var group = go.AddComponent<LODGroup>();
            group.SetLODs(lods);
            group.RecalculateBounds();
            group.size = size; // the whole tree's, so a stump does not vanish at a short distance
        }

        private void Fall(float step)
        {
            for (int i = falling.Count - 1; i >= 0; i--)
            {
                var f = falling[i];
                if (f.crown == null)
                {
                    falling.RemoveAt(i);
                    continue;
                }
                if (f.wait > 0f)
                {
                    f.wait -= step;
                    falling[i] = f;
                    continue;
                }
                f.time += step;
                float k = Mathf.Clamp01(f.time / f.takes);
                float angle = f.rest * Mathf.Pow(k, 2.4f);
                // One short bounce after it lands.
                float after = f.time - f.takes;
                if (after > 0f) angle = f.rest - Mathf.Abs(Mathf.Sin(after * 11f)) * 3f * Mathf.Exp(-after * 6f);
                Pose(f, angle, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1f, k)));
                falling[i] = f;
                if (after < 0.8f) continue;

                Pose(f, f.rest, 1f);
                Land(f);
                falling.RemoveAt(i);
            }
        }

        // The butt slips off the stump to the ground over the second half of the fall.
        private static void Pose(Falling f, float angle, float slid) =>
            f.crown.SetPositionAndRotation(Vector3.Lerp(f.hinge, f.butt, slid), Quaternion.AngleAxis(angle, f.about));

        private static readonly Collider[] Near = new Collider[16];

        // Turns the fall aside until its resting line misses the big rocks; straight on if every line hits one.
        private static Vector3 ClearOfRocks(Vector3 hinge, Vector3 away, float length, float radius)
        {
            for (int step = 0; step <= 12; step++)
            {
                float turn = ((step + 1) / 2) * 15f * (step % 2 == 0 ? -1f : 1f);
                Vector3 flat = Quaternion.AngleAxis(turn, Vector3.up) * away;
                TreeRest.Lie(hinge, flat, length, radius, out var butt, out float angle);
                Vector3 along = Quaternion.AngleAxis(angle, Vector3.Cross(Vector3.up, flat)) * Vector3.up;
                int found = Physics.OverlapCapsuleNonAlloc(butt + along * 1.5f + Vector3.up * 0.3f,
                    butt + along * (length * 0.8f) + Vector3.up * 0.3f, radius, Near, ~0, QueryTriggerInteraction.Ignore);
                bool rock = false;
                for (int i = 0; i < found; i++) rock |= Near[i].transform.root.name == "Rocks";
                if (!rock) return flat;
            }
            return away;
        }

        private void Land(Falling f)
        {
            Vector3 along = f.crown.rotation * Vector3.up;
            Vector3 middle = f.crown.position + along * (f.length * 0.4f);
            PlaceholderSfx.Play(SfxKind.Thud, middle, 0.9f, 10f);
            // A log must not appear round the player's head.
            var head = Camera.main;
            if (head != null)
            {
                Vector3 to = head.transform.position - f.crown.position;
                float t = Mathf.Clamp(Vector3.Dot(to, along), 0f, f.length * 0.8f);
                if ((to - along * t).magnitude < f.radius + 1.2f) return;
            }
            var log = f.crown.gameObject.AddComponent<CapsuleCollider>();
            log.direction = 1;
            log.radius = f.radius * 0.9f;
            log.height = f.length * 0.8f;
            log.center = new Vector3(0f, f.length * 0.4f, 0f);
        }

        // One shared burst of needles, so a hundred trees cost no more than one particle system.
        private void Shed(Vector3 at, float spread)
        {
            if (leaves == null)
            {
                var go = new GameObject("BlastLeaves");
                go.transform.SetParent(transform, false);
                leaves = go.AddComponent<ParticleSystem>();
                leaves.Stop();
                var main = leaves.main;
                main.playOnAwake = false;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 500;
                main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.5f);
                main.startSize = 0.2f;
                main.startRotation3D = true;
                main.gravityModifier = 0.25f;
                var emission = leaves.emission;
                emission.enabled = false;
                var shape = leaves.shape;
                shape.enabled = false;
                var renderer = go.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Mesh;
                renderer.mesh = cutting.LeafMesh;
                renderer.sharedMaterial = cutting.Leaf;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                leaves.Play();
            }
            for (int i = 0; i < 8; i++)
            {
                var emit = new ParticleSystem.EmitParams
                {
                    position = at + Random.insideUnitSphere * Mathf.Max(1f, spread),
                    velocity = Random.insideUnitSphere * 2f,
                    rotation3D = Random.insideUnitSphere * 180f
                };
                leaves.Emit(emit, 1);
            }
        }
    }
}
