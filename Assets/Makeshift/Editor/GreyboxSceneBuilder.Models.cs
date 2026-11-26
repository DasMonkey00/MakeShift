using System.IO;
using UnityEditor;
using UnityEngine;

namespace Makeshift.EditorTools
{
    /// <summary>
    /// Loads the Poly Pizza models in Assets/Makeshift/Models, turns their glTF Shader Graph materials into URP Lit
    /// (Shader Graph glTF shaders are a risk on Quest), and fits them to a size and pivot so they can replace a placeholder.
    /// </summary>
    public static partial class GreyboxSceneBuilder
    {
        const string ModelDir = "Assets/Makeshift/Models/";
        const string ModelMatDir = "Assets/Makeshift/Materials/Models/";

        public enum Pivot { Center, Bottom }

        static bool HasModel(string file) => AssetDatabase.LoadAssetAtPath<GameObject>(ModelDir + file + ".glb") != null;

        /// <summary>URP Lit copy of a glTF material (base colour and base texture only).</summary>
        static Material ModelMat(string file, Material src, Color? tint)
        {
            Directory.CreateDirectory(ModelMatDir);
            string tintTag = tint.HasValue ? "_" + ColorUtility.ToHtmlStringRGB(tint.Value) : "";
            string safe = (file + "_" + src.name + tintTag).Replace(" ", "_").Replace(".", "_");
            string path = ModelMatDir + safe + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            Color c = Color.white;
            if (src.HasProperty("baseColorFactor")) c = src.GetColor("baseColorFactor");
            Texture tex = src.HasProperty("baseColorTexture") ? src.GetTexture("baseColorTexture") : null;
            if (tint.HasValue) c = tint.Value;
            m.SetColor("_BaseColor", c);
            m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Smoothness", 0.15f);
            m.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Bounds HolderBounds(Transform holder)
        {
            bool first = true;
            var b = new Bounds();
            foreach (var mf in holder.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var m = holder.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var w = m.MultiplyPoint3x4(corner);
                    if (first) { b = new Bounds(w, Vector3.zero); first = false; } else b.Encapsulate(w);
                }
            }
            return b;
        }

        /// <summary>
        /// Puts a model under parent. size is the target length of the chosen axis (0 x, 1 y, 2 z, -1 longest) after rotation.
        /// alignLongToX picks the yaw that lays the model's long side along X. Returns null if the model is missing.
        /// </summary>
        static GameObject Model(Transform parent, string file, float size, int axis = -1, Pivot pivot = Pivot.Center,
            Vector3 euler = default, Vector3 offset = default, bool alignLongToX = false, Color? tint = null)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(ModelDir + file + ".glb");
            if (src == null) return null;

            var holder = new GameObject("Model_" + file);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = Vector3.zero;
            holder.transform.localRotation = Quaternion.identity;
            holder.transform.localScale = Vector3.one;
            var inst = Object.Instantiate(src, holder.transform);
            inst.name = file;
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.Euler(euler);
            inst.transform.localScale = Vector3.one;

            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) if (mats[i] != null) mats[i] = ModelMat(file, mats[i], tint);
                r.sharedMaterials = mats;
            }

            if (alignLongToX)
            {
                float bestYaw = 0f, bestZ = float.MaxValue;
                for (float yaw = 0f; yaw < 180f; yaw += 5f)
                {
                    inst.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(euler);
                    float z = HolderBounds(holder.transform).size.z;
                    if (z < bestZ) { bestZ = z; bestYaw = yaw; }
                }
                inst.transform.localRotation = Quaternion.Euler(0f, bestYaw, 0f) * Quaternion.Euler(euler);
            }

            var b = HolderBounds(holder.transform);
            float len = axis < 0 ? Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) : b.size[axis];
            float s = size / Mathf.Max(0.0001f, len);
            Vector3 pv = pivot == Pivot.Bottom ? new Vector3(b.center.x, b.min.y, b.center.z) : b.center;
            inst.transform.localScale = Vector3.one * s;
            inst.transform.localPosition = -pv * s + offset;
            return holder;
        }

        /// <summary>Stretches a model to an exact box (for walls and panels).</summary>
        static GameObject ModelFit(Transform parent, string file, Vector3 size, Vector3 euler = default, Vector3 center = default)
        {
            var holder = Model(parent, file, 1f, 1, Pivot.Center, euler);
            if (holder == null) return null;
            var inst = holder.transform.GetChild(0);
            inst.localScale = Vector3.one;
            inst.localPosition = Vector3.zero;
            var b = HolderBounds(holder.transform);
            var s = new Vector3(size.x / Mathf.Max(0.0001f, b.size.x), size.y / Mathf.Max(0.0001f, b.size.y), size.z / Mathf.Max(0.0001f, b.size.z));
            inst.localScale = s;
            inst.localPosition = -Vector3.Scale(b.center, s) + center;
            return holder;
        }

        static void HideChildren(Transform t, params string[] names)
        {
            foreach (var n in names)
            {
                var c = t.Find(n);
                if (c != null) Object.DestroyImmediate(c.gameObject);
            }
        }
    }
}
