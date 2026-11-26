using UnityEngine;

namespace Makeshift.Survival
{
    /// <summary>Anything a survivor can eat. Cook it first if it has a Cookable.</summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Edible : MonoBehaviour
    {
        [SerializeField] private float nutrition = 25f;

        public Rigidbody Body { get; private set; }
        public float Nutrition
        {
            get
            {
                var c = GetComponent<Cookable>();
                return nutrition * (c != null ? c.NutritionMultiplier : 1f);
            }
        }

        private void Awake() { Body = GetComponent<Rigidbody>(); }
    }
}
