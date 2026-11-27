using System;
using UnityEngine;

namespace Makeshift.Audio
{
    /// <summary>Placeholder rain, wind and thunder, generated in code. Swap in recordings on the StormController.</summary>
    public static class StormSounds
    {
        private const int Rate = 22050;

        /// <summary>A steady hiss with scattered heavier drops.</summary>
        public static AudioClip Rain()
        {
            var rng = new System.Random(31);
            var s = new float[Rate * 5];
            float soft = 0f, body = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float white = Noise(rng);
                soft += 0.45f * (white - soft);
                body += 0.06f * (white - body);
                s[i] = (soft - body) * 0.32f + body * 0.35f;
            }
            for (int d = 0; d < 900; d++)
            {
                int start = rng.Next(0, s.Length - 400);
                float strength = Mathf.Pow((float)rng.NextDouble(), 2f) * 0.22f;
                int n = rng.Next(60, 300);
                for (int i = 0; i < n; i++) s[start + i] += Noise(rng) * strength * Mathf.Exp(-6f * i / n);
            }
            return Loop("RainLoop", s);
        }

        /// <summary>Low rushing air that swells and falls, with a faint whistle.</summary>
        public static AudioClip Wind()
        {
            var rng = new System.Random(57);
            var s = new float[Rate * 9];
            float a = 0f, b = 0f, low = 0f, band = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)Rate;
                float white = Noise(rng);
                float swell = Mathf.PerlinNoise(t * 0.45f, 1.7f);
                float alpha = 0.012f + 0.05f * swell;
                a += alpha * (white - a);
                b += alpha * (a - b);
                // A resonant filter for the whistle through the branches.
                float f = 2f * Mathf.Sin(Mathf.PI * (380f + 260f * swell) / Rate);
                low += f * band;
                band += f * (white - low - 0.12f * band);
                s[i] = b * 2.6f * (0.35f + swell) + band * 0.012f * swell * swell;
            }
            return Loop("WindLoop", s);
        }

        /// <summary>0 = close, with a crack; 1 = further off; 2 = a long distant roll.</summary>
        public static AudioClip Thunder(int distance)
        {
            var rng = new System.Random(90 + distance);
            float seconds = 4.5f + distance * 1.2f;
            var s = new float[(int)(Rate * seconds)];
            float a = 0f, b = 0f;
            float alpha = distance == 0 ? 0.05f : distance == 1 ? 0.028f : 0.016f;

            // Several overlapping swells make it roll.
            var peaks = new float[5];
            var sizes = new float[5];
            for (int p = 0; p < peaks.Length; p++)
            {
                peaks[p] = p == 0 ? 0.1f + distance * 0.35f : (float)rng.NextDouble() * seconds * 0.65f;
                sizes[p] = p == 0 ? 1f : 0.3f + 0.6f * (float)rng.NextDouble();
            }
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)Rate;
                float white = Noise(rng);
                a += alpha * (white - a);
                b += alpha * (a - b);
                float envelope = 0f;
                for (int p = 0; p < peaks.Length; p++)
                {
                    float since = t - peaks[p];
                    if (since < -0.08f) continue;
                    float attack = Mathf.Clamp01((since + 0.08f) / (0.08f + distance * 0.2f));
                    envelope += sizes[p] * attack * Mathf.Exp(-Mathf.Max(0f, since) * 1.5f);
                }
                float flutter = 0.7f + 0.6f * Mathf.PerlinNoise(t * 14f, distance);
                float tail = Mathf.Clamp01((seconds - t) / 1f);
                s[i] = b * 7f * envelope * flutter * tail;
                if (distance == 0 && t < 0.6f) s[i] += white * 0.5f * Mathf.Exp(-t * 9f) * (Mathf.PerlinNoise(t * 60f, 0.5f) * 1.6f - 0.3f);
                s[i] = (float)Math.Tanh(s[i]); // soft limit instead of harsh clipping
            }
            return Make("Thunder" + distance, s);
        }

        private static float Noise(System.Random rng) => (float)rng.NextDouble() * 2f - 1f;

        /// <summary>Folds the last half second into the start so the loop has no click.</summary>
        private static AudioClip Loop(string name, float[] raw)
        {
            int fade = Rate / 2;
            var s = new float[raw.Length - fade];
            Array.Copy(raw, s, s.Length);
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                s[i] = raw[i] * Mathf.Sqrt(k) + raw[s.Length + i] * Mathf.Sqrt(1f - k);
            }
            return Make(name, s);
        }

        private static AudioClip Make(string name, float[] samples)
        {
            for (int i = 0; i < samples.Length; i++) samples[i] = Mathf.Clamp(samples[i], -1f, 1f);
            var clip = AudioClip.Create(name, samples.Length, 1, Rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
