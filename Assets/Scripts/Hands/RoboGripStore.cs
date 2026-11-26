using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Where the hand + item offsets found with HandAlignPro are kept, so they survive scene rebuilds and builds. One line per item:
///   name  px py pz  qx qy qz qw        (offset from the tracked controller, right hand; the left hand uses the mirror image)
/// In the editor the file is Assets/Scripts/Hands/Resources/GripOffsets.txt (read and written directly). In a built app it is read from
/// persistentDataPath/GripOffsets.txt if present (written there by HandAlignPro), otherwise from the copy baked into Resources.
/// </summary>
public static class RoboGripStore {
    public static event System.Action<string> Changed;
    static Dictionary<string, KeyValuePair<Vector3, Quaternion>> data;

    static string Path_ {
        get {
#if UNITY_EDITOR
            return System.IO.Path.Combine(Application.dataPath, "Scripts/Hands/Resources/GripOffsets.txt");
#else
            return System.IO.Path.Combine(Application.persistentDataPath, "GripOffsets.txt");
#endif
        }
    }

    static void Load() {
        data = new Dictionary<string, KeyValuePair<Vector3, Quaternion>>();
        string text = null;
        try {
            if (File.Exists(Path_)) text = File.ReadAllText(Path_);
#if !UNITY_EDITOR
            else { var ta = Resources.Load<TextAsset>("GripOffsets"); if (ta != null) text = ta.text; }
#endif
        } catch (System.Exception e) { Debug.LogWarning("RoboGripStore: could not read offsets: " + e.Message); }
        if (string.IsNullOrEmpty(text)) return;
        foreach (var raw in text.Split('\n')) {
            var line = raw.Trim(); if (line.Length == 0 || line[0] == '#') continue;
            var t = line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < 8) continue;
            var f = new float[7]; bool ok = true;
            for (int i = 0; i < 7; i++) ok &= float.TryParse(t[1 + i], NumberStyles.Float, CultureInfo.InvariantCulture, out f[i]);
            if (!ok) continue;
            data[t[0]] = new KeyValuePair<Vector3, Quaternion>(new Vector3(f[0], f[1], f[2]), new Quaternion(f[3], f[4], f[5], f[6]));
        }
    }

    public static bool TryGet(string key, out Vector3 pos, out Quaternion rot) {
        if (data == null) Load();
        KeyValuePair<Vector3, Quaternion> v;
        if (data.TryGetValue(key, out v)) { pos = v.Key; rot = v.Value; return true; }
        pos = Vector3.zero; rot = Quaternion.identity; return false;
    }

    public static void Set(string key, Vector3 pos, Quaternion rot) {
        if (data == null) Load();
        data[key] = new KeyValuePair<Vector3, Quaternion>(pos, rot);
        var sb = new StringBuilder();
        sb.AppendLine("# HandAlignPro grip offsets: name  pos(x y z)  rotation quaternion(x y z w), from the tracked controller, right hand");
        foreach (var kv in data) {
            var p = kv.Value.Key; var q = kv.Value.Value;
            sb.AppendLine(kv.Key + " " + string.Format(CultureInfo.InvariantCulture, "{0:F5} {1:F5} {2:F5} {3:F5} {4:F5} {5:F5} {6:F5}", p.x, p.y, p.z, q.x, q.y, q.z, q.w));
        }
        try { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path_)); File.WriteAllText(Path_, sb.ToString()); Debug.Log("RoboGripStore: saved " + key + " to " + Path_); }
        catch (System.Exception e) { Debug.LogWarning("RoboGripStore: could not save: " + e.Message); }
        if (Changed != null) Changed(key);
    }
}
