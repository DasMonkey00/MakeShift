using System.Collections;
using Makeshift.Core;
using UnityEngine;

namespace Makeshift.Crafting
{
    /// <summary>
    /// A sticky coating from the glue tub. Dipping a stick shows a glue blob on the dipped end, so you can see it is
    /// glued. While wet, the next loose thing this object touches becomes part of it, even if that thing is in your
    /// other hand. One coating at a time: a glued stick takes no more glue. The blob stays on after a join.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Glue : MonoBehaviour
    {
        [Tooltip("Seconds the coating stays wet after dipping.")]
        [SerializeField] private float wetSeconds = 30f;
        [SerializeField] private float blobRadius = 0.035f;

        private static Material blobMaterial;
        private float wetUntil;
        private bool joined;
        private Rigidbody body;
        private GameObject blob;
        private Coroutine joining;

        public bool Wet => Time.time < wetUntil;

        /// <summary>True once this object has something glued to it. Vines only lash objects that are glued.</summary>
        public bool Joined => joined;

        /// <summary>Already carrying glue, wet or used, so the tub adds no more.</summary>
        public bool Coated => Wet || joined;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
        }

        /// <summary>Dipped at a world point: coats the object and shows the blob there. False if it already has glue.</summary>
        public bool Coat(Vector3 worldPoint)
        {
            if (Coated) return false;
            wetUntil = Time.time + wetSeconds;
            if (blob == null) ShowBlob(worldPoint);
            else blob.transform.position = worldPoint;
            return true;
        }

        private void Update()
        {
            // A dipped-but-unused coating dries up and the blob goes with it. A used one keeps its blob.
            if (!joined && !Wet && blob != null) Destroy(blob);
        }

        private void ShowBlob(Vector3 worldPoint)
        {
            blob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            blob.name = "GlueBlob";
            Object.Destroy(blob.GetComponent<Collider>());
            blob.transform.SetParent(transform, true);
            blob.transform.position = worldPoint;
            blob.transform.localScale = Vector3.one * blobRadius * 2f;
            if (blobMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
                blobMaterial = new Material(shader) { color = new Color(0.95f, 0.78f, 0.25f) };
                if (blobMaterial.HasProperty("_BaseColor")) blobMaterial.SetColor("_BaseColor", new Color(0.95f, 0.78f, 0.25f));
            }
            blob.GetComponent<Renderer>().sharedMaterial = blobMaterial;
            blob.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void OnCollisionEnter(Collision collision) { TryJoin(collision); }

        // Stay as well: the two may already be touching when the first try is not allowed.
        private void OnCollisionStay(Collision collision) { TryJoin(collision); }

        /// <summary>The glue gave way: the coating is gone and the stick can be dipped again.</summary>
        public void ComeUnstuck()
        {
            joined = false;
            wetUntil = 0f;
            if (blob != null) Destroy(blob);
        }

        private void TryJoin(Collision collision)
        {
            var other = collision.rigidbody;
            if (joining != null || !Wet || joined || !CanJoin(other)) return;
            Vector3 contact = collision.contactCount > 0 ? collision.GetContact(0).point : other.worldCenterOfMass;
            joining = StartCoroutine(Join(other, transform.InverseTransformPoint(contact)));
        }

        private bool CanJoin(Rigidbody other)
        {
            if (other == null || other == body || other.isKinematic) return false;
            if (HandCheck.PlayerBody(other)) return false;                       // never glue to hands or head
            if (other.GetComponentInChildren<GlueTub>() != null) return false;   // the tub is not glued to what is dipped in it
            if (other.GetComponent<StickyBinder>() != null) return false;        // a vine wraps; it does not get glued
            if (other.GetComponent<Glue>() is Glue g && g.Wet) return false;     // two wet things do not join here
            return true;
        }

        private IEnumerator Join(Rigidbody other, Vector3 contactLocal)
        {
            // Pressing a held thing onto the glue is how you build: the hand lets go and the thing stays on.
            HandCheck.Release(other);
            // Merge after the physics step: bodies cannot be taken apart inside a collision callback.
            yield return new WaitForFixedUpdate();
            joining = null;
            if (!Wet || !CanJoin(other) || HandCheck.Held(other)) yield break;

            wetUntil = 0f; // used up
            joined = true;
            // Glued means one piece: the other thing becomes part of this body. No joint, so no wobble or double weight.
            // The bond remembers where they met: that is where a vine wraps, and where weak glue gives way.
            GlueBond.Join(body, other, transform.TransformPoint(contactLocal));
        }
    }
}
