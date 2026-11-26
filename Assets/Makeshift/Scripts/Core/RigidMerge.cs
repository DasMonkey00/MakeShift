using System;
using System.Collections.Generic;
using Autohand;
using Makeshift.Settling;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Makeshift.Core
{
    /// <summary>
    /// Joins two loose things by making one of them part of the other's rigid body. Used by glue and lashing.
    /// A joint between two bodies wobbled, felt heavy and let the parts drift; one body does not. The absorbed piece
    /// keeps its colliders, visuals and traits (so a flint still strikes and a rock still counts as Heavy), but it no
    /// longer has its own weight, grab or settle. The keeper gains half the piece's weight and its traits.
    /// Call from Update or FixedUpdate code, not from inside a collision or trigger callback.
    /// </summary>
    public static class RigidMerge
    {
        public static void Absorb(Rigidbody keep, Rigidbody absorb)
        {
            if (keep == null || absorb == null || keep == absorb) return;
            var root = absorb.transform;

            // The absorbed piece stops being a loose body of its own. Immediate, and in this order: Unity refuses to
            // remove a Rigidbody while any component still requires it (which silently left glued stones loose).
            // Scripts that depend on the grab or on each other (a machine part and its hand hook) go first, in
            // dependency order, or Unity refuses to remove what they need.
            RemoveDependents(root);
            foreach (var grab in root.GetComponentsInChildren<Grabbable>(true)) Object.DestroyImmediate(grab);
            foreach (var child in root.GetComponentsInChildren<GrabbableChild>(true)) Object.DestroyImmediate(child);
            foreach (var settle in root.GetComponentsInChildren<PropSettle>(true)) Object.DestroyImmediate(settle);
            foreach (var joint in root.GetComponents<Joint>()) Object.DestroyImmediate(joint);
            float addedMass = absorb.mass * 0.5f;
            var addedTraits = root.TryGetComponent(out TraitHolder traits) ? traits.Traits : Trait.None;
            Object.DestroyImmediate(absorb);

            root.SetParent(keep.transform, true);
            // Switch the colliders off and on so physics hands them to the keeper's body; without this the added
            // part could pass straight through things.
            foreach (var col in root.GetComponentsInChildren<Collider>(true))
            {
                if (!col.enabled) continue;
                col.enabled = false;
                col.enabled = true;
            }
            Physics.SyncTransforms();
            MakePartOf(keep, root);

            // A head on a stick hits harder: some of its weight and its traits (Heavy, Sharp) carry over.
            keep.mass += addedMass;
            keep.ResetCenterOfMass();
            keep.ResetInertiaTensor();
            if (addedTraits != Trait.None && keep.TryGetComponent(out TraitHolder keeper)) keeper.Add(addedTraits);
        }

        /// <summary>
        /// Removes every script on the piece that needs its body or grab, directly or through another script, always
        /// taking a script only once nothing left on the object still requires it.
        /// </summary>
        private static void RemoveDependents(Transform root)
        {
            var doomed = new List<MonoBehaviour>();
            var gone = new List<Type> { typeof(Rigidbody), typeof(Grabbable) };
            var all = root.GetComponents<MonoBehaviour>();
            for (bool found = true; found;)
            {
                found = false;
                foreach (var b in all)
                {
                    if (b == null || b is Grabbable || doomed.Contains(b)) continue;
                    foreach (var type in gone)
                    {
                        if (!Requires(b.GetType(), type)) continue;
                        doomed.Add(b);
                        gone.Add(b.GetType());
                        found = true;
                        break;
                    }
                }
            }
            while (doomed.Count > 0)
            {
                int pick = doomed.Count - 1;
                for (int i = 0; i < doomed.Count; i++)
                {
                    bool needed = false;
                    foreach (var other in doomed)
                        if (other != doomed[i] && Requires(other.GetType(), doomed[i].GetType())) needed = true;
                    if (!needed) { pick = i; break; }
                }
                Object.DestroyImmediate(doomed[pick]);
                doomed.RemoveAt(pick);
            }
        }

        private static bool Requires(Type type, Type needed)
        {
            foreach (RequireComponent r in type.GetCustomAttributes(typeof(RequireComponent), true))
                foreach (var t in new[] { r.m_Type0, r.m_Type1, r.m_Type2 })
                    if (t != null && t != typeof(Transform) && t.IsAssignableFrom(needed)) return true;
            return false;
        }

        /// <summary>Tells Auto Hand the new colliders belong to the keeper, so the added part can be grabbed too.</summary>
        private static void MakePartOf(Rigidbody keep, Transform root)
        {
            var grab = keep.GetComponent<Grabbable>();
            if (grab == null) return;
            foreach (var col in root.GetComponentsInChildren<Collider>(true))
            {
                if (col.isTrigger) continue;
                col.gameObject.layer = keep.gameObject.layer;
                var child = col.GetComponent<GrabbableChild>();
                if (child == null) child = col.gameObject.AddComponent<GrabbableChild>();
                child.grabParent = grab;
                // Already in a hand: the new part must not shove that hand about.
                foreach (var hand in grab.GetHeldBy()) hand.HandIgnoreCollider(col, true);
            }
        }
    }
}
