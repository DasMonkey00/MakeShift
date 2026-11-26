using Autohand;
using Makeshift.Core;
using UnityEngine;
using UnityEngine.Events;

namespace Makeshift.Power
{
    /// <summary>
    /// Where power goes in. Accepts a PowerPlug, or a PowerSource dropped straight in
    /// (a battery shoved into a toaster). Connections are never perfect: each one gets
    /// a random quality, and bad ones flicker. It works, badly.
    /// Needs a trigger Collider on this object.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class PowerPort : MonoBehaviour
    {
        [Header("Connection quality (randomised per connection)")]
        [SerializeField, Range(0f, 1f)] private float minQuality = 0.55f;
        [SerializeField, Range(0f, 1f)] private float maxQuality = 1f;
        [Tooltip("How often a bad connection drops out, per second, at the worst quality.")]
        [SerializeField] private float maxFlickerRate = 3f;
        [Tooltip("Disconnect if the plug moves this far from the port.")]
        [SerializeField] private float breakDistance = 0.25f;

        [Header("Events")]
        public UnityEvent onConnected;
        public UnityEvent onDisconnected;
        public UnityEvent onFlicker;

        public bool IsConnected => source != null;
        public float Quality { get; private set; }

        private PowerSource source;
        private Transform connectedTransform;
        private float connectDistance;
        // The plug seated in this socket. It stays dynamic so a hand can pull it out again.
        private Rigidbody seated;
        private Grabbable seatedGrab;
        private float seatAt;

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (IsConnected) return;

            var plug = other.GetComponentInParent<PowerPlug>();
            if (plug != null && plug.Source != null && plug.ConnectedPort == null)
            {
                plug.ConnectedPort = this;
                Connect(plug.Source, plug.transform);
                Seat(plug.GetComponent<Rigidbody>());
                return;
            }

            var direct = other.GetComponentInParent<PowerSource>();
            if (direct != null) Connect(direct, direct.transform);
        }

        private void Connect(PowerSource s, Transform t)
        {
            source = s;
            connectedTransform = t;
            connectDistance = Vector3.Distance(t.position, transform.position);
            Quality = Random.Range(minQuality, maxQuality);
            onConnected?.Invoke();
        }

        /// <summary>Takes the plug out of the hand and snaps it into the socket.</summary>
        private void Seat(Rigidbody body)
        {
            if (body == null || body.isKinematic) return;
            seated = body;
            seatedGrab = body.GetComponent<Grabbable>();
            HandCheck.Release(body);
            seatAt = Time.time + 0.05f; // give the hand a moment to let go
            body.useGravity = false;
            connectDistance = 0f;
            Makeshift.Audio.PlaceholderSfx.Play(Makeshift.Audio.SfxKind.Crack, transform.position, 0.5f);
        }

        private void FixedUpdate()
        {
            if (seated == null || Time.time < seatAt) return;
            bool pulled = seated.isKinematic || HandCheck.Held(seated) || (seatedGrab != null && seatedGrab.BeingGrabbed())
                || (seated.position - transform.position).sqrMagnitude > 0.09f;
            if (pulled) { Disconnect(); return; }
            seated.position = transform.position;
            seated.rotation = transform.rotation;
            seated.linearVelocity = Vector3.zero;
            seated.angularVelocity = Vector3.zero;
        }

        public void Disconnect()
        {
            if (!IsConnected) return;
            var plug = connectedTransform != null ? connectedTransform.GetComponent<PowerPlug>() : null;
            if (plug != null) plug.ConnectedPort = null;
            if (seated != null) seated.useGravity = true;
            seated = null;
            source = null;
            connectedTransform = null;
            onDisconnected?.Invoke();
        }

        /// <summary>
        /// Called by a machine each frame. Returns 0..1: how well its demand was met.
        /// </summary>
        public float Request(float demandPerSecond, float deltaTime)
        {
            if (!IsConnected || demandPerSecond <= 0f || deltaTime <= 0f) return 0f;

            // A bad connection sometimes drops out for a frame.
            float flickerRate = (1f - Quality) * maxFlickerRate;
            if (Random.value < flickerRate * deltaTime)
            {
                onFlicker?.Invoke();
                return 0f;
            }

            float supplied = source.Draw(demandPerSecond * Quality, deltaTime);
            return Mathf.Clamp01(supplied / (demandPerSecond * deltaTime));
        }

        private void Update()
        {
            if (!IsConnected || (seated != null && Time.time < seatAt + 0.1f)) return;
            if (connectedTransform == null ||
                Vector3.Distance(connectedTransform.position, transform.position) > connectDistance + breakDistance)
            {
                Disconnect();
            }
        }
    }
}
