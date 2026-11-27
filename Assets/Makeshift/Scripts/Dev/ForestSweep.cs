using System.Collections;
using System.Text;
using Makeshift.Rendering;
using Unity.Profiling;
using UnityEngine;

namespace Makeshift.Dev
{
    /// <summary>
    /// Measures each extra forest density: draws and triangles seen eight ways from two spots, and how long the
    /// scenery merge takes to sort its trees when the view moves. Add it in play mode and read <see cref="Report"/>
    /// when <see cref="Done"/>. Editor numbers, for comparing densities, not Quest frame times.
    /// </summary>
    public class ForestSweep : MonoBehaviour
    {
        public bool Done { get; private set; }
        public string Report { get; private set; } = "";

        private IEnumerator Start()
        {
            var forest = FindAnyObjectByType<ForestDensity>();
            var merger = FindAnyObjectByType<SceneryInstancer>();
            var chunks = FindAnyObjectByType<ChunkedScenery>();
            var presets = FindAnyObjectByType<DetailPresets>();
            var terrain = FindAnyObjectByType<Terrain>();
            var head = Camera.main;
            var cam = new GameObject("SweepCamera").AddComponent<Camera>();
            cam.CopyFrom(head);
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.fieldOfView = 96f;
            cam.aspect = 1.05f;
            head.enabled = false;
            presets.SetViewer(cam);
            merger.SetViewer(cam);
            chunks.SetViewer(cam);

            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            var triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            var text = new StringBuilder();
            var spots = new[] { new Vector2(167.5f, 10.5f), new Vector2(152f, 12f) };
            var clock = new System.Diagnostics.Stopwatch();

            for (int step = 0; step < 4; step++)
            {
                long d = 0, t = 0, worst = 0, mostTris = 0;
                double sort = 0;
                int n = 0;
                foreach (var spot in spots)
                {
                    var at = new Vector3(spot.x, 0f, spot.y);
                    at.y = terrain.SampleHeight(at) + terrain.transform.position.y + 1.7f;
                    for (int yaw = 0; yaw < 360; yaw += 45)
                    {
                        cam.transform.SetPositionAndRotation(at, Quaternion.Euler(8f, yaw, 0f));
                        for (int f = 0; f < 5; f++) yield return null;
                        d += draws.LastValue;
                        t += triangles.LastValue;
                        worst = System.Math.Max(worst, draws.LastValue);
                        mostTris = System.Math.Max(mostTris, triangles.LastValue);
                        sort += merger.LastSortMs;
                        n++;
                    }
                }
                text.AppendLine($"forest {forest.Label}: draws {d / n} worst {worst}  triangles {t / n} worst {mostTris}" +
                                $"  scenery sort {sort / n:0.0} ms");
                forest.Cycle();
                yield return null;
            }

            draws.Dispose();
            triangles.Dispose();
            presets.SetViewer(head);
            merger.SetViewer(head);
            chunks.SetViewer(head);
            head.enabled = true;
            Destroy(cam.gameObject);
            Report = text.ToString();
            Done = true;
        }
    }
}
