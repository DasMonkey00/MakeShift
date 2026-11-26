using UnityEngine;
using Hand = Autohand.Hand;

/// <summary>
/// The hand + item offset from the tracked controller (found with HandAlignPro, kept in RoboGripStore under the item's name). While the item is
/// held the hand follows a child of its normal follow target carrying the offset. Mirrored for the left hand. If HandAlignPro saves a new
/// offset while the item is held, the held pose updates at once.
/// </summary>
public class RoboGripOffset {
    readonly string key; readonly Vector3 defPos; readonly Quaternion defRot;
    Hand hand; Transform orig, proxy;

    public RoboGripOffset(string itemKey, Vector3 defaultPos, Quaternion defaultRot) { key = itemKey; defPos = defaultPos; defRot = defaultRot; }

    void Pose(out Vector3 p, out Quaternion q) {
        if (!RoboGripStore.TryGet(key, out p, out q)) { p = defPos; q = defRot; }
        if (hand != null && hand.left) { p.x = -p.x; q = new Quaternion(q.x, -q.y, -q.z, q.w); }
    }

    void Refresh(string changedKey) {
        if (changedKey != key || proxy == null) return;
        Vector3 p; Quaternion q; Pose(out p, out q); proxy.localPosition = p; proxy.localRotation = q;
    }

    public void Apply(Hand h) {
        if (h == null || hand != null || h.follow == null) return;
        hand = h; orig = h.follow;
        proxy = new GameObject("grip offset").transform; proxy.SetParent(orig, false);        // rides on the tracked controller, so it never lags
        Vector3 p; Quaternion q; Pose(out p, out q); proxy.localPosition = p; proxy.localRotation = q;
        h.follow = proxy;
        RoboGripStore.Changed += Refresh;
    }

    public void Remove(Hand h) {
        if (hand == null || h != hand) return;
        RoboGripStore.Changed -= Refresh;
        if (hand.follow == proxy || (hand.follow != null && hand.follow.name == "HandAlignPro Proxy")) hand.follow = orig;
        if (proxy != null) Object.Destroy(proxy.gameObject);
        hand = null; proxy = null; orig = null;
    }
}
