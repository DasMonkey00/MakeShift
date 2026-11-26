using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Makeshift.EditorTools
{
    /// <summary>Batch report on the imported models: size, triangles, materials. Output in Logs/ModelReport.txt.</summary>
    public static class ModelReport
    {
        public static void Run()
        {
            AssetDatabase.Refresh();
            var sb = new StringBuilder();
            foreach (var path in Directory.GetFiles("Assets/Makeshift/Models", "*.glb"))
            {
                string p = path.Replace('\\', '/');
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go == null) { sb.AppendLine(p + ": FAILED TO IMPORT"); continue; }
                int tris = 0;
                Bounds b = new Bounds();
                bool first = true;
                var sbm = new StringBuilder();
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    tris += mf.sharedMesh.triangles.Length / 3;
                    var r = mf.GetComponent<Renderer>();
                    var m = mf.transform.localToWorldMatrix;
                    var mb = mf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        var w = m.MultiplyPoint3x4(c);
                        if (first) { b = new Bounds(w, Vector3.zero); first = false; } else b.Encapsulate(w);
                    }
                    if (r != null)
                        foreach (var mat in r.sharedMaterials)
                            if (mat != null) sbm.Append($" [{mat.shader.name}|{mat.name}|col={(mat.HasProperty("baseColorFactor") ? mat.GetColor("baseColorFactor").ToString() : "-")}|tex={(mat.HasProperty("baseColorTexture") && mat.GetTexture("baseColorTexture") != null ? "y" : "n")}]");
                }
                sb.AppendLine($"{Path.GetFileName(p)}: tris={tris} size={b.size.x:0.###}x{b.size.y:0.###}x{b.size.z:0.###} center={b.center:0.###} children={go.transform.childCount} skinned={go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length} anim={AssetDatabase.LoadAllAssetsAtPath(p).Length}");
                sb.AppendLine("   mats:" + sbm);
            }
            File.WriteAllText("Logs/ModelReport.txt", sb.ToString());
            Debug.Log("[MODELS] report written");
        }
    }
}
