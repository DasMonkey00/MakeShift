using UnityEngine;

namespace Makeshift.Audio
{
    /// <summary>
    /// A looping fire sound on a flame object. It plays while the flame is switched on and gets louder with the
    /// flame's scale. The clip is generated in code, once, and shared by every fire.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class FireCrackle : MonoBehaviour
    {
        [Tooltip("Loudness at flame scale 1 (a full campfire).")]
        [SerializeField] private float volume = 0.55f;
        [SerializeField] private float minDistance = 0.6f;
        [SerializeField] private float maxDistance = 14f;

        private const int Rate = 22050;
        private const float Seconds = 4f;
        private static AudioClip shared;
        private AudioSource source;

        public void Configure(float loudness) => volume = loudness;

        private void OnEnable()
        {
            if (source == null) source = GetComponent<AudioSource>();
            if (shared == null) shared = Build();
            source.clip = shared;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.dopplerLevel = 0f;
            source.pitch = Random.Range(0.92f, 1.08f);
            Fit();
            source.time = Random.Range(0f, Seconds - 0.1f); // two fires never crackle in step
            source.Play();
        }

        private void Update() => Fit();

        private void Fit() => source.volume = volume * Mathf.Clamp(transform.lossyScale.x, 0.3f, 1.2f);

        /// <summary>A soft low rumble with random pops. The end is crossfaded into the start so the loop has no click.</summary>
        private static AudioClip Build()
        {
            var rng = new System.Random(7);
            int length = (int)(Seconds * Rate);
            int fade = Rate / 4;
            var raw = new float[length + fade];

            float low = 0f, hiss = 0f;
            for (int i = 0; i < raw.Length; i++)
            {
                float white = (float)rng.NextDouble() * 2f - 1f;
                low += 0.03f * (white - low);
                float high = white - hiss;
                hiss = white;
                float sway = 0.75f + 0.25f * Mathf.PerlinNoise(i / (float)Rate * 3f, 0.3f);
                raw[i] = (low * 0.9f + high * 0.008f) * sway;
            }

            // Pops: very short bright bursts, mostly small, now and then a loud snap.
            for (int p = 0; p < (int)(Seconds * 14); p++)
            {
                int start = rng.Next(0, raw.Length - Rate / 20);
                float strength = Mathf.Pow((float)rng.NextDouble(), 3f) * 0.75f + 0.05f;
                int n = rng.Next(Rate / 500, Rate / 60);
                float previous = 0f;
                for (int i = 0; i < n; i++)
                {
                    float white = (float)rng.NextDouble() * 2f - 1f;
                    float envelope = Mathf.Exp(-5f * i / n);
                    raw[start + i] += (white - previous * 0.6f) * 0.6f * strength * envelope;
                    previous = white;
                }
            }

            var samples = new float[length];
            for (int i = 0; i < length; i++)
            {
                float s = raw[i];
                if (i < fade)
                {
                    float k = i / (float)fade;
                    s = raw[i] * k + raw[length + i] * (1f - k);
                }
                samples[i] = Mathf.Clamp(s, -1f, 1f);
            }

            var clip = AudioClip.Create("FireLoop", length, 1, Rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
