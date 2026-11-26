// Compiles only once Photon Fusion 2 is installed (its weaver defines FUSION_WEAVER).
// UNTESTED: written against the Fusion 2 Shared-mode API without Fusion in the project.
#if FUSION_WEAVER
using System.Collections;
using Autohand;
using Fusion;
using UnityEngine;

namespace Makeshift.Network
{
    /// <summary>
    /// Auto Hand grab -> Fusion state authority (Shared mode).
    /// Put next to Grabbable on any object players can pick up, with a NetworkObject and
    /// NetworkRigidbody3D (or NetworkTransform). Highlight (hover) requests authority early to
    /// hide latency; the last release keeps authority until the object comes to rest.
    /// Our own pattern; not copied from anyone's file.
    /// </summary>
    [RequireComponent(typeof(Grabbable), typeof(NetworkObject))]
    public class NetworkGrabbable : NetworkBehaviour
    {
        [Tooltip("Request authority when a hand hovers over it, not only on grab.")]
        [SerializeField] private bool requestOnHighlight = true;
        [Tooltip("After the last release, wait until speed is below this before letting go of authority.")]
        [SerializeField] private float restSpeed = 0.05f;
        [SerializeField] private float restSeconds = 0.5f;
        [Tooltip("Give authority back at rest. Off = keep it with the last holder.")]
        [SerializeField] private bool releaseAtRest = false;

        private Grabbable grabbable;
        private Rigidbody body;
        private Coroutine restRoutine;

        private void Awake()
        {
            grabbable = GetComponent<Grabbable>();
            body = GetComponent<Rigidbody>();
        }

        public override void Spawned()
        {
            grabbable.OnGrabEvent += OnGrab;
            grabbable.OnReleaseEvent += OnRelease;
            if (requestOnHighlight) grabbable.OnHighlightEvent += OnHighlight;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            grabbable.OnGrabEvent -= OnGrab;
            grabbable.OnReleaseEvent -= OnRelease;
            grabbable.OnHighlightEvent -= OnHighlight;
        }

        private void OnHighlight(Hand hand, Grabbable g) => TakeAuthority();

        private void OnGrab(Hand hand, Grabbable g)
        {
            if (restRoutine != null) { StopCoroutine(restRoutine); restRoutine = null; }
            TakeAuthority();
        }

        private void OnRelease(Hand hand, Grabbable g)
        {
            // Auto Hand fires release per hand; only act once nobody holds it.
            if (grabbable.GetHeldBy().Count > 0 || !releaseAtRest) return;
            if (restRoutine != null) StopCoroutine(restRoutine);
            restRoutine = StartCoroutine(ReleaseWhenAtRest());
        }

        private void TakeAuthority()
        {
            if (Object != null && !Object.HasStateAuthority) Object.RequestStateAuthority();
        }

        private IEnumerator ReleaseWhenAtRest()
        {
            float still = 0f;
            while (still < restSeconds)
            {
                if (grabbable.GetHeldBy().Count > 0) yield break;
                still = body.linearVelocity.magnitude < restSpeed ? still + Time.deltaTime : 0f;
                yield return null;
            }
            if (Object != null && Object.HasStateAuthority) Object.ReleaseStateAuthority();
            restRoutine = null;
        }
    }
}
#endif
