using Makeshift.Audio;
using Makeshift.Core;
using UnityEngine;

namespace Makeshift.Crafting
{
    /// <summary>
    /// A tub of glue. Dip a stick into it and it comes out wet, with a glop and a glue blob on the dipped end.
    /// Only things marked Gluable (sticks) take glue, once. Needs a trigger Collider on this object.
    /// The tub itself is a grabbable prop; its trigger is a child zone.
    /// </summary>
    public class GlueTub : MonoBehaviour
    {
        private void OnTriggerEnter(Collider other) { Dip(other); }

        // Stay as well: a stick can already be in the tub when it becomes able to take glue.
        private void OnTriggerStay(Collider other) { Dip(other); }

        private void Dip(Collider other)
        {
            var rb = other.attachedRigidbody;
            if (rb == null || rb.isKinematic || HandCheck.PlayerBody(rb)) return;
            if (rb.GetComponentInChildren<GlueTub>() != null) return; // the tub does not dip itself
            if (rb.GetComponent<Gluable>() == null) return;           // glue only goes on sticks

            // Where the thing touches the glue: the nearest point on it to the middle of the tub.
            // Non-convex meshes cannot answer ClosestPoint, so they fall back to their centre.
            bool canQuery = !(other is MeshCollider mc) || mc.convex;
            Vector3 dipPoint = canQuery ? other.ClosestPoint(transform.position) : other.bounds.center;
            var glue = rb.GetComponent<Glue>();
            if (glue == null) glue = rb.gameObject.AddComponent<Glue>();
            if (glue.Coat(dipPoint)) PlaceholderSfx.Play(SfxKind.Glop, dipPoint, 0.7f);
        }
    }
}
