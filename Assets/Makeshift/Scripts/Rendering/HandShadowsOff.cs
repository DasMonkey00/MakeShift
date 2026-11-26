using Autohand;
using UnityEngine;

namespace Makeshift.Rendering
{
    /// <summary>
    /// Stops the player's hand models from casting shadows. The hands sit right at the camera, so their shadows
    /// are big, noisy and cost shadow-map resolution for no gain. Rechecks every half second because hand
    /// models can be swapped or created after start.
    /// </summary>
    public class HandShadowsOff : MonoBehaviour
    {
        private float next;

        private void Update()
        {
            if (Time.time < next) return;
            next = Time.time + 0.5f;
            var hands = GetComponentsInChildren<Hand>(true);
            foreach (var hand in hands)
            {
                foreach (var r in hand.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
        }
    }
}
