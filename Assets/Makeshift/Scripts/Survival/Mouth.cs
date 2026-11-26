using Makeshift.Core;
using UnityEngine;

namespace Makeshift.Survival
{
    /// <summary>
    /// Put on a small trigger sphere at the player's head. Bring a held Edible to your face to eat it.
    /// Needs a trigger Collider; the Survivor is found on a parent.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class Mouth : MonoBehaviour
    {
        private Survivor survivor;

        private void Awake()
        {
            survivor = GetComponentInParent<Survivor>();
            GetComponent<SphereCollider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (survivor == null) return;
            var food = other.GetComponentInParent<Edible>();
            if (food == null) return;
            // With a hand system running, only food in a hand counts (not food on the floor).
            if (HandCheck.IsHeld != null && !HandCheck.Held(food.Body)) return;
            survivor.Eat(food);
        }
    }
}
