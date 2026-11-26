using UnityEngine;
using UnityEngine.Events;

namespace Makeshift.Machines
{
    /// <summary>
    /// A rock face or seam that drills chew through. Every time enough progress
    /// builds up, a chunk of ore pops out where the drill is hitting.
    /// </summary>
    public class OreNode : MonoBehaviour
    {
        [SerializeField] private GameObject orePrefab;
        [Tooltip("Drilling progress needed per chunk. Harder rock = higher.")]
        [SerializeField] private float progressPerChunk = 5f;
        [SerializeField] private int chunksAvailable = 6;
        [SerializeField] private float popOutSpeed = 1.5f;

        [Header("Events")]
        public UnityEvent onChunkDropped;
        public UnityEvent onDepleted;

        public int ChunksLeft => chunksAvailable;
        private float progress;

        public void Dig(float amount, Vector3 hitPoint, Vector3 hitNormal)
        {
            if (chunksAvailable <= 0 || amount <= 0f) return;
            progress += amount;
            if (progress < progressPerChunk) return;

            progress -= progressPerChunk;
            chunksAvailable--;

            if (orePrefab != null)
            {
                var chunk = Instantiate(orePrefab, hitPoint + hitNormal * 0.1f, Random.rotation);
                var rb = chunk.GetComponent<Rigidbody>();
                if (rb != null) rb.AddForce(hitNormal * popOutSpeed, ForceMode.VelocityChange);
            }
            onChunkDropped?.Invoke();

            if (chunksAvailable <= 0) onDepleted?.Invoke();
        }
    }
}
