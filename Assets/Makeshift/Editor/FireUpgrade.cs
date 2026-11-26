using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Makeshift.EditorTools
{
    /// <summary>
    /// Restyles the fires and particles already placed in the scenes to the Fully Opaque Fire asset, without
    /// rebuilding the scenes. Run from the menu, or drop the request file and it runs once after the next reload.
    /// </summary>
    static class FireUpgrade
    {
        const string Request = "Logs/apply-opaque-fire.request";
        const string Report = "Logs/OpaqueFire.txt";
        static readonly string[] Scenes = { "Assets/Scenes/Playground.unity", "Assets/Scenes/Greybox.unity" };
        static readonly string[] ParticleMaterials = { "SmokeParticle", "SparkParticle", "WaterParticle" };

        [InitializeOnLoadMethod]
        static void RunIfRequested()
        {
            if (File.Exists(Request)) EditorApplication.update += WaitThenRun;
        }

        static void WaitThenRun()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating) return;
            EditorApplication.update -= WaitThenRun;
            File.Delete(Request);
            Apply();
        }

        [MenuItem("Makeshift/Apply Opaque Fire Style")]
        public static void Apply()
        {
            var log = new StringBuilder();
            try
            {
                foreach (var name in ParticleMaterials) ParticleFactory.ParticleMat(name);

                foreach (var path in Scenes)
                {
                    var scene = SceneManager.GetSceneByPath(path);
                    bool opened = !scene.isLoaded;
                    if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    bool hadEdits = scene.isDirty;

                    int fires = 0, particles = 0;
                    var all = new List<Transform>();
                    foreach (var root in scene.GetRootGameObjects())
                        all.AddRange(root.GetComponentsInChildren<Transform>(true));

                    foreach (var t in all)
                    {
                        if (t == null || t.name != "Flame") continue;
                        if (UpgradeFlame(t)) fires++;
                    }

                    // Soft glow at every fire, and the slightly wider player body.
                    foreach (var t in all)
                    {
                        if (t == null || t.name != "Flame" || t.Find("Glow") != null) continue;
                        var fire = t.Find("VFX_Fire");
                        if (fire == null) continue;
                        ParticleFactory.AddGlow(t.gameObject, fire.localScale.x / ParticleFactory.FireScale);
                        particles++;
                    }
                    foreach (var root in scene.GetRootGameObjects())
                        foreach (var player in root.GetComponentsInChildren<Autohand.AutoHandPlayer>(true))
                        {
                            var body = player.GetComponent<CapsuleCollider>();
                            var so = new SerializedObject(player);
                            var headRadius = so.FindProperty("headRadius");
                            if (body == null || (Mathf.Approximately(body.radius, 0.143f) && Mathf.Approximately(headRadius.floatValue, 0.132f))) continue;
                            body.radius = 0.143f;
                            headRadius.floatValue = 0.132f;
                            so.ApplyModifiedPropertiesWithoutUndo();
                            EditorUtility.SetDirty(body);
                            particles++;
                        }

                    // The first pass left grey smoke over the campfire; the asset's fire has none.
                    foreach (var root in scene.GetRootGameObjects())
                        foreach (var campfire in root.GetComponentsInChildren<Makeshift.Fire.Campfire>(true))
                        {
                            var smoke = campfire.transform.Find("Flame/Smoke");
                            if (smoke == null) continue;
                            Object.DestroyImmediate(smoke.gameObject);
                            particles++;
                        }

                    foreach (var root in scene.GetRootGameObjects())
                    {
                        foreach (var r in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
                        {
                            var m = r.sharedMaterial;
                            if (m == null || System.Array.IndexOf(ParticleMaterials, m.name) < 0) continue;
                            ParticleFactory.Style(r.GetComponent<ParticleSystem>(), m.name);
                            EditorUtility.SetDirty(r);
                            particles++;
                        }
                    }

                    if (fires + particles > 0)
                    {
                        EditorSceneManager.MarkSceneDirty(scene);
                        if (hadEdits) log.AppendLine($"{path}: had unsaved edits, NOT saved. Save it yourself.");
                        else EditorSceneManager.SaveScene(scene);
                    }
                    log.AppendLine($"{path}: {fires} fires and {particles} particle systems restyled");
                    if (opened && !hadEdits) EditorSceneManager.CloseScene(scene, SceneManager.sceneCount > 1);
                }
                AssetDatabase.SaveAssets();
                log.AppendLine("DONE");
            }
            catch (System.Exception e)
            {
                log.AppendLine("FAILED: " + e);
                Debug.LogException(e);
            }
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Report, log.ToString());
            Debug.Log("[OPAQUE FIRE]\n" + log);
        }

        static bool UpgradeFlame(Transform flame)
        {
            float size = -1f;
            // The previous asset: a child scaled 0.24 x size.
            for (int i = flame.childCount - 1; i >= 0; i--)
            {
                var child = flame.GetChild(i);
                if (!child.name.StartsWith("VFX_Stylized_Fire")) continue;
                size = child.localScale.x / 0.24f;
                Object.DestroyImmediate(child.gameObject);
            }
            // A missing prefab keeps its name but gains a suffix; the check above covers that too.
            if (size < 0f) return false;
            ParticleFactory.AddFire(flame.gameObject, size);

            var campfire = flame.parent != null ? flame.parent.GetComponent<Makeshift.Fire.Campfire>() : null;
            if (campfire != null)
            {
                ParticleFactory.AddFireflies(flame.gameObject);
                var pile = campfire.transform.Find("FuelPile");
                if (pile != null) ParticleFactory.StyleFuelPile(pile);
                var light = campfire.transform.Find("FireLight");
                if (light != null) light.GetComponent<Light>().color = ParticleFactory.FireLightColor;
            }
            return true;
        }
    }
}
