using UnityEngine;

/// <summary>
/// Tells RoboFingerAssist how a held object should look in the hand. Normal Auto Hand physics grabbing is untouched; the assist only
/// finishes the finger positions afterwards (thumb wraps a handle, or a small item is pinched between thumb and index).
/// </summary>
public class RoboGripHint : MonoBehaviour {
    public enum Mode { Handle, Pinch }
    public Mode mode = Mode.Handle;
    [Tooltip("Handle mode: colliders that count as the handle. The thumb assist only runs when the palm is closest to one of these.")]
    public Collider[] handleColliders;
}
