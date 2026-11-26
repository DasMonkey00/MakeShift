using UnityEngine;
using UnityEngine.XR;
using Autohand.Demo;

/// <summary>
/// Controller button reads. Uses Auto Hand's own controller link first (the same device the working grip/trigger come from) and
/// falls back to Unity's XR input devices.
/// </summary>
public static class RoboInput {
    public static bool Pressed(bool left, CommonButton button) {
        var link = left ? Autohand.HandControllerLink.handLeft : Autohand.HandControllerLink.handRight;
        var xr = link as XRHandControllerLink;
        if (xr != null) {
            try { if (xr.ButtonPressed(button)) return true; } catch { }
        }
        var dev = InputDevices.GetDeviceAtXRNode(left ? XRNode.LeftHand : XRNode.RightHand);
        if (!dev.isValid) return false;
        InputFeatureUsage<bool> usage;
        switch (button) {
            case CommonButton.primaryButton: usage = CommonUsages.primaryButton; break;
            case CommonButton.secondaryButton: usage = CommonUsages.secondaryButton; break;
            case CommonButton.primary2DAxisClick: usage = CommonUsages.primary2DAxisClick; break;
            case CommonButton.gripButton: usage = CommonUsages.gripButton; break;
            default: usage = CommonUsages.triggerButton; break;
        }
        return dev.TryGetFeatureValue(usage, out bool b) && b;
    }
}
