using UnityEngine;

namespace Makeshift.Core
{
    /// <summary>
    /// Put one on the root of every interactable object and tick its traits.
    /// Other systems look it up with TraitHolder.TryGet(collider, out var t).
    /// </summary>
    [DisallowMultipleComponent]
    public class TraitHolder : MonoBehaviour
    {
        [SerializeField] private Trait traits = Trait.None;

        public Trait Traits => traits;

        public bool Has(Trait t) => (traits & t) == t;
        public bool HasAny(Trait t) => (traits & t) != 0;
        public void Add(Trait t) => traits |= t;
        public void Remove(Trait t) => traits &= ~t;

        /// <summary>Finds the TraitHolder on a collider's object or any parent.</summary>
        public static bool TryGet(Component c, out TraitHolder holder)
        {
            holder = c != null ? c.GetComponentInParent<TraitHolder>() : null;
            return holder != null;
        }

        public static bool Has(Component c, Trait t)
        {
            return TryGet(c, out var h) && h.Has(t);
        }
    }
}
