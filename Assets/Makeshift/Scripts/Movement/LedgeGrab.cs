using System.Collections.Generic;
using Autohand;
using Makeshift.Audio;
using UnityEngine;

namespace Makeshift.Movement
{
    /// <summary>
    /// The top edge of a climbing wall. Reach a hand over the edge and hold it: that is the grab. Then pull the hand
    /// down toward your hip. Once most of the pull is done, the survivor is pushed up and over onto the platform.
    /// The player's own pull does the work; the push only finishes it. Let go before that and nothing happens.
    /// Needs a trigger BoxCollider on this object; it should cover the wall top and the strip under it.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class LedgeGrab : MonoBehaviour
    {
        [Tooltip("Player root to move. Found automatically when empty.")]
        [SerializeField] private Transform playerRoot;
        [Tooltip("Height of the top surface the survivor lands on (feet).")]
        [SerializeField] private float topY = 4.02f;
        [Tooltip("Z the survivor lands at: just past the wall edge, on the platform.")]
        [SerializeField] private float landZ = -10.8f;
        [Tooltip("Hand x is clamped to this range when choosing where to land.")]
        [SerializeField] private float lateralLimit = 1.8f;
        [Tooltip("A hand must be this close to the top edge (or above it) to grab.")]
        [SerializeField] private float grabHeight = 3.75f;
        [Tooltip("Seconds a hand has to stay on the edge before it counts as a grab.")]
        [SerializeField] private float holdSeconds = 0.1f;
        [Tooltip("Metres of downward hand pull needed before the push takes over.")]
        [SerializeField] private float pullNeeded = 0.12f;
        [Tooltip("Seconds the push takes once it starts.")]
        [SerializeField] private float pushSeconds = 0.5f;
        [Tooltip("How high the survivor lifts over the edge during the push.")]
        [SerializeField] private float liftHeight = 0.3f;
        [Tooltip("Seconds before another grab is allowed.")]
        [SerializeField] private float cooldown = 1.5f;

        private enum State { Idle, Hanging, Pushing }

        private struct Touch
        {
            public float first;
            public float last;
        }

        private readonly Dictionary<Hand, Touch> touches = new Dictionary<Hand, Touch>();
        private readonly List<Hand> stale = new List<Hand>();
        private State state;
        private Hand gripHand;
        private float lastHandY;
        private float pulled;
        private Vector3 gripAt;
        private float gripTime;
        private float nextAllowed;
        private float pushStart;
        private Vector3 from;
        private Vector3 to;

        private void Awake()
        {
            if (playerRoot == null)
            {
                var player = FindAnyObjectByType<AutoHandPlayer>();
                if (player != null) playerRoot = player.transform;
            }
        }

        private void OnTriggerStay(Collider other)
        {
            var hand = other.GetComponentInParent<Hand>();
            if (hand == null) return;
            if (!touches.TryGetValue(hand, out var t)) t = new Touch { first = Time.time };
            t.last = Time.time;
            touches[hand] = t;
        }

        private void Update()
        {
            if (playerRoot == null) return;

            // Forget hands that left the edge.
            stale.Clear();
            foreach (var kv in touches)
                if (Time.time - kv.Value.last > 0.15f) stale.Add(kv.Key);
            foreach (var h in stale) touches.Remove(h);

            switch (state)
            {
                case State.Idle:
                    if (Time.time < nextAllowed) return;
                    foreach (var kv in touches)
                    {
                        if (kv.Value.last - kv.Value.first < holdSeconds) continue;
                        if (kv.Key.transform.position.y < grabHeight) continue; // only a hand over the edge grabs
                        BeginHanging(kv.Key);
                        return;
                    }
                    break;

                case State.Hanging:
                    // The hand may leave the zone while pulling down. Give up only if it wanders off or takes too long.
                    if (Vector3.Distance(gripHand.transform.position, gripAt) > 0.9f || Time.time - gripTime > 4f)
                    {
                        state = State.Idle;
                        gripHand = null;
                        nextAllowed = Time.time + 0.3f;
                        return;
                    }
                    // Downward movement of the gripping hand is the pull. Upward movement does not count.
                    float y = gripHand.transform.position.y;
                    pulled += Mathf.Max(0f, lastHandY - y);
                    lastHandY = y;
                    if (pulled >= pullNeeded) BeginPush();
                    break;

                case State.Pushing:
                    float t = Mathf.Clamp01((Time.time - pushStart) / pushSeconds);
                    float e = t * t * (3f - 2f * t); // ease in and out
                    Vector3 p = Vector3.Lerp(from, to, e);
                    p.y += liftHeight * Mathf.Sin(t * Mathf.PI);
                    playerRoot.position = p;
                    if (t >= 1f)
                    {
                        state = State.Idle;
                        gripHand = null;
                        nextAllowed = Time.time + cooldown;
                    }
                    break;
            }
        }

        private void BeginHanging(Hand hand)
        {
            state = State.Hanging;
            gripHand = hand;
            lastHandY = hand.transform.position.y;
            pulled = 0f;
            gripAt = hand.transform.position;
            gripTime = Time.time;
            PlaceholderSfx.Play(SfxKind.Bonk, hand.transform.position, 0.4f);
        }

        private void BeginPush()
        {
            // Land under the gripping hand, clamped to the platform, just past the edge.
            float x = Mathf.Clamp(gripHand.transform.position.x, -lateralLimit, lateralLimit);
            from = playerRoot.position;
            to = new Vector3(x, topY, landZ);
            pushStart = Time.time;
            state = State.Pushing;
            touches.Clear();
            PlaceholderSfx.Play(SfxKind.Jump, from, 0.6f);
        }
    }
}
