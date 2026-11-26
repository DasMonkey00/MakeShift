using UnityEditor;
using UnityEngine;

namespace Makeshift.EditorTools
{
    public static class GrassStats
    {
        public static void Print()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PaleFernStudio/Looksmith/Samples/Demo/Prefabs/Grass1.prefab");
            var mf = go.GetComponentInChildren<MeshFilter>();
            var m = mf.sharedMesh;
            long tris = m.triangles.Length / 3;
            int clumps = 3773;
            Debug.Log($"[GRASSSTATS] clump verts={m.vertexCount} tris={tris} bounds={m.bounds.size}");
            Debug.Log($"[GRASSSTATS] field verts={(long)m.vertexCount * clumps} tris={tris * clumps} (all chunks, before culling)");
            // Chunk is 8x8 m; a 360 view sees at most nearby chunks; estimate visible fraction with 40 m draw reach.
            Debug.Log($"[GRASSSTATS] per chunk tris={tris * 77} (about 77 clumps per chunk)");
        }
    }
}
