using System;
using System.Collections.Generic;
using UnityEngine;

namespace Makeshift.Engine
{
    /// <summary>
    /// Keeps the awake loose rigidbodies near the players under a cap.
    ///
    /// Inside the bubble (metres around the nearest viewer point), the nearest bodies stay dynamic, up to the cap.
    /// Beyond the bubble, a body that has come to rest turns kinematic, which makes it a static collider: it costs
    /// no simulation until a player comes back inside the bubble edge minus the hysteresis. Moving bodies are never
    /// frozen mid-flight; they freeze once they settle.
    ///
    /// Exempt bodies (hands, networked objects, see <see cref="IsExempt"/>) are never changed. Held bodies
    /// (<see cref="IsHeld"/>) are kept awake so they never sleep mid-grab.
    ///
    /// Each client runs this on its own loose scenery. Networked objects are exempt, so nothing here fights
    /// network sync. Bodies that start kinematic are never tracked.
    /// </summary>
    public class PhysicsBubble : MonoBehaviour
    {
        /// <summary>Bodies this bubble never changes: hands and networked objects. Set by the hand integration.</summary>
        public static Func<Rigidbody, bool> IsExempt;
        /// <summary>Bodies a player is holding. Set by the hand integration.</summary>
        public static Func<Rigidbody, bool> IsHeld;

        private const float RescanSeconds = 2f;
        /// <summary>Below this speed (m/s, or rad/s for spin) a body counts as at rest.</summary>
        private const float RestSpeed = 0.05f;

        private sealed class Entry
        {
            public Rigidbody body;
            public float distance;
            public bool frozen;
        }

        private static readonly Comparison<Entry> ByDistance = (a, b) => a.distance.CompareTo(b.distance);

        private readonly List<Entry> entries = new List<Entry>(256);
        private readonly HashSet<Rigidbody> known = new HashSet<Rigidbody>();
        private float decideTimer, rescanTimer;

        /// <summary>Radius in metres, cap, hysteresis and decision interval; set from the budget profile.</summary>
        public float Radius = 20f;
        public int Cap = 40;
        public float Hysteresis = 3f;
        public float Interval = 0.25f;

        public bool On { get; private set; } = true;
        public int Tracked => entries.Count;
        /// <summary>Loose bodies awake right now (not frozen, not sleeping).</summary>
        public int AwakeCount { get; private set; }
        /// <summary>Loose bodies frozen by the bubble right now.</summary>
        public int Frozen { get; private set; }

        public void Configure(BudgetProfile profile)
        {
            if (profile == null) return;
            Radius = profile.bubbleRadius;
            Cap = profile.rigidbodyCap;
            Hysteresis = Mathf.Min(profile.hysteresis, Radius * 0.5f);
            Interval = Mathf.Max(0.05f, profile.bubbleInterval);
            SetOn(profile.bubbleOn && Cap > 0);
        }

        public void SetOn(bool on)
        {
            if (On == on) return;
            On = on;
            if (!on) ThawAll();
        }

        public void SetCap(int cap)
        {
            Cap = Mathf.Max(0, cap);
            SetOn(Cap > 0);
        }

        /// <summary>Put a body under the bubble now, rather than waiting for the next scan.</summary>
        public void Register(Rigidbody body)
        {
            if (body == null || body.isKinematic || !known.Add(body)) return;
            entries.Add(new Entry { body = body });
        }

        private void OnDisable() => ThawAll();

        private void Update()
        {
            rescanTimer -= Time.deltaTime;
            if (rescanTimer <= 0f)
            {
                rescanTimer = RescanSeconds;
                Rescan();
            }

            decideTimer -= Time.deltaTime;
            if (decideTimer > 0f) return;
            decideTimer = Interval;
            // Without the hand integration nothing says which bodies are hands, so the bubble stays off rather than freeze them.
            if (On && IsExempt != null) Decide();
        }

        private void Rescan()
        {
            var found = FindObjectsByType<Rigidbody>();
            foreach (var body in found) Register(body);
        }

        private void Decide()
        {
            if (Viewers.Count == 0) return;

            // Drop bodies that were destroyed since the last pass.
            int write = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.body == null) continue;
                entries[write++] = e;
            }
            if (write != entries.Count)
            {
                entries.RemoveRange(write, entries.Count - write);
                known.Clear();
                foreach (var e in entries) known.Add(e.body);
            }

            foreach (var e in entries) e.distance = Viewers.NearestDistance(e.body.position);
            entries.Sort(ByDistance);

            int inside = 0, awake = 0, frozen = 0;
            foreach (var e in entries)
            {
                var body = e.body;
                if (!body.gameObject.activeInHierarchy) continue; // pooled away
                if (e.frozen && !body.isKinematic) e.frozen = false; // someone else woke it (a reused pool object)
                if (IsExempt != null && IsExempt(body))
                {
                    if (e.frozen) Thaw(e);
                    if (!body.isKinematic && IsHeld != null && IsHeld(body) && body.IsSleeping()) body.WakeUp();
                }
                else
                {
                    bool wantFrozen;
                    if (e.distance > Radius) wantFrozen = true;
                    else if (e.frozen && e.distance > Radius - Hysteresis) wantFrozen = true;
                    else
                    {
                        inside++;
                        wantFrozen = inside > Cap;
                    }

                    // Only a body at rest is frozen; a moving one is left to land first.
                    if (wantFrozen && !e.frozen && !AtRest(body)) wantFrozen = false;
                    if (wantFrozen && !e.frozen) Freeze(e);
                    else if (!wantFrozen && e.frozen) Thaw(e);
                }

                if (e.frozen) frozen++;
                else if (!body.isKinematic && !body.IsSleeping()) awake++;
            }
            AwakeCount = awake;
            Frozen = frozen;
        }

        private static bool AtRest(Rigidbody body)
        {
            return body.IsSleeping()
                || (body.linearVelocity.sqrMagnitude < RestSpeed * RestSpeed
                    && body.angularVelocity.sqrMagnitude < RestSpeed * RestSpeed);
        }

        private static void Freeze(Entry e)
        {
            e.body.linearVelocity = Vector3.zero;
            e.body.angularVelocity = Vector3.zero;
            e.body.isKinematic = true;
            e.frozen = true;
        }

        private static void Thaw(Entry e)
        {
            e.body.isKinematic = false;
            e.body.WakeUp();
            e.frozen = false;
        }

        private void ThawAll()
        {
            foreach (var e in entries)
                if (e.frozen && e.body != null) Thaw(e);
            Frozen = 0;
        }
    }
}
