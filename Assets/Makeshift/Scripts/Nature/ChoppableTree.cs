using System.Collections;
using System.Collections.Generic;
using Makeshift.Audio;
using Makeshift.Core;
using Makeshift.Engine;
using Makeshift.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Makeshift.Nature
{
    /// <summary>
    /// A standing tree an axe can bring down. Each bite carves a bigger notch, to 60% of the way
    /// through; the last one snaps what is left. The tree creaks, tips towards the cut, sheds leaves, hits the ground and stays there.
    ///
    /// The fall is scripted, not simulated: a fourteen metre rigidbody next to a player is far too ready to fling them,
    /// and it costs more on the headset. While it tips it looks for the ground, rocks and other trees along its length,
    /// glances off slight knocks and stops on solid ones. The fall always ends (time and lean are capped), and a resting
    /// tree runs nothing at all. The model is only cut in two at the first bite.
    /// </summary>
    public class ChoppableTree : MonoBehaviour
    {
        public static readonly List<ChoppableTree> All = new List<ChoppableTree>();

        [SerializeField] private int hitsToFell = 5;
        [Tooltip("A tree at least this tall, in metres, is a large one.")]
        [SerializeField] private float largeHeight = 15f;
        [SerializeField] private int largeHitsToFell = 8;
        [SerializeField] private LODGroup lodGroup;
        [Tooltip("The model's renderers, closest detail first.")]
        [SerializeField] private MeshRenderer[] renderers;
        [Tooltip("Readable copies of the same models, in the same order.")]
        [SerializeField] private Mesh[] meshes;
        [Tooltip("Which submesh is bark; the others are leaves.")]
        [SerializeField] private int trunkSubmesh;
        [SerializeField] private Material woodMaterial;
        [SerializeField] private Material chipMaterial;
        [SerializeField] private Material leafMaterial;
        [SerializeField] private Mesh leafMesh;

        private const float MinBite = 0.25f, MaxBite = 1.9f; // heights above the ground an axe can touch the trunk
        private const float CutHeight = 1f;                  // every tree is cut here, about a player's waist
        private const float CutReach = 0.35f;               // how far above or below that a swing still cuts
        // Half height of the carved piece, metres. Taller than the widest notch, so the notch never opens the joints.
        private const float BandHalf = 0.42f;
        // How far through the trunk the notch reaches after each hit. The rest is what snaps.
        private static readonly float[] Through = { 0.18f, 0.29f, 0.39f, 0.5f, 0.6f };
        private const float Splinter = 0f; // TEST: flat caps to see if the splinter spikes cause the seam

        private const float MaxFallSeconds = 12f;  // the fall ends by now whatever happens
        private const float MaxLean = 125f;        // degrees; only a steep drop lets it go this far
        private const float LeanStep = 0.75f;      // the furthest it moves between two collision checks, degrees
        private const float MaxGlance = 25f;       // how far knocks may turn the fall in all, degrees
        private const float GroundFrom = 35f;      // leans below this never meet the ground
        private static readonly Collider[] Touching = new Collider[24];
        private readonly HashSet<Collider> ignored = new HashSet<Collider>(); // already in contact while upright

        public enum Phase { Standing, Falling, Resting }

        public int Hits { get; private set; }
        public int HitsToFell => hitsToFell;
        public Phase State { get; private set; }
        public bool Felled => State != Phase.Standing;
        /// <summary>The fallen tree has come to rest.</summary>
        public bool Landed => State == Phase.Resting;
        /// <summary>The rock or tree holding the fallen trunk up. Null when it lies on the ground.</summary>
        public Collider RestingOn { get; private set; }
        /// <summary>Trunk radius at chest height, metres.</summary>
        public float Radius { get; private set; }
        /// <summary>The part that falls, once the first bite has split the model.</summary>
        public Transform Crown => crown;
        /// <summary>Flat direction the tree falls, towards the side of the first bite.</summary>
        public Vector3 FallDirection { get; private set; }

        private Vector3 axis;       // a point on the trunk's middle line at chest height
        private float ground;
        private BoxCollider authored;
        private CapsuleCollider trunk;

        private Transform stump, crown;
        private MeshRenderer stumpCut, crownCut;
        private TreeMesh.Ring low, high;
        private TreeMesh.Notch notch;
        private Vector3 cutPoint;   // world, on the trunk's middle line at cut height
        private Vector3 hinge;
        private float height;       // from the hinge to the tip, metres
        private ParticleSystem leaves, chips;
        private Vector3[] leafPoints; // on the crown, relative to the hinge while upright
        private float[] spread;       // leaf radius per metre above the hinge
        private Coroutine shake;

        private float Scale => renderers[0].transform.lossyScale.y;

        /// <summary>The wrist menu's tree cutting switch. Off, axe bites do nothing.</summary>
        public static bool CuttingOn = true;

        private void OnEnable()
        {
            All.Add(this);
            if (crown != null) crown.gameObject.SetActive(true);
        }

        private void OnDisable()
        {
            All.Remove(this);
            // The fallen part is not a child, so it follows the tree when its patch of forest is switched off.
            if (crown != null) crown.gameObject.SetActive(false);
        }

        /// <summary>Sets up a tree turned choppable during play (<see cref="TreeCutting"/>).</summary>
        public void Configure(LODGroup group, MeshRenderer[] models, Mesh[] readable, int bark,
            Material wood, Material chip, Material leaf, Mesh leafShape)
        {
            lodGroup = group;
            renderers = models;
            meshes = readable;
            trunkSubmesh = bark;
            woodMaterial = wood;
            chipMaterial = chip;
            leafMaterial = leaf;
            leafMesh = leafShape;
            Init();
        }

        private void Start() => Init();

        // Runs once: at the start for a tree set up in the scene, at once for one prepared by an axe swing.
        private void Init()
        {
            if (trunk != null || renderers == null || renderers.Length == 0) return;
            if (meshes[0].bounds.size.y * Scale >= largeHeight) hitsToFell = largeHitsToFell;
            ground = GroundAt(transform.position, transform.position.y);
            var model = renderers[0].transform;
            float chest = model.InverseTransformPoint(new Vector3(transform.position.x, ground + 1f, transform.position.z)).y;
            var ring = TreeMesh.Profile(meshes[0], trunkSubmesh, chest, 1.2f / Scale);
            axis = model.TransformPoint(ring.centre);
            Radius = ring.MeanRadius * Scale;

            // The pack's thin square post is swapped for a round collider the size of the drawn trunk.
            authored = GetComponent<BoxCollider>();
            if (authored != null) authored.enabled = false;
            var solid = new GameObject("TrunkCollider");
            solid.transform.position = new Vector3(axis.x, ground, axis.z);
            solid.transform.SetParent(transform, true);
            trunk = solid.AddComponent<CapsuleCollider>();
            trunk.radius = Radius * 0.92f;
            // As tall as the pack's post, so another tree falling this way meets it high up too.
            float tall = authored != null
                ? Mathf.Max(3f, transform.TransformPoint(authored.center + Vector3.up * authored.size.y * 0.5f).y - ground)
                : 9f;
            trunk.height = tall;
            trunk.center = new Vector3(0f, tall * 0.5f, 0f);
        }

        /// <summary>True when a point is within <paramref name="reach"/> of the bark, at a height an axe can work on.</summary>
        public bool Near(Vector3 point, float reach)
        {
            float up = point.y - ground;
            if (up < MinBite || up > MaxBite) return false;
            return new Vector2(point.x - axis.x, point.z - axis.z).magnitude - Radius <= reach;
        }

        /// <summary>Brings down a tree that is already notched, at once.</summary>
        public void Fell()
        {
            if (Felled || Hits == 0) return;
            Hits = hitsToFell;
            State = Phase.Falling;
            if (shake != null) StopCoroutine(shake);
            StartCoroutine(Fall());
        }

        /// <summary>Gives up a tree no axe has marked, so that something else can bring it down.</summary>
        public void Retire()
        {
            if (Hits > 0) return;
            if (trunk != null) Destroy(trunk.gameObject);
            enabled = false;
        }

        /// <summary>True when a point is level with the cut: higher or lower blows only bounce off.</summary>
        public bool AtCutHeight(Vector3 point) => Mathf.Abs(point.y - ground - CutHeight) <= CutReach;

        /// <summary>The flat direction from a point to the trunk's middle line.</summary>
        public Vector3 Toward(Vector3 point) => Vector3.ProjectOnPlane(axis - point, Vector3.up).normalized;

        /// <summary>The one entry point for axe bites. <paramref name="point"/> is where the head met the bark.</summary>
        public void Chop(Vector3 point)
        {
            if (!CuttingOn || Felled || !AtCutHeight(point)) return;
            Vector3 side = Vector3.ProjectOnPlane(point - axis, Vector3.up);
            side = side.sqrMagnitude > 0.0001f ? side.normalized : -transform.forward;
            if (Hits == 0) Split(side);
            Hits++;

            Vector3 face = cutPoint + side * Radius;
            PlaceholderSfx.Play(SfxKind.Chop, face, 1f, 4f);
            Chips(face, side, 14 + Hits * 8);
            ThrowChunk(face, side);
            ShedLeaves(4 + Hits * 3);

            if (Hits >= hitsToFell)
            {
                State = Phase.Falling;
                if (shake != null) StopCoroutine(shake);
                StartCoroutine(Fall());
                return;
            }
            Carve();
            if (shake != null) StopCoroutine(shake);
            shake = StartCoroutine(Shake(side));
        }

        // ---------- cutting ----------

        private void Split(Vector3 side)
        {
            var model = renderers[0].transform;
            float c = model.InverseTransformPoint(new Vector3(axis.x, ground + CutHeight, axis.z)).y;
            float band = BandHalf / Scale;
            low = TreeMesh.Profile(meshes[0], trunkSubmesh, c - band, 1.2f / Scale);
            high = TreeMesh.Profile(meshes[0], trunkSubmesh, c + band, 1.2f / Scale);
            cutPoint = model.TransformPoint(Vector3.Lerp(low.centre, high.centre, 0.5f));

            Vector3 local = model.InverseTransformDirection(side);
            local.y = 0f;
            notch = new TreeMesh.Notch { height = c, side = local.normalized };
            FallDirection = side;

            float radius = (low.MeanRadius + high.MeanRadius) * 0.5f * Scale;
            // It turns about the back of the finished notch, where the unbroken wood begins.
            hinge = cutPoint + side * radius * (1f - 2f * Through[Through.Length - 1]);

            stump = new GameObject(name + " stump").transform;
            stump.SetPositionAndRotation(transform.position, transform.rotation);
            stump.localScale = transform.lossyScale;
            stump.SetParent(transform.parent, true);
            stump.gameObject.AddComponent<LiveScenery>(); // its cut mesh changes, so it is never merged

            crown = new GameObject(name + " crown").transform;
            crown.position = hinge;
            var top = new GameObject("Model").transform;
            top.SetPositionAndRotation(transform.position, transform.rotation);
            top.localScale = transform.lossyScale;
            top.SetParent(crown, true);

            var switcher = FindAnyObjectByType<ToonLookSwitcher>();
            var authoredLods = lodGroup.GetLODs();
            var stumpLods = new LOD[renderers.Length];
            var crownLods = new LOD[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var source = renderers[i];
                if (TreeCutting.Instance != null) meshes[i] = TreeCutting.Instance.Fuller(meshes[i]);
                // The coarser models have no carved piece: they are simply cut at the notch.
                float below = i == 0 ? c - band : c;
                float above = i == 0 ? c + band : c;
                var foot = Piece(stump, source, "Stump" + i, TreeMesh.Slice(meshes[i], trunkSubmesh, below, c, false));
                var upper = Piece(top, source, "Top" + i, TreeMesh.Slice(meshes[i], trunkSubmesh, above, c, true));
                if (switcher != null)
                {
                    switcher.Follow(source, foot);
                    switcher.Follow(source, upper);
                }
                var forStump = new List<Renderer> { foot };
                var forCrown = new List<Renderer> { upper };
                if (i == 0)
                {
                    stumpCut = Piece(stump, source, "Cut", null);
                    crownCut = Piece(top, source, "Cut top", null);
                    var skin = new[] { source.sharedMaterials[trunkSubmesh], woodMaterial };
                    foreach (var cut in new[] { stumpCut, crownCut })
                    {
                        cut.sharedMaterials = skin;
                        if (switcher != null) switcher.Follow(source, cut, trunkSubmesh, woodMaterial);
                    }
                    forStump.Add(stumpCut);
                    forCrown.Add(crownCut);
                }
                // The coarsest cut model lasts as far as the whole tree was drawn, flat far level included.
                int level = i == renderers.Length - 1 ? authoredLods.Length - 1 : Mathf.Min(i, authoredLods.Length - 1);
                float transition = authoredLods[level].screenRelativeTransitionHeight;
                stumpLods[i] = new LOD(transition, forStump.ToArray());
                crownLods[i] = new LOD(transition, forCrown.ToArray());
            }
            Group(stump.gameObject, stumpLods);
            Group(top.gameObject, crownLods);

            ReadCrown();

            var chunked = FindAnyObjectByType<ChunkedScenery>();
            if (chunked != null) chunked.Release(lodGroup); // a welded square is rebuilt without this tree
            lodGroup.enabled = false;
            foreach (var lod in authoredLods)
                foreach (var r in lod.renderers)
                    if (r != null) r.gameObject.SetActive(false);
            if (switcher != null) switcher.Withdraw(lodGroup); // the standing tree was drawn by the scenery merge

            leaves = Particles("TreeLeaves", leafMaterial, leafMesh, 0.17f, new Vector2(4.5f, 7f), 0.09f, 900);
            var noise = leaves.noise;
            noise.enabled = true;
            noise.strength = 0.55f;
            noise.frequency = 0.5f;
            var floor = new GameObject("LeafFloor").transform;
            floor.position = new Vector3(hinge.x, GroundAt(hinge + FallDirection * height * 0.4f, ground) + 0.04f, hinge.z);
            floor.SetParent(leaves.transform, true);
            var landing = leaves.collision;
            landing.enabled = true;
            landing.type = ParticleSystemCollisionType.Planes;
            landing.AddPlane(floor);
            landing.bounce = 0f;
            landing.dampen = 1f;
            chips = Particles("TreeChips", chipMaterial, CubeMesh(), 0.045f, new Vector2(0.6f, 1.3f), 1f, 160);
        }

        private MeshRenderer Piece(Transform parent, MeshRenderer source, string pieceName, Mesh mesh)
        {
            var go = new GameObject(pieceName);
            go.layer = source.gameObject.layer;
            go.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            go.transform.localScale = source.transform.lossyScale; // world size, set while it has no parent
            go.transform.SetParent(parent, true);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = source.sharedMaterials;
            r.shadowCastingMode = source.shadowCastingMode;
            r.receiveShadows = source.receiveShadows;
            return r;
        }

        private void Group(GameObject go, LOD[] lods)
        {
            var group = go.AddComponent<LODGroup>();
            group.SetLODs(lods);
            group.RecalculateBounds();
            // Same size as the whole tree, so the stump does not vanish at a short distance.
            group.size = lodGroup.size;
        }

        // How far along the notch is after a bite, as a place in Through: the same range however many bites it takes.
        private float Progress(int hit) =>
            Mathf.Clamp01((hit - 1f) / Mathf.Max(1, hitsToFell - 1)) * (Through.Length - 1);

        private void Carve()
        {
            float radius = (low.MeanRadius + high.MeanRadius) * 0.5f;
            float step = Progress(Hits);
            int whole = Mathf.Min((int)step, Through.Length - 2);
            notch.depth = radius * 2f * Mathf.Lerp(Through[whole], Through[whole + 1], step - whole);
            notch.halfHeight = (0.06f + 0.045f * (step + 1f)) / Scale;
            float band = BandHalf / Scale;
            Replace(stumpCut, TreeMesh.Section(meshes[0], trunkSubmesh, low, high, notch.height - band, notch.height + band, notch, false, false, 0f));
        }

        private static void Replace(MeshRenderer r, Mesh mesh)
        {
            var filter = r.GetComponent<MeshFilter>();
            if (filter.sharedMesh != null) Destroy(filter.sharedMesh);
            filter.sharedMesh = mesh;
        }

        /// <summary>Where the leaves are: for shedding them, and for knowing how the fallen crown rests.</summary>
        private void ReadCrown()
        {
            var model = renderers[0].transform;
            var v = meshes[0].vertices;
            var points = new List<Vector3>();
            float tip = 1f;
            for (int s = 0; s < meshes[0].subMeshCount; s++)
            {
                var tris = meshes[0].GetTriangles(s);
                for (int i = 0; i < tris.Length; i++)
                {
                    Vector3 p = model.TransformPoint(v[tris[i]]) - hinge;
                    tip = Mathf.Max(tip, p.y);
                    if (s != trunkSubmesh && p.y > 0.8f && i % 5 == 0) points.Add(p);
                }
            }
            height = tip;
            leafPoints = points.ToArray();
            spread = new float[Mathf.CeilToInt(height) + 1];
            foreach (var p in leafPoints)
            {
                int bin = Mathf.Clamp((int)p.y, 0, spread.Length - 1);
                spread[bin] = Mathf.Max(spread[bin], new Vector2(p.x + hinge.x - cutPoint.x, p.z + hinge.z - cutPoint.z).magnitude);
            }
        }

        private IEnumerator Shake(Vector3 side)
        {
            Vector3 about = Vector3.Cross(Vector3.up, -side);
            float lean = 0.12f * (Progress(Hits) + 1f);
            for (float t = 0f; t < 0.7f; t += Time.deltaTime)
            {
                float a = Mathf.Sin(t * 28f) * Mathf.Exp(-t * 6f) * lean;
                crown.rotation = Quaternion.AngleAxis(a, about);
                yield return null;
            }
            crown.rotation = Quaternion.identity;
        }

        // ---------- falling ----------

        private IEnumerator Fall()
        {
            // Broken through: both halves get a splintered end.
            float band = BandHalf / Scale;
            notch.depth = (low.MeanRadius + high.MeanRadius) * Through[Through.Length - 1];
            notch.halfHeight = (0.06f + 0.045f * (Progress(hitsToFell - 1) + 1f)) / Scale;
            Replace(stumpCut, TreeMesh.Section(meshes[0], trunkSubmesh, low, high, notch.height - band, notch.height, notch, true, false, Splinter / Scale));
            Replace(crownCut, TreeMesh.Section(meshes[0], trunkSubmesh, low, high, notch.height, notch.height + band, notch, false, true, Splinter / Scale));
            crown.rotation = Quaternion.identity;

            if (trunk != null)
            {
                float top = cutPoint.y - ground;
                trunk.height = Mathf.Max(top, trunk.radius * 2f);
                trunk.center = new Vector3(0f, top * 0.5f, 0f);
            }

            Vector3 about = Vector3.Cross(Vector3.up, FallDirection).normalized;
            float glance = 0f; // degrees the fall has been knocked round from where it set off

            void Pose(float lean, float round)
            {
                crown.SetPositionAndRotation(Pivot(lean, round), Lean(about, lean, round));
            }

            // Whatever already touches the standing tree cannot stop it.
            Physics.SyncTransforms();
            ignored.Clear();
            foreach (float lean in new[] { 0f, 2.5f })
            {
                // One stretch of trunk reports at a time; ignored ones drop out, so ask until none are left.
                int n;
                while ((n = Overlaps(about, lean, 0f)) > 0)
                    while (n > 0) ignored.Add(Touching[--n]);
            }

            // Giving way: the last fibres creak and snap before anything much moves.
            PlaceholderSfx.Play(SfxKind.Creak, cutPoint, 1f, 8f);
            const float giving = 1.5f;
            float nextSnap = 0.25f;
            for (float t = 0f; t < giving; t += Time.deltaTime)
            {
                float k = t / giving;
                Pose(2.5f * k * k + Mathf.Sin(t * 23f) * 0.2f * (1f - k), glance);
                if (t >= nextSnap)
                {
                    nextSnap += Random.Range(0.3f, 0.55f);
                    PlaceholderSfx.Play(SfxKind.Snap, cutPoint, 0.8f, 6f);
                    Chips(cutPoint + FallDirection * -Radius, -FallDirection, 6);
                    ShedLeaves(8);
                }
                yield return null;
            }

            // Tipping: slow at first, then faster as the weight goes over, as an upright pole does.
            float angle = 2.5f, speed = 3f; // degrees, degrees per second
            float pull = Mathf.Rad2Deg * 1.5f * 9.81f / height * 0.9f;
            float[] cracks = { 4f, 9f, 17f, 34f };
            int crack = 0;
            float shed = 0f;
            bool creaked = false, stopped = false;
            Collider brushed = null;
            for (float elapsed = 0f; !stopped && elapsed < MaxFallSeconds; elapsed += Time.deltaTime)
            {
                float dt = Mathf.Min(Time.deltaTime, 0.033f);
                speed += pull * Mathf.Sin(angle * Mathf.Deg2Rad + 0.03f) * dt;
                float target = angle + speed * dt;
                // In small steps, so a fast crown cannot pass through a thin trunk between two frames.
                while (!stopped && angle < target)
                {
                    float next = Mathf.Min(target, angle + LeanStep, MaxLean);
                    if (next >= GroundFrom && MeetsGround(about, next, glance))
                    {
                        stopped = true;
                        break;
                    }
                    var hit = Obstacle(about, next, glance);
                    if (hit != null)
                    {
                        if (!Glance(about, next, ref glance))
                        {
                            RestingOn = hit;
                            stopped = true;
                            break;
                        }
                        if (hit != brushed)
                        {
                            brushed = hit;
                            Vector3 at = Pivot(next, glance) + Lean(about, next, glance) * Vector3.up * height * 0.6f;
                            PlaceholderSfx.Play(SfxKind.Snap, at, 1f, 8f);
                            ShedLeaves(40);
                            speed *= 0.8f;
                        }
                    }
                    angle = next;
                    if (angle >= MaxLean) stopped = true;
                }
                Pose(angle, glance);
                if (crack < cracks.Length && angle >= cracks[crack])
                {
                    crack++;
                    PlaceholderSfx.Play(SfxKind.Snap, Pivot(angle, glance) + crown.up * crack, 1f, 8f);
                }
                if (!creaked && angle > 7f)
                {
                    creaked = true;
                    PlaceholderSfx.Play(SfxKind.Creak, cutPoint, 0.8f, 8f);
                }
                shed += Mathf.Lerp(30f, 140f, angle / 90f) * dt;
                int now = (int)shed;
                shed -= now;
                ShedLeaves(now);
                yield return null;
            }

            // The blow, as hard as the tree was moving.
            float force = Mathf.Clamp01(speed / 40f);

            // Trees must reach the ground: one that stopped on a rock, on another tree or on its stump slides off.
            FindLie(glance, out Vector3 butt, out float flatAngle, out float turn);
            Vector3 from = Pivot(angle, glance);
            float fromAngle = angle, fromGlance = glance;
            float far = Mathf.Max(Mathf.Abs(flatAngle - angle) / 40f, Vector3.Distance(from, butt) / 1.8f,
                Mathf.Abs(turn - glance) / 60f);
            if (far > 0.05f)
            {
                if (RestingOn != null) PlaceholderSfx.Play(SfxKind.Creak, crown.position + crown.up * height * 0.5f, 1f, 12f);
                float takes = Mathf.Clamp(far, 0.25f, 1.6f);
                for (float t = 0f; t < takes; t += Time.deltaTime)
                {
                    float k = t / takes;
                    k *= k; // gathering speed, as something sliding free does
                    settled = Vector3.Lerp(from, butt, k);
                    Pose(angle = Mathf.Lerp(fromAngle, flatAngle, k), glance = Mathf.Lerp(fromGlance, turn, k));
                    yield return null;
                }
                force = Mathf.Max(force, 0.5f);
            }
            settled = butt;
            angle = flatAngle;
            glance = turn;
            Pose(angle, glance);
            RestingOn = null;

            Vector3 strike = crown.position + crown.up * height * 0.5f;
            PlaceholderSfx.Play(SfxKind.Thud, strike, Mathf.Lerp(0.35f, 1f, force), 14f);
            PlaceholderSfx.Play(SfxKind.Snap, strike, 1f, 10f);
            BurstLeaves((int)Mathf.Lerp(220f, 450f, force), Mathf.Lerp(0.7f, 1f, force));
            foreach (var (lift, time) in new[] { (2.2f, 0.5f), (0.8f, 0.32f), (0.25f, 0.22f) })
            {
                for (float t = 0f; t < time; t += Time.deltaTime)
                {
                    Pose(angle - Mathf.Sin(Mathf.PI * t / time) * Mathf.Min(lift * force, angle), glance);
                    yield return null;
                }
                Pose(angle, glance);
                ShedLeaves((int)(lift * force * 12f));
            }
            Rest();
        }

        /// <summary>The end of it: the trunk turns solid where it lies and nothing is checked or moved again.</summary>
        private void Rest()
        {
            State = Phase.Resting;

            // Solid only now that it has stopped, so nothing is flung on the way down.
            var lying = new GameObject("LogCollider");
            lying.transform.SetParent(crown, false);
            var log = lying.AddComponent<CapsuleCollider>();
            log.radius = Radius * 0.8f;
            log.height = height * 0.7f;
            log.center = new Vector3(cutPoint.x - hinge.x, height * 0.35f + 0.2f, cutPoint.z - hinge.z);
            StartCoroutine(LetThePlayerOut(log));

            // The last leaves and chips finish falling, then their systems go.
            if (leaves != null) Destroy(leaves.gameObject, 8f);
            if (chips != null) Destroy(chips.gameObject, 3f);
            leaves = chips = null;
            leafPoints = null;
            ignored.Clear();
        }

        /// <summary>
        /// The tree falls towards whoever cut it. A log turning solid round them would throw them, so any part of the
        /// player caught in it passes through until it is clear. Checks end after a minute.
        /// </summary>
        private IEnumerator LetThePlayerOut(CapsuleCollider log)
        {
            Vector3 middle = log.transform.TransformPoint(log.center);
            Vector3 half = log.transform.up * Mathf.Max(0f, log.height * 0.5f - log.radius);
            var caught = new HashSet<Collider>();
            foreach (var c in Physics.OverlapCapsule(middle - half, middle + half, log.radius + 0.3f, ~0, QueryTriggerInteraction.Ignore))
                if (HandCheck.PlayerBody(c.attachedRigidbody))
                {
                    Physics.IgnoreCollision(log, c, true);
                    caught.Add(c);
                }

            var wait = new WaitForSeconds(0.5f);
            for (int check = 0; check < 120 && caught.Count > 0; check++)
            {
                yield return wait;
                var still = new HashSet<Collider>(Physics.OverlapCapsule(middle - half, middle + half, log.radius + 0.05f, ~0, QueryTriggerInteraction.Ignore));
                caught.RemoveWhere(c =>
                {
                    if (c == null) return true;
                    if (still.Contains(c)) return false;
                    Physics.IgnoreCollision(log, c, false);
                    return true;
                });
            }
        }

        private static Quaternion Lean(Vector3 about, float angle, float glance) =>
            Quaternion.AngleAxis(angle, Quaternion.AngleAxis(glance, Vector3.up) * about);

        /// <summary>The butt rides the stump for the first part of the fall, then slips off to the ground.</summary>
        private Vector3 Pivot(float angle, float glance)
        {
            if (settled.HasValue) return settled.Value; // sliding to, or lying at, its resting place

            Vector3 rest = hinge + Quaternion.AngleAxis(glance, Vector3.up) * FallDirection * 0.45f;
            rest.y = GroundAt(rest, ground) + Radius * 0.9f;
            return Vector3.Lerp(hinge, rest, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(48f, 80f, angle)));
        }

        private Vector3? settled;

        // The lying pose nearest to the way it was going: straight on if that line is clear, else turned to one side or
        // the other until it is; if nowhere is clear, the line with the least in it.
        private void FindLie(float glance, out Vector3 butt, out float angle, out float turn)
        {
            Vector3 about = Vector3.Cross(Vector3.up, FallDirection).normalized;
            int least = int.MaxValue;
            butt = default;
            angle = 90f;
            turn = glance;
            for (int step = 0; step <= 20; step++)
            {
                float tried = glance + ((step + 1) / 2) * 9f * (step % 2 == 0 ? -1f : 1f);
                Vector3 flat = Quaternion.AngleAxis(tried, Vector3.up) * FallDirection;
                TreeRest.Lie(hinge, flat, height, Radius, out var at, out float lean);
                var before = settled;
                settled = at;
                int inTheWay = Overlaps(about, lean, tried);
                settled = before;
                if (inTheWay >= least) continue;
                least = inTheWay;
                butt = at;
                angle = lean;
                turn = tried;
                if (inTheWay == 0) return;
            }
        }

        private float LeafRadius(float up) => spread[Mathf.Clamp((int)up, 0, spread.Length - 1)];

        /// <summary>True when the trunk, held up by its own crushed branches, has reached the ground at this lean.</summary>
        private bool MeetsGround(Vector3 about, float angle, float glance)
        {
            Vector3 up = Lean(about, angle, glance) * Vector3.up;
            Vector3 from = Pivot(angle, glance);
            for (float s = height * 0.3f; s <= height; s += 0.5f)
            {
                Vector3 p = from + up * s;
                float clear = s > height - 1f ? 0.1f : Radius * 0.6f + LeafRadius(s) * 0.3f;
                if (p.y < GroundAt(p, ground) + clear) return true;
            }
            return false;
        }

        /// <summary>The first solid thing in the way at this lean, or null.</summary>
        private Collider Obstacle(Vector3 about, float angle, float glance) =>
            Overlaps(about, angle, glance) > 0 ? Touching[0] : null;

        /// <summary>
        /// Fills <see cref="Touching"/> with the fixed scenery the trunk would overlap at this lean and returns how many.
        /// Leaves give way, so only the trunk and the thick of the branches count. Loose things and the player are passed
        /// over; the terrain is judged by height in <see cref="MeetsGround"/>.
        /// </summary>
        private int Overlaps(Vector3 about, float angle, float glance)
        {
            Vector3 up = Lean(about, angle, glance) * Vector3.up;
            Vector3 from = Pivot(angle, glance);
            const float length = 1.4f;
            float end = height - 0.5f;
            for (float s = 1.2f; s < end; s += length)
            {
                float thick = Radius * 0.7f + LeafRadius(s + length * 0.5f) * 0.12f;
                int found = Physics.OverlapCapsuleNonAlloc(from + up * s, from + up * Mathf.Min(s + length, end), thick,
                    Touching, ~0, QueryTriggerInteraction.Ignore);
                int kept = 0;
                for (int i = 0; i < found; i++)
                {
                    var c = Touching[i];
                    if (c.attachedRigidbody != null || c is TerrainCollider || ignored.Contains(c)) continue;
                    var t = c.transform;
                    if (t.IsChildOf(transform) || t.IsChildOf(stump) || t.IsChildOf(crown)) continue;
                    Touching[kept++] = c;
                }
                if (kept > 0) return kept;
            }
            return 0;
        }

        /// <summary>A knock off the side of something: turns the fall a little if that gets the trunk past.</summary>
        private bool Glance(Vector3 about, float angle, ref float glance)
        {
            for (float turn = 1.5f; turn <= 3f; turn += 1.5f)
                for (int side = -1; side <= 1; side += 2)
                {
                    float tried = glance + side * turn;
                    if (Mathf.Abs(tried) > MaxGlance) continue;
                    if (angle >= GroundFrom && MeetsGround(about, angle, tried)) continue;
                    if (Obstacle(about, angle, tried) != null) continue;
                    glance = tried;
                    return true;
                }
            return false;
        }

        // ---------- effects ----------

        /// <summary>
        /// The crown hitting: a cloud of leaves is thrown up and out from the whole length of it, then drifts down.
        /// </summary>
        private void BurstLeaves(int count, float strength)
        {
            if (leaves == null || leafPoints == null || leafPoints.Length == 0) return;
            Quaternion turn = crown.rotation;
            Vector3 along = turn * Vector3.up;
            float floor = GroundAt(crown.position + along * height * 0.5f, ground);
            for (int i = 0; i < count; i++)
            {
                Vector3 at = crown.position + turn * leafPoints[Random.Range(0, leafPoints.Length)];
                at.y = Mathf.Max(at.y, floor + Random.Range(0.1f, 0.5f)); // out of the ground, not under it
                // Away from the trunk's line, flat, so the cloud rolls out to both sides.
                Vector3 out_ = Vector3.ProjectOnPlane(at - crown.position, along);
                out_.y = 0f;
                out_ = out_.sqrMagnitude > 0.01f ? out_.normalized : Random.insideUnitSphere;
                var e = new ParticleSystem.EmitParams
                {
                    position = at,
                    velocity = (out_ * Random.Range(0.6f, 3f) + Vector3.up * Random.Range(0.8f, 3f)
                                + Random.insideUnitSphere * 0.8f) * strength,
                    startSize = Random.Range(0.2f, 0.38f),
                    rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f)),
                };
                leaves.Emit(e, 1);
            }
        }

        private void ShedLeaves(int count)
        {
            if (leaves == null || leafPoints == null || leafPoints.Length == 0) return;
            Quaternion turn = crown.rotation;
            for (int i = 0; i < count; i++)
            {
                var e = new ParticleSystem.EmitParams
                {
                    position = crown.position + turn * leafPoints[Random.Range(0, leafPoints.Length)],
                    velocity = Random.insideUnitSphere * 0.7f + Vector3.up * 0.2f,
                    rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f)),
                };
                // While the tree still stands only the low branches are in view.
                if (!Felled && e.position.y > hinge.y + 5f) e.position = new Vector3(e.position.x, hinge.y + Random.Range(2.5f, 5f), e.position.z);
                leaves.Emit(e, 1);
            }
        }

        private void Chips(Vector3 at, Vector3 side, int count)
        {
            if (chips == null) return;
            for (int i = 0; i < count; i++)
            {
                var e = new ParticleSystem.EmitParams
                {
                    position = at + Random.insideUnitSphere * 0.04f,
                    velocity = side * Random.Range(0.4f, 1.8f) + Random.insideUnitSphere * 1.1f + Vector3.up * Random.Range(0.3f, 1.5f),
                    startSize = Random.Range(0.012f, 0.045f),
                    rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f)),
                };
                chips.Emit(e, 1);
            }
        }

        /// <summary>A wedge of wood knocked out of the notch, bigger with every bite. It lies where it lands.</summary>
        private void ThrowChunk(Vector3 at, Vector3 side)
        {
            var chunk = Chunks.Take(40f);
            float size = 0.035f + 0.018f * Hits;
            chunk.transform.position = at + side * (size * 0.8f + 0.03f);
            chunk.transform.rotation = Random.rotation;
            chunk.transform.localScale = new Vector3(size * 1.5f, size * 0.55f, size);
            chunk.GetComponent<Renderer>().sharedMaterial = woodMaterial;
            chunk.SetActive(true);
            var body = chunk.GetComponent<Rigidbody>();
            body.isKinematic = false; // a reused chunk may have been frozen by the physics bubble
            body.position = chunk.transform.position;
            body.rotation = chunk.transform.rotation;
            body.linearVelocity = side * Random.Range(0.6f, 1.2f) + Vector3.up * 0.8f + Random.insideUnitSphere * 0.4f;
            body.angularVelocity = Random.insideUnitSphere * 8f;
        }

        /// <summary>Every tree shares these, so any amount of chopping leaves at most this many chunks lying about.</summary>
        private static readonly CappedPool Chunks = new CappedPool(12, MakeChunk);

        private static GameObject MakeChunk()
        {
            var chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chunk.name = "WoodChunk";
            chunk.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var body = chunk.AddComponent<Rigidbody>();
            body.mass = 0.08f;
            body.angularDamping = 2f;
            return chunk;
        }

        /// <summary>A particle system that only emits when told to.</summary>
        private ParticleSystem Particles(string systemName, Material material, Mesh mesh, float size, Vector2 life, float gravity, int max)
        {
            var go = new GameObject(systemName);
            go.transform.position = cutPoint;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            float unit = mesh != null ? Mathf.Max(0.001f, Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.y, mesh.bounds.size.z)) : 1f;
            var main = ps.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f / unit, size * 1.2f / unit);
            main.startRotation3D = true;
            main.gravityModifier = gravity;
            main.maxParticles = max;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.separateAxes = true;
            spin.x = new ParticleSystem.MinMaxCurve(-3f, 3f);
            spin.y = new ParticleSystem.MinMaxCurve(-3f, 3f);
            spin.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
            var draw = go.GetComponent<ParticleSystemRenderer>();
            draw.renderMode = ParticleSystemRenderMode.Mesh;
            draw.mesh = mesh;
            draw.sharedMaterial = material;
            draw.alignment = ParticleSystemRenderSpace.World;
            draw.enableGPUInstancing = true;
            draw.shadowCastingMode = ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        private static Mesh CubeMesh()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mesh = cube.GetComponent<MeshFilter>().sharedMesh;
            Destroy(cube);
            return mesh;
        }

        private static float GroundAt(Vector3 p, float otherwise)
        {
            var terrain = Terrain.activeTerrain;
            return terrain != null ? terrain.SampleHeight(p) + terrain.transform.position.y : otherwise;
        }
    }
}
