using UnityEngine;
using UnityEngine.InputSystem;

public class GameplayCursor : MonoBehaviour
{
    public static bool IsLocked { get; private set; }
    public static bool ConsumedClickThisFrame { get; private set; }

    void Start()
    {
        Lock();
    }

    void Update()
    {
        ConsumedClickThisFrame = false;

        var keyboard = Keyboard.current;
        var mouse = Mouse.current;

        if (IsLocked)
        {
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Unlock();
            }
        }
        else
        {
            // Only auto-relock on a click during normal gameplay (ESC-to-unlock
            // free-look). While a modal UI is open (LevelUp/Clear/Game Over all
            // set Time.timeScale = 0f while they're up), the very click meant
            // to press a UI button would otherwise also relock the cursor here
            // - and since Cursor.lockState = Locked stops the Input System from
            // reporting a live pointer position for the rest of that click
            // gesture, the button's pointer-up never lands back on it, so
            // Button.onClick never fires even though the panel renders fine.
            // The owning UI (e.g. LevelUpUI.Choose()) already calls Lock()
            // itself once the player actually makes a choice.
            if (Time.timeScale > 0f && mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                Lock();
                ConsumedClickThisFrame = true;
            }
        }
    }

    public static void Lock()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        IsLocked = true;
    }

    public static void Unlock()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        IsLocked = false;
    }
}