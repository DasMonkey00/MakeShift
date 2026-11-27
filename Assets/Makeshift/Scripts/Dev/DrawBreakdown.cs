using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Makeshift.Rendering;
using Makeshift.UI;
using Unity.Profiling;
using UnityEngine;

namespace Makeshift.Dev
{
    /// <summary>
    /// Says where the draw calls go: hides one part of the scene (or flips one switch) at a time and reports how far
    /// the count drops, seen from a stand-in camera looking eight ways from two spots. Done once per quality level
    /// named in <see cref="Levels"/>. Add it in play mode and read <see cref="Report"/> when <see cref="Done"/>.
    /// </summary>
    public class DrawBreakdown : MonoBehaviour
    {
        public bool Done { get; private set; }
        public string Report { get; private set; } = "";

        private static readonly string[] Levels = { "Ultra", "Meta Quest (Build Profile)" };

        private ProfilerRecorder draws, setPass, casters;
        private Camera cam;
        private Terrain terrain;
        private Transform panel;
        private long lastDraws, lastSetPass, lastCasters, lastTris, worstDraws;
        private ProfilerRecorder triangles;

        private IEnumerator Start()
        {
            var presets = FindAnyObjectByType<DetailPresets>();
            var looks = FindAnyObjectByType<ToonLookSwitcher>();
            var menu = FindAnyObjectByType<WristMenu>();
            var head = Camera.main;
            terrain = FindAnyObjectByType<Terrain>();
            var merger = FindAnyObjectByType<SceneryInstancer>();
            cam = new GameObject("MeasureCamera").AddComponent<Camera>();
            cam.CopyFrom(head);
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.fieldOfView = 96f;
            cam.aspect = 1.05f;
            head.enabled = false;

            draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            casters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
            triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");

            var all = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude);
            Renderer[] Under(params string[] roots) =>
                all.Where(r => r != null && roots.Contains(r.transform.root.name)).ToArray();
            var camp = new[] { "Campfire", "CampTable", "Log", "Stone", "Flint", "Torch", "StandingTorch", "TorchStake" };
            var parts = new (string name, Renderer[] set)[]
            {
                ("grass", Under("Grass")),
                ("trees", Under("Trees")),
                ("background tree cards", Under("Background Trees")),
                ("plants", Under("Plants")),
                ("rocks", Under("Rocks")),
                ("flowers, mushrooms, decals, props", Under("Flowers", "Mushrooms", "Decals", "Props")),
                ("camp objects", Under(camp).Where(r => !(r is ParticleSystemRenderer)).ToArray()),
                ("fire effects", Under(camp).Where(r => r is ParticleSystemRenderer).ToArray()),
                ("scene particles (fog, leaves, fireflies)", Under("Particles")),
                ("player hands and pointers", Under("XRPlayer")),
            };

            var text = new StringBuilder();
            int level = QualitySettings.GetQualityLevel();
            int preset = presets.Current;
            foreach (var name in Levels)
            {
                int index = Array.IndexOf(QualitySettings.names, name);
                if (index < 0) continue;
                QualitySettings.SetQualityLevel(index, true);
                yield return null;
                presets.SetViewer(cam);
                merger.SetViewer(cam);
                presets.Apply(preset);
                text.AppendLine($"== {name}, LOD {presets.CurrentName}");

                yield return Sample();
                long baseDraws = lastDraws;
                text.AppendLine($"everything: draws {lastDraws} worst {worstDraws}  triangles {lastTris}  setpass {lastSetPass}  shadow casters {lastCasters}  merged renderers {merger.Covered}");

                foreach (var part in parts)
                {
                    foreach (var r in part.set) r.forceRenderingOff = true;
                    merger.MarkDirty();
                    yield return Sample();
                    foreach (var r in part.set) r.forceRenderingOff = false;
                    merger.MarkDirty();
                    text.AppendLine($"  {part.name}: {baseDraws - lastDraws}  ({part.set.Length} renderers)");
                }

                terrain.drawHeightmap = false;
                yield return Sample();
                terrain.drawHeightmap = true;
                text.AppendLine($"  terrain: {baseDraws - lastDraws}");

                looks.ToggleShadows();
                yield return Sample();
                looks.ToggleShadows();
                text.AppendLine($"  shadows (switched off): {baseDraws - lastDraws}");

                // The World Budget's shadow presets, to see what each distance costs.
                var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                    as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
                if (urp != null)
                {
                    float distance = urp.shadowDistance;
                    foreach (float metres in Makeshift.Engine.BudgetProfile.ShadowPresets)
                    {
                        urp.shadowDistance = metres;
                        merger.MarkDirty();
                        yield return Sample();
                        text.AppendLine($"  shadow distance {metres} m: draws {lastDraws} worst {worstDraws}  triangles {lastTris}  shadow casters {lastCasters}");
                    }
                    urp.shadowDistance = distance;
                    merger.MarkDirty();
                }

                var mode = looks.Drawing;
                var chunked = looks.GetComponent<ChunkedScenery>();
                foreach (ToonLookSwitcher.DrawMode other in Enum.GetValues(typeof(ToonLookSwitcher.DrawMode)))
                {
                    looks.SetDrawMode(other);
                    if (chunked != null) chunked.SetViewer(cam);
                    yield return Sample();
                    text.AppendLine($"  draw mode {other}: draws {lastDraws} worst {worstDraws}  triangles {lastTris}  shadow casters {lastCasters}");
                }
                if (chunked != null) chunked.SetViewer(head);
                looks.SetDrawMode(mode);

                looks.ToggleParticles();
                yield return Sample();
                looks.ToggleParticles();
                text.AppendLine($"  menu's particle button saves: {baseDraws - lastDraws}");

                var open = typeof(WristMenu).GetMethod("SetOpen", BindingFlags.Instance | BindingFlags.NonPublic);
                open.Invoke(menu, new object[] { true });
                var root = (GameObject)typeof(WristMenu).GetField("panelRoot", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(menu);
                panel = root.transform;
                yield return Sample();
                panel = null;
                open.Invoke(menu, new object[] { false });
                text.AppendLine($"  menu open adds: {lastDraws - baseDraws}");

#if UNITY_EDITOR
                var gameView = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
                UnityEditor.EditorWindow.FocusWindowIfItsOpen(gameView);
                yield return Sample();
                long gameOnly = lastDraws;
                UnityEditor.EditorWindow.FocusWindowIfItsOpen(typeof(UnityEditor.SceneView));
                yield return Sample();
                text.AppendLine($"  editor: Game tab in front {gameOnly}, Scene tab in front {lastDraws}");
                UnityEditor.EditorWindow.FocusWindowIfItsOpen(gameView);
#endif
            }

            QualitySettings.SetQualityLevel(level, true);
            draws.Dispose();
            setPass.Dispose();
            casters.Dispose();
            triangles.Dispose();
            presets.SetViewer(head);
            merger.SetViewer(head);
            head.enabled = true;
            Destroy(cam.gameObject);
            Report = text.ToString();
            Done = true;
        }

        private IEnumerator Sample()
        {
            var spots = new[] { new Vector2(167.5f, 10.5f), new Vector2(152f, 12f) };
            long d = 0, s = 0, c = 0, t = 0, worst = 0;
            int n = 0;
            foreach (var spot in spots)
            {
                var at = new Vector3(spot.x, 0f, spot.y);
                at.y = terrain.SampleHeight(at) + terrain.transform.position.y + 1.7f;
                for (int yaw = 0; yaw < 360; yaw += 45)
                {
                    cam.transform.SetPositionAndRotation(at, Quaternion.Euler(8f, yaw, 0f));
                    if (panel != null)
                    {
                        var forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                        panel.SetPositionAndRotation(at + forward * 0.56f + Vector3.down * 0.36f,
                            Quaternion.LookRotation(forward) * Quaternion.Euler(40f, 0f, 0f));
                    }
                    for (int f = 0; f < 5; f++) yield return null;
                    d += draws.LastValue;
                    s += setPass.LastValue;
                    c += casters.LastValue;
                    t += triangles.LastValue;
                    worst = Math.Max(worst, draws.LastValue);
                    n++;
                }
            }
            lastDraws = d / n;
            lastSetPass = s / n;
            lastCasters = c / n;
            lastTris = t / n;
            worstDraws = worst;
        }
    }
}
