using Autohand;
using UnityEngine;

namespace Makeshift.Audio
{
    /// <summary>
    /// Soft footsteps: one step for every stride the player's body covers on the ground, from the stick or from
    /// walking in the room. The step sounds are generated in code.
    /// </summary>
    public class Footsteps : MonoBehaviour
    {
        [SerializeField] private AutoHandPlayer player;
        [SerializeField] private float stride = 0.75f;
        [SerializeField] private float volume = 0.11f;

        private const int Rate = 22050;
        private AudioClip[] steps;
        private AudioSource source;
        private Vector3 last;
        private float travelled;
        private int previous = -1;

        private void Start()
        {
            if (player == null) player = GetComponentInChildren<AutoHandPlayer>(true);
            if (player == null) { enabled = false; return; }

            var go = new GameObject("FootstepAudio");
            go.transform.SetParent(player.transform, false);
            source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0.6f;
            source.dopplerLevel = 0f;

            steps = new AudioClip[4];
            for (int i = 0; i < steps.Length; i++) steps[i] = Build(i);
            last = player.transform.position;
        }

        private void Update()
        {
            Vector3 now = player.transform.position;
            Vector3 move = now - last;
            last = now;
            move.y = 0f;
            float distance = move.magnitude;

            if (distance > 0.6f) { travelled = 0f; return; } // a teleport, not a walk
            if (distance < 0.0015f || !player.IsGrounded()) return;

            travelled += distance;
            if (travelled < stride) return;
            travelled = 0f;

            int pick = Random.Range(0, steps.Length - 1);
            if (pick >= previous && previous >= 0) pick++; // never the same step twice running
            previous = pick;
            source.pitch = Random.Range(0.9f, 1.1f);
            source.PlayOneShot(steps[pick], volume * Random.Range(0.8f, 1f));
        }

        /// <summary>A dull heel thud with a faint brush of grass after it.</summary>
        private static AudioClip Build(int variant)
        {
            var rng = new System.Random(100 + variant);
            int length = (int)(0.2f * Rate);
            var samples = new float[length];
            float low = 0f, mid = 0f;
            float thudAlpha = 0.04f + 0.008f * variant;
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)Rate;
                float white = (float)rng.NextDouble() * 2f - 1f;
                low += thudAlpha * (white - low);
                mid += 0.35f * (white - mid);
                float thud = low * 3.2f * Mathf.Min(1f, t / 0.008f) * Mathf.Exp(-t * 32f);
                float brush = (mid - low) * 0.22f * Mathf.Sin(Mathf.PI * Mathf.Clamp01((t - 0.02f) / 0.15f));
                samples[i] = Mathf.Clamp(thud + brush, -1f, 1f);
            }
            var clip = AudioClip.Create("Step" + variant, length, 1, Rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
