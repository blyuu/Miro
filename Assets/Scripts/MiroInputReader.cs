using UnityEngine;
using UnityEngine.InputSystem;

public class MiroInputReader : MonoBehaviour
{
    public Vector2 MoveInput { get; private set; }
    public bool SprayHeld { get; private set; }
    public bool JumpPressed { get; private set; }

    // Pushed each frame by MobileJoystick/MobileSprayButton (when present and
    // unblocked) - this reader merges them with keyboard/mouse rather than
    // PlayerMovement/PlayerSpray knowing anything about touch at all.
    private Vector2 mobileMoveInput;
    private bool mobileSprayHeld;

    // Separate source for MobileCameraDragInput's tap-to-attack pulse -
    // deliberately NOT sharing mobileSprayHeld: that field is also written by
    // MobileSprayButton, and a tap's own "pulse ended, set false" could
    // otherwise stomp a genuinely-still-held spray button press from a
    // different finger. ORed in alongside mobileSprayHeld below instead, so
    // either source alone (or both at once) correctly holds SprayHeld true.
    private bool mobileTapAttackHeld;

    public void SetMobileMoveInput(Vector2 value) => mobileMoveInput = value;
    public void SetMobileSprayHeld(bool held) => mobileSprayHeld = held;
    public void SetMobileTapAttackHeld(bool held) => mobileTapAttackHeld = held;

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            MoveInput = Vector2.zero;
            JumpPressed = false;
        }
        else
        {
            float x = 0f;
            float y = 0f;
            if (keyboard.aKey.isPressed) x -= 1f;
            if (keyboard.dKey.isPressed) x += 1f;
            if (keyboard.sKey.isPressed) y -= 1f;
            if (keyboard.wKey.isPressed) y += 1f;

            MoveInput = new Vector2(x, y);
            JumpPressed = keyboard.spaceKey.wasPressedThisFrame;
        }

        var mouse = Mouse.current;
        bool rawHeld = mouse != null && mouse.leftButton.isPressed;
        SprayHeld = GameplayCursor.IsLocked && !GameplayCursor.ConsumedClickThisFrame && rawHeld;

        // Mobile overrides only take effect when actually non-zero/held, so
        // desktop keyboard/mouse behaves exactly as before when no touch
        // controls are present or active.
        if (mobileMoveInput.sqrMagnitude > 0.0001f) MoveInput = mobileMoveInput;
        SprayHeld = SprayHeld || mobileSprayHeld || mobileTapAttackHeld;
    }
}