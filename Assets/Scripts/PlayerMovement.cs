using UnityEngine;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(MiroInputReader))]
public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private PlayerSpray spray;
    [SerializeField] private PlayerHealth playerHealth;

    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float rotationSmoothTime = 0.12f;
    [SerializeField] private float aimRotationSpeed = 720f;
    [SerializeField] private float gravity = -25f;
    [SerializeField] private float groundedStickVelocity = -2f;
    [SerializeField] private float jumpHeight = 1.35f;

    // No foot-contact Animation Events exist on the character's run clip
    // (checked - 0 events), so footsteps are driven by actual CharacterController
    // speed instead. moveSpeed is constant (no sprint exists yet), so in
    // practice this always lands on stepIntervalNormal today; the speed-based
    // blend is here so a future speed upgrade is handled correctly without
    // revisiting this.
    [SerializeField] private AudioClip[] footstepClips;
    [SerializeField] private float footstepVolumeMin = 0.25f;
    [SerializeField] private float footstepVolumeMax = 0.40f;
    [SerializeField] private float footstepPitchJitter = 0.04f;
    [SerializeField] private float stepIntervalNormal = 0.42f;
    [SerializeField] private float stepIntervalFast = 0.32f;
    [SerializeField] private float footstepSpeedThreshold = 0.5f;

    private float footstepTimer;

    private CharacterController controller;
    private MiroInputReader input;
    private float verticalVelocity;
    private float rotationVelocity;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        input = GetComponent<MiroInputReader>();
        if (spray == null) spray = GetComponent<PlayerSpray>();
        if (playerHealth == null) playerHealth = GetComponent<PlayerHealth>();

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    void Update()
    {
        Vector2 moveInput = input.MoveInput;
        Vector3 moveDirection = Vector3.zero;

        if (moveInput.sqrMagnitude > 0.0001f && cameraTransform != null)
        {
            Vector3 camForward = cameraTransform.forward;
            Vector3 camRight = cameraTransform.right;
            camForward.y = 0f;
            camRight.y = 0f;
            camForward.Normalize();
            camRight.Normalize();

            moveDirection = camForward * moveInput.y + camRight * moveInput.x;
        }

        bool isMoving = moveDirection.sqrMagnitude > 0.0001f;
        bool isSpraying = input.SprayHeld;

        if (isSpraying && spray != null)
        {
            // Phase D/8: while spraying, facing follows the camera's aim
            // instead of movement - moveDirection above (used for
            // controller.Move() below) is untouched, so WASD still strafes
            // freely around the character's aim-locked facing.
            Vector3 aimDir = spray.AimDirection;
            aimDir.y = 0f;
            if (aimDir.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(aimDir.normalized);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, aimRotationSpeed * Time.deltaTime);
            }
        }
        else if (isMoving)
        {
            float targetAngle = Mathf.Atan2(moveDirection.x, moveDirection.z) * Mathf.Rad2Deg;
            float smoothedAngle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref rotationVelocity, rotationSmoothTime);
            transform.rotation = Quaternion.Euler(0f, smoothedAngle, 0f);
        }

        bool grounded = controller.isGrounded;

        if (grounded && verticalVelocity < 0f)
        {
            verticalVelocity = groundedStickVelocity;
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        if (grounded && input.JumpPressed)
        {
            verticalVelocity = Mathf.Sqrt(2f * (-gravity) * jumpHeight);
        }

        Vector3 horizontalMotion = moveDirection.normalized * moveSpeed;
        Vector3 motion = new Vector3(horizontalMotion.x, verticalVelocity, horizontalMotion.z);
        controller.Move(motion * Time.deltaTime);

        UpdateFootsteps(grounded);
    }

    // controller.velocity (post-Move, actual traveled speed) rather than the
    // raw input direction - so footsteps reflect real movement, not input
    // that was blocked by a wall this frame.
    private void UpdateFootsteps(bool grounded)
    {
        Vector3 horizontalVelocity = controller.velocity;
        horizontalVelocity.y = 0f;
        float speed = horizontalVelocity.magnitude;

        bool canStep = grounded && speed > footstepSpeedThreshold && Time.timeScale > 0f &&
                       (playerHealth == null || !playerHealth.IsDead);

        if (!canStep)
        {
            footstepTimer = 0f;
            return;
        }

        footstepTimer -= Time.deltaTime;
        if (footstepTimer > 0f) return;

        float speedRatio = moveSpeed > 0f ? Mathf.Clamp01((speed - moveSpeed) / moveSpeed) : 0f;
        footstepTimer = Mathf.Lerp(stepIntervalNormal, stepIntervalFast, speedRatio);

        if (AudioManager.Instance != null)
        {
            float volume = Random.Range(footstepVolumeMin, footstepVolumeMax);
            AudioManager.Instance.PlaySfxRandom(footstepClips, volume, footstepPitchJitter);
        }
    }
}