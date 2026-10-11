using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using Unity.Cinemachine;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

// Dual-purpose touch controller for mobile empty-screen input: camera-drag
// AND tap-to-attack, because they're the same gesture space (any finger that
// starts on non-UI screen) and have to be arbitrated by ONE system rather
// than two scripts racing for the same touch. ALSO the sole camera-axis
// writer for the spray button's PUBG-Mobile-style "hold to fire, drag to
// also look around" finger - see RegisterSprayButtonTouch/
// ReleaseSprayButtonTouch below, called by MobileSprayButton.
//
// Deliberately NOT a UGUI IDragHandler for the camera itself (see history
// below) - Cinemachine's own CinemachineInputAxisController reads its "Look"
// action's raw <Pointer>/delta every frame regardless of what's under the
// finger (see MobileControlsRoot, which disables that component whenever
// mobile controls are active), so camera input has to be driven manually
// here regardless.
//
// Camera-eligible fingers are tracked as a POOL
// (_cameraEligibleTouchIds), with exactly ONE promoted to
// _activeCameraTouchId at a time (so simultaneous eligible fingers never
// sum their rotation) - when the active one releases, the next eligible one
// in the pool (if any) is promoted automatically. A finger enters the pool
// two ways:
//   1) Starts on NON-UI screen (checked via a real EventSystem.RaycastAll AT
//      THAT SCREEN POSITION, not EventSystem.IsPointerOverGameObject(id) -
//      EnhancedTouch's touchId and whatever pointerId
//      InputSystemUIInputModule assigns a touch are not guaranteed to be the
//      same id space, so a position raycast sidesteps that mapping
//      entirely) and then drags far enough to cross tapMaxMovementRefPixels
//      (scaled by the Canvas's current scaleFactor so the feel is consistent
//      across device resolutions/DPI - WebGL's Screen.dpi is unreliable,
//      this is the robust substitute). A touch starting ON the
//      joystick/spray button/any other UI never enters this path at all -
//      this is the ONLY place "started on UI" is excluded, and it still
//      applies unconditionally (the joystick never gets to drive the
//      camera this way, matching the "I" test case).
//   2) Is explicitly registered by MobileSprayButton via
//      RegisterSprayButtonTouch - the ONE deliberate exception to rule 1,
//      requested specifically so holding/dragging the spray button fires
//      AND looks around with the same finger. No movement-distance
//      gating here (unlike path 1) since there's no competing "was this a
//      tap" question for a finger that's already continuously firing.
// Either way, becoming newly active skips exactly one frame of delta first
// (never the stale jump from wherever the finger started to its current
// position).
public class MobileCameraDragInput : MonoBehaviour
{
    [SerializeField] private CinemachineOrbitalFollow orbitalFollow;
    [SerializeField] private MiroInputReader targetInput;
    [SerializeField] private float sensitivity = 0.15f;

    [Header("Tap vs. drag classification (reference-resolution px / seconds, device-tunable)")]
    [SerializeField] private float tapMaxMovementRefPixels = 24f;
    [SerializeField] private float tapMaxDuration = 0.25f;
    [SerializeField] private float tapAttackPulseDuration = 0.05f;

    private struct PendingTouch
    {
        public int touchId;
        public Vector2 startPosition;
        public float startTime;
    }

    private readonly List<PendingTouch> _pending = new List<PendingTouch>();
    private readonly List<int> _cameraEligibleTouchIds = new List<int>();
    private readonly List<RaycastResult> _raycastResults = new List<RaycastResult>();
    private Canvas _canvas;

    private int _activeCameraTouchId = -1;
    private bool _skipNextCameraDelta;
    private float _tapPulseEndTime = -1f;

    void Awake()
    {
        if (orbitalFollow == null)
        {
            var go = GameObject.Find("FreeLook Camera");
            if (go != null) orbitalFollow = go.GetComponent<CinemachineOrbitalFollow>();
        }

        if (targetInput == null)
        {
            var player = GameObject.Find("Player");
            if (player != null) targetInput = player.GetComponent<MiroInputReader>();
        }

        _canvas = GetComponentInParent<Canvas>();

        // Left over from the old IDragHandler approach: an invisible
        // raycastTarget Image sized to this object's own rect, previously
        // needed so UGUI's raycaster had something to hit before walking up
        // to find an IDragHandler. Now that input is read via EnhancedTouch
        // instead, that same Image would otherwise make our own IsOverUI()
        // check think every touch over this whole area is "on UI" - i.e. it
        // would wrongly block both camera rotation and tap-attack over what
        // is actually empty game screen. Turn raycasting off on it; the
        // GameObject itself just needs to keep existing to host this script.
        var graphic = GetComponent<Graphic>();
        if (graphic != null) graphic.raycastTarget = false;
    }

    void OnEnable()
    {
        EnhancedTouchSupport.Enable();
        Touch.onFingerDown += HandleFingerDown;
        Touch.onFingerUp += HandleFingerUp;
    }

    void OnDisable()
    {
        Touch.onFingerDown -= HandleFingerDown;
        Touch.onFingerUp -= HandleFingerUp;
        _activeCameraTouchId = -1;
        _skipNextCameraDelta = false;
        _cameraEligibleTouchIds.Clear();
        _pending.Clear();
        if (_tapPulseEndTime > 0f && targetInput != null) targetInput.SetMobileTapAttackHeld(false);
        _tapPulseEndTime = -1f;
    }

    // --- Called by MobileSprayButton (the one exception to "started on UI -> never camera-eligible") ---

    public void RegisterSprayButtonTouch(int touchId) => AddCameraEligible(touchId);
    public void ReleaseSprayButtonTouch(int touchId) => RemoveCameraEligible(touchId);

    private void HandleFingerDown(Finger finger)
    {
        var touch = finger.currentTouch;
        if (IsOverUI(touch.screenPosition)) return; // started on the joystick/spray/any UI - never eligible via THIS path, even later off empty screen

        _pending.Add(new PendingTouch
        {
            touchId = touch.touchId,
            startPosition = touch.screenPosition,
            startTime = Time.unscaledTime,
        });
    }

    private void HandleFingerUp(Finger finger)
    {
        int touchId = finger.currentTouch.touchId;

        // No-op if this id was never in the pool (e.g. a joystick touch, or
        // any other UI touch) - safe to call unconditionally for every
        // finger lift on the whole screen.
        RemoveCameraEligible(touchId);

        for (int i = 0; i < _pending.Count; i++)
        {
            if (_pending[i].touchId != touchId) continue;

            float held = Time.unscaledTime - _pending[i].startTime;
            _pending.RemoveAt(i);

            if (held <= tapMaxDuration) FireTapAttack();
            return;
        }
    }

    private void AddCameraEligible(int touchId)
    {
        if (!_cameraEligibleTouchIds.Contains(touchId)) _cameraEligibleTouchIds.Add(touchId);
        EnsureActiveCameraTouch();
    }

    private void RemoveCameraEligible(int touchId)
    {
        _cameraEligibleTouchIds.Remove(touchId);
        if (_activeCameraTouchId == touchId)
        {
            _activeCameraTouchId = -1;
            EnsureActiveCameraTouch();
        }
    }

    // Never preempts an existing active finger - only promotes when there
    // isn't one, so two simultaneously-eligible fingers (e.g. spray button
    // held AND an empty-screen drag crossed its threshold) can't both drive
    // rotation at once. The displaced one simply waits in the pool until the
    // active one releases.
    private void EnsureActiveCameraTouch()
    {
        if (_activeCameraTouchId != -1) return;
        if (_cameraEligibleTouchIds.Count == 0) return;

        _activeCameraTouchId = _cameraEligibleTouchIds[_cameraEligibleTouchIds.Count - 1];
        _skipNextCameraDelta = true;
    }

    private void FireTapAttack()
    {
        if (targetInput == null) return;
        targetInput.SetMobileTapAttackHeld(true);
        _tapPulseEndTime = Time.unscaledTime + tapAttackPulseDuration;
    }

    private bool IsOverUI(Vector2 screenPosition)
    {
        if (EventSystem.current == null) return false;
        var data = new PointerEventData(EventSystem.current) { position = screenPosition };
        _raycastResults.Clear();
        EventSystem.current.RaycastAll(data, _raycastResults);
        return _raycastResults.Count > 0;
    }

    void Update()
    {
        UpdateCamera();
        UpdatePendingTouches();
        UpdateTapPulse();
    }

    private void UpdateCamera()
    {
        if (orbitalFollow == null || _activeCameraTouchId == -1) return;

        foreach (var touch in Touch.activeTouches)
        {
            if (touch.touchId != _activeCameraTouchId) continue;

            if (touch.phase == UnityEngine.InputSystem.TouchPhase.Ended || touch.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
            {
                RemoveCameraEligible(_activeCameraTouchId);
                return; // no camera finger this frame -> zero delta, not the last known direction
            }

            if (_skipNextCameraDelta) { _skipNextCameraDelta = false; return; }

            ApplyCameraDelta(touch.delta);
            return;
        }

        // The active touchId wasn't in this frame's active list at all (can
        // happen the same frame a touch ends) - same "no finger, no delta" rule.
        RemoveCameraEligible(_activeCameraTouchId);
    }

    private void UpdatePendingTouches()
    {
        if (_pending.Count == 0) return;

        float movementThreshold = tapMaxMovementRefPixels * (_canvas != null ? _canvas.scaleFactor : 1f);
        float thresholdSq = movementThreshold * movementThreshold;

        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var pending = _pending[i];
            Vector2? currentPos = FindActiveTouchPosition(pending.touchId);
            if (currentPos == null) continue; // HandleFingerUp will remove it when the up event arrives

            if ((currentPos.Value - pending.startPosition).sqrMagnitude >= thresholdSq)
            {
                _pending.RemoveAt(i); // committed to camera - the crossing frame's delta is intentionally not applied
                AddCameraEligible(pending.touchId);
            }
        }
    }

    private static Vector2? FindActiveTouchPosition(int touchId)
    {
        foreach (var touch in Touch.activeTouches)
        {
            if (touch.touchId == touchId) return touch.screenPosition;
        }
        return null;
    }

    private void UpdateTapPulse()
    {
        if (_tapPulseEndTime < 0f) return;
        if (Time.unscaledTime < _tapPulseEndTime) return;

        if (targetInput != null) targetInput.SetMobileTapAttackHeld(false);
        _tapPulseEndTime = -1f;
    }

    private void ApplyCameraDelta(Vector2 delta)
    {
        float sens = sensitivity * GameSettings.MouseSensitivity;

        var h = orbitalFollow.HorizontalAxis;
        h.Value = h.ClampValue(h.Value + delta.x * sens);
        orbitalFollow.HorizontalAxis = h;

        var v = orbitalFollow.VerticalAxis;
        v.Value = v.ClampValue(v.Value - delta.y * sens);
        orbitalFollow.VerticalAxis = v;
    }
}
