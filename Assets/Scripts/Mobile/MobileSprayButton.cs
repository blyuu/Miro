using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// PointerDown/PointerUp still drive SprayHeld directly (hold-to-fire, exactly
// as before - PlayerSpray's own fire-rate/damage/paint/audio logic is
// untouched and never duplicated here). OnDrag is new, for two things:
//   1) Purely VISUAL feedback - a PUBG-Mobile-style inner "Handle" (built at
//      runtime below, not in the Scene) slides toward the finger, clamped to
//      a radius, then springs back to center on release. This handle's
//      position/offset is NEVER read as camera or movement input - only
//      drawn.
//   2) While held, THIS finger is also registered with MobileCameraDragInput
//      as camera-eligible - the one deliberate exception to "a touch that
//      starts on UI never drives the camera" (see that script's header).
//      Camera rotation itself still comes from that script reading this
//      SAME finger's raw per-frame EnhancedTouch delta, never this handle's
//      clamped offset - dragging far enough to pin the handle at its radius
//      doesn't cap rotation speed, it's just where the visual stops.
//
// Bridging UGUI's PointerEventData to the EnhancedTouch touchId
// MobileCameraDragInput tracks by: a touch-originated PointerEventData from
// InputSystemUIInputModule is actually an ExtendedPointerEventData. Reading
// InputSystemUIInputModule's own source confirms it computes a combined/
// encoded value for PointerEventData.pointerId for its own internal
// bookkeeping (not the same id space EnhancedTouch uses) but separately
// stores the ORIGINAL TouchControl.touchId on ExtendedPointerEventData.touchId
// - the exact same id EnhancedTouch.Touch.touchId exposes. That field, not
// pointerId, is what gets passed to MobileCameraDragInput below.
[DefaultExecutionOrder(-100)] // must build Handle before MobileControlsRoot.Awake() scales it
public class MobileSprayButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    [SerializeField] private MiroInputReader targetInput;
    [SerializeField] private Image buttonImage;
    [SerializeField] private float pressedScale = 0.92f;
    [SerializeField] private float pressedBrightness = 1.25f;

    [Header("Runtime-built joystick-style handle (visual only - see class comment)")]
    [SerializeField] private float handleSizeRatio = 0.55f; // fraction of this button's own width
    [SerializeField] private float handleAlpha = 0.45f;

    private Vector3 normalScale;
    private Color normalColor;
    private bool pressed;

    private MobileCameraDragInput cameraInput;
    private RectTransform handle;
    private int activeTouchId = -1; // -1 = not currently a registered touch (released, or a non-touch pointer like a mouse)

    void Awake()
    {
        normalScale = transform.localScale;
        if (buttonImage != null) normalColor = buttonImage.color;

        cameraInput = FindFirstObjectByType<MobileCameraDragInput>();
        BuildHandle();
    }

    // Creates the inner handle + re-parents the existing Icon under it, all
    // at runtime - the Scene itself is never touched (same "bootstrap instead
    // of a Scene edit" pattern already used elsewhere in Assets/Scripts/Mobile).
    private void BuildHandle()
    {
        var root = (RectTransform)transform;
        var icon = transform.Find("Icon") as RectTransform;
        var visual = transform.Find("Visual");

        var handleGo = new GameObject("Handle", typeof(RectTransform));
        handle = (RectTransform)handleGo.transform;
        handle.SetParent(root, false);
        handle.anchorMin = handle.anchorMax = new Vector2(0.5f, 0.5f);
        handle.pivot = new Vector2(0.5f, 0.5f);
        handle.anchoredPosition = Vector2.zero;

        float size = root.sizeDelta.x * handleSizeRatio;
        handle.sizeDelta = new Vector2(size, size);

        var handleImage = handleGo.AddComponent<Image>();
        var visualImage = visual != null ? visual.GetComponent<Image>() : null;
        handleImage.sprite = visualImage != null ? visualImage.sprite : null;
        Color c = buttonImage != null ? buttonImage.color : Color.white;
        c.a = handleAlpha;
        handleImage.color = c;
        handleImage.raycastTarget = false; // the button root's own invisible Image still owns all raycasting

        if (icon != null) icon.SetParent(handle, false); // anchors/anchoredPosition already (0.5,0.5)/(0,0) - stays centered
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        pressed = true;
        if (targetInput != null) targetInput.SetMobileSprayHeld(true);

        transform.localScale = normalScale * pressedScale;
        if (buttonImage != null)
        {
            var c = normalColor * pressedBrightness;
            c.a = normalColor.a;
            buttonImage.color = c;
        }

        activeTouchId = ExtractTouchId(eventData);
        if (activeTouchId != -1 && cameraInput != null) cameraInput.RegisterSprayButtonTouch(activeTouchId);
    }

    public void OnDrag(PointerEventData eventData)
    {
        // Visual-only handle offset - recomputes the clamp radius fresh from
        // the CURRENT actual sizes every call (rather than a value cached in
        // Awake) so this stays correct no matter when/whether
        // MobileControlsRoot has scaled this button and its handle up.
        if (handle == null) return;

        var root = (RectTransform)transform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            root, eventData.position, eventData.pressEventCamera, out var localPoint);

        float maxRadius = Mathf.Max(0f, root.sizeDelta.x * 0.5f - handle.sizeDelta.x * 0.5f);
        handle.anchoredPosition = Vector2.ClampMagnitude(localPoint, maxRadius);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        ReleaseVisual();
        if (targetInput != null) targetInput.SetMobileSprayHeld(false);
        ReleaseCameraRegistration();
    }

    // Called by MobileControlsRoot when a modal opens mid-press - the
    // EventSystem doesn't auto-cancel an in-progress press/drag just because
    // CanvasGroup.blocksRaycasts turned off, so this is the actual guarantee
    // against a stuck fire-hold or a camera finger that never releases.
    public void ForceRelease()
    {
        if (!pressed) return;
        ReleaseVisual();
        if (targetInput != null) targetInput.SetMobileSprayHeld(false);
        ReleaseCameraRegistration();
    }

    private void ReleaseCameraRegistration()
    {
        if (activeTouchId != -1 && cameraInput != null) cameraInput.ReleaseSprayButtonTouch(activeTouchId);
        activeTouchId = -1;
    }

    private void ReleaseVisual()
    {
        pressed = false;
        transform.localScale = normalScale;
        if (buttonImage != null) buttonImage.color = normalColor;
        if (handle != null) handle.anchoredPosition = Vector2.zero;
    }

    private static int ExtractTouchId(PointerEventData eventData)
    {
        // Mouse/pen (PC, or Editor testing without a touch device) has no
        // EnhancedTouch id - camera-eligibility via this path simply doesn't
        // apply then, same as it never has for PC.
        if (eventData is ExtendedPointerEventData extended && extended.pointerType == UIPointerType.Touch)
            return extended.touchId;
        return -1;
    }
}
