using System;
using Makeshift.Combat;
using UnityEngine;

namespace Makeshift.Creatures
{
    public enum ScavengerMode { Wander, Chase, Flee, Stunned, Dead }

    /// <summary>
    /// The first creature: a thieving little scavenger. It wanders, spots loose Scavengable items,
    /// grabs one and runs for its den. Bonk it hard and it drops the loot and is stunned for a moment.
    /// It stashes stolen goods at the den, so everything can be recovered.
    /// Needs a Damageable and a kinematic Rigidbody + collider (it moves by transform).
    /// </summary>
    [RequireComponent(typeof(Damageable))]
    public class ScavengerCreature : MonoBehaviour
    {
        /// <summary>Optional hook for the hand system: true while a body is held by a player.</summary>
        public static Func<Rigidbody, bool> IsHeld;

        [SerializeField] private Transform den;
        [SerializeField] private Transform mouth;
        [SerializeField] private float walkSpeed = 1f;
        [SerializeField] private float runSpeed = 2.5f;
        [SerializeField] private float sightRange = 6f;
        [SerializeField] private float wanderRadius = 4f;
        [SerializeField] private float grabReach = 0.4f;
        [SerializeField] private float stunSeconds = 1.5f;
        [Tooltip("A hit at least this hard knocks the loot loose.")]
        [SerializeField] private float dropDamage = 2f;

        public ScavengerMode Mode { get; private set; } = ScavengerMode.Wander;
        public bool HasLoot => carried != null;
        public Scavengable Loot => carried;
        public Transform Mouth => mouth;

        private Damageable health;
        private Scavengable target, carried;
        private RigidbodyInterpolation heldInterpolation;
        private CollisionDetectionMode heldDetection;
        private Vector3 gripCentre, pickedFrom;
        private Quaternion gripRotation, pickedRotation;
        private float pickedAt;
        private Vector3 home, wanderTarget;
        private float modeTimer;

        /// <summary>Own physics layer, so raiders never shove players (see SetupCollisions).</summary>
        public const string CreatureLayer = "Creature";
        private static readonly string[] NoPushLayers = { "HandPlayer", "Hand" };

        private void Awake()
        {
            health = GetComponent<Damageable>();
            home = transform.position;
            if (mouth == null) mouth = transform;
            else PutMouthInFront();
            health.onHit.AddListener(OnHit);
            health.onDied.AddListener(OnDied);
            PickWanderTarget();
            SetupCollisions();
        }

        /// <summary>The loot is carried just ahead of the body, where it can be seen, not inside the head.</summary>
        private void PutMouthInFront()
        {
            var capsule = GetComponent<CapsuleCollider>();
            if (capsule == null || mouth.parent != transform || capsule.direction != 2) return;
            Vector3 p = mouth.localPosition;
            p.z = Mathf.Max(p.z, capsule.center.z + capsule.height * 0.5f + 0.05f);
            mouth.localPosition = p;
        }

        /// <summary>
        /// A raider is a kinematic body moved by its transform, so any dynamic body it touches
        /// gets shoved. The player's body and hands therefore ignore its collider. Loose items
        /// (grabbable, held or thrown) still collide, so bonking a raider still works.
        /// </summary>
        private void SetupCollisions()
        {
            int creature = LayerMask.NameToLayer(CreatureLayer);
            if (creature < 0)
            {
                Debug.LogWarning($"[Creature] layer '{CreatureLayer}' is missing; add it in Tags and Layers.", this);
                return;
            }
            SetLayerRecursive(transform, creature);
            foreach (var name in NoPushLayers)
            {
                int l = LayerMask.NameToLayer(name);
                if (l >= 0) Physics.IgnoreLayerCollision(creature, l, true);
            }
        }

        private static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform child in t) SetLayerRecursive(child, layer);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            modeTimer -= dt;

            // A player can pull the loot out of its mouth.
            if (carried != null && IsHeld != null && IsHeld(carried.Body))
            {
                var taken = carried;
                carried = null;
                Release(taken);
            }

            switch (Mode)
            {
                case ScavengerMode.Wander: TickWander(dt); break;
                case ScavengerMode.Chase: TickChase(dt); break;
                case ScavengerMode.Flee: TickFlee(dt); break;
                case ScavengerMode.Stunned:
                    if (modeTimer <= 0f) Mode = carried != null ? ScavengerMode.Flee : ScavengerMode.Wander;
                    break;
            }
        }

        private void TickWander(float dt)
        {
            if (modeTimer <= 0f || MoveToward(wanderTarget, walkSpeed, dt, 0.2f)) PickWanderTarget();

            target = FindLoot();
            if (target != null) Mode = ScavengerMode.Chase;
        }

        private void TickChase(float dt)
        {
            if (target == null || !Available(target)) { target = null; Mode = ScavengerMode.Wander; return; }
            // Reach is measured from the body's middle, so it grows with the creature. Its own body passes over the
            // item on the way in; otherwise it nudged the meat away just before it could take it.
            IgnoreOwnColliders(target, true);
            if (MoveToward(target.transform.position, runSpeed, dt, grabReach * Mathf.Max(1f, transform.localScale.x))) Grab(target);
        }

        private void TickFlee(float dt)
        {
            Vector3 denPos = den != null ? den.position : home;
            if (MoveToward(denPos, runSpeed, dt, 0.3f))
            {
                var stashed = carried;
                Drop(false);
                if (stashed != null) stashed.IgnoredUntil = Time.time + 20f; // not picked straight back up
                Mode = ScavengerMode.Wander;
                PickWanderTarget();
            }
        }

        private void Grab(Scavengable item)
        {
            carried = item;
            target = null;
            item.Holder = this;
            heldInterpolation = item.Body.interpolation;
            heldDetection = item.Body.collisionDetectionMode;
            // Interpolation would keep writing the old physics pose over ours, so the item lagged and jittered.
            item.Body.interpolation = RigidbodyInterpolation.None;
            item.Body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            item.Body.isKinematic = true;
            IgnoreOwnColliders(item, true);
            MeasureGrip(item);
            pickedAt = Time.time;
            pickedFrom = item.transform.position;
            pickedRotation = item.transform.rotation;
            Mode = ScavengerMode.Flee;
        }

        /// <summary>Held crosswise, like a dog with a bone: the long side across the mouth, centred on it.</summary>
        private void MeasureGrip(Scavengable item)
        {
            Transform t = item.transform;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            foreach (var r in item.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is LineRenderer || !r.enabled) continue;
                Bounds b = r.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = t.InverseTransformPoint(r.transform.TransformPoint(corner));
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                }
            }
            if (min.x > max.x) { min = max = Vector3.zero; }
            Vector3 size = max - min;
            gripCentre = (min + max) * 0.5f;
            Vector3 longAxis = size.x >= size.y && size.x >= size.z ? Vector3.right : size.y >= size.z ? Vector3.up : Vector3.forward;
            gripRotation = Quaternion.FromToRotation(longAxis, Vector3.right);
        }

        /// <summary>Keeps the loot in the mouth. Runs after movement so it never trails behind.</summary>
        private void LateUpdate()
        {
            if (carried == null) return;
            Quaternion rotation = mouth.rotation * gripRotation;
            Vector3 position = mouth.position - rotation * Vector3.Scale(gripCentre, carried.transform.lossyScale);
            float k = Mathf.Clamp01((Time.time - pickedAt) / 0.2f); // lifted into the mouth, not popped there
            carried.transform.SetPositionAndRotation(Vector3.Lerp(pickedFrom, position, k),
                Quaternion.Slerp(pickedRotation, rotation, k));
        }

        private void IgnoreOwnColliders(Scavengable item, bool ignore)
        {
            if (item == null) return;
            foreach (var mine in GetComponentsInChildren<Collider>())
                foreach (var theirs in item.GetComponentsInChildren<Collider>())
                    if (mine != theirs && mine.enabled && theirs.enabled) Physics.IgnoreCollision(mine, theirs, ignore);
        }

        private void Release(Scavengable item)
        {
            item.Holder = null;
            item.Body.isKinematic = false;
            item.Body.interpolation = heldInterpolation;
            item.Body.collisionDetectionMode = heldDetection;
            IgnoreOwnColliders(item, false);
        }

        private void OnDestroy()
        {
            if (carried != null) { Release(carried); carried = null; }
        }

        /// <summary>Let go of whatever it carries (used when a raider leaves at dawn).</summary>
        public void DropLoot() => Drop(false);

        private void Drop(bool knockedLoose)
        {
            if (carried == null) return;
            var item = carried;
            carried = null;
            Release(item);
            Vector3 push = knockedLoose ? Vector3.up * 2f + UnityEngine.Random.insideUnitSphere : Vector3.up * 0.3f;
            item.Body.AddForce(push, ForceMode.VelocityChange);
        }

        private void OnHit(float amount)
        {
            if (health.IsDead) return;
            if (amount >= dropDamage) Drop(true);
            Mode = ScavengerMode.Stunned;
            modeTimer = stunSeconds;
        }

        private void OnDied()
        {
            Drop(true);
            Mode = ScavengerMode.Dead;
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 90f);
        }

        // ---------- helpers ----------

        private Scavengable FindLoot()
        {
            Scavengable best = null;
            float bestDist = sightRange;
            foreach (var s in Scavengable.All)
            {
                if (!Available(s)) continue;
                float d = Vector3.Distance(transform.position, s.transform.position);
                if (d < bestDist) { bestDist = d; best = s; }
            }
            return best;
        }

        private bool Available(Scavengable s)
        {
            if (s == null || s == carried || s.Holder != null || s.Body.isKinematic || Time.time < s.IgnoredUntil) return false;
            return IsHeld == null || !IsHeld(s.Body);
        }

        private void PickWanderTarget()
        {
            Vector2 r = UnityEngine.Random.insideUnitCircle * wanderRadius;
            wanderTarget = home + new Vector3(r.x, 0f, r.y);
            modeTimer = UnityEngine.Random.Range(3f, 6f);
        }

        /// <summary>Flat-ground movement. Returns true on arrival.</summary>
        private bool MoveToward(Vector3 point, float speed, float dt, float stopDistance)
        {
            Vector3 flat = point - transform.position;
            flat.y = 0f;
            if (flat.magnitude <= stopDistance) return true;
            Vector3 dir = flat.normalized;
            transform.position += dir * (speed * dt);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 10f * dt);
            return false;
        }
    }
}
