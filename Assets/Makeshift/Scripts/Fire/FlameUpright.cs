using UnityEngine;

namespace Makeshift.Fire
{
    /// <summary>
    /// Keeps a flame or smoke emitter pointing up in the world. The emitter follows its parent (a log, a torch) for
    /// position only, so a log turned over does not send the fire sideways or down.
    /// </summary>
    public class FlameUpright : MonoBehaviour
    {
        // Euler(-90, 0, 0) is the emitter's own "up" orientation used by ParticleFactory.
        private static readonly Quaternion Upright = Quaternion.Euler(-90f, 0f, 0f);

        private void LateUpdate()
        {
            transform.rotation = Upright;
        }
    }
}
