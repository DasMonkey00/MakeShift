using UnityEngine;

/// <summary>
/// Put this on any held item that shoots (or points) so HandAlignPro uses "gun mode": the item's muzzle direction is kept fixed
/// while the hand is aligned around it. Autohand's demo Pistol is also recognised without this component.
/// </summary>
public class GunMuzzle : MonoBehaviour {
    public Transform muzzle;
}
