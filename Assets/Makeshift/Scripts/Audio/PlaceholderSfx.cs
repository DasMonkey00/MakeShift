using System;
using System.Collections.Generic;
using UnityEngine;

namespace Makeshift.Audio
{
    /// <summary>The interactions that get a placeholder sound while we test. Replace the clips later.</summary>
    public enum SfxKind { Flint, Fire, Water, Jump, Bonk, Drill, Cook, Glop, Crack, Break, Chomp, Squish, Sizzle, Done, Fill }

    /// <summary>
    /// Placeholder sounds, generated in code so there are no audio files to import yet.
    /// Each kind is built once (a few milliseconds of samples) and played at a world position.
    /// Good enough to tell interactions apart in playtests; swap in real clips per kind later.
    /// </summary>
    public static class PlaceholderSfx
    {
        private const int Rate = 22050;
        private static readonly Dictionary<SfxKind, AudioClip> clips = new Dictionary<SfxKind, AudioClip>();

        private static readonly Dictionary<SfxKind, float> lastPlayed = new Dictionary<SfxKind, float>();

        /// <summary>Shortest gap between two plays of the same kind. Stops repeats from stacking and distorting.</summary>
        private static float MinGap(SfxKind kind) => kind == SfxKind.Water ? 0.45f : 0.12f;

        public static void Play(SfxKind kind, Vector3 position, float volume = 0.8f)
        {
            if (lastPlayed.TryGetValue(kind, out float last) && Time.unscaledTime - last < MinGap(kind)) return;
            lastPlayed[kind] = Time.unscaledTime;

            if (!clips.TryGetValue(kind, out var clip) || clip == null)
            {
                clip = Build(kind);
                clips[kind] = clip;
            }
            AudioSource.PlayClipAtPoint(clip, position, volume);
        }

        private static AudioClip Build(SfxKind kind)
        {
            var rng = new System.Random(kind.GetHashCode() & 0x7fffffff);
            float seconds;
            float[] s;
            switch (kind)
            {
                case SfxKind.Flint:
                    seconds = 0.08f;
                    s = Noise(rng, seconds, t => Mathf.Exp(-t * 60f) * 0.9f);
                    break;
                case SfxKind.Fire:
                    seconds = 0.7f;
                    s = LowPassNoise(rng, seconds, 0.04f, t => 0.35f + 0.25f * Mathf.Sin(t * 9f));
                    break;
                case SfxKind.Water:
                    seconds = 0.5f;
                    // A glugg: a falling pitch with bubbling pulses, not hiss. Quieter, so it does not clip when repeated.
                    s = Sweep(seconds, 260f, 90f, t => (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 9f * t)) * Mathf.Exp(-t * 5f) * 0.45f);
                    break;
                case SfxKind.Jump:
                    seconds = 0.2f;
                    s = Sweep(seconds, 220f, 440f, t => Mathf.Exp(-t * 14f) * 0.5f);
                    break;
                case SfxKind.Bonk:
                    seconds = 0.25f;
                    s = Tone(seconds, 120f, t => Mathf.Exp(-t * 18f) * 0.8f);
                    break;
                case SfxKind.Drill:
                    seconds = 0.5f;
                    s = Buzz(seconds, 90f, 25f);
                    break;
                case SfxKind.Glop:
                    seconds = 0.35f;
                    s = Sweep(seconds, 260f, 110f, t => Mathf.Exp(-t * 10f) * 0.6f);
                    break;
                case SfxKind.Cook:
                    seconds = 0.8f;
                    s = HighPassNoise(rng, seconds, t => 0.25f + 0.1f * Mathf.Sin(t * 30f));
                    break;
                case SfxKind.Crack:
                    // A sharp clack of stone: a short burst of bright noise.
                    seconds = 0.12f;
                    s = HighPassNoise(rng, seconds, t => Mathf.Exp(-t * 35f) * 0.9f);
                    break;
                case SfxKind.Break:
                    // Crumbling: rougher, longer noise that stutters as the pieces part.
                    seconds = 0.45f;
                    s = LowPassNoise(rng, seconds, 0.25f, t => Mathf.Exp(-t * 7f) * (0.6f + 0.4f * Mathf.Sin(t * 90f)) * 0.6f);
                    break;
                case SfxKind.Chomp:
                    // Two quick crunching bites.
                    seconds = 0.3f;
                    s = LowPassNoise(rng, seconds, 0.18f, t => (Mathf.Exp(-t * 30f) + (t > 0.14f ? Mathf.Exp(-(t - 0.14f) * 30f) : 0f)) * 0.8f);
                    break;
                case SfxKind.Squish:
                    seconds = 0.22f;
                    s = LowPassNoise(rng, seconds, 0.09f, t => Mathf.Exp(-t * 16f) * (0.7f + 0.3f * Mathf.Sin(t * 140f)) * 0.8f);
                    break;
                case SfxKind.Sizzle:
                    // Crackling fat: bright noise with random pops.
                    seconds = 0.9f;
                    s = HighPassNoise(rng, seconds, t => 0.12f + 0.2f * Mathf.Pow(Mathf.Abs(Mathf.Sin(t * 37f) * Mathf.Sin(t * 13f)), 6f));
                    break;
                case SfxKind.Done:
                    // One loud burst of sizzle that dies away: the meat is ready.
                    seconds = 1.1f;
                    s = HighPassNoise(rng, seconds, t => Mathf.Exp(-t * 3f) * 0.95f);
                    break;
                case SfxKind.Fill:
                    // Glugging water: a rising pitch with bubbles.
                    seconds = 0.55f;
                    s = Sweep(seconds, 130f, 300f, t => (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 11f * t)) * 0.5f * Mathf.Sin(Mathf.PI * t / 0.55f));
                    break;
                default:
                    seconds = 0.1f;
                    s = new float[Mathf.CeilToInt(seconds * Rate)];
                    break;
            }
            var clip = AudioClip.Create(kind.ToString(), s.Length, 1, Rate, false);
            clip.SetData(s, 0);
            return clip;
        }

        private static float[] Noise(System.Random rng, float seconds, Func<float, float> env)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                s[i] = ((float)rng.NextDouble() * 2f - 1f) * env(t);
            }
            return s;
        }

        private static float[] LowPassNoise(System.Random rng, float seconds, float alpha, Func<float, float> env)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var s = new float[n];
            float y = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                y += alpha * (((float)rng.NextDouble() * 2f - 1f) - y);
                s[i] = y * 3f * env(t);
            }
            return s;
        }

        private static float[] HighPassNoise(System.Random rng, float seconds, Func<float, float> env)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var s = new float[n];
            float prev = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float x = (float)rng.NextDouble() * 2f - 1f;
                s[i] = (x - prev) * 0.5f * env(t);
                prev = x;
            }
            return s;
        }

        private static float[] Tone(float seconds, float hz, Func<float, float> env)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                s[i] = Mathf.Sin(2f * Mathf.PI * hz * t) * env(t);
            }
            return s;
        }

        private static float[] Sweep(float seconds, float fromHz, float toHz, Func<float, float> env)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var s = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float hz = Mathf.Lerp(fromHz, toHz, t / seconds);
                phase += 2f * Mathf.PI * hz / Rate;
                s[i] = Mathf.Sin(phase) * env(t);
            }
            return s;
        }

        private static float[] Buzz(float seconds, float hz, float wobbleHz)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float square = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * hz * t));
                float wobble = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * wobbleHz * t);
                s[i] = square * 0.25f * wobble;
            }
            return s;
        }
    }
}
