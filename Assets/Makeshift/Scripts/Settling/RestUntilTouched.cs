using Autohand;
using UnityEngine;

namespace Makeshift.Settling
{
    /// <summary>
    /// Holds a prop exactly where it was laid, on a slope it would otherwise creep down, until a hand takes it or
    /// something knocks it. From then on it is an ordinary loose body.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class RestUntilTouched : MonoBehaviour
    {
        [SerializeField] private float knockSpeed = 0.8f;

        private Rigidbody body;
        private Grabbable grab;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            grab = GetComponent<Grabbable>();
            body.constraints = RigidbodyConstraints.FreezeAll;
        }

        private void OnEnable()
        {
            if (grab != null) grab.OnBeforeGrabEvent += Taken;
        }

        private void OnDisable()
        {
            if (grab != null) grab.OnBeforeGrabEvent -= Taken;
        }

        private void Taken(Hand hand, Grabbable grabbable) => Release();

        private void OnCollisionEnter(Collision collision)
        {
            if (enabled && collision.relativeVelocity.magnitude > knockSpeed && collision.rigidbody != null) Release();
        }

        private void Release()
        {
            if (!enabled) return;
            body.constraints = RigidbodyConstraints.None;
            body.WakeUp();
            enabled = false;
        }
    }
}
