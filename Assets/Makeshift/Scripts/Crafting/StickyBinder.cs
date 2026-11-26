using System.Collections.Generic;
using Makeshift.Audio;
using Makeshift.Core;
using UnityEngine;

namespace Makeshift.Crafting
{
    /// <summary>
    /// The vine. Hold it to the joint of a glued weapon and its end sticks there. Carry the vine round the joint twice
    /// (move your hand round it, or twist the weapon in your other hand): the rope winds on as you go, then snaps tight
    /// into place and the joint is permanent. Pull too far away and it comes off again.
    ///
    /// The rope is a visual: a path wound round the surface of the weapon plus a free end that swings under gravity.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class StickyBinder : MonoBehaviour
    {
        [Tooltip("How close the vine has to come to the glued joint for its end to stick.")]
        [SerializeField] private float attachRange = 0.13f;
        [Tooltip("Turns round the joint needed to finish the lashing.")]
        [SerializeField] private float wrapsNeeded = 2f;
        [Tooltip("The end comes off if the vine is pulled this far from the joint.")]
        [SerializeField] private float detachRange = 1f;
        [Tooltip("Only works while it is in a hand. Switched off by the headless tests.")]
        [SerializeField] private bool requireHeld = true;
        [Tooltip("The vine is used up when it lashes something.")]
        [SerializeField] private bool consumeOnLash = true;
        [Header("Rope look")]
        [SerializeField] private Mesh ringMesh;
        [SerializeField] private Material ringMaterial;
        [SerializeField] private float ropeWidth = 0.012f;

        private const int Buckets = 24;          // directions round the joint where the surface is measured
        private const int FreePoints = 14;       // points in the swinging end
        private const float StepDegrees = 15f;
        private const float BandHalfWidth = 0.025f;

        public int BondCount { get; private set; }
        public GlueBond Target => target;
        /// <summary>0..1 towards the finished lashing.</summary>
        public float Progress01 => Mathf.Clamp01(Mathf.Abs(wound) / (360f * wrapsNeeded));

        private Rigidbody body;
        private GlueBond target;
        private Rigidbody keeper;
        // The wrap, in the keeper's local space.
        private Vector3 axis, centre, side, up;
        private readonly float[] radius = new float[Buckets];
        private float startAngle, lastAngle, wound;
        private bool hasAngle;
        private float nextCreak;
        private float noAttachUntil;

        private GameObject ropeRoot, coil;
        private LineRenderer rope;
        private readonly Vector3[] free = new Vector3[FreePoints];
        private readonly Vector3[] freeLast = new Vector3[FreePoints];
        private readonly List<Vector3> path = new List<Vector3>();
        private readonly List<Renderer> hidden = new List<Renderer>();
        private readonly List<(Collider, Collider)> ignored = new List<(Collider, Collider)>();

        private void Awake() { body = GetComponent<Rigidbody>(); }
        private void OnDestroy() { Detach(); }
        private void OnDisable() { Detach(); }

        private bool Held => !requireHeld || HandCheck.Held(body);

        /// <summary>Where the free end of the rope is: the holding hand, or the vine itself.</summary>
        private Vector3 EndPoint()
        {
            if (HandCheck.Held(body) && HandCheck.HoldPoint != null && HandCheck.HoldPoint(body) is Vector3 hand) return hand;
            return body.worldCenterOfMass;
        }

        private void FixedUpdate()
        {
            if (ReferenceEquals(target, null))
            {
                if (Held && Time.time >= noAttachUntil) TryAttach();
                return;
            }
            if (target == null || target.Lashed) { Detach(); return; } // broke apart, or another vine got there first

            Vector3 end = EndPoint();
            if (Vector3.Distance(end, target.JointWorld) > detachRange)
            {
                Detach();
                noAttachUntil = Time.time + 0.5f;
                return;
            }
            if (!Held) return;

            if (!AngleOf(end, out float angle)) { hasAngle = false; return; }
            if (hasAngle)
            {
                float step = Mathf.DeltaAngle(lastAngle, angle);
                // A jump this big means the hand passed through the middle: which way round is unknown.
                if (Mathf.Abs(step) < 150f) wound += step;
            }
            else if (wound == 0f) startAngle = angle;
            lastAngle = angle;
            hasAngle = true;

            if (Mathf.Abs(wound) > 60f && Time.time >= nextCreak)
            {
                nextCreak = Time.time + 0.6f;
                PlaceholderSfx.Play(SfxKind.Crack, target.JointWorld, 0.15f);
            }
            if (Progress01 >= 1f) SnapTight();
        }

        // ---------- attaching ----------

        private void TryAttach()
        {
            foreach (var bond in GlueBond.All)
            {
                if (bond == null || bond.Lashed || bond.gameObject == gameObject) continue;
                Vector3 joint = bond.JointWorld;
                foreach (var col in GetComponentsInChildren<Collider>())
                {
                    if (col.isTrigger) continue;
                    if (Vector3.Distance(col.ClosestPoint(joint), joint) <= attachRange) { Attach(bond); return; }
                }
            }
        }

        public void Attach(GlueBond bond)
        {
            if (bond == null || bond.Lashed) return;
            Detach();
            target = bond;
            keeper = bond.GetComponent<Rigidbody>();
            MeasureJoint();
            wound = 0f;
            hasAngle = AngleOf(EndPoint(), out lastAngle);
            startAngle = lastAngle;

            // The vine passes round the weapon instead of knocking it about.
            foreach (var mine in GetComponentsInChildren<Collider>())
            {
                foreach (var theirs in keeper.GetComponentsInChildren<Collider>())
                {
                    if (mine.isTrigger || theirs.isTrigger) continue;
                    Physics.IgnoreCollision(mine, theirs, true);
                    ignored.Add((mine, theirs));
                }
            }
            // From here on the rope stands for the vine.
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                r.enabled = false;
                hidden.Add(r);
            }
            ropeRoot = new GameObject("VineRope");
            rope = NewLine(ropeRoot, true);
            coil = BuildCoil(ropeRoot.transform);
            Vector3 a = keeper.transform.TransformPoint(Wrapped(0f, 0f));
            Vector3 b = EndPoint();
            for (int i = 0; i < FreePoints; i++) free[i] = freeLast[i] = Vector3.Lerp(a, b, i / (FreePoints - 1f));
            PlaceholderSfx.Play(SfxKind.Glop, bond.JointWorld, 0.6f);
        }

        private void Detach()
        {
            if (ReferenceEquals(target, null)) return;
            target = null;
            keeper = null;
            wound = 0f;
            hasAngle = false;
            if (ropeRoot != null) Destroy(ropeRoot);
            foreach (var r in hidden) if (r != null) r.enabled = true;
            hidden.Clear();
            foreach (var (mine, theirs) in ignored)
                if (mine != null && theirs != null && mine.enabled && theirs.enabled) Physics.IgnoreCollision(mine, theirs, false);
            ignored.Clear();
        }

        /// <summary>Finds the axis to wind round (the length of the stick) and how thick the weapon is at the joint.</summary>
        private void MeasureJoint()
        {
            Transform k = keeper.transform;
            Vector3 joint = target.JointLocal;
            axis = Vector3.right;
            Vector3 middle = Vector3.zero;
            float longest = 0f;
            foreach (var col in keeper.GetComponentsInChildren<Collider>())
            {
                if (col.isTrigger || (target.Part != null && col.transform.IsChildOf(target.Part))) continue;
                if (col is CapsuleCollider c && c.height > longest)
                {
                    longest = c.height;
                    Vector3 dir = c.direction == 0 ? Vector3.right : c.direction == 1 ? Vector3.up : Vector3.forward;
                    axis = k.InverseTransformDirection(c.transform.TransformDirection(dir)).normalized;
                    middle = k.InverseTransformPoint(c.transform.TransformPoint(c.center));
                }
                else if (col is BoxCollider b)
                {
                    Vector3 s = b.size;
                    float len = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
                    if (len <= longest) continue;
                    longest = len;
                    Vector3 dir = len == s.x ? Vector3.right : len == s.y ? Vector3.up : Vector3.forward;
                    axis = k.InverseTransformDirection(b.transform.TransformDirection(dir)).normalized;
                    middle = k.InverseTransformPoint(b.transform.TransformPoint(b.center));
                }
            }
            // The wrap sits where the joint is along the stick, pulled a little onto the stick.
            float along = Vector3.Dot(joint - middle, axis);
            along -= Mathf.Sign(along) * Mathf.Min(0.025f, Mathf.Abs(along));
            // A big head covers the joint: lash the stick just behind it, where the rope can pull tight.
            if (HeadEdge(middle, Mathf.Sign(along), out float edge))
            {
                float behind = edge - Mathf.Sign(along) * BandHalfWidth * 1.2f;
                if (Mathf.Abs(behind) < Mathf.Abs(along)) along = behind;
            }
            centre = middle + axis * along;
            side = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.forward).normalized;
            up = Vector3.Cross(axis, side).normalized;

            var cols = keeper.GetComponentsInChildren<Collider>();
            const float reach = 0.3f;
            var raw = new float[Buckets];
            for (int i = 0; i < Buckets; i++)
            {
                Vector3 dir = Direction(i * 360f / Buckets);
                Vector3 from = k.TransformPoint(centre + dir * reach);
                Vector3 to = k.TransformPoint(centre);
                var ray = new Ray(from, (to - from).normalized);
                float r = 0.022f;
                foreach (var col in cols)
                    if (!col.isTrigger && col.Raycast(ray, out var hit, reach)) r = Mathf.Max(r, reach - hit.distance);
                // The drawn shapes are often fatter than their colliders: the rope has to clear what can be seen.
                for (int band = -1; band <= 1; band++)
                    r = Mathf.Max(r, DrawnRadius(centre + axis * (band * BandHalfWidth), dir, reach));
                raw[i] = Mathf.Min(r, 0.13f) + ropeWidth * 0.6f;
            }
            // Rope pulled tight rounds off sharp steps.
            for (int i = 0; i < Buckets; i++)
                radius[i] = Mathf.Max(raw[i], (raw[(i + Buckets - 1) % Buckets] + raw[i] + raw[(i + 1) % Buckets]) / 3f);
        }

        /// <summary>The distance along the axis where the drawn glued-on piece begins, seen from the stick's middle.</summary>
        private bool HeadEdge(Vector3 middle, float towards, out float edge)
        {
            edge = 0f;
            if (target.Part == null) return false;
            float nearest = float.MaxValue;
            foreach (var filter in target.Part.GetComponentsInChildren<MeshFilter>())
            {
                var drawn = filter.GetComponent<MeshRenderer>();
                if (filter.sharedMesh == null || drawn == null || !drawn.enabled) continue;
                Bounds b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = keeper.transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    nearest = Mathf.Min(nearest, Vector3.Dot(local - middle, axis) * towards);
                }
            }
            if (nearest == float.MaxValue || nearest <= 0f) return false; // covers the middle: leave it at the joint
            edge = nearest * towards;
            return true;
        }

        /// <summary>How far out the weapon's visible surface is from a point on the axis, along one direction.</summary>
        private float DrawnRadius(Vector3 onAxis, Vector3 dir, float reach)
        {
            Transform k = keeper.transform;
            float best = 0f;
            foreach (var filter in keeper.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                var drawn = filter.GetComponent<MeshRenderer>();
                if (mesh == null || drawn == null || !drawn.enabled || !mesh.isReadable) continue;
                if (filter.name == "GlueBlob" || filter.name == "Lashing") continue;
                Matrix4x4 toKeeper = k.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var points = mesh.vertices;
                var corners = mesh.triangles;
                for (int i = 0; i + 2 < corners.Length; i += 3)
                {
                    Vector3 a = toKeeper.MultiplyPoint3x4(points[corners[i]]);
                    Vector3 e1 = toKeeper.MultiplyPoint3x4(points[corners[i + 1]]) - a;
                    Vector3 e2 = toKeeper.MultiplyPoint3x4(points[corners[i + 2]]) - a;
                    Vector3 p = Vector3.Cross(dir, e2);
                    float det = Vector3.Dot(e1, p);
                    if (Mathf.Abs(det) < 1e-10f) continue;
                    Vector3 s = onAxis - a;
                    float u = Vector3.Dot(s, p) / det;
                    if (u < 0f || u > 1f) continue;
                    Vector3 q = Vector3.Cross(s, e1);
                    float v = Vector3.Dot(dir, q) / det;
                    if (v < 0f || u + v > 1f) continue;
                    float t = Vector3.Dot(e2, q) / det;
                    if (t > best && t < reach) best = t;
                }
            }
            return best;
        }

        private Vector3 Direction(float degrees)
        {
            float a = degrees * Mathf.Deg2Rad;
            return side * Mathf.Cos(a) + up * Mathf.Sin(a);
        }

        private bool AngleOf(Vector3 world, out float degrees)
        {
            Vector3 p = keeper.transform.InverseTransformPoint(world) - centre;
            p -= axis * Vector3.Dot(p, axis);
            degrees = Mathf.Atan2(Vector3.Dot(p, up), Vector3.Dot(p, side)) * Mathf.Rad2Deg;
            return p.magnitude >= 0.025f;
        }

        /// <summary>A point on the wound rope (keeper space) after turning <paramref name="turned"/> degrees.</summary>
        private Vector3 Wrapped(float turned, float along)
        {
            float angle = startAngle + turned;
            float slot = Mathf.Repeat(angle, 360f) / 360f * Buckets;
            int i = (int)slot % Buckets;
            float r = Mathf.Lerp(radius[i], radius[(i + 1) % Buckets], slot - Mathf.Floor(slot));
            return centre + axis * along + Direction(angle) * r;
        }

        // ---------- drawing ----------

        private void LateUpdate()
        {
            if (target == null || keeper == null || rope == null) return;
            Transform k = keeper.transform;
            path.Clear();
            float total = 360f * wrapsNeeded;
            float turned = Mathf.Clamp(wound, -total, total);
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(turned) / StepDegrees));
            for (int s = 0; s <= steps; s++)
            {
                float t = turned * s / steps;
                float along = Mathf.Lerp(-BandHalfWidth, BandHalfWidth, Mathf.Abs(t) / total);
                path.Add(k.TransformPoint(Wrapped(t, along)));
            }
            SwingFreeEnd(path[path.Count - 1], EndPoint(), Mathf.Min(Time.deltaTime, 0.025f));
            for (int i = 1; i < FreePoints; i++) path.Add(free[i]);

            rope.positionCount = path.Count;
            for (int i = 0; i < path.Count; i++) rope.SetPosition(i, path[i]);
            if (coil != null) coil.transform.position = free[FreePoints - 1];
        }

        /// <summary>Verlet rope between the last wound point and the hand: sags, swings and settles.</summary>
        private void SwingFreeEnd(Vector3 a, Vector3 b, float dt)
        {
            for (int i = 1; i < FreePoints - 1; i++)
            {
                Vector3 now = free[i];
                free[i] += (now - freeLast[i]) * 0.92f + Physics.gravity * (dt * dt);
                freeLast[i] = now;
            }
            float segment = Mathf.Max(0.12f, Vector3.Distance(a, b) * 1.08f) / (FreePoints - 1);
            for (int pass = 0; pass < 8; pass++)
            {
                free[0] = a;
                free[FreePoints - 1] = b;
                for (int i = 0; i < FreePoints - 1; i++)
                {
                    Vector3 d = free[i + 1] - free[i];
                    float len = d.magnitude;
                    if (len <= segment || len < 0.0001f) continue; // rope pulls but never pushes
                    Vector3 fix = d * ((len - segment) / len * 0.5f);
                    free[i] += fix;
                    free[i + 1] -= fix;
                }
            }
            free[0] = a;
            free[FreePoints - 1] = b;
        }

        private LineRenderer NewLine(GameObject owner, bool worldSpace)
        {
            var line = owner.AddComponent<LineRenderer>();
            line.useWorldSpace = worldSpace;
            line.startWidth = line.endWidth = ropeWidth;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = ringMaterial;
            return line;
        }

        /// <summary>The loose loops of vine left in the hand.</summary>
        private GameObject BuildCoil(Transform parent)
        {
            var go = new GameObject("Coil");
            go.transform.SetParent(parent, false);
            if (ringMesh == null) return go;
            for (int i = 0; i < 2; i++)
            {
                var ring = new GameObject("Loop" + i);
                ring.transform.SetParent(go.transform, false);
                ring.transform.localRotation = Quaternion.Euler(70f + i * 25f, i * 60f, 0f);
                ring.AddComponent<MeshFilter>().sharedMesh = ringMesh;
                var mr = ring.AddComponent<MeshRenderer>();
                mr.sharedMaterial = ringMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return go;
        }

        // ---------- finishing ----------

        /// <summary>A solid round rope along a path, so the finished wrap reads from every side.</summary>
        private static Mesh Tube(Vector3[] along, float thickness, int sides)
        {
            var vertices = new Vector3[along.Length * sides];
            var normals = new Vector3[vertices.Length];
            var triangles = new int[(along.Length - 1) * sides * 6];
            for (int i = 0; i < along.Length; i++)
            {
                Vector3 forward = (along[Mathf.Min(i + 1, along.Length - 1)] - along[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 right = Vector3.Cross(forward, Mathf.Abs(forward.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                Vector3 top = Vector3.Cross(right, forward);
                for (int s = 0; s < sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides;
                    Vector3 n = right * Mathf.Cos(a) + top * Mathf.Sin(a);
                    vertices[i * sides + s] = along[i] + n * thickness;
                    normals[i * sides + s] = n;
                }
            }
            int k = 0;
            for (int i = 0; i < along.Length - 1; i++)
                for (int s = 0; s < sides; s++)
                {
                    int a = i * sides + s, b = i * sides + (s + 1) % sides, c = a + sides, d = b + sides;
                    triangles[k++] = a; triangles[k++] = b; triangles[k++] = c;
                    triangles[k++] = b; triangles[k++] = d; triangles[k++] = c;
                }
            var mesh = new Mesh { name = "Lashing", vertices = vertices, normals = normals, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>The wraps jump into their neat, tight, final place and the joint becomes permanent.</summary>
        private void SnapTight()
        {
            var bond = target;
            var lashing = new GameObject("Lashing");
            lashing.transform.SetParent(keeper.transform, false);
            const float turns = 4.5f;
            int count = Mathf.CeilToInt(turns * 360f / StepDegrees) + 1;
            var coilPath = new Vector3[count];
            float way = Mathf.Sign(wound);
            for (int i = 0; i < count; i++)
            {
                float t = i / (count - 1f);
                coilPath[i] = Wrapped(way * t * turns * 360f, Mathf.Lerp(-BandHalfWidth * 1.3f, BandHalfWidth * 1.3f, t));
            }
            lashing.AddComponent<MeshFilter>().sharedMesh = Tube(coilPath, ropeWidth * 0.6f, 6);
            var skin = lashing.AddComponent<MeshRenderer>();
            skin.sharedMaterial = ringMaterial;
            skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            bond.Lash();
            BondCount++;
            PlaceholderSfx.Play(SfxKind.Done, bond.JointWorld, 1f);
            Detach();
            noAttachUntil = Time.time + 1f;
            if (consumeOnLash)
            {
                HandCheck.Release(body);
                Destroy(gameObject);
            }
        }
    }
}
