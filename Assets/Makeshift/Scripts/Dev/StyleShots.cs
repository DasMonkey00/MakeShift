using System.Collections;
using System.IO;
using Makeshift.Fire;
using Makeshift.Rendering;
using Makeshift.Survival;
using UnityEngine;

namespace Makeshift.Dev
{
    /// <summary>
    /// Editor-only helper (run with -makeshiftShots): renders a PNG of every playground area at noon into Logs/shots, then exits.
    /// Used to eyeball placeholder models, particles, shadows and notes without a headset.
    /// </summary>
    public class StyleShots : MonoBehaviour
    {
#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-makeshiftShots") >= 0)
                new GameObject("StyleShots").AddComponent<StyleShots>();
        }

        private Camera cam;
        private RenderTexture rt;
        private string dir;

        private IEnumerator Shoot(string name, Vector3 pos, Vector3 look, int frames = 8)
        {
            cam.transform.position = pos;
            cam.transform.LookAt(look);
            for (int i = 0; i < frames; i++) yield return null;
            cam.Render();
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            Debug.Log("[SHOT] " + name);
        }
#endif

        private IEnumerator Start()
        {
#if UNITY_EDITOR
            yield return new WaitForSeconds(1f);
            dir = Path.GetFullPath("Logs/shots");
            Directory.CreateDirectory(dir);

            var go = new GameObject("ShotCam");
            cam = go.AddComponent<Camera>();
            cam.fieldOfView = 70f;
            cam.clearFlags = CameraClearFlags.Skybox;
            rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt;

            var cycle = FindAnyObjectByType<DayNightCycle>();
            var tod = FindAnyObjectByType<TimeOfDaySky>();
            if (cycle != null) cycle.Paused = true;
            if (tod != null) tod.SetHour(12f);

            yield return Shoot("spawn_drill_station", new Vector3(0.3f, 1.6f, -1.2f), new Vector3(1.0f, 0.9f, 1.2f));
            yield return Shoot("drill_close", new Vector3(1.6f, 1.45f, -0.7f), new Vector3(1.6f, 1.0f, 1.3f));
            yield return Shoot("info_notes", new Vector3(-1.3f, 1.25f, 0.3f), new Vector3(-1.3f, 0.77f, 0.8f));

            var fire = FindAnyObjectByType<Campfire>();
            yield return Shoot("camp_unlit", new Vector3(-5.2f, 1.5f, -2.2f), new Vector3(-8.2f, 0.4f, -0.3f));
            if (fire != null) { fire.AddFuel(120f); fire.Light(); }
            yield return Shoot("camp_lit", new Vector3(-5.2f, 1.5f, -2.2f), new Vector3(-8.2f, 0.4f, -0.3f), 40);
            yield return Shoot("fire_close", new Vector3(-6.9f, 0.9f, -0.9f), new Vector3(-8f, 0.2f, 0f), 30);
            yield return Shoot("camp_table", new Vector3(-7.0f, 1.8f, -1.5f), new Vector3(-9f, 0.8f, -1.5f));
            yield return Shoot("trough", new Vector3(-8.5f, 1.4f, 2.4f), new Vector3(-10.5f, 0.3f, 0.5f), 40);
            yield return Shoot("craft_bench", new Vector3(5.8f, 1.9f, -1.6f), new Vector3(8f, 0.8f, 0f));
            yield return Shoot("combat_range", new Vector3(-1f, 1.7f, 6.2f), new Vector3(0f, 0.7f, 10f));
            yield return Shoot("climb_wall", new Vector3(0f, 1.7f, -6f), new Vector3(0f, 2f, -10f));
            yield return Shoot("platform_umbrella", new Vector3(2.5f, 5.4f, -9.5f), new Vector3(0f, 4.2f, -12.5f));
            yield return Shoot("scavenger", new Vector3(-1.4f, 0.9f, 0.3f), new Vector3(-2.2f, 0.25f, 1.6f));
            yield return Shoot("wide_shadows", new Vector3(-3f, 2.2f, -4f), new Vector3(2f, 0f, 3f));
            UnityEditor.EditorApplication.Exit(0);
#else
            yield break;
#endif
        }
    }
}
