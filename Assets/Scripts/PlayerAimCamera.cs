using UnityEngine;
using Unity.Cinemachine;

// Blends the existing FreeLook camera toward a Paint Action Shooter framing
// while Spray is held, and back when released. Reads the Normal baseline
// straight from the live Cinemachine components at Awake (never hardcoded),
// and only ever adds the Aim deltas below on top of it.
//
// Composition.ScreenPosition sign convention (confirmed from Cinemachine's
// own field tooltip): 0 = screen center, +0.5 = right edge, -0.5 = left edge.
// To put Miro on screen-LEFT (so the aim/spray space reads on the right),
// aimScreenPositionOffset.x must be NEGATIVE.
public class PlayerAimCamera : MonoBehaviour
{
    [SerializeField] private CinemachineCamera cineCamera;
    [SerializeField] private CinemachineOrbitalFollow orbitalFollow;
    [SerializeField] private CinemachineRotationComposer rotationComposer;
    [SerializeField] private CinemachineInputAxisController inputAxisController;
    [SerializeField] private MiroInputReader input;

    [SerializeField] private float aimShoulderOffsetX = 0.6f;
    [SerializeField] private float aimHeightOffset = 0.25f;
    [SerializeField] private Vector2 aimScreenPositionOffset = new Vector2(-0.18f, 0.05f);
    [SerializeField] private float aimFovDelta = -2f;
    [SerializeField] private float transitionDuration = 0.25f;

    [Header("Ground visibility / pitch (Center ring of the ThreeRing orbit)")]
    [SerializeField] private float aimCenterHeightDelta = 0.3f;
    [SerializeField] private float aimCenterRadiusDelta = -0.2f;

    [Header("Distance (applied to all three orbit rings equally)")]
    [SerializeField] private float aimRadiusDelta = 0.3f;

    private Vector3 normalTargetOffset;
    private Vector2 normalScreenPosition;
    private float normalFov;

    private float normalCenterHeight, normalCenterRadius;
    private float normalTopRadius, normalBottomRadius;

    private float blend;

    void Awake()
    {
        if (cineCamera == null)
        {
            var go = GameObject.Find("FreeLook Camera");
            if (go != null) cineCamera = go.GetComponent<CinemachineCamera>();
        }
        if (orbitalFollow == null && cineCamera != null) orbitalFollow = cineCamera.GetComponent<CinemachineOrbitalFollow>();
        if (rotationComposer == null && cineCamera != null) rotationComposer = cineCamera.GetComponent<CinemachineRotationComposer>();
        if (inputAxisController == null && cineCamera != null) inputAxisController = cineCamera.GetComponent<CinemachineInputAxisController>();

        ApplyMouseSensitivity();

        if (input == null)
        {
            var player = GameObject.Find("Player");
            if (player != null) input = player.GetComponent<MiroInputReader>();
        }

        if (orbitalFollow != null)
        {
            normalTargetOffset = orbitalFollow.TargetOffset;
            normalCenterHeight = orbitalFollow.Orbits.Center.Height;
            normalCenterRadius = orbitalFollow.Orbits.Center.Radius;
            normalTopRadius = orbitalFollow.Orbits.Top.Radius;
            normalBottomRadius = orbitalFollow.Orbits.Bottom.Radius;
        }
        if (rotationComposer != null) normalScreenPosition = rotationComposer.Composition.ScreenPosition;
        if (cineCamera != null) normalFov = cineCamera.Lens.FieldOfView;
    }

    // Scales only the look axes (not "Orbit Scale", which is scroll-wheel
    // zoom) by the saved sensitivity multiplier, preserving each axis's own
    // authored base gain/sign (Look Orbit Y is naturally -1, for instance)
    // rather than overwriting it outright. Reads the base gain once per axis
    // (whatever is authored in the scene) and multiplies by sensitivity -
    // safe to call only once at startup since it's not re-entrant.
    private void ApplyMouseSensitivity()
    {
        if (inputAxisController == null) return;

        float sensitivity = GameSettings.MouseSensitivity;
        var controllers = inputAxisController.Controllers;
        for (int i = 0; i < controllers.Count; i++)
        {
            var controller = controllers[i];
            if (controller.Name == "Look Orbit X" || controller.Name == "Look Orbit Y")
            {
                controller.Input.Gain *= sensitivity;
            }
        }
    }

    void Update()
    {
        bool aiming = input != null && input.SprayHeld;
        float target = aiming ? 1f : 0f;
        float step = transitionDuration > 0f ? Time.deltaTime / transitionDuration : 1f;
        blend = Mathf.MoveTowards(blend, target, step);

        // Ease-in-out on top of the fixed-duration linear blend above, using
        // only Unity's built-in SmoothStep - no tween library.
        float t = Mathf.SmoothStep(0f, 1f, blend);

        if (orbitalFollow != null)
        {
            Vector3 aimTargetOffset = normalTargetOffset + new Vector3(aimShoulderOffsetX, aimHeightOffset, 0f);
            orbitalFollow.TargetOffset = Vector3.Lerp(normalTargetOffset, aimTargetOffset, t);

            var orbits = orbitalFollow.Orbits;
            orbits.Center.Height = Mathf.Lerp(normalCenterHeight, normalCenterHeight + aimCenterHeightDelta, t);
            orbits.Center.Radius = Mathf.Lerp(normalCenterRadius, normalCenterRadius + aimCenterRadiusDelta + aimRadiusDelta, t);
            orbits.Top.Radius = Mathf.Lerp(normalTopRadius, normalTopRadius + aimRadiusDelta, t);
            orbits.Bottom.Radius = Mathf.Lerp(normalBottomRadius, normalBottomRadius + aimRadiusDelta, t);
            orbitalFollow.Orbits = orbits;
        }

        if (rotationComposer != null)
        {
            var composition = rotationComposer.Composition;
            composition.ScreenPosition = Vector2.Lerp(normalScreenPosition, normalScreenPosition + aimScreenPositionOffset, t);
            rotationComposer.Composition = composition;
        }

        if (cineCamera != null)
        {
            var lens = cineCamera.Lens;
            lens.FieldOfView = Mathf.Lerp(normalFov, normalFov + aimFovDelta, t);
            cineCamera.Lens = lens;
        }
    }
}
