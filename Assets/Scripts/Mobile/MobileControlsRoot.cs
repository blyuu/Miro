using UnityEngine;
using Unity.Cinemachine;

// Top-level gate for every mobile touch control (Joystick/Spray/CameraDrag):
// shown only on a touch platform (or when forced, for Editor/mouse testing),
// and raycast-blocked whenever a modal (LevelUp/Clear/GameOver) is open or
// gameplay is paused - so touches can never reach gameplay controls
// underneath an open menu. This is the same regression class already fixed
// once for the Upgrade Button click bug; the guard lives here instead of
// relying on Canvas sibling order alone.
//
// Also the single place that decides whether Cinemachine's own
// CinemachineInputAxisController is allowed to run: its "Look" action is
// bound to <Pointer>/delta (Cinemachine's packaged default input actions),
// and Touchscreen is a Pointer device - so on a real touch device it reads
// ANY finger's raw delta every frame, completely bypassing UGUI's hit
// testing. That's what was still rotating the camera from the joystick/spray
// finger even after fixing EventSystem's PointerBehavior. Disabling it
// whenever mobile controls are shown hands camera rotation entirely to
// MobileCameraDragInput's own touch-ownership tracking instead - PC leaves
// this component alone (enabled, untouched) since it never has this
// component disabled there.
[RequireComponent(typeof(CanvasGroup))]
public class MobileControlsRoot : MonoBehaviour
{
    [SerializeField] private bool forceMobileControls = false;
    [SerializeField] private GameObject levelUpPanel;
    [SerializeField] private GameObject clearPanel;
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private MobileJoystick joystick;
    [SerializeField] private MobileSprayButton sprayButton;

    private CanvasGroup canvasGroup;
    private bool wasBlocked;

    // Visual/touch-area sizing for the joystick and spray button, applied at
    // runtime instead of baked into the Scene - keeps the Scene file
    // untouched and recomputes cleanly from scratch on every fresh load
    // (Retry included). These are the ONLY sizing constants in the project -
    // target final CSS-pixel diameters on the real device screen, not a
    // multiplier against whatever the Scene happens to author (a multiplier
    // can silently compound or drift; an absolute CSS-px target can't -
    // every Awake() recomputes the exact same result from these four
    // numbers). See TargetSizeDelta() below for the CSS-px -> Canvas
    // reference-resolution-unit conversion.
    private const float JoystickBaseTargetCssPx = 260f; // was 160 - user asked for +100px more after on-device testing
    private const float JoystickHandleTargetCssPx = 75f;
    private const float SprayBaseTargetCssPx = 150f;
    private const float SprayHandleTargetCssPx = 75f;

    // The joystick/spray button's invisible touch-hit root was authored
    // deliberately larger than its visible ring (easier to grab than the
    // ring alone) - these preserve that original ratio against the new
    // target visual size rather than hardcoding a second CSS-px constant.
    // Joystick: 180 / 145 (root / Visual) from the authored Scene sizes.
    private const float JoystickHitAreaRatio = 180f / 145f;
    // Spray: 220 / 156 (root / Visual) from the authored Scene sizes.
    private const float SprayHitAreaRatio = 220f / 156f;
    // Joystick maxRadius(42) / (Visual(145) / 2) from the authored Scene
    // values - how far toward the ring's edge the handle was allowed to
    // travel, preserved proportionally at the new visual size.
    private const float JoystickRadiusRatio = 42f / (145f / 2f);

    void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();

        bool show = forceMobileControls || PlatformInputDetector.IsMobileBrowser;
        gameObject.SetActive(show);

        if (show)
        {
            var freeLookGo = GameObject.Find("FreeLook Camera");
            var inputAxisController = freeLookGo != null ? freeLookGo.GetComponent<CinemachineInputAxisController>() : null;
            if (inputAxisController != null) inputAxisController.enabled = false;

            ScaleUpControls();
            ApplySafeAreaOffsets();
        }
    }

    // Resizes the joystick and spray button (and their visual children) to
    // their final target CSS-pixel sizes, computed fresh against THIS
    // device's actual Canvas.scaleFactor/devicePixelRatio - both are corner/
    // center anchored already, so setting sizeDelta alone resizes them in
    // place with no anchoredPosition changes needed, and they're in opposite
    // screen corners so this can't make them overlap each other.
    private void ScaleUpControls()
    {
        if (joystick != null)
        {
            float visualSize = TargetSizeDelta(JoystickBaseTargetCssPx);
            SetSize(joystick.transform as RectTransform, visualSize * JoystickHitAreaRatio);
            SetSize(joystick.transform.Find("Visual") as RectTransform, visualSize);
            SetSize(joystick.transform.Find("Handle") as RectTransform, TargetSizeDelta(JoystickHandleTargetCssPx));
            joystick.SetMaxRadius(visualSize * 0.5f * JoystickRadiusRatio);
        }

        if (sprayButton != null)
        {
            // "Icon" now lives under the runtime-built "Handle" (see
            // MobileSprayButton, [DefaultExecutionOrder(-100)] guarantees
            // that Handle already exists by the time this runs) instead of
            // directly under the button root.
            float visualSize = TargetSizeDelta(SprayBaseTargetCssPx);
            float handleSize = TargetSizeDelta(SprayHandleTargetCssPx);
            SetSize(sprayButton.transform as RectTransform, visualSize * SprayHitAreaRatio);
            SetSize(sprayButton.transform.Find("Visual") as RectTransform, visualSize);
            SetSize(sprayButton.transform.Find("Handle") as RectTransform, handleSize);
            // Icon keeps its originally-authored proportion to the Visual
            // ring (73 / 156), now applied against the new Handle size since
            // Icon lives inside Handle at runtime.
            SetSize(sprayButton.transform.Find("Handle/Icon") as RectTransform, handleSize * (73f / 156f));
        }
    }

    // Canvas.scaleFactor converts a Canvas reference-resolution size into
    // "Screen px" - which on WebGL is CSS px * devicePixelRatio, NOT CSS px
    // itself. Dividing that back out here is what makes targetCssPx land as
    // an exact, device-independent on-screen CSS pixel size instead of an
    // assumption baked in for one assumed resolution/DPI.
    private float TargetSizeDelta(float targetCssPx)
    {
        var canvas = GetComponentInParent<Canvas>();
        float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        return targetCssPx * PlatformInputDetector.DevicePixelRatio / scale;
    }

    private static void SetSize(RectTransform rt, float diameter)
    {
        if (rt == null) return;
        rt.sizeDelta = new Vector2(diameter, diameter);
    }

    // No SafeArea system exists anywhere in this project yet. The joystick
    // and spray button are each anchored to a screen CORNER via
    // anchoredPosition (not a full-stretch rect), so the usual "SafeArea ->
    // anchorMin/anchorMax" recipe doesn't apply directly - instead nudge
    // their anchoredPosition inward by Screen.safeArea's inset on whichever
    // edges each one actually touches, converted from device screen pixels
    // into the Canvas's reference-resolution units via its current
    // scaleFactor (same conversion PlatformInputDetector's sibling scripts
    // already rely on CanvasScaler for elsewhere in this project).
    private void ApplySafeAreaOffsets()
    {
        var canvas = GetComponentInParent<Canvas>();
        float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;

        Rect safeArea = Screen.safeArea;
        float leftInset = safeArea.xMin / scale;
        float rightInset = (Screen.width - safeArea.xMax) / scale;
        float bottomInset = safeArea.yMin / scale;

        if (joystick != null)
        {
            var leftControlArea = joystick.transform.parent as RectTransform;
            if (leftControlArea != null)
            {
                var pos = leftControlArea.anchoredPosition;
                pos.x += leftInset;
                pos.y += bottomInset;
                leftControlArea.anchoredPosition = pos;
            }
        }

        if (sprayButton != null)
        {
            var rt = sprayButton.transform as RectTransform;
            if (rt != null)
            {
                var pos = rt.anchoredPosition;
                pos.x -= rightInset;
                pos.y += bottomInset;
                rt.anchoredPosition = pos;
            }
        }
    }

    void Update()
    {
        bool blocked = Time.timeScale <= 0f ||
                       IsOpen(levelUpPanel) || IsOpen(clearPanel) || IsOpen(gameOverPanel);

        canvasGroup.blocksRaycasts = !blocked;
        canvasGroup.interactable = !blocked;

        if (blocked && !wasBlocked)
        {
            if (joystick != null) joystick.ForceRelease();
            if (sprayButton != null) sprayButton.ForceRelease();
        }
        wasBlocked = blocked;
    }

    private static bool IsOpen(GameObject go) => go != null && go.activeInHierarchy;
}
