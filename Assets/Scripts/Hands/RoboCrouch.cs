using UnityEngine;
using Autohand;
using Autohand.Demo;

/// <summary>Click the left thumbstick to toggle crouching: the view drops to half height and back.</summary>
public class RoboCrouch : MonoBehaviour {
    public AutoHandPlayer player;
    public float crouchFraction = 0.5f;          // fraction of the standing head height removed when crouched
    public float speed = 2.5f;                   // metres per second
    bool last, crouched;
    float standingHeight = 1.6f;

    void Awake() {
        if (player == null) player = GetComponentInParent<AutoHandPlayer>();
        if (player == null) player = GetComponentInChildren<AutoHandPlayer>();
    }

    void Update() {
        if (player == null) return;
        bool pressed = RoboInput.Pressed(true, CommonButton.primary2DAxisClick);
        if (pressed && !last) Toggle();
        last = pressed;

        if (!crouched) {
            float h = player.headCamera.transform.position.y - player.transform.position.y;
            if (h > 1.0f && h < 2.4f) standingHeight = Mathf.Lerp(standingHeight, h, 0.05f);   // follows the player's real standing height (sane values only)
        }
        player.crouching = false;                                                        // we drive the height offset ourselves
        float target = crouched ? -standingHeight * crouchFraction : 0f;
        player.heightOffset = Mathf.MoveTowards(player.heightOffset, target, speed * Time.deltaTime);
    }

    public void Toggle() {
        crouched = !crouched;
        Debug.Log("RoboCrouch: " + (crouched ? "crouched" : "standing") + " (standing height " + standingHeight.ToString("F2") + ")");
    }
}
