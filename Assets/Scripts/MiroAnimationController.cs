using UnityEngine;

public class MiroAnimationController : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private CharacterController controller;
    [SerializeField] private MiroInputReader input;

    private static readonly int SpeedParam = Animator.StringToHash("Speed");
    private static readonly int GroundedParam = Animator.StringToHash("Grounded");
    private static readonly int SprayingParam = Animator.StringToHash("Spraying");
    private static readonly int JumpParam = Animator.StringToHash("Jump");

    void Awake()
    {
        if (controller == null) controller = GetComponent<CharacterController>();
        if (input == null) input = GetComponent<MiroInputReader>();
    }

    void Update()
    {
        if (animator == null || controller == null || input == null) return;

        Vector3 horizontalVelocity = controller.velocity;
        horizontalVelocity.y = 0f;

        animator.SetFloat(SpeedParam, horizontalVelocity.magnitude);
        animator.SetBool(GroundedParam, controller.isGrounded);
        animator.SetBool(SprayingParam, input.SprayHeld);

        if (controller.isGrounded && input.JumpPressed)
        {
            animator.SetTrigger(JumpParam);
        }
    }
}