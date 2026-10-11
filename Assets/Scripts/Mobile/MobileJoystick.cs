using UnityEngine;
using UnityEngine.EventSystems;

// Left-thumb virtual joystick. Built on plain UGUI pointer interfaces rather
// than polling Touchscreen.current directly - UGUI already gives each touch
// its own PointerEventData (multitouch) and only calls these when the touch
// began over this object's own rect (no stealing input meant for the camera
// drag area or other UI), so no manual fingerId bookkeeping is needed.
public class MobileJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [SerializeField] private RectTransform background;
    [SerializeField] private RectTransform handle;
    [SerializeField] private MiroInputReader targetInput;
    [SerializeField] private float maxRadius = 70f;
    [SerializeField] private float deadZone = 0.15f;

    private int activePointerId = -1;

    public void OnPointerDown(PointerEventData eventData)
    {
        activePointerId = eventData.pointerId;
        UpdateHandle(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.pointerId != activePointerId) return;
        UpdateHandle(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.pointerId != activePointerId) return;
        ForceRelease();
    }

    // Called once by MobileControlsRoot (mobile only) right after it resizes
    // this joystick's visual RectTransforms to their final CSS-px-exact
    // size - keeps the handle's travel distance (and therefore input
    // normalization) proportional to the new background size. Absolute
    // assignment (not a multiply) so this can never compound across repeated
    // calls or scene reloads.
    public void SetMaxRadius(float value) => maxRadius = value;

    // Called by MobileControlsRoot when a modal opens mid-drag - the
    // EventSystem doesn't auto-cancel an in-progress drag just because
    // CanvasGroup.blocksRaycasts turned off, so this is the actual guarantee
    // against a stuck held direction.
    public void ForceRelease()
    {
        activePointerId = -1;
        if (handle != null) handle.anchoredPosition = Vector2.zero;
        if (targetInput != null) targetInput.SetMobileMoveInput(Vector2.zero);
    }

    private void UpdateHandle(PointerEventData eventData)
    {
        if (background == null) return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            background, eventData.position, eventData.pressEventCamera, out var localPoint);

        Vector2 clamped = Vector2.ClampMagnitude(localPoint, maxRadius);
        if (handle != null) handle.anchoredPosition = clamped;

        float magnitude01 = clamped.magnitude / maxRadius;
        Vector2 direction = magnitude01 > 0.0001f ? clamped.normalized : Vector2.zero;
        float adjusted = magnitude01 < deadZone ? 0f : (magnitude01 - deadZone) / (1f - deadZone);

        if (targetInput != null) targetInput.SetMobileMoveInput(direction * adjusted);
    }
}
