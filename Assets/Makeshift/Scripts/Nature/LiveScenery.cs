using UnityEngine;

namespace Makeshift.Nature
{
    /// <summary>
    /// Marks scenery that changes shape in play. The scenery merge and the chunked trees leave anything under a
    /// script to draw itself, so a stale copy of it is never drawn.
    /// </summary>
    public class LiveScenery : MonoBehaviour
    {
    }
}
