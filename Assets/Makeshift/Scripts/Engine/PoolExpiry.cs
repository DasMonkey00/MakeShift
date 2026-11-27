using UnityEngine;

namespace Makeshift.Engine
{
    /// <summary>Switches a pooled object off after its time, so <see cref="CappedPool"/> can use it again.</summary>
    public class PoolExpiry : MonoBehaviour
    {
        public float Seconds = 40f;
        private float left;

        private void OnEnable() => left = Seconds;

        private void Update()
        {
            left -= Time.deltaTime;
            if (left <= 0f) gameObject.SetActive(false);
        }
    }
}
