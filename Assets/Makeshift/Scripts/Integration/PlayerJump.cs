using Autohand;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Makeshift.Integration
{
    /// <summary>
    /// A jump button: A on the right controller (Quest: A, PC VR: the primary button), or Space on a keyboard.
    /// Jumping only works while the player is standing on something (Auto Hand's grounded check).
    /// </summary>
    public class PlayerJump : MonoBehaviour
    {
        [SerializeField] private AutoHandPlayer player;
        [Tooltip("Take-off speed in m/s. 3.2 is about half a metre of hop.")]
        [SerializeField] private float jumpSpeed = 3.2f;

#if ENABLE_INPUT_SYSTEM
        private InputAction jump;

        private void OnEnable()
        {
            jump = new InputAction("Jump", InputActionType.Button);
            jump.AddBinding("<XRController>{RightHand}/primaryButton");
            jump.AddBinding("<Keyboard>/space");
            jump.performed += OnJump;
            jump.Enable();
        }

        private void OnDisable()
        {
            if (jump == null) return;
            jump.performed -= OnJump;
            jump.Disable();
            jump.Dispose();
            jump = null;
        }

        private void OnJump(InputAction.CallbackContext ctx) { Jump(); }
#endif

        public void Jump()
        {
            if (player == null) player = GetComponentInChildren<AutoHandPlayer>(true);
            if (player != null) player.Jump(jumpSpeed);
            Makeshift.Audio.PlaceholderSfx.Play(Makeshift.Audio.SfxKind.Jump, transform.position, 0.5f);
        }
    }
}
