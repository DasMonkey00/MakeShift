using System.Collections.Generic;
using Autohand;
using Makeshift.Core;
using Makeshift.Survival;
using UnityEngine;
using UnityEngine.Events;

namespace Makeshift.Fire
{
    /// <summary>
    /// A fire you build and keep alive. Anything with the Burns trait that is set or thrown into the pit snaps into
    /// place as fuel and stays there until it burns away. Food that can cook snaps above the fuel and can be taken
    /// back off. Light it with sparks (flint on stone) or something already burning, splash water on it to put it out.
    /// Needs a trigger Collider and a HeatSource on this object.
    /// </summary>
    [RequireComponent(typeof(HeatSource))]
    public class Campfire : MonoBehaviour
    {
        [SerializeField] private float fuelPerItem = 30f;
        [SerializeField] private float maxFuel = 120f;
        [SerializeField] private GameObject flameVisual;
        [Tooltip("Stand-in log pile, shown when there is fuel but no real logs in the pit (wrist menu fuel).")]
        [SerializeField] private GameObject fuelVisual;

        [Tooltip("Puffs of smoke for a few seconds after the fire goes out.")]
        [SerializeField] private ParticleSystem outSmoke;

        [Header("Snapping")]
        [Tooltip("A thing snaps in once it is loose and its centre is this close to the middle of the pit (flat distance).")]
        [SerializeField] private float captureRadius = 0.36f;
        [SerializeField] private float captureHeight = 0.7f;

        [Header("Events")]
        public UnityEvent onLit;
        public UnityEvent onOut;

        public bool IsLit { get; private set; }
        public float Fuel { get; private set; }
        public float Fuel01 => maxFuel > 0f ? Mathf.Clamp01(Fuel / maxFuel) : 0f;
        /// <summary>Real logs (or other fuel) fixed in the pit.</summary>
        public int LogsInPit => logs.Count;
        /// <summary>Food sitting on the fire.</summary>
        public int FoodOnFire => food.Count;

        private HeatSource heat;

        [Tooltip("Grass is kept out of this radius around the fire so the pit stays bare.")]
        [SerializeField] private float grassClearRadius = 1.4f;

        private BoxCollider fuelBed;
        private Light fireLight;

        [Tooltip("Spark hits needed to light an unlit fire. Rolled between min and max when the fire is built.")]
        [SerializeField] private int minStrikes = 3;
        [SerializeField] private int maxStrikes = 6;
        private int strikesNeeded;
        private int strikes;

        // Where fuel goes: two logs along X, two more across them.
        private static readonly Vector3[] FuelSpots =
        {
            new Vector3(0f, 0.08f, -0.08f), new Vector3(0f, 0.08f, 0.08f),
            new Vector3(-0.08f, 0.14f, 0f), new Vector3(0.08f, 0.14f, 0f),
        };
        private static readonly float[] FuelYaw = { 0f, 0f, 90f, 90f };
        // Where food goes: low in the flames, lifted a little when a second layer of logs is under it.
        private static readonly Vector3[] FoodSpots =
        {
            new Vector3(-0.12f, 0.15f, 0f), new Vector3(0.12f, 0.15f, 0f),
            new Vector3(0f, 0.15f, 0.13f), new Vector3(0f, 0.15f, -0.13f),
        };

        private struct Log { public GameObject go; public int spot; }
        private class Food { public Rigidbody body; public Grabbable grab; public Quaternion rotation; public int spot; }

        private readonly List<Log> logs = new List<Log>();   // oldest first
        private readonly List<Food> food = new List<Food>();
        private readonly HashSet<Rigidbody> inPit = new HashSet<Rigidbody>();
        private readonly List<Rigidbody> candidates = new List<Rigidbody>();

        private void Awake()
        {
            heat = GetComponent<HeatSource>();
            // A solid fuel bed while there is fuel, so things land on the fire instead of falling through it.
            var bedGo = transform.Find("FuelBed");
            if (bedGo != null) fuelBed = bedGo.GetComponent<BoxCollider>();
            // The fire lights what is around it. One shadowless point light: cheap enough for Quest.
            var lightGo = transform.Find("FireLight");
            if (lightGo != null) fireLight = lightGo.GetComponent<Light>();
            Apply();
            Makeshift.Rendering.GrassField.Exclude(transform.position, grassClearRadius);
            strikesNeeded = Random.Range(minStrikes, maxStrikes + 1);
        }

        public void AddFuel(float seconds)
        {
            Fuel = Mathf.Min(maxFuel, Fuel + seconds);
            RefreshVisuals();
        }

        /// <summary>A spark hit the fire. It takes a few hits (3 to 6) to catch.</summary>
        public void Strike()
        {
            if (IsLit || Fuel <= 0f) return;
            strikes++;
            if (strikes >= strikesNeeded) Light();
        }

        public void Light()
        {
            if (IsLit || Fuel <= 0f) return;
            IsLit = true;
            if (outSmoke != null) outSmoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Makeshift.Audio.PlaceholderSfx.Play(Makeshift.Audio.SfxKind.Fire, transform.position, 0.6f);
            Apply();
            onLit?.Invoke();
        }

        /// <summary>Water calls this.</summary>
        public void Douse()
        {
            if (!IsLit) return;
            IsLit = false;
            strikes = 0;
            if (outSmoke != null) outSmoke.Play();
            Makeshift.Audio.PlaceholderSfx.Play(Makeshift.Audio.SfxKind.Water, transform.position, 0.7f);
            Apply();
            onOut?.Invoke();
        }

        private void Apply()
        {
            if (heat == null) heat = GetComponent<HeatSource>();
            if (heat != null) heat.Active = IsLit;
            if (flameVisual != null) flameVisual.SetActive(IsLit);
            if (fireLight != null) fireLight.enabled = IsLit;
            RefreshVisuals();
        }

        private void RefreshVisuals()
        {
            if (fuelBed != null) fuelBed.enabled = Fuel > 0.01f;
            if (fuelVisual != null)
            {
                fuelVisual.SetActive(Fuel > 0.01f && logs.Count == 0);
                fuelVisual.transform.localScale = Vector3.one * Mathf.Lerp(0.45f, 1f, Fuel01);
            }
            if (flameVisual != null && IsLit)
                flameVisual.transform.localScale = Vector3.one * Mathf.Lerp(0.55f, 1.15f, Fuel01);
        }

        private void Update()
        {
            if (!IsLit) return;
            if (fireLight != null)
                fireLight.intensity = (0.9f + 0.2f * Mathf.PerlinNoise(Time.time * 3f, 0.5f)) * Mathf.Lerp(0.5f, 1.2f, Fuel01);
            Fuel = Mathf.Max(0f, Fuel - Time.deltaTime);
            BurnAwayLogs();
            if (Fuel <= 0f) { Douse(); return; }
            RefreshVisuals();
        }

        /// <summary>As the fuel runs down, the oldest logs crumble away.</summary>
        private void BurnAwayLogs()
        {
            int keep = Mathf.CeilToInt(Fuel / Mathf.Max(1f, fuelPerItem) - 0.001f);
            while (logs.Count > keep)
            {
                if (logs[0].go != null) Destroy(logs[0].go);
                logs.RemoveAt(0);
            }
        }

        private void FixedUpdate()
        {
            logs.RemoveAll(l => l.go == null);
            HoldFood();

            if (inPit.Count == 0) return;
            inPit.RemoveWhere(b => b == null);
            candidates.Clear();
            candidates.AddRange(inPit); // snapping changes physics state, so walk a copy
            foreach (var body in candidates) TrySnap(body);
        }

        private void OnTriggerEnter(Collider other)
        {
            var burning = other.GetComponentInParent<Flammable>();
            if (burning != null && burning.IsBurning) Light();

            var body = other.attachedRigidbody;
            // Snapped in FixedUpdate once loose: a log held over the pit only counts after release.
            if (body != null) inPit.Add(body);
        }

        private void OnTriggerExit(Collider other)
        {
            var body = other.attachedRigidbody;
            if (body != null) inPit.Remove(body);
        }

        private void TrySnap(Rigidbody body)
        {
            if (body.isKinematic || HandCheck.Held(body) || HandCheck.PlayerBody(body)) return;
            var grab = body.GetComponent<Grabbable>();
            if (grab != null && grab.BeingGrabbed()) return;

            Vector3 d = body.worldCenterOfMass - transform.position;
            if (d.y > captureHeight || d.x * d.x + d.z * d.z > captureRadius * captureRadius) return;

            if (body.GetComponent<Cookable>() != null) { SnapFood(body, grab); return; }

            if (body.GetComponent<Edible>() != null || body.GetComponent<NotFuel>() != null) return;
            if (!body.TryGetComponent(out TraitHolder holder) || !holder.Has(Trait.Burns)) return;
            if (body.GetComponentInParent<Campfire>() != null) return;
            // Something with other things glued or lashed to it is a tool someone made, not firewood.
            if (body.GetComponentsInChildren<TraitHolder>().Length > 1) return;
            SnapFuel(body, grab);
        }

        /// <summary>Fixes a piece of fuel in the pit. It cannot be picked up again.</summary>
        private void SnapFuel(Rigidbody body, Grabbable grab)
        {
            int spot = FreeSpot(FuelSpots.Length, i => logs.Exists(l => l.spot == i), FuelSpots, body.position);
            if (spot < 0) return;

            var flam = body.GetComponent<Flammable>();
            bool wasBurning = flam != null && flam.IsBurning;
            if (flam != null) flam.Contain(); // the campfire does the burning now; the log spreads nothing of its own
            if (grab != null) grab.isGrabbable = false;

            Vector3 pos = transform.TransformPoint(FuelSpots[spot]);
            Quaternion rot = transform.rotation * Quaternion.Euler(0f, FuelYaw[spot], 0f);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.interpolation = RigidbodyInterpolation.None;
            body.isKinematic = true;
            body.transform.SetPositionAndRotation(pos, rot);
            body.position = pos;
            body.rotation = rot;

            logs.Add(new Log { go = body.gameObject, spot = spot });
            inPit.Remove(body);
            AddFuel(fuelPerItem);
            if (wasBurning) Light();
        }

        /// <summary>
        /// Holds food in the flames. The body stays dynamic (Auto Hand refuses to grab a kinematic one) and is
        /// simply put back in its spot every physics step until a hand or a thief takes it.
        /// </summary>
        private void SnapFood(Rigidbody body, Grabbable grab)
        {
            if (food.Exists(f => f.body == body)) return;
            int spot = FreeSpot(FoodSpots.Length, i => food.Exists(f => f.spot == i), FoodSpots, body.position);
            if (spot < 0) return;
            var item = new Food { body = body, grab = grab, spot = spot, rotation = Quaternion.Euler(0f, body.rotation.eulerAngles.y, 0f) };
            food.Add(item);
            body.useGravity = false;
            Pin(item);
        }

        private void HoldFood()
        {
            for (int i = food.Count - 1; i >= 0; i--)
            {
                var item = food[i];
                var body = item.body;
                if (body == null) { food.RemoveAt(i); continue; }
                bool taken = body.isKinematic || HandCheck.Held(body) || (item.grab != null && item.grab.BeingGrabbed())
                    || (body.position - FoodPoint(item.spot)).sqrMagnitude > 0.09f;
                if (taken)
                {
                    body.useGravity = true;
                    food.RemoveAt(i);
                    continue;
                }
                Pin(item);
            }
        }

        private Vector3 FoodPoint(int spot)
        {
            return transform.TransformPoint(FoodSpots[spot] + Vector3.up * (logs.Count > 2 ? 0.06f : 0f));
        }

        private void Pin(Food item)
        {
            item.body.position = FoodPoint(item.spot);
            item.body.rotation = item.rotation;
            item.body.linearVelocity = Vector3.zero;
            item.body.angularVelocity = Vector3.zero;
        }

        /// <summary>The free spot nearest to where the thing came in, or -1 if all are taken.</summary>
        private int FreeSpot(int count, System.Predicate<int> taken, Vector3[] spots, Vector3 from)
        {
            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (taken(i)) continue;
                float d = (transform.TransformPoint(spots[i]) - from).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }
    }
}
