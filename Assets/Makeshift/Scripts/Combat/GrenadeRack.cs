using System.Collections;
using UnityEngine;

namespace Makeshift.Combat
{
    /// <summary>
    /// The grenade's place on the table. It puts one out at the start, and another some seconds after each one has blown
    /// up.
    /// </summary>
    public class GrenadeRack : MonoBehaviour
    {
        [SerializeField] private GameObject prefab;
        [Tooltip("Seconds after a blast before the next grenade appears.")]
        [SerializeField] private float respawnAfter = 10f;

        /// <summary>The grenade out now, or null while the next one is on its way.</summary>
        public FellingGrenade Current { get; private set; }

        private void Start() => Put();

        private void Put()
        {
            if (prefab == null) return;
            var made = Instantiate(prefab, transform.position, transform.rotation);
            Current = made.GetComponentInChildren<FellingGrenade>();
            if (Current != null) Current.Exploded += OnExploded;
        }

        private void OnExploded(FellingGrenade blown)
        {
            blown.Exploded -= OnExploded;
            Current = null;
            StartCoroutine(Later());
        }

        private IEnumerator Later()
        {
            yield return new WaitForSeconds(respawnAfter);
            Put();
        }
    }
}
