using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ShooterPrototype.Player
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class FpsCharacterController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private Camera playerCamera;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 5.5f;
        [SerializeField] private float jumpHeight = 1.25f;
        [SerializeField] private float gravity = -24f;

        [Header("Look")]
        [SerializeField] private float mouseSensitivity = 2.2f;
        [SerializeField] private float maxLookAngle = 80f;

        [Header("State")]
        [SerializeField] private bool lockCursorOnEnable = true;

        private CharacterController characterController;
        private float verticalVelocity;
        private float cameraPitch;
        private float horizontalSpeed;
        private float moveInputMagnitude;

        public bool IsGrounded => characterController != null && characterController.isGrounded;
        public float VerticalVelocity => verticalVelocity;
        public float HorizontalSpeed => horizontalSpeed;
        public float MoveInputMagnitude => moveInputMagnitude;

        public void Configure(Transform pivot, Camera localCamera, bool shouldLockCursor = true)
        {
            cameraPivot = pivot;
            playerCamera = localCamera;
            lockCursorOnEnable = shouldLockCursor;
        }

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();

            if (cameraPivot == null)
            {
                cameraPivot = transform;
            }

            if (playerCamera == null)
            {
                playerCamera = GetComponentInChildren<Camera>();
            }
        }

        private void OnEnable()
        {
            if (!lockCursorOnEnable)
            {
                return;
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void OnDisable()
        {
            if (!lockCursorOnEnable)
            {
                return;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            TickLook();
            TickMove();
        }

        private void TickLook()
        {
            var lookDelta = ReadLookInput();
            var mouseX = lookDelta.x * mouseSensitivity;
            var mouseY = lookDelta.y * mouseSensitivity;

            transform.Rotate(Vector3.up * mouseX, Space.Self);

            cameraPitch -= mouseY;
            cameraPitch = Mathf.Clamp(cameraPitch, -maxLookAngle, maxLookAngle);
            cameraPivot.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
        }

        private void TickMove()
        {
            var moveInput = ReadMoveInput();
            var inputX = moveInput.x;
            var inputZ = moveInput.y;
            moveInputMagnitude = Mathf.Clamp01(moveInput.magnitude);

            var moveDirection = (transform.right * inputX + transform.forward * inputZ);
            if (moveDirection.sqrMagnitude > 1f)
            {
                moveDirection.Normalize();
            }

            if (characterController.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -1.5f;
            }

            if (characterController.isGrounded && ReadJumpPressed())
            {
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }

            verticalVelocity += gravity * Time.deltaTime;

            var velocity = moveDirection * moveSpeed;
            velocity.y = verticalVelocity;
            characterController.Move(velocity * Time.deltaTime);

            var ccVelocity = characterController.velocity;
            horizontalSpeed = new Vector2(ccVelocity.x, ccVelocity.z).magnitude;
        }

        private static Vector2 ReadMoveInput()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current == null)
            {
                return Vector2.zero;
            }

            var x = 0f;
            var y = 0f;
            if (Keyboard.current.aKey.isPressed) x -= 1f;
            if (Keyboard.current.dKey.isPressed) x += 1f;
            if (Keyboard.current.sKey.isPressed) y -= 1f;
            if (Keyboard.current.wKey.isPressed) y += 1f;
            return new Vector2(x, y);
#else
            return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
        }

        private static Vector2 ReadLookInput()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null)
            {
                return Vector2.zero;
            }

            return Mouse.current.delta.ReadValue() * 0.02f;
#else
            return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#endif
        }

        private static bool ReadJumpPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
#else
            return Input.GetButtonDown("Jump");
#endif
        }
    }
}
