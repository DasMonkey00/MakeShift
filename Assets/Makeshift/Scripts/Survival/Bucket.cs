using System.Collections.Generic;
using Makeshift.Audio;
using Makeshift.Core;
using Makeshift.Fire;
using UnityEngine;

namespace Makeshift.Survival
{
    /// <summary>
    /// A bucket you can fill and tip. Hold it in water and it fills. Tip it and the water runs out over the low side
    /// of the rim: a fuller bucket spills sooner and a steeper tilt pours faster. The water is a stream of drops that
    /// fall, hit things, splash, run off them and push loose props. Enough of it on a campfire puts it out.
    ///
    /// The drops are a small simulation of our own (no rigidbodies), drawn through the bucket's particle system.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Bucket : MonoBehaviour
    {
        [Tooltip("Up vector dotted with world up below which a full bucket spills.")]
        [SerializeField] private float pourUpThreshold = 0.45f;
        [SerializeField] private float checkRadius = 0.2f;
        [Tooltip("Seconds in the water to fill up: a dip is enough.")]
        [SerializeField] private float dipSeconds = 0.15f;
        [Tooltip("Drops that must land on a campfire to put it out.")]
        [SerializeField] private int dropsToDouse = 12;

        private const int MaxDrops = 90;
        private const float DropRadius = 0.012f;
        private const float DropsPerBucket = 110f;

        private struct Drop
        {
            public Vector3 position, velocity;
            public float life, size;
            public bool landed;
        }

        // Serialized so the builder's wiring is saved with the scene; without this the water showed on an empty bucket.
        [SerializeField] private GameObject surface;
        [SerializeField] private ParticleSystem stream;

        /// <summary>0 empty .. 1 brim full.</summary>
        public float Fill01 { get; private set; }
        public bool Full => Fill01 >= 0.999f;
        public bool Pouring { get; private set; }
        public int DropCount => drops.Count;

        private readonly List<Drop> drops = new List<Drop>(MaxDrops);
        private readonly Dictionary<Campfire, int> onFire = new Dictionary<Campfire, int>();
        private ParticleSystem.Particle[] drawn;
        private static readonly Collider[] Hits = new Collider[16];
        private static readonly RaycastHit[] Casts = new RaycastHit[8];
        private Rigidbody body;
        private Vector3 surfaceScale, surfacePos;
        private Quaternion surfaceRot;
        private Vector3 rimLocal = new Vector3(0f, 0.12f, 0f);
        private float rimRadius = 0.07f;
        private float nextGlug, nextSplashSound, owed, refillAfter;
        private Campfire[] fires;

        /// <summary>The water surface inside the pail and the particle system that draws the drops. Both are optional.</summary>
        public void SetVisuals(GameObject waterSurface, ParticleSystem pourStream)
        {
            surface = waterSurface;
            stream = pourStream;
            if (stream == null) return;
            var main = stream.main;
            main.playOnAwake = false;
            stream.Stop();
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            if (TryGetComponent(out BoxCollider box))
            {
                rimLocal = box.center + Vector3.up * (box.size.y * 0.5f);
                rimRadius = Mathf.Min(box.size.x, box.size.z) * 0.45f;
            }
            if (surface != null)
            {
                surfaceScale = surface.transform.localScale;
                surfacePos = surface.transform.localPosition;
                surfaceRot = surface.transform.localRotation;
                surface.SetActive(false);
            }
            if (stream == null) return;
            // The system only draws: this script decides where every drop is.
            stream.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = stream.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;
            main.maxParticles = MaxDrops;
            var emission = stream.emission;
            emission.enabled = false;
            drawn = new ParticleSystem.Particle[MaxDrops];
        }

        /// <summary>Fills the bucket at once (tests and debugging).</summary>
        public void SetLevel(float level) => Fill01 = Mathf.Clamp01(level);

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            bool wet = InWater();
            Pouring = false;

            if (wet)
            {
                // The cooldown stops a bucket that was just emptied over water from refilling on the next frame.
                if (!Full && Time.time >= refillAfter)
                {
                    if (Time.time >= nextGlug)
                    {
                        nextGlug = Time.time + 0.55f;
                        PlaceholderSfx.Play(SfxKind.Fill, transform.position, 0.6f);
                    }
                    Fill01 = Mathf.Min(1f, Fill01 + dt / Mathf.Max(0.02f, dipSeconds));
                    if (Full) PlaceholderSfx.Play(SfxKind.Water, transform.position, 0.8f);
                }
            }
            else if (Fill01 > 0f)
            {
                float tilt = Vector3.Dot(transform.up, Vector3.up);
                // Full to the brim it spills at a slight tilt; nearly empty it has to go right over.
                float spillAt = Mathf.Lerp(-0.1f, pourUpThreshold + 0.25f, Fill01);
                if (tilt < spillAt) Pour(Mathf.Clamp01((spillAt - tilt) / 0.8f), dt);
            }
            Simulate(dt);
        }

        private void Pour(float steepness, float dt)
        {
            Pouring = true;
            float outflow = Mathf.Lerp(0.25f, 1.1f, steepness); // buckets per second
            float amount = Mathf.Min(Fill01, outflow * dt);
            Fill01 -= amount;
            refillAfter = Time.time + 1f;
            owed += amount * DropsPerBucket;

            // The lip is the lowest point of the rim.
            Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, transform.up);
            downhill = downhill.sqrMagnitude < 0.001f ? transform.forward : downhill.normalized;
            Vector3 lip = transform.TransformPoint(rimLocal) + downhill * (rimRadius * transform.lossyScale.x);
            Vector3 flow = body.GetPointVelocity(lip) + downhill * (0.25f + 0.5f * steepness)
                           + transform.up * (0.2f + 0.6f * steepness);

            while (owed >= 1f)
            {
                owed -= 1f;
                if (drops.Count >= MaxDrops) drops.RemoveAt(0);
                Vector3 jitter = Random.insideUnitSphere;
                drops.Add(new Drop
                {
                    position = lip + jitter * 0.012f + flow * (Random.value * dt),
                    velocity = flow + jitter * 0.1f,
                    life = 1.6f,
                    size = Random.Range(0.028f, 0.048f),
                });
            }
            if (Time.time >= nextGlug)
            {
                nextGlug = Time.time + 0.45f;
                PlaceholderSfx.Play(SfxKind.Water, lip, 0.35f);
            }
        }

        private void Simulate(float dt)
        {
            for (int i = drops.Count - 1; i >= 0; i--)
            {
                Drop d = drops[i];
                d.life -= dt;
                d.velocity += Physics.gravity * dt;
                Vector3 step = d.velocity * dt;
                float distance = step.magnitude;
                if (distance > 0.0001f && FirstHit(d.position, step / distance, distance, out RaycastHit hit))
                {
                    d.position = hit.point + hit.normal * DropRadius;
                    float into = Vector3.Dot(d.velocity, hit.normal);
                    Vector3 along = d.velocity - hit.normal * into;
                    if (!d.landed)
                    {
                        d.landed = true;
                        d.life = Mathf.Min(d.life, Random.Range(0.35f, 0.9f));
                        Land(hit, d.velocity);
                        // Splash: part of the fall turns into spray sideways.
                        along += Vector3.ProjectOnPlane(Random.insideUnitSphere, hit.normal) * (Mathf.Abs(into) * 0.35f);
                        d.velocity = along * 0.8f - hit.normal * (into * 0.22f);
                    }
                    else d.velocity = along * 0.9f; // then it clings and runs along the surface
                }
                else d.position += step;

                if (d.life <= 0f || d.position.y < -5f) drops.RemoveAt(i);
                else drops[i] = d;
            }
        }

        private bool FirstHit(Vector3 from, Vector3 direction, float distance, out RaycastHit first)
        {
            first = default;
            float best = float.MaxValue;
            int count = Physics.SphereCastNonAlloc(from, DropRadius, direction, Casts, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var h = Casts[i];
                // Distance 0 means the drop started inside it (leaving the lip); its own bucket never counts.
                if (h.distance <= 0f || h.distance >= best || h.collider.attachedRigidbody == body) continue;
                if (HandCheck.PlayerBody(h.collider.attachedRigidbody)) continue;
                best = h.distance;
                first = h;
            }
            return best < float.MaxValue;
        }

        private void Land(RaycastHit hit, Vector3 velocity)
        {
            var rb = hit.collider.attachedRigidbody;
            if (rb != null && !rb.isKinematic) rb.AddForceAtPosition(velocity * 0.004f, hit.point, ForceMode.Impulse);

            var burning = hit.collider.GetComponentInParent<Flammable>();
            if (burning != null && burning.IsBurning) burning.Extinguish();
            var machine = hit.collider.GetComponentInParent<Machines.Machine>();
            if (machine != null) machine.Douse();

            if (fires == null) fires = FindObjectsByType<Campfire>();
            foreach (var fire in fires)
            {
                if (fire == null || !fire.IsLit) continue;
                Vector3 off = hit.point - fire.transform.position;
                if (new Vector2(off.x, off.z).magnitude > 0.42f || off.y > 0.6f) continue;
                onFire.TryGetValue(fire, out int n);
                if (++n >= dropsToDouse) { fire.Douse(); n = 0; }
                onFire[fire] = n;
            }
            if (Time.time >= nextSplashSound)
            {
                nextSplashSound = Time.time + 0.3f;
                PlaceholderSfx.Play(SfxKind.Squish, hit.point, 0.25f);
            }
        }

        private void LateUpdate()
        {
            if (surface != null)
            {
                // The water sinks as it pours and stays nearly level while the pail tilts round it.
                surface.SetActive(Fill01 > 0.02f);
                float wide = Mathf.Lerp(0.7f, 1f, Fill01);
                surface.transform.localScale = Vector3.Scale(surfaceScale, new Vector3(wide, 1f, wide));
                surface.transform.localPosition = new Vector3(surfacePos.x, Mathf.Lerp(surfacePos.y * 0.3f, surfacePos.y, Fill01), surfacePos.z);
                Quaternion tilted = transform.rotation * surfaceRot;
                Quaternion level = Quaternion.FromToRotation(transform.up, Vector3.up) * tilted;
                surface.transform.rotation = Quaternion.RotateTowards(tilted, level, 28f);
            }
            if (stream == null || drawn == null) return;
            if (drops.Count == 0)
            {
                if (stream.isPlaying) stream.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                return;
            }
            if (!stream.isPlaying) stream.Play();
            for (int i = 0; i < drops.Count; i++)
            {
                Drop d = drops[i];
                drawn[i] = new ParticleSystem.Particle
                {
                    position = d.position,
                    velocity = Vector3.zero,
                    startSize = d.size,
                    startColor = new Color32(90, 165, 235, 255),
                    // The system shrinks particles as they age: keep them young until they are about to vanish.
                    startLifetime = 1f,
                    remainingLifetime = Mathf.Clamp(d.life * 1.6f, 0.05f, 0.75f),
                    randomSeed = (uint)(i + 1),
                };
            }
            stream.SetParticles(drawn, drops.Count);
        }

        private bool InWater()
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position, checkRadius, Hits, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
                if (Hits[i].GetComponentInParent<WaterVolume>() != null) return true;
            return false;
        }
    }
}
