using System.Collections.Generic;
using UnityEngine;

namespace Makeshift.Nature
{
    /// <summary>Cuts a tree model in two and builds the carved piece of trunk between the halves. Needs readable meshes.</summary>
    public static class TreeMesh
    {
        public const int Segments = 64;

        /// <summary>The outline of the trunk at one height, in mesh space.</summary>
        public class Ring
        {
            public Vector3 centre;
            public readonly float[] radius = new float[Segments];
            public readonly Vector2[] uv = new Vector2[Segments];
            public float MeanRadius
            {
                get { float s = 0f; foreach (var r in radius) s += r; return s / Segments; }
            }
        }

        private static Vector3 Direction(int k)
        {
            float a = k * Mathf.PI * 2f / Segments;
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        }

        /// <summary>Reads the trunk's cross section at height <paramref name="y"/>. Bits further than <paramref name="within"/> from the axis are ignored.</summary>
        public static Ring Profile(Mesh mesh, int trunk, float y, float within)
        {
            var v = mesh.vertices;
            var uv = mesh.uv;
            var tris = mesh.GetTriangles(trunk);
            var edges = new List<(Vector2 a, Vector2 b, Vector2 ua, Vector2 ub)>();
            var cut = new Vector3[2];
            var cutUv = new Vector2[2];
            for (int t = 0; t < tris.Length; t += 3)
            {
                int n = 0;
                for (int e = 0; e < 3 && n < 2; e++)
                {
                    int i = tris[t + e], j = tris[t + (e + 1) % 3];
                    if ((v[i].y < y) == (v[j].y < y)) continue;
                    float f = (y - v[i].y) / (v[j].y - v[i].y);
                    cut[n] = Vector3.Lerp(v[i], v[j], f);
                    cutUv[n] = uv.Length == v.Length ? Vector2.Lerp(uv[i], uv[j], f) : Vector2.zero;
                    n++;
                }
                if (n < 2) continue;
                var a = new Vector2(cut[0].x, cut[0].z);
                var b = new Vector2(cut[1].x, cut[1].z);
                if (a.magnitude > within || b.magnitude > within) continue;
                edges.Add((a, b, cutUv[0], cutUv[1]));
            }

            var ring = new Ring();
            Vector2 mid = Vector2.zero;
            float length = 0f;
            foreach (var e in edges)
            {
                float l = Vector2.Distance(e.a, e.b);
                mid += (e.a + e.b) * 0.5f * l;
                length += l;
            }
            if (length > 0f) mid /= length;
            ring.centre = new Vector3(mid.x, y, mid.y);

            float found = 0f;
            int hits = 0;
            Vector2 anyUv = edges.Count > 0 ? edges[0].ua : Vector2.zero;
            for (int k = 0; k < Segments; k++)
            {
                var d = new Vector2(Direction(k).x, Direction(k).z);
                float best = 0f;
                Vector2 bestUv = anyUv;
                foreach (var e in edges)
                {
                    Vector2 s = e.b - e.a, w = e.a - mid;
                    float den = d.x * s.y - d.y * s.x;
                    if (Mathf.Abs(den) < 1e-9f) continue;
                    float along = (w.x * s.y - w.y * s.x) / den;
                    float u = (w.x * d.y - w.y * d.x) / den;
                    if (u < -0.001f || u > 1.001f || along <= best) continue;
                    best = along;
                    bestUv = Vector2.Lerp(e.ua, e.ub, u);
                }
                ring.radius[k] = best;
                ring.uv[k] = bestUv;
                if (best > 0f) { found += best; hits++; }
            }
            float fill = hits > 0 ? found / hits : 0.2f;
            for (int k = 0; k < Segments; k++)
                if (ring.radius[k] <= 0f) ring.radius[k] = fill;
            return ring;
        }

        // ---------- slicing ----------

        /// <summary>
        /// The part of the model above (or below) height <paramref name="y"/>. The trunk submesh is clipped exactly;
        /// leaves are never cut, each goes whole to the side its middle is on, judged against <paramref name="leafY"/>.
        /// </summary>
        public static Mesh Slice(Mesh source, int trunk, float y, float leafY, bool above)
        {
            var v = source.vertices;
            var normals = source.normals;
            var tangents = source.tangents;
            var colors = source.colors;
            var uvs = new List<Vector4>[4];
            for (int c = 0; c < 4; c++)
            {
                var list = new List<Vector4>();
                source.GetUVs(c, list);
                uvs[c] = list.Count == v.Length ? list : null;
            }

            var outV = new List<Vector3>();
            var outN = new List<Vector3>();
            var outT = new List<Vector4>();
            var outC = new List<Color>();
            var outUv = new[] { new List<Vector4>(), new List<Vector4>(), new List<Vector4>(), new List<Vector4>() };
            var map = new Dictionary<int, int>();

            int Add(int i, int j, float f)
            {
                outV.Add(Vector3.Lerp(v[i], v[j], f));
                if (normals.Length == v.Length) outN.Add(Vector3.Slerp(normals[i], normals[j], f));
                if (tangents.Length == v.Length) outT.Add(Vector4.Lerp(tangents[i], tangents[j], f));
                if (colors.Length == v.Length) outC.Add(Color.Lerp(colors[i], colors[j], f));
                for (int c = 0; c < 4; c++)
                    if (uvs[c] != null) outUv[c].Add(Vector4.Lerp(uvs[c][i], uvs[c][j], f));
                return outV.Count - 1;
            }
            int Keep(int i)
            {
                if (!map.TryGetValue(i, out int index)) map[i] = index = Add(i, i, 0f);
                return index;
            }

            var subs = new List<int>[source.subMeshCount];
            var poly = new List<int>(4);
            for (int s = 0; s < source.subMeshCount; s++)
            {
                var tris = source.GetTriangles(s);
                var kept = subs[s] = new List<int>(tris.Length);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    if (s != trunk)
                    {
                        float middle = (v[tris[t]].y + v[tris[t + 1]].y + v[tris[t + 2]].y) / 3f;
                        if ((middle >= leafY) != above) continue;
                        for (int e = 0; e < 3; e++) kept.Add(Keep(tris[t + e]));
                        continue;
                    }
                    poly.Clear();
                    for (int e = 0; e < 3; e++)
                    {
                        int i = tris[t + e], j = tris[t + (e + 1) % 3];
                        bool inI = (v[i].y >= y) == above, inJ = (v[j].y >= y) == above;
                        if (inI) poly.Add(Keep(i));
                        if (inI != inJ) poly.Add(Add(i, j, (y - v[i].y) / (v[j].y - v[i].y)));
                    }
                    for (int k = 2; k < poly.Count; k++)
                    {
                        kept.Add(poly[0]);
                        kept.Add(poly[k - 1]);
                        kept.Add(poly[k]);
                    }
                }
            }

            var mesh = new Mesh { name = source.name + (above ? " top" : " stump") };
            if (outV.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(outV);
            if (outN.Count == outV.Count) mesh.SetNormals(outN);
            if (outT.Count == outV.Count) mesh.SetTangents(outT);
            if (outC.Count == outV.Count) mesh.SetColors(outC);
            for (int c = 0; c < 4; c++)
                if (uvs[c] != null) mesh.SetUVs(c, outUv[c]);
            mesh.subMeshCount = subs.Length;
            for (int s = 0; s < subs.Length; s++) mesh.SetTriangles(subs[s], s);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------- the carved piece ----------

        /// <summary>The same key for an edge from either of the two triangles that share it.</summary>
        private static (Vector3Int, Vector3Int) Edge(Vector3 a, Vector3 b)
        {
            Vector3Int p = Vector3Int.RoundToInt(a * 20000f), q = Vector3Int.RoundToInt(b * 20000f);
            bool first = p.x != q.x ? p.x < q.x : p.y != q.y ? p.y < q.y : p.z < q.z;
            return first ? (p, q) : (q, p);
        }

        /// <summary>The axe's V-shaped bite, in mesh units.</summary>
        public struct Notch
        {
            public float height;     // y of the deepest point
            public float halfHeight; // how far the V opens above and below it
            public float depth;      // how far in from the bark it goes
            public Vector3 side;     // flat direction from the trunk's middle to the cut face
        }

        /// <summary>A corner of the trunk with everything the model knows about it, so cut bark matches the rest.</summary>
        private struct Corner
        {
            public Vector3 at, normal;
            public Vector4 tangent, uv0, uv1, uv2, uv3;
            public Color colour;

            public static Corner Lerp(in Corner a, in Corner b, float f) => new Corner
            {
                at = Vector3.Lerp(a.at, b.at, f),
                normal = Vector3.Slerp(a.normal, b.normal, f),
                tangent = Vector4.Lerp(a.tangent, b.tangent, f),
                uv0 = Vector4.Lerp(a.uv0, b.uv0, f),
                uv1 = Vector4.Lerp(a.uv1, b.uv1, f),
                uv2 = Vector4.Lerp(a.uv2, b.uv2, f),
                uv3 = Vector4.Lerp(a.uv3, b.uv3, f),
                colour = Color.Lerp(a.colour, b.colour, f)
            };
        }

        /// <summary>
        /// The model's own trunk between heights <paramref name="from"/> and <paramref name="to"/>, with the notch
        /// hacked out of it. Submesh 0 is the untouched bark, exactly as the model draws it, so it joins the rest of
        /// the tree without a seam; submesh 1 is the bare wood. The edge between them is split through the triangles
        /// and is ragged, as an axe leaves it. A cap closes a broken end with splinters.
        /// </summary>
        public static Mesh Section(Mesh source, int trunk, Ring low, Ring high, float from, float to, Notch notch,
            bool capTop, bool capBottom, float splinter)
        {
            int face = 0;
            float facing = -2f;
            for (int k = 0; k < Segments; k++)
            {
                float d = Vector3.Dot(Direction(k), notch.side);
                if (d > facing) { facing = d; face = k; }
            }
            Vector3 lateral = Vector3.Cross(Vector3.up, notch.side);
            float width = Mathf.Max(0.001f, low.MeanRadius + high.MeanRadius);
            float reach = Mathf.Max(low.MeanRadius, high.MeanRadius) * 1.35f; // further out than this is a branch

            Vector3 Centre(float y)
            {
                var c = Vector3.Lerp(low.centre, high.centre, Mathf.InverseLerp(low.centre.y, high.centre.y, y));
                c.y = y;
                return c;
            }

            // A few broad facets across the cut: straight between random corners, so it looks hacked, not sawn.
            // The same for both halves and every bite at one spot. It only ever takes less wood, never more.
            float Facet(float across, int seed)
            {
                const float perWidth = 5f;
                float x = across / width * perWidth + 40f;
                int i = Mathf.FloorToInt(x);
                float Value(int n) => Mathf.Repeat(Mathf.Sin(n * 127.1f + seed * 311.7f) * 43758.55f, 1f);
                return Mathf.Lerp(Value(i), Value(i + 1), x - i);
            }

            // How far past the cut face a point is; positive is chopped away.
            float Over(Vector3 at)
            {
                Vector3 offset = at - Centre(at.y);
                float side = Vector3.Dot(offset, lateral);
                float y = at.y + (Facet(side, 2) - 0.5f) * notch.halfHeight * 0.5f;
                if (new Vector2(offset.x, offset.z).magnitude > reach) return -0.00001f;
                float open = Mathf.Clamp01(1f - Mathf.Abs(y - notch.height) / Mathf.Max(0.001f, notch.halfHeight));
                float bite = notch.depth * open * (1f - 0.22f * Facet(side, 7));
                float t = Mathf.InverseLerp(low.centre.y, high.centre.y, at.y);
                float surface = Mathf.Lerp(low.radius[face], high.radius[face], t);
                // Towards the notch's lips the cut face is eased out past the bark, so the value runs smoothly
                // through zero there. A sudden step would leave the edge in teeth.
                float clear = surface * 0.25f * (1f - Mathf.Clamp01(open * 4f));
                return Vector3.Dot(offset, notch.side) - (surface - bite + clear);
            }

            Vector2 Grain(Vector3 at)
            {
                Vector3 c = Vector3.Lerp(low.centre, high.centre, 0.5f);
                // Height adds in, so the grain runs on down a sloping face instead of smearing.
                return new Vector2((at.x - c.x) / width + 0.5f,
                    (at.z - c.z) / width + 0.5f + (at.y - notch.height) / width * 0.12f);
            }

            var corners = new List<Corner>();
            var bark = new List<int>();
            var wood = new List<int>();

            void Add(List<int> into, Corner a, Corner b, Corner c)
            {
                int i = corners.Count;
                corners.Add(a); corners.Add(b); corners.Add(c);
                into.Add(i); into.Add(i + 1); into.Add(i + 2);
            }

            // Bare wood, flat shaded and facing outward.
            // keepWinding: the caller already has the corners in facing order.
            // smooth: every corner takes the outward normal, so a broken face shades as one surface with no facet lines.
            void Wood(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, bool keepWinding = false, bool smooth = false)
            {
                Vector3 cross = Vector3.Cross(b - a, c - a);
                if (cross.sqrMagnitude < 1e-14f) return;
                if (!keepWinding && Vector3.Dot(cross, outward) < 0f) { (b, c) = (c, b); cross = -cross; }
                Vector3 flat = cross.normalized;
                // A sliver pressed almost edge on has no useful direction of its own and would shade as a dark line.
                if (Vector3.Dot(flat, outward.normalized) < 0.35f) flat = outward.normalized;
                if (smooth) flat = outward.normalized;
                Corner Make(Vector3 at) => new Corner
                {
                    at = at, normal = flat, tangent = new Vector4(1f, 0f, 0f, 1f), uv0 = Grain(at), colour = Color.black
                };
                Add(wood, Make(a), Make(b), Make(c));
            }

            var tri = new Corner[3];
            var past = new float[3];
            var kept = new List<Corner>(4);

            // Splits one small triangle along the edge of the cut: bark stays put, the chopped part is pressed back
            // onto the cut face.
            void Small(Corner p, Corner q, Corner r)
            {
                tri[0] = p; tri[1] = q; tri[2] = r;
                for (int e = 0; e < 3; e++) past[e] = Over(tri[e].at);
                for (int pass = 0; pass < 2; pass++)
                {
                    bool chopped = pass == 1;
                    kept.Clear();
                    for (int i = 0; i < 3; i++)
                    {
                        int j = (i + 1) % 3;
                        bool inI = (past[i] > 0f) == chopped, inJ = (past[j] > 0f) == chopped;
                        if (inI)
                        {
                            var corner = tri[i];
                            if (chopped) corner.at -= notch.side * past[i];
                            kept.Add(corner);
                        }
                        if (inI != inJ) kept.Add(Corner.Lerp(tri[i], tri[j], past[i] / (past[i] - past[j])));
                    }
                    for (int i = 2; i < kept.Count; i++)
                    {
                        if (!chopped) Add(bark, kept[0], kept[i - 1], kept[i]);
                        else Wood(kept[0].at, kept[i - 1].at, kept[i].at,
                            notch.side + Vector3.up * (Mathf.Sign(notch.height - kept[0].at.y) * 0.2f));
                    }
                }
            }

            var v = source.vertices;
            var normals = source.normals;
            var tangents = source.tangents;
            var colours = source.colors;
            var uvs = new List<Vector4>[4];
            for (int c = 0; c < 4; c++)
            {
                var list = new List<Vector4>();
                source.GetUVs(c, list);
                uvs[c] = list.Count == v.Length ? list : null;
            }
            Corner Read(int i) => new Corner
            {
                at = v[i],
                normal = normals.Length == v.Length ? normals[i] : Vector3.up,
                tangent = tangents.Length == v.Length ? tangents[i] : default,
                colour = colours.Length == v.Length ? colours[i] : Color.black,
                uv0 = uvs[0] != null ? uvs[0][i] : default,
                uv1 = uvs[1] != null ? uvs[1][i] : default,
                uv2 = uvs[2] != null ? uvs[2][i] : default,
                uv3 = uvs[3] != null ? uvs[3][i] : default
            };

            // Keeps the part of a polygon on one side of a height.
            List<Corner> Clip(List<Corner> poly, float y, bool above)
            {
                var result = new List<Corner>(poly.Count + 2);
                for (int i = 0; i < poly.Count; i++)
                {
                    Corner p = poly[i], q = poly[(i + 1) % poly.Count];
                    bool inP = (p.at.y >= y) == above, inQ = (q.at.y >= y) == above;
                    if (inP) result.Add(p);
                    if (inP != inQ) result.Add(Corner.Lerp(p, q, (y - p.at.y) / (q.at.y - p.at.y)));
                }
                return result;
            }

            // Pieces this small give the ragged edge its shape.
            float fine = Mathf.Max(0.001f, Mathf.Min(notch.halfHeight / 3f, width / 9f));
            // Only bark the axe can have touched is split small; the rest goes straight across.
            bool Near(Corner a, Corner b, Corner c)
            {
                float bottom = Mathf.Min(a.at.y, b.at.y, c.at.y), top = Mathf.Max(a.at.y, b.at.y, c.at.y);
                if (bottom > notch.height + notch.halfHeight * 1.3f || top < notch.height - notch.halfHeight * 1.3f) return false;
                // Measured from the middle line at each point's own height, as Over does, so that no triangle the
                // cut reaches is left whole: its neighbour would be pressed in and leave a crack between them.
                float furthest = Mathf.Max(Vector3.Dot(a.at - Centre(a.at.y), notch.side),
                    Vector3.Dot(b.at - Centre(b.at.y), notch.side), Vector3.Dot(c.at - Centre(c.at.y), notch.side));
                return furthest > Mathf.Min(low.radius[face], high.radius[face]) - notch.depth - width * 0.05f;
            }
            var triangles = source.GetTriangles(trunk);
            var whole = new List<Corner>(3);
            var near = new List<(Corner a, Corner b, Corner c)>();
            var far = new List<(Corner a, Corner b, Corner c)>();
            float longest = 0f;
            for (int t = 0; t < triangles.Length; t += 3)
            {
                whole.Clear();
                for (int e = 0; e < 3; e++) whole.Add(Read(triangles[t + e]));
                var band = Clip(Clip(whole, from, true), to, false);
                for (int f = 2; f < band.Count; f++)
                {
                    Corner a = band[0], b = band[f - 1], c = band[f];
                    if (!Near(a, b, c))
                    {
                        far.Add((a, b, c));
                        continue;
                    }
                    near.Add((a, b, c));
                    longest = Mathf.Max(longest, Vector3.Distance(a.at, b.at), Vector3.Distance(b.at, c.at),
                        Vector3.Distance(c.at, a.at));
                }
            }

            // One count for all of them: two triangles sharing an edge but split differently leave cracks in it.
            int n = Mathf.Clamp(Mathf.CeilToInt(longest / fine), 1, 14);
            var split = new HashSet<(Vector3Int, Vector3Int)>();
            foreach (var (a, b, c) in near)
            {
                split.Add(Edge(a.at, b.at));
                split.Add(Edge(b.at, c.at));
                split.Add(Edge(c.at, a.at));
                // The triangle split into n x n small ones, in rows from corner a towards edge b-c.
                Corner At(int row, int j) => row == 0 ? a
                    : Corner.Lerp(a, Corner.Lerp(b, c, j / (float)row), row / (float)n);
                for (int row = 0; row < n; row++)
                    for (int j = 0; j <= row; j++)
                    {
                        Small(At(row, j), At(row + 1, j), At(row + 1, j + 1));
                        if (j < row) Small(At(row, j), At(row + 1, j + 1), At(row, j + 1));
                    }
            }

            // A whole triangle beside a split one takes the same points along their shared edge, or the join sparkles.
            var rim = new List<Corner>();
            foreach (var (a, b, c) in far)
            {
                rim.Clear();
                var ends = new[] { a, b, c };
                for (int e = 0; e < 3; e++)
                {
                    Corner p = ends[e], q = ends[(e + 1) % 3];
                    rim.Add(p);
                    if (split.Contains(Edge(p.at, q.at)))
                        for (int k = 1; k < n; k++) rim.Add(Corner.Lerp(p, q, k / (float)n));
                }
                if (rim.Count == 3)
                {
                    Add(bark, a, b, c);
                    continue;
                }
                var middle = Corner.Lerp(a, Corner.Lerp(b, c, 0.5f), 2f / 3f);
                for (int i = 0; i < rim.Count; i++) Add(bark, middle, rim[i], rim[(i + 1) % rim.Count]);
            }

            // Closes a broken end, built from the very edges the trunk was cut off along, so no gap can open
            // between the two.
            void Cap(float y, float up)
            {
                Vector3 centre = Centre(y);
                var rim = new List<(Vector3 a, Vector3 b)>();
                foreach (var list in new[] { bark, wood })
                    for (int i = 0; i < list.Count; i += 3)
                        for (int e = 0; e < 3; e++)
                        {
                            Vector3 p = corners[list[i + e]].at, q = corners[list[i + (e + 1) % 3]].at;
                            if (Mathf.Abs(p.y - y) > 1e-4f || Mathf.Abs(q.y - y) > 1e-4f) continue;
                            if ((p - q).sqrMagnitude < 1e-10f) continue;
                            Vector3 half = (p + q) * 0.5f - centre;
                            if (new Vector2(half.x, half.z).magnitude <= reach) rim.Add((p, q)); // not a branch
                        }
                if (rim.Count == 0) return;

                Vector3 middle = Vector3.zero;
                float length = 0f;
                foreach (var (p, q) in rim)
                {
                    float l = Vector3.Distance(p, q);
                    middle += (p + q) * 0.5f * l;
                    length += l;
                }
                middle /= length;

                // Splinters a few degrees wide, big enough to read as torn wood. The same seed on both halves, so the
                // torn faces look like they came apart.
                var random = new System.Random(71);
                var spikes = new float[16];
                for (int k = 0; k < spikes.Length; k++) spikes[k] = (float)random.NextDouble();
                Vector3 Inner(Vector3 p)
                {
                    float turn = Mathf.Atan2(p.z - middle.z, p.x - middle.x) / (Mathf.PI * 2f) + 0.5f;
                    float at = turn * spikes.Length;
                    int k = Mathf.FloorToInt(at);
                    float spike = Mathf.Lerp(spikes[k % spikes.Length], spikes[(k + 1) % spikes.Length], at - k);
                    Vector3 q = Vector3.Lerp(middle, p, 0.6f);
                    q.y = y + up * spike * spike * splinter;
                    return q;
                }

                Vector3 outward = Vector3.up * up;
                foreach (var (p, q) in rim)
                {
                    Vector3 ip = Inner(p), iq = Inner(q);
                    // Each edge is walked against the way the trunk's own triangle walks it. That faces every
                    // piece correctly whatever the outline's shape; turning them by eye left slits in the end.
                    Wood(q, p, ip, outward, true, true);
                    Wood(q, ip, iq, outward, true, true);
                    Wood(iq, ip, middle, outward, true, true);
                }
            }
            if (capTop) Cap(to, 1f);
            if (capBottom) Cap(from, -1f);

            var mesh = new Mesh { name = "Cut trunk" };
            if (corners.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            var positions = new List<Vector3>(corners.Count);
            var normalsOut = new List<Vector3>(corners.Count);
            var tangentsOut = new List<Vector4>(corners.Count);
            var coloursOut = new List<Color>(corners.Count);
            var uvOut = new[] { new List<Vector4>(), new List<Vector4>(), new List<Vector4>(), new List<Vector4>() };
            foreach (var corner in corners)
            {
                positions.Add(corner.at);
                normalsOut.Add(corner.normal);
                tangentsOut.Add(corner.tangent);
                coloursOut.Add(corner.colour);
                uvOut[0].Add(corner.uv0); uvOut[1].Add(corner.uv1); uvOut[2].Add(corner.uv2); uvOut[3].Add(corner.uv3);
            }
            mesh.SetVertices(positions);
            mesh.SetNormals(normalsOut);
            mesh.SetTangents(tangentsOut);
            if (colours.Length == v.Length) mesh.SetColors(coloursOut);
            for (int c = 0; c < 4; c++)
                if (c == 0 || uvs[c] != null) mesh.SetUVs(c, uvOut[c]);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(bark, 0);
            mesh.SetTriangles(wood, 1);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
