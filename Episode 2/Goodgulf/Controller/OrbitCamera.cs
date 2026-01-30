using UnityEngine;
using UnityEngine.InputSystem;

namespace Goodgulf.Controller
{

public class OrbitCamera : MonoBehaviour
{
    // --------------------
    // Target
    // --------------------
    public Transform target;   // Object the camera orbits around (player)

    // --------------------
    // Camera tuning
    // --------------------
    [Header("Orbit Settings")]
    public float distance = 5f;            // Distance from target
    public float mouseSensitivity = 1.5f;  // Look sensitivity (mouse / stick)
    public float minYAngle = -30f;          // Minimum vertical angle
    public float maxYAngle = 70f;           // Maximum vertical angle
    public float smoothTime = 0.05f;        // Camera smoothing time

    // --------------------
    // Internal state
    // --------------------
    private PlayerInputActions inputActions; // Input Actions instance
    private Vector2 lookInput;               // Mouse / right stick input

    private float yaw;       // Horizontal rotation
    private float pitch;     // Vertical rotation
    private Vector3 currentVelocity; // Used by SmoothDamp

    void Awake()
    {
        // Create input action instance
        inputActions = new PlayerInputActions();
    }

    void OnEnable()
    {
        // Enable Player action map
        inputActions.Player.Enable();

        // Subscribe to look input
        inputActions.Player.Look.performed += ctx =>
            lookInput = ctx.ReadValue<Vector2>();

        inputActions.Player.Look.canceled += ctx =>
            lookInput = Vector2.zero;
    }

    void OnDisable()
    {
        // Disable inputs when object is disabled
        inputActions.Player.Disable();
    }

    void Start()
    {
        // Initialize rotation values from current camera rotation
        Vector3 angles = transform.eulerAngles;
        yaw = angles.y;
        pitch = angles.x;

        // Lock and hide cursor for camera control
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void LateUpdate()
    {
        if (!target) return;

        // Accumulate rotation from input
        yaw += lookInput.x * mouseSensitivity * Time.deltaTime * 100f;
        pitch -= lookInput.y * mouseSensitivity * Time.deltaTime * 100f;

        // Clamp vertical rotation
        pitch = Mathf.Clamp(pitch, minYAngle, maxYAngle);

        // Calculate desired rotation
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);

        // Calculate desired camera position
        Vector3 desiredPosition =
            target.position - rotation * Vector3.forward * distance;

        // Smoothly move camera
        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref currentVelocity,
            smoothTime
        );

        // Apply rotation
        transform.rotation = rotation;
    }
}
}
