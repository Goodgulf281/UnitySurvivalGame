using UnityEngine;
using UnityEngine.InputSystem;

namespace Goodgulf.Controller
{

    [RequireComponent(typeof(CharacterController))]
    public class ThirdPersonController : MonoBehaviour
    {
        // --------------------
        // Movement tuning
        // --------------------
        [Header("Movement")] public float moveSpeed = 5f; // Horizontal movement speed
        public float rotationSpeed = 10f; // How fast the character rotates toward movement direction
        public float jumpForce = 5f; // Jump strength
        public float gravity = -9.81f; // Gravity applied manually

        // --------------------
        // References
        // --------------------
        [Header("References")] public Transform cameraTransform; // Used to make movement camera-relative

        // --------------------
        // Internal state
        // --------------------
        private CharacterController controller; // CharacterController component
        private PlayerInputActions inputActions; // Generated Input Actions class

        private Vector2 moveInput; // Stores movement input (WASD / stick)
        private bool jumpPressed; // Set when jump input is pressed
        private Vector3 velocity; // Vertical velocity (gravity + jump)
        private bool isGrounded; // Grounded state

        void Awake()
        {
            // Cache required components
            controller = GetComponent<CharacterController>();

            // Create input action instance
            inputActions = new PlayerInputActions();
        }

        void OnEnable()
        {
            // Enable Player action map
            inputActions.Player.Enable();

            // Subscribe to movement input
            inputActions.Player.Move.performed += ctx =>
                moveInput = ctx.ReadValue<Vector2>();

            inputActions.Player.Move.canceled += ctx =>
                moveInput = Vector2.zero;

            // Subscribe to jump input
            inputActions.Player.Jump.performed += ctx =>
                jumpPressed = true;
        }

        void OnDisable()
        {
            // Disable inputs when object is disabled
            inputActions.Player.Disable();
        }

        void Update()
        {
            HandleGroundCheck();
            HandleMovement();
            HandleGravityAndJump();
        }

        /// <summary>
        /// Checks if the player is grounded and keeps them snapped to ground.
        /// </summary>
        void HandleGroundCheck()
        {
            isGrounded = controller.isGrounded;

            // Small downward force to prevent floating
            if (isGrounded && velocity.y < 0f)
            {
                velocity.y = -2f;
            }
        }

        /// <summary>
        /// Handles horizontal movement and rotation.
        /// </summary>
        void HandleMovement()
        {
            // Convert input into world-space direction
            Vector3 inputDir = new Vector3(moveInput.x, 0f, moveInput.y);

            // Ignore tiny input values
            if (inputDir.magnitude < 0.1f) return;

            // Get camera-relative directions
            Vector3 camForward = cameraTransform.forward;
            Vector3 camRight = cameraTransform.right;

            // Remove vertical component so movement stays on ground plane
            camForward.y = 0f;
            camRight.y = 0f;

            // Combine input with camera orientation
            Vector3 moveDir =
                camForward.normalized * inputDir.z +
                camRight.normalized * inputDir.x;

            // Smoothly rotate character toward movement direction
            Quaternion targetRotation = Quaternion.LookRotation(moveDir);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );

            // Move character
            controller.Move(moveDir * moveSpeed * Time.deltaTime);
        }

        /// <summary>
        /// Applies gravity and handles jumping.
        /// </summary>
        void HandleGravityAndJump()
        {
            // Jump only when grounded
            if (isGrounded && jumpPressed)
            {
                velocity.y = Mathf.Sqrt(jumpForce * -2f * gravity);
                jumpPressed = false; // Consume jump input
            }

            // Apply gravity
            velocity.y += gravity * Time.deltaTime;

            // Apply vertical movement
            controller.Move(velocity * Time.deltaTime);
        }
    }

}