using System.Collections;
using Makeshift.Audio;
using Makeshift.Fire;
using UnityEngine;

namespace Makeshift.Rendering
{
    /// <summary>
    /// Fades the scene into a storm and back: the light darkens and storm cloud closes over, then the wind picks up,
    /// then rain starts and puts the fires out, with lightning in the cloud and thunder after it.
    ///
    /// The sounds are generated in code. Assign clips here to replace them with recorded ones.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class StormController : MonoBehaviour
    {
        [SerializeField] private ToonLookSwitcher looks;
        [SerializeField] private Light sun;
        [SerializeField] private Camera head;
        [SerializeField] private float fadeSeconds = 14f;

        [Header("Sky and rain")]
        [SerializeField] private Renderer cloudDome;
        [SerializeField] private ParticleSystem rain;
        [SerializeField] private float rainRate = 900f;
        [Tooltip("Leaves torn loose around the player while the wind is up.")]
        [SerializeField] private ParticleSystem blownLeaves;
        [SerializeField] private float blownLeafRate = 18f;
        [Tooltip("Direction the wind blows towards, flat on the ground.")]
        [SerializeField] private Vector3 windDirection = new Vector3(1f, 0f, 0.35f);

        [Header("Scene particles")]
        [Tooltip("Falling leaf systems: more and faster in wind.")]
        [SerializeField] private ParticleSystem[] leaves;
        [Tooltip("Fireflies, sun shafts and the like: stop when it rains.")]
        [SerializeField] private ParticleSystem[] fairWeather;

        [Tooltip("Unlit haze that would glow in the dark: dimmed with the sky.")]
        [SerializeField] private ParticleSystem[] haze;

        [Header("Fire")]
        [SerializeField] private Campfire campfire;

        [Header("Sound (empty = generated)")]
        [SerializeField] private AudioClip rainClip;
        [SerializeField] private AudioClip windClip;
        [SerializeField] private AudioClip[] thunderClips;

        /// <summary>True while the storm is on or building.</summary>
        public bool Active { get; private set; }
        /// <summary>0 = fair weather, 1 = full storm.</summary>
        public float Amount { get; private set; }

        private static readonly int StormId = Shader.PropertyToID("_Storm");
        private static readonly int FlashId = Shader.PropertyToID("_Flash");
        private static readonly int FlashDirId = Shader.PropertyToID("_FlashDir");
        private static readonly int HorizonId = Shader.PropertyToID("_Horizon");
        private static readonly int WindOffsetId = Shader.PropertyToID("_MkWindOffset");
        private static readonly int WindBoostId = Shader.PropertyToID("_MkWindBoost");
        private static readonly int StormDarkId = Shader.PropertyToID("_MkStormDark");

        private MaterialPropertyBlock block;
        private AudioSource rainSource, windSource, thunderSource;
        private float[] leafRates;
        private Color[] hazeColors;
        private Flammable[] flammables;
        private float windOffset, flash, nextStrike, nextFireCheck;
        private Vector3 flashDirection = Vector3.up;
        private bool applied, strikeRunning;

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            leafRates = new float[leaves != null ? leaves.Length : 0];
            for (int i = 0; i < leafRates.Length; i++)
                if (leaves[i] != null) leafRates[i] = leaves[i].emission.rateOverTimeMultiplier;
            hazeColors = new Color[haze != null ? haze.Length : 0];
            for (int i = 0; i < hazeColors.Length; i++)
                if (haze[i] != null) hazeColors[i] = haze[i].main.startColor.color;
            SetRate(rain, 0f);
            SetRate(blownLeaves, 0f);
            if (cloudDome != null) cloudDome.enabled = false;
        }

        private void DimHaze(float dark, float flashNow)
        {
            for (int i = 0; i < hazeColors.Length; i++)
            {
                if (haze[i] == null) continue;
                float light = Mathf.Clamp01(Mathf.Lerp(1f, 0.16f, dark) + 0.6f * flashNow);
                var c = hazeColors[i] * new Color(light, light, light * Mathf.Lerp(1f, 1.4f, dark), 1f);
                var main = haze[i].main;
                main.startColor = c;
            }
        }

        private void OnDisable()
        {
            // Shader globals outlive play mode in the editor.
            Shader.SetGlobalFloat(WindOffsetId, 0f);
            Shader.SetGlobalFloat(WindBoostId, 0f);
            Shader.SetGlobalFloat(StormDarkId, 0f);
        }

        public void Toggle() => SetStorm(!Active);

        public void SetStorm(bool on)
        {
            Active = on;
            if (on && rainSource == null) BuildAudio();
        }

        private static float Phase(float amount, float from, float to) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, amount));

        private void LateUpdate()
        {
            Amount = Mathf.MoveTowards(Amount, Active ? 1f : 0f, Time.deltaTime / Mathf.Max(0.1f, fadeSeconds));
            if (Amount <= 0f)
            {
                if (applied) Clear();
                return;
            }
            applied = true;

            float dark = Phase(Amount, 0f, 0.55f);
            float wind = Phase(Amount, 0.2f, 0.75f);
            float wet = Phase(Amount, 0.45f, 0.95f);
            float gust = 0.6f + 0.8f * Mathf.PerlinNoise(Time.time * 0.35f, 4.2f);

            Weather(wind, wet, gust);
            Lightning();
            Sky(dark);
        }

        private void Weather(float wind, float wet, float gust)
        {
            // The foliage shaders run their sway on this extra time, so it speeds up without jumping.
            windOffset += Time.deltaTime * wind * 2.5f;
            Shader.SetGlobalFloat(WindOffsetId, windOffset);
            Shader.SetGlobalFloat(WindBoostId, wind * gust);

            Vector3 at = head != null ? head.transform.position : transform.position;
            Vector3 blow = windDirection.normalized;

            if (rain != null)
            {
                rain.transform.position = at + Vector3.up * 7f - blow * (2.5f * wind);
                SetRate(rain, rainRate * wet);
                var velocity = rain.velocityOverLifetime;
                velocity.x = blow.x * 4.5f * wind * gust;
                velocity.y = -13f;
                velocity.z = blow.z * 4.5f * wind * gust;
            }
            if (blownLeaves != null)
            {
                blownLeaves.transform.position = at + Vector3.up * 1.5f - blow * 9f;
                SetRate(blownLeaves, blownLeafRate * wind * gust);
                var velocity = blownLeaves.velocityOverLifetime;
                velocity.x = blow.x * 5f * gust;
                velocity.y = -0.4f;
                velocity.z = blow.z * 5f * gust;
            }

            for (int i = 0; i < leafRates.Length; i++)
            {
                if (leaves[i] == null) continue;
                var emission = leaves[i].emission;
                emission.rateOverTimeMultiplier = leafRates[i] * (1f + 3f * wind);
                var main = leaves[i].main;
                main.simulationSpeed = 1f + 1.8f * wind * gust;
            }
            SetFairWeather(wet < 0.2f);

            if (wet > 0.35f && Time.time >= nextFireCheck) Douse();

            if (rainSource != null)
            {
                rainSource.volume = 0.55f * wet;
                windSource.volume = 0.5f * wind * Mathf.Clamp(gust, 0.5f, 1.3f);
                windSource.pitch = 0.9f + 0.2f * gust;
            }
        }

        private void Douse()
        {
            nextFireCheck = Time.time + 0.5f;
            if (campfire != null && campfire.IsLit) campfire.Douse();
            if (flammables == null) flammables = FindObjectsByType<Flammable>(FindObjectsInactive.Exclude);
            foreach (var f in flammables)
                if (f != null) f.Extinguish(3f); // stays wet while the rain goes on
        }

        private void Lightning()
        {
            if (!Active || Amount < 0.65f)
            {
                nextStrike = Time.time + 1.5f;
                return;
            }
            if (!strikeRunning && Time.time >= nextStrike) StartCoroutine(Strike());
        }

        private IEnumerator Strike()
        {
            strikeRunning = true;
            float distance = Random.value;
            float azimuth = Random.Range(0f, 360f);
            flashDirection = Quaternion.Euler(-Random.Range(18f, 55f), azimuth, 0f) * Vector3.forward;

            int pulses = Random.Range(2, 5);
            for (int i = 0; i < pulses; i++)
            {
                float peak = Mathf.Lerp(1f, 0.4f, distance) * Random.Range(0.6f, 1f);
                float length = Random.Range(0.08f, 0.22f);
                for (float t = 0f; t < length; t += Time.deltaTime)
                {
                    flash = peak * (1f - t / length);
                    yield return null;
                }
                flash = 0f;
                yield return new WaitForSeconds(Random.Range(0.03f, 0.12f));
            }

            // Light first, then sound: the further off, the longer the wait and the duller the rumble.
            yield return new WaitForSeconds(0.25f + distance * 2.5f);
            if (thunderSource != null && thunderClips != null && thunderClips.Length > 0)
            {
                int pick = Mathf.Min(thunderClips.Length - 1, (int)(distance * thunderClips.Length));
                thunderSource.pitch = Random.Range(0.85f, 1.1f);
                thunderSource.PlayOneShot(thunderClips[pick], Mathf.Lerp(1f, 0.55f, distance) * Mathf.Clamp01(Amount));
            }
            nextStrike = Time.time + Random.Range(3f, 10f);
            strikeRunning = false;
        }

        private void Sky(float dark)
        {
            var p = looks != null ? looks.CurrentPreset : null;
            if (p == null) return;
            var flashColor = new Color(0.75f, 0.82f, 1f);
            Shader.SetGlobalFloat(StormDarkId, Mathf.Clamp01(dark - 0.6f * flash));
            DimHaze(dark, flash);

            // Night is already dim: the storm never makes a preset brighter than it was.
            float brightness = Mathf.Min(1f, p.sunColor.maxColorComponent * p.sunIntensity / 1.4f);
            Color fog = Color.Lerp(p.fogColor, new Color(0.17f, 0.2f, 0.24f) * Mathf.Lerp(0.55f, 1f, brightness), dark);

            if (sun != null)
            {
                sun.color = Color.Lerp(Color.Lerp(p.sunColor, new Color(0.5f, 0.6f, 0.75f), dark), flashColor, flash);
                sun.intensity = Mathf.Lerp(p.sunIntensity, p.sunIntensity * 0.28f, dark) + 3f * flash;
            }
            Color add = flashColor * (0.7f * flash);
            RenderSettings.ambientSkyColor = Color.Lerp(p.ambientSky, p.ambientSky * new Color(0.3f, 0.36f, 0.47f), dark) + add;
            RenderSettings.ambientEquatorColor = Color.Lerp(p.ambientEquator, p.ambientEquator * new Color(0.3f, 0.38f, 0.5f), dark) + add;
            RenderSettings.ambientGroundColor = Color.Lerp(p.ambientGround, p.ambientGround * 0.35f, dark) + add * 0.4f;
            RenderSettings.fogColor = fog + flashColor * (0.3f * flash);
            RenderSettings.fogDensity = Mathf.Lerp(p.fogDensity, Mathf.Max(p.fogDensity, 0.028f), dark);
            RenderSettings.fogStartDistance = Mathf.Lerp(p.fogStart, 3f, dark);
            RenderSettings.fogEndDistance = Mathf.Lerp(p.fogEnd, 65f, dark);

            if (cloudDome != null)
            {
                cloudDome.enabled = true;
                cloudDome.transform.position = head != null ? head.transform.position : transform.position;
                block.SetFloat(StormId, dark);
                block.SetFloat(FlashId, flash);
                block.SetVector(FlashDirId, flashDirection);
                block.SetColor(HorizonId, RenderSettings.fog ? RenderSettings.fogColor : fog);
                cloudDome.SetPropertyBlock(block);
            }
        }

        private void Clear()
        {
            applied = false;
            flash = 0f;
            StopAllCoroutines();
            strikeRunning = false;
            SetRate(rain, 0f);
            SetRate(blownLeaves, 0f);
            if (cloudDome != null) cloudDome.enabled = false;
            for (int i = 0; i < leafRates.Length; i++)
            {
                if (leaves[i] == null) continue;
                var emission = leaves[i].emission;
                emission.rateOverTimeMultiplier = leafRates[i];
                var main = leaves[i].main;
                main.simulationSpeed = 1f;
            }
            SetFairWeather(true);
            DimHaze(0f, 0f);
            Shader.SetGlobalFloat(WindBoostId, 0f);
            Shader.SetGlobalFloat(StormDarkId, 0f);
            if (rainSource != null) rainSource.volume = windSource.volume = 0f;
            if (looks != null) looks.ReapplyLighting();
        }

        private void SetFairWeather(bool on)
        {
            if (fairWeather == null) return;
            foreach (var ps in fairWeather)
            {
                if (ps == null) continue;
                var emission = ps.emission;
                emission.enabled = on;
            }
        }

        private static void SetRate(ParticleSystem ps, float rate)
        {
            if (ps == null) return;
            var emission = ps.emission;
            emission.rateOverTime = rate;
            if (rate > 0f && !ps.isPlaying) ps.Play();
        }

        private void BuildAudio()
        {
            if (rainClip == null) rainClip = StormSounds.Rain();
            if (windClip == null) windClip = StormSounds.Wind();
            if (thunderClips == null || thunderClips.Length == 0)
                thunderClips = new[] { StormSounds.Thunder(0), StormSounds.Thunder(1), StormSounds.Thunder(2) };
            rainSource = Source("RainAudio", rainClip, true);
            windSource = Source("WindAudio", windClip, true);
            thunderSource = Source("ThunderAudio", null, false);
        }

        private AudioSource Source(string name, AudioClip clip, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.spatialBlend = 0f;
            source.playOnAwake = false;
            source.loop = loop;
            source.clip = clip;
            source.volume = loop ? 0f : 1f;
            if (loop) source.Play();
            return source;
        }
    }
}
