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
    private Vector2 mousePosition;
    private Camera aimCamera;
    private bool mouseAiming;
    private float verticalSpeed;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (viewCamera == null || !viewCamera.TryGetComponent(out aimCamera))
        {
            Debug.LogError("PlayerMovement requires a viewCamera reference with a Camera component.", this);
            enabled = false;
        }
    }

    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    public void OnAim(InputValue value)
    {
        aimInput = value.Get<Vector2>();
        if (aimInput.sqrMagnitude > 0.01f)
            mouseAiming = false;
    }

    public void OnMouseAim(InputValue value)
    {
        mousePosition = value.Get<Vector2>();
        mouseAiming = true;
    }

    private void Update()
    {
        if (viewCamera == null || aimCamera == null)
        {
            Debug.LogError("PlayerMovement lost its view camera reference.", this);
            enabled = false;
            return;
        }

        // Camera directions projected onto the arena floor.
        Vector3 forward = viewCamera.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
        {
            Vector3 cameraRight = Vector3.ProjectOnPlane(viewCamera.right, Vector3.up).normalized;
            forward = Vector3.Cross(cameraRight, Vector3.up);
        }
        else
        {
            forward.Normalize();
        }

        Vector3 right = Vector3.Cross(Vector3.up, forward);

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

        Vector3 aimDirection = Vector3.zero;
        if (mouseAiming)
        {
            // Intersect the cursor ray with the player's horizontal plane.
            // Recalculate after moving so a stationary cursor remains a world-space target.
            Ray ray = aimCamera.ScreenPointToRay(mousePosition);
            Plane aimPlane = new Plane(Vector3.up, transform.position);
            if (aimPlane.Raycast(ray, out float distance))
                aimDirection = ray.GetPoint(distance) - transform.position;
        }
        else if (aimInput.sqrMagnitude > 0.01f)
        {
            aimDirection = right * aimInput.x + forward * aimInput.y;
        }

        // Keep the last facing direction when the stick is released or the cursor is over the player.
        aimDirection.y = 0f;
        if (aimDirection.sqrMagnitude > 0.0001f)
        {
            transform.rotation =
                Quaternion.LookRotation(aimDirection, Vector3.up);
        }
    }

    private void OnDisable()
    {
        moveInput = Vector2.zero;
        aimInput = Vector2.zero;
        mouseAiming = false;
        verticalSpeed = 0f;
    }
}
