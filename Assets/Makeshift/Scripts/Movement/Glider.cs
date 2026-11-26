using UnityEngine;

namespace Makeshift.Movement
{
    /// <summary>
    /// An umbrella (or any big flappy thing). While open and attached to a body, it caps the fall
    /// speed and turns some of the fall into forward drift. Works on any Rigidbody, so a thrown umbrella
    /// glider and a player both work. The hand system sets Target when a player holds it.
    /// </summary>
    public class Glider : MonoBehaviour
    {
        [Tooltip("Falls no faster than this while open.")]
        [SerializeField] private float maxFallSpeed = 0.8f;
        [Tooltip("How quickly it brakes the fall.")]
        [SerializeField] private float brake = 40f;
        [Tooltip("Forward acceleration per m/s of braked fall.")]
        [SerializeField] private float forwardPerBrakedSpeed = 0.5f;
        [SerializeField] private float maxForwardSpeed = 6f;
        [SerializeField] private GameObject canopy;
        [Tooltip("Shown while the umbrella is closed (the furled look). Optional.")]
        [SerializeField] private GameObject furled;
        [SerializeField] private bool startOpen = false;

        public Rigidbody Target { get; set; }
        public bool IsOpen { get; private set; }

        private void Awake()
        {
            SetOpen(startOpen);
            if (Target == null) Target = GetComponent<Rigidbody>();
        }

        public void SetOpen(bool open)
        {
            IsOpen = open;
            if (canopy != null) canopy.SetActive(open);
            if (furled != null) furled.SetActive(!open);
        }

        public void Toggle() { SetOpen(!IsOpen); }

        private void FixedUpdate()
        {
            if (!IsOpen || Target == null || Target.isKinematic) return;

            Vector3 v = Target.linearVelocity;
            if (v.y >= -maxFallSpeed) return;

            float excess = -maxFallSpeed - v.y; // negative number of m/s too fast
            float newY = Mathf.MoveTowards(v.y, -maxFallSpeed, brake * Time.fixedDeltaTime);
            float braked = newY - v.y;
            v.y = newY;

            // Some of the braked fall becomes drift along the canopy's facing, capped.
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude > 0.0001f && excess > 0f)
            {
                fwd.Normalize();
                float horizontal = Vector3.Dot(new Vector3(v.x, 0f, v.z), fwd);
                if (horizontal < maxForwardSpeed)
                    v += fwd * (braked * forwardPerBrakedSpeed);
            }
            Target.linearVelocity = v;
        }
    }
}
