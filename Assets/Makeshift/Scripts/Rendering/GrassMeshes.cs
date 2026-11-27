using System.Collections.Generic;
using UnityEngine;

namespace Makeshift.Rendering
{
    /// <summary>
    /// Makes the two meshes a grass or flower patch is drawn with, from its closest model: the whole patch, and the
    /// blades that stay when it is seen from further off. Both carry each blade's foot and mark in the third UV set
    /// (see Scenery.shader), and the far one is exactly what the near one has become by the distance they change over,
    /// so the change cannot be seen.
    /// </summary>
    public static class GrassMeshes
    {
        private const int KeepOneIn = 3;
        private const float Stays = 0.5f, Sinks = 1f;

        public static void Make(Mesh source, out Mesh near, out Mesh far)
        {
            var vertices = source.vertices;
            var triangles = source.triangles;

            // Blades are the pieces whose triangles share no corner position with any other piece.
            var parent = new int[vertices.Length];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int i)
            {
                while (parent[i] != i) i = parent[i] = parent[parent[i]];
                return i;
            }
            var places = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < vertices.Length; i++)
            {
                var key = Vector3Int.RoundToInt(vertices[i] * 1000f);
                if (places.TryGetValue(key, out int other)) parent[Find(i)] = Find(other);
                else places[key] = i;
            }
            for (int i = 0; i < triangles.Length; i += 3)
            {
                parent[Find(triangles[i])] = Find(triangles[i + 1]);
                parent[Find(triangles[i + 1])] = Find(triangles[i + 2]);
            }

            var blades = new Dictionary<int, List<int>>();
            for (int i = 0; i < vertices.Length; i++)
            {
                int root = Find(i);
                if (!blades.TryGetValue(root, out var list)) blades[root] = list = new List<int>(8);
                list.Add(i);
            }

            // Each blade's foot: the middle of its lowest corners.
            var feet = new Dictionary<int, Vector3>();
            foreach (var blade in blades)
            {
                float low = float.MaxValue;
                foreach (int i in blade.Value) low = Mathf.Min(low, vertices[i].y);
                Vector3 sum = Vector3.zero;
                int n = 0;
                foreach (int i in blade.Value)
                    if (vertices[i].y < low + 0.05f)
                    {
                        sum += vertices[i];
                        n++;
                    }
                feet[blade.Key] = sum / n;
            }

            // Keep every few blades going round the clump, so what stays is spread over all of it.
            var order = new List<int>(blades.Keys);
            order.Sort((a, b) => Mathf.Atan2(feet[a].z, feet[a].x).CompareTo(Mathf.Atan2(feet[b].z, feet[b].x)));
            var kept = new HashSet<int>();
            for (int i = 0; i < order.Count; i += KeepOneIn) kept.Add(order[i]);

            var marks = new List<Vector4>(vertices.Length);
            for (int i = 0; i < vertices.Length; i++)
            {
                int root = Find(i);
                Vector3 foot = feet[root];
                marks.Add(new Vector4(foot.x, foot.y, foot.z, kept.Contains(root) ? Stays : Sinks));
            }

            near = Object.Instantiate(source);
            near.name = source.name + " near";
            near.SetUVs(2, marks);

            var keep = new List<int>();
            for (int i = 0; i < triangles.Length; i += 3)
                if (kept.Contains(Find(triangles[i])))
                {
                    keep.Add(triangles[i]);
                    keep.Add(triangles[i + 1]);
                    keep.Add(triangles[i + 2]);
                }
            far = Compact(near, keep, marks);
            far.name = source.name + " far";
        }

        // A mesh of only the corners these triangles use.
        private static Mesh Compact(Mesh from, List<int> triangles, List<Vector4> marks)
        {
            var v = from.vertices;
            var normals = from.normals;
            var tangents = from.tangents;
            var colours = from.colors;
            var uv = from.uv;
            var map = new Dictionary<int, int>();
            var outV = new List<Vector3>();
            var outN = new List<Vector3>();
            var outT = new List<Vector4>();
            var outC = new List<Color>();
            var outUv = new List<Vector2>();
            var outMarks = new List<Vector4>();
            var indices = new int[triangles.Count];
            for (int i = 0; i < triangles.Count; i++)
            {
                int old = triangles[i];
                if (!map.TryGetValue(old, out int index))
                {
                    map[old] = index = outV.Count;
                    outV.Add(v[old]);
                    if (normals.Length == v.Length) outN.Add(normals[old]);
                    if (tangents.Length == v.Length) outT.Add(tangents[old]);
                    if (colours.Length == v.Length) outC.Add(colours[old]);
                    if (uv.Length == v.Length) outUv.Add(uv[old]);
                    outMarks.Add(marks[old]);
                }
                indices[i] = index;
            }
            var mesh = new Mesh();
            mesh.SetVertices(outV);
            if (outN.Count == outV.Count) mesh.SetNormals(outN);
            if (outT.Count == outV.Count) mesh.SetTangents(outT);
            if (outC.Count == outV.Count) mesh.SetColors(outC);
            if (outUv.Count == outV.Count) mesh.SetUVs(0, outUv);
            mesh.SetUVs(2, outMarks);
            mesh.SetTriangles(indices, 0);
            mesh.bounds = from.bounds;
            return mesh;
        }
    }
}
