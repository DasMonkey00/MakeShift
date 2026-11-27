using System;
using System.Text;
using Makeshift.Engine;
using Makeshift.Platform;
using Unity.Profiling;
using UnityEngine;

namespace Makeshift.UI
{
    /// <summary>
    /// The numbers we hold Quest 3 to at 90 fps, each shown against its limit and coloured by how close it is:
    /// green below 75% of the limit, yellow from 75%, orange from 90%, red over it. Refreshes twice a second.
    /// Beside each number is its worst over the last 10 seconds: the highest value, or for fps the lowest.
    /// Put it next to a <see cref="WristMenu"/> to see it there.
    ///
    /// The limits are our own working targets, set here, to be adjusted from headset measurements. Draw calls and
    /// triangles only exist in the editor and in development builds. Heat and GPU load need Meta's OVR Metrics Tool.
    /// </summary>
    public class PerfStats : MonoBehaviour
    {
        [Header("Limits")]
        [SerializeField] private float targetFps = 90f;
        [SerializeField] private int limitDrawCalls = 300;
        [SerializeField] private int limitTriangles = 1000000;
        [SerializeField] private int limitMemoryMb = 3500;
        [Tooltip("Meta asks for something on screen within 4 seconds.")]
        [SerializeField] private float limitLoadSeconds = 4f;
        [Tooltip("Share of frames that arrived late.")]
        [SerializeField] private float limitLatePercent = 5f;

        /// <summary>The four columns of the menu card, line for line.</summary>
        public string Names { get; private set; } = "measuring...";
        public string Values { get; private set; } = "";
        /// <summary>Each value's worst over the last 10 seconds.</summary>
        public string Peaks { get; private set; } = "";
        public string Limits { get; private set; } = "";
        /// <summary>Under the columns: headset rate, colour key and warnings.</summary>
        public string Footer { get; private set; } = "";
        /// <summary>A few lines for the on-view readout.</summary>
        public string Short { get; private set; } = "";

        private const string Green = "#5FE06B";
        private const string Yellow = "#FFE23D";
        private const string Orange = "#FF9A1F";
        private const string Red = "#FF3B30";
        private const string Grey = "#A8AEB8";

        private ProfilerRecorder drawCalls, triangles, usedMemory;
        private WorldBudget budget;
        private readonly FrameTiming[] timings = new FrameTiming[1];
        private readonly StringBuilder names = new StringBuilder(160);
        private readonly StringBuilder values = new StringBuilder(256);
        private readonly StringBuilder limits = new StringBuilder(160);
        private readonly StringBuilder peaks = new StringBuilder(256);

        private const int Slices = 20; // half a second each, so 10 seconds

        /// <summary>The highest value added in the last <see cref="Slices"/> slices. -1 while nothing was added.</summary>
        private sealed class Peak
        {
            private readonly double[] closed = new double[Slices - 1];
            private double current = -1;
            private int next;

            public Peak()
            {
                for (int i = 0; i < closed.Length; i++) closed[i] = -1;
            }

            public void Add(double v)
            {
                if (v > current) current = v;
            }

            public double Value
            {
                get
                {
                    double max = current;
                    foreach (var v in closed)
                        if (v > max) max = v;
                    return max;
                }
            }

            /// <summary>Ends the running slice; the oldest one drops out.</summary>
            public void Close()
            {
                closed[next] = current;
                next = (next + 1) % closed.Length;
                current = -1;
            }
        }

        private readonly Peak cpuPeak = new Peak(), gpuPeak = new Peak(), drawPeak = new Peak(), triPeak = new Peak(),
            memoryPeak = new Peak(), bodyPeak = new Peak(), latePeak = new Peak();
        // The lowest frame rate, held as its distance below this ceiling so that it can be kept as a highest value.
        private const double FpsCeiling = 10000;
        private readonly Peak fpsDip = new Peak();

        private float window;
        private int frames, late;
        private double cpuSum, gpuSum;
        private int cpuCount, gpuCount;
        private float loadSeconds = -1f;

        private void OnEnable()
        {
            drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            usedMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
            budget = FindAnyObjectByType<WorldBudget>();
        }

        private void OnDisable()
        {
            drawCalls.Dispose();
            triangles.Dispose();
            usedMemory.Dispose();
        }

        private void Update()
        {
            // Time from the scene starting to load until its first frame is on screen.
            if (loadSeconds < 0f && Time.frameCount > 1) loadSeconds = Time.timeSinceLevelLoad + Time.unscaledDeltaTime;

            float dt = Time.unscaledDeltaTime;
            window += dt;
            frames++;
            if (dt > 1.25f / targetFps) late++;

            // The first frames are the scene still loading; they would sit in the peaks for 10 seconds.
            bool settled = Time.frameCount > 2;
            if (settled)
            {
                drawPeak.Add(Read(drawCalls));
                triPeak.Add(Read(triangles));
            }

            FrameTimingManager.CaptureFrameTimings();
            double gpuMs = 0;
            if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
            {
                if (timings[0].cpuMainThreadFrameTime > 0)
                {
                    cpuSum += timings[0].cpuMainThreadFrameTime;
                    cpuCount++;
                    if (settled) cpuPeak.Add(timings[0].cpuMainThreadFrameTime);
                }
                gpuMs = timings[0].gpuFrameTime;
            }
            if (gpuMs <= 0)
            {
                // Some headsets only report GPU time through the XR display.
                var display = DisplayRefresh.Running();
                if (display != null && display.TryGetAppGPUTimeLastFrame(out float seconds)) gpuMs = seconds * 1000.0;
            }
            if (gpuMs > 0)
            {
                gpuSum += gpuMs;
                gpuCount++;
                if (settled) gpuPeak.Add(gpuMs);
            }

            if (window < 0.5f) return;
            Compose(frames / window);
            window = 0f;
            frames = late = cpuCount = gpuCount = 0;
            cpuSum = gpuSum = 0;
        }

        private void Compose(float fps)
        {
            float budgetMs = 1000f / targetFps;
            names.Clear();
            values.Clear();
            limits.Clear();
            peaks.Clear();
            names.Append($"<color={Grey}>STAT</color>");
            values.Append($"<color={Grey}>NOW</color>");
            peaks.Append($"<color={Grey}>10s PEAK</color>");
            limits.Append($"<color={Grey}>LIMIT</color>");

            // Frame rate counts the other way round: it must not fall below the target.
            fpsDip.Add(FpsCeiling - fps);
            double lowest = FpsCeiling - fpsDip.Value;
            string fpsColor = FpsColor(fps);
            Row("fps", $"{fps:0}", $"{lowest:0}", $"min {targetFps:0}", fpsColor, FpsColor(lowest));

            double cpu = cpuCount > 0 ? cpuSum / cpuCount : -1;
            double gpu = gpuCount > 0 ? gpuSum / gpuCount : -1;
            Measured("CPU ms", cpu, cpuPeak.Value, budgetMs, $"{budgetMs:0.0}", Millis);
            Measured("GPU ms", gpu, gpuPeak.Value, budgetMs, $"{budgetMs:0.0}", Millis);

            float latePercent = frames > 0 ? 100f * late / frames : 0f;
            latePeak.Add(latePercent);
            double worstLate = latePeak.Value;
            Row("late frames", $"{latePercent:0}%", $"{worstLate:0}%", $"{limitLatePercent:0}%",
                LateColor(latePercent), LateColor(worstLate));

            long draws = Read(drawCalls);
            long tris = Read(triangles);
            Measured("draw calls", draws, drawPeak.Value, limitDrawCalls, Count(limitDrawCalls), Whole);
            Measured("triangles", tris, triPeak.Value, limitTriangles, Count(limitTriangles), Whole);

            // Awake loose rigidbodies against the physics bubble's cap (the cap is the limit; "off" when the bubble is off).
            var bubble = budget != null ? budget.Bubble : null;
            if (bubble != null)
            {
                if (bubble.On && bubble.Cap > 0)
                {
                    bodyPeak.Add(bubble.AwakeCount);
                    Measured("rigidbodies", bubble.AwakeCount, bodyPeak.Value, bubble.Cap, bubble.Cap.ToString(), Whole);
                }
                else Row("rigidbodies", "off", "-", "-", Grey, Grey);
            }

            long bytes = Read(usedMemory);
            long mb = bytes < 0 ? -1 : bytes / (1024 * 1024);
            memoryPeak.Add(mb);
            Measured("memory MB", mb, memoryPeak.Value, limitMemoryMb, limitMemoryMb.ToString(), Whole0);

            float load = Mathf.Max(0f, loadSeconds);
            Row("load s", $"{load:0.0}", "-", $"{limitLoadSeconds:0.0}", Near(load / limitLoadSeconds), Grey);

            Names = names.ToString();
            Values = values.ToString();
            Peaks = peaks.ToString();
            Limits = limits.ToString();

            float hz = DisplayRefresh.CurrentHz;
            string headset = hz <= 0f ? $"<color={Grey}>headset rate: not reported</color>"
                : hz >= targetFps - 0.5f ? $"<color={Green}>headset {hz:0} Hz</color>"
                : $"<color={Red}>headset {hz:0} Hz, not {targetFps:0}</color>";
            string footer = $"{headset}\n<color={Green}>ok</color>  <color={Yellow}>75%+</color>  " +
                            $"<color={Orange}>90%+</color>  <color={Red}>over limit</color>\n" +
                            $"<color={Grey}>peak: worst in 10 s (fps: lowest)</color>";
            if (draws < 0 || tris < 0) footer += $"\n<color={Grey}>counts need a development build</color>";
            if (Application.isEditor) footer += $"\n<color={Grey}>editor numbers, not Quest</color>";
            Footer = footer;

            Short = $"<color={fpsColor}>{fps:0} fps</color> <color={FpsColor(lowest)}>low {lowest:0}</color>  {headset}\n" +
                    $"{Mini("CPU", cpu, cpuPeak.Value, budgetMs, Millis)}  {Mini("GPU", gpu, gpuPeak.Value, budgetMs, Millis)}\n" +
                    $"{Mini("draws", draws, drawPeak.Value, limitDrawCalls, Whole)}  {Mini("tris", tris, triPeak.Value, limitTriangles, Whole)}";

            cpuPeak.Close();
            gpuPeak.Close();
            drawPeak.Close();
            triPeak.Close();
            memoryPeak.Close();
            bodyPeak.Close();
            latePeak.Close();
            fpsDip.Close();
        }

        private void Row(string name, string value, string peak, string limit, string color, string peakColor)
        {
            names.Append($"\n<color={color}>{name}</color>");
            values.Append($"\n<color={color}>{value}</color>");
            peaks.Append($"\n<color={peakColor}>{peak}</color>");
            limits.Append($"\n{limit}");
        }

        /// <summary>A row for a value that must stay under its limit. A negative value was not reported.</summary>
        private void Measured(string name, double value, double peak, double limit, string limitText, Func<double, string> format) =>
            Row(name, value < 0 ? "n/a" : format(value), peak < 0 ? "n/a" : format(peak), limitText,
                value < 0 ? Grey : Near(value / limit), peak < 0 ? Grey : Near(peak / limit));

        private static string Mini(string name, double value, double peak, double limit, Func<double, string> format)
        {
            if (value < 0) return $"<color={Grey}>{name} n/a</color>";
            string now = $"<color={Near(value / limit)}>{name} {format(value)}/{format(limit)}</color>";
            return peak < 0 ? now : $"{now} <color={Near(peak / limit)}>pk {format(peak)}</color>";
        }

        private static readonly Func<double, string> Millis = v => v.ToString("0.0");
        private static readonly Func<double, string> Whole = v => Count((long)v);
        private static readonly Func<double, string> Whole0 = v => v.ToString("0");

        private string FpsColor(double fps) =>
            fps >= targetFps - 1f ? Green : fps >= targetFps - 5f ? Yellow : fps >= targetFps - 10f ? Orange : Red;

        private string LateColor(double percent) =>
            percent < 1 ? Green : percent < 3 ? Yellow : percent <= limitLatePercent ? Orange : Red;

        /// <summary>Colour for a value that must stay under its limit.</summary>
        private static string Near(double share) => share < 0.75 ? Green : share < 0.9 ? Yellow : share <= 1.0 ? Orange : Red;

        /// <summary>The counter's latest value, or -1 where this build or platform does not report it.</summary>
        private static long Read(ProfilerRecorder recorder) => recorder.Valid && recorder.LastValue > 0 ? recorder.LastValue : -1;

        private static string Count(long v) =>
            v < 0 ? "n/a" : v >= 1000000 ? $"{v / 1000000f:0.00}M" : v >= 10000 ? $"{v / 1000f:0}k" : v.ToString();
    }
}
