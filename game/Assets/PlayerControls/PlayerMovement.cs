using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private Transform viewCamera;
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float gravity = -20f;

    private CharacterController controller;
    private Vector2 moveInput;
    private Vector2 aimInput;
    private float verticalSpeed;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    public void OnAim(InputValue value)
    {
        aimInput = value.Get<Vector2>();
    }

    private void Update()
    {
        if (viewCamera == null)
            return;

        // Camera directions projected onto the arena floor.
        Vector3 forward = viewCamera.forward;
        forward.y = 0f;
        forward.Normalize();

        Vector3 right = viewCamera.right;
        right.y = 0f;
        right.Normalize();

        // Move relative to the camera, regardless of aiming direction.
        Vector3 movement =
            right * moveInput.x + forward * moveInput.y;

        // Prevent diagonal keyboard movement from being faster.
        // Preserve partial stick input for slower movement.
        movement = Vector3.ClampMagnitude(movement, 1f);

        if (controller.isGrounded && verticalSpeed < 0f)
            verticalSpeed = -2f;

        verticalSpeed += gravity * Time.deltaTime;

        Vector3 velocity =
            movement * moveSpeed + Vector3.up * verticalSpeed;

        controller.Move(velocity * Time.deltaTime);

        // Keep the last aiming direction when the stick is released.
        if (aimInput.sqrMagnitude > 0.01f)
        {
            Vector3 aimDirection =
                right * aimInput.x + forward * aimInput.y;

            transform.rotation =
                Quaternion.LookRotation(aimDirection, Vector3.up);
        }
    }

    private void OnDisable()
    {
        moveInput = Vector2.zero;
        aimInput = Vector2.zero;
        verticalSpeed = 0f;
    }
}