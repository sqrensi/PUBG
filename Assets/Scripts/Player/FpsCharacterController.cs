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
        [SerializeField] private float crouchSpeedMultiplier = 0.55f;
        [SerializeField] private float sneakSpeedMultiplier = 0.78f;
        [SerializeField] private float crouchControllerHeight = 1f;
        [SerializeField] private float crouchDownSmoothTime = 0.18f;
        [SerializeField] private float crouchUpSmoothTime = 0.22f;
        [SerializeField] private float crouchCameraOffset = 0.38f;

        [Header("Look")]
        [SerializeField] private float mouseSensitivity = 2.2f;
        [SerializeField] private float maxLookAngle = 80f;
        [SerializeField] private float hipMaxLookAngle = 50f;
        [SerializeField] private float adsMaxLookAngle = 40f;
        [SerializeField] private bool enableRecoilRecovery = true;
        [SerializeField] private float manualRecoilRecoveryScale = 1f;

        [Header("State")]
        [SerializeField] private bool lockCursorOnEnable = true;
        [SerializeField] private bool toggleCursorWithTab = true;
        [SerializeField] private bool pauseControlsWhenCursorUnlocked = true;

        [Header("Ground Check")]
        [SerializeField] private float groundedSphereRadius = 0.2f;
        [SerializeField] private float groundedSphereOffset = 0.03f;
        [SerializeField] private float groundedCheckDistance = 0.08f;
        [SerializeField] private LayerMask groundedMask = ~0;

        private CharacterController characterController;
        private float verticalVelocity;
        private float cameraPitch;
        private float horizontalSpeed;
        private float moveInputMagnitude;
        private bool isGrounded;
        private bool isCrouching;
        private bool isSneaking;
        private float recoilPitchOffset;
        private float recoilRecoverySpeed = 18f;
        private float recoilRecoveryBoostUntil;
        private float recoilRecoveryBoostMultiplier = 1f;
        private bool autoRecoilRecoveryActive = true;
        private float standingHeight;
        private float standingCenterY;
        private float standingCameraLocalY;
        private float characterBottomOffset;
        private float crouchHeightVelocity;
        private float crouchCameraVelocity;
        private readonly Collider[] standCheckHits = new Collider[16];
        private float nextFootstepAt;
        private int footstepSequence;
        private PlayerAudioController audioController;
        [Header("Audio")]
        [SerializeField] private float footstepIntervalSlow = 0.8f;
        [SerializeField] private float footstepIntervalFast = 0.42f;

        public bool IsGrounded => isGrounded;
        public bool IsCrouching => isCrouching;
        public bool IsSneaking => isSneaking;
        public float CrouchBlend01
        {
            get
            {
                if (cameraPivot == null || crouchCameraOffset <= 0.001f)
                {
                    return isCrouching ? 1f : 0f;
                }

                var down = standingCameraLocalY - cameraPivot.localPosition.y;
                return Mathf.Clamp01(down / Mathf.Max(0.001f, crouchCameraOffset));
            }
        }
        public float VerticalVelocity => verticalVelocity;
        public float HorizontalSpeed => horizontalSpeed;
        public float MoveInputMagnitude => moveInputMagnitude;
        public float CurrentLookPitch => cameraPitch + recoilPitchOffset;
        public int LastFootstepSequence => footstepSequence;

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
            audioController = GetComponent<PlayerAudioController>();

            standingHeight = characterController != null ? characterController.height : 1.8f;
            standingCenterY = characterController != null ? characterController.center.y : standingHeight * 0.5f;
            standingCameraLocalY = cameraPivot != null ? cameraPivot.localPosition.y : 1.6f;
            characterBottomOffset = standingCenterY - (standingHeight * 0.5f);
            isGrounded = EvaluateGrounded();
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
            HandleCursorToggle();
            if (ShouldPauseControls())
            {
                horizontalSpeed = 0f;
                moveInputMagnitude = 0f;
                return;
            }

            TickLook();
            TickMove();
        }

        private void HandleCursorToggle()
        {
            if (!toggleCursorWithTab || !ReadToggleCursorPressed())
            {
                return;
            }

            var locked = Cursor.lockState == CursorLockMode.Locked;
            Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = locked;
        }

        private bool ShouldPauseControls()
        {
            if (!pauseControlsWhenCursorUnlocked)
            {
                return false;
            }

            return Cursor.lockState != CursorLockMode.Locked;
        }

        private void TickLook()
        {
            var lookDelta = ReadLookInput();
            var mouseX = lookDelta.x * mouseSensitivity;
            var mouseY = lookDelta.y * mouseSensitivity;

            transform.Rotate(Vector3.up * mouseX, Space.Self);

            cameraPitch -= mouseY;
            ApplyManualRecoilRecovery(mouseY);
            var fallback = Mathf.Clamp(maxLookAngle, 1f, 89f);
            var hipLimit = Mathf.Clamp(hipMaxLookAngle, 1f, 89f);
            var adsLimit = Mathf.Clamp(adsMaxLookAngle, 1f, 89f);
            var lookLimit = ReadAimPressed() ? adsLimit : hipLimit;
            if (lookLimit <= 0f)
            {
                lookLimit = fallback;
            }
            cameraPitch = Mathf.Clamp(cameraPitch, -lookLimit, lookLimit);
            if (enableRecoilRecovery && autoRecoilRecoveryActive)
            {
                var recoverySpeed = recoilRecoverySpeed;
                if (Time.time < recoilRecoveryBoostUntil)
                {
                    recoverySpeed *= Mathf.Max(1f, recoilRecoveryBoostMultiplier);
                }
                else
                {
                    recoilRecoveryBoostMultiplier = 1f;
                }

                recoilPitchOffset = Mathf.MoveTowards(recoilPitchOffset, 0f, recoverySpeed * Time.deltaTime);
            }
            cameraPivot.localRotation = Quaternion.Euler(cameraPitch + recoilPitchOffset, 0f, 0f);
        }

        private void ApplyManualRecoilRecovery(float mouseY)
        {
            // Pulling mouse down should manually compensate recoil even when auto recovery is disabled.
            if (mouseY >= -0.0001f)
            {
                return;
            }

            var manualRecoveryAmount = -mouseY * Mathf.Max(0f, manualRecoilRecoveryScale);
            recoilPitchOffset = Mathf.MoveTowards(recoilPitchOffset, 0f, manualRecoveryAmount);
        }

        public void ApplyRecoil(float pitchUpDegrees, float yawDegrees)
        {
            recoilPitchOffset -= Mathf.Abs(pitchUpDegrees);
            if (Mathf.Abs(yawDegrees) > 0.0001f)
            {
                transform.Rotate(Vector3.up * yawDegrees, Space.Self);
            }
        }

        public void BoostRecoilRecovery(float durationSeconds, float multiplier, float dampFactor = 1f)
        {
            if (!enableRecoilRecovery || !autoRecoilRecoveryActive)
            {
                return;
            }

            recoilRecoveryBoostUntil = Time.time + Mathf.Max(0f, durationSeconds);
            recoilRecoveryBoostMultiplier = Mathf.Max(1f, multiplier);
            if (dampFactor < 0.999f)
            {
                recoilPitchOffset *= Mathf.Clamp01(dampFactor);
            }
        }

        public void SetAutoRecoilRecoveryActive(bool isActive)
        {
            autoRecoilRecoveryActive = isActive;
            if (!autoRecoilRecoveryActive)
            {
                recoilRecoveryBoostUntil = 0f;
                recoilRecoveryBoostMultiplier = 1f;
            }
        }

        private void TickMove()
        {
            UpdateCrouchState();
            isSneaking = !isCrouching && ReadSneakPressed();

            var moveInput = ReadMoveInput();
            var inputX = moveInput.x;
            var inputZ = moveInput.y;
            moveInputMagnitude = Mathf.Clamp01(moveInput.magnitude);

            var moveDirection = (transform.right * inputX + transform.forward * inputZ);
            if (moveDirection.sqrMagnitude > 1f)
            {
                moveDirection.Normalize();
            }

            isGrounded = EvaluateGrounded();
            if (isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -1.5f;
            }

            if (isGrounded && ReadJumpPressed())
            {
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                audioController?.PlayJump(true);
            }

            verticalVelocity += gravity * Time.deltaTime;

            var speedMultiplier = 1f;
            if (isCrouching)
            {
                speedMultiplier = Mathf.Clamp(crouchSpeedMultiplier, 0.1f, 1f);
            }
            else if (isSneaking)
            {
                speedMultiplier = Mathf.Clamp(sneakSpeedMultiplier, 0.1f, 1f);
            }
            var velocity = moveDirection * (moveSpeed * speedMultiplier);
            velocity.y = verticalVelocity;
            characterController.Move(velocity * Time.deltaTime);
            isGrounded = EvaluateGrounded();

            var ccVelocity = characterController.velocity;
            horizontalSpeed = new Vector2(ccVelocity.x, ccVelocity.z).magnitude;
            TryEmitFootstep();
        }

        public void PlayRemoteFootstep()
        {
            audioController?.PlayFootstep(false);
        }

        public void PlayRemoteJump()
        {
            audioController?.PlayJump(false);
        }

        private void TryEmitFootstep()
        {
            // Crouch movement is intentionally silent.
            if (isCrouching || isSneaking)
            {
                return;
            }

            if (!isGrounded || moveInputMagnitude < 0.12f || Time.time < nextFootstepAt)
            {
                return;
            }

            var cadence = Mathf.Lerp(
                Mathf.Max(0.1f, footstepIntervalSlow),
                Mathf.Max(0.08f, footstepIntervalFast),
                Mathf.Clamp01(horizontalSpeed / Mathf.Max(0.01f, moveSpeed)));

            nextFootstepAt = Time.time + cadence;
            footstepSequence++;
            audioController?.PlayFootstep(true);
        }

        private void UpdateCrouchState()
        {
            if (characterController == null || cameraPivot == null)
            {
                return;
            }

            // Hold-to-crouch: input state directly drives crouch, no toggle.
            var crouchPressed = ReadCrouchPressed();
            if (!crouchPressed && isCrouching && !CanStandUp())
            {
                crouchPressed = true;
            }

            isCrouching = crouchPressed;

            var targetHeight = isCrouching
                ? Mathf.Clamp(crouchControllerHeight, 0.8f, standingHeight)
                : standingHeight;
            var smoothTime = isCrouching
                ? Mathf.Max(0.01f, crouchDownSmoothTime)
                : Mathf.Max(0.01f, crouchUpSmoothTime);
            var nextHeight = Mathf.SmoothDamp(
                characterController.height,
                targetHeight,
                ref crouchHeightVelocity,
                smoothTime);
            characterController.height = nextHeight;
            var center = characterController.center;
            center.y = characterBottomOffset + nextHeight * 0.5f;
            characterController.center = center;

            var targetCameraY = isCrouching
                ? standingCameraLocalY - Mathf.Max(0.01f, crouchCameraOffset)
                : standingCameraLocalY;
            var cameraLocalPos = cameraPivot.localPosition;
            cameraLocalPos.y = Mathf.SmoothDamp(
                cameraLocalPos.y,
                targetCameraY,
                ref crouchCameraVelocity,
                smoothTime);
            cameraPivot.localPosition = cameraLocalPos;
        }

        private bool CanStandUp()
        {
            if (characterController == null)
            {
                return true;
            }

            var radius = Mathf.Max(0.05f, characterController.radius * 0.95f);
            var bottomY = transform.position.y + characterBottomOffset + radius;
            var desiredTopY = transform.position.y + characterBottomOffset + standingHeight - radius;
            var bottom = new Vector3(transform.position.x, bottomY, transform.position.z);
            var top = new Vector3(transform.position.x, desiredTopY, transform.position.z);
            var hitCount = Physics.OverlapCapsuleNonAlloc(
                bottom,
                top,
                radius,
                standCheckHits,
                groundedMask,
                QueryTriggerInteraction.Ignore);
            for (var i = 0; i < hitCount; i++)
            {
                var hit = standCheckHits[i];
                if (hit == null)
                {
                    continue;
                }

                if (hit.transform.IsChildOf(transform))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private bool EvaluateGrounded()
        {
            if (characterController == null)
            {
                return false;
            }

            var worldCenter = transform.TransformPoint(characterController.center);
            var feetY = worldCenter.y - (characterController.height * 0.5f) + characterController.radius;
            var radius = Mathf.Max(0.01f, groundedSphereRadius);
            var castDistance = Mathf.Max(0.01f, groundedCheckDistance);
            var castOrigin = new Vector3(
                worldCenter.x,
                feetY + radius + groundedSphereOffset,
                worldCenter.z);

            var hits = Physics.SphereCastAll(
                castOrigin,
                radius,
                Vector3.down,
                castDistance,
                groundedMask,
                QueryTriggerInteraction.Ignore);

            for (var i = 0; i < hits.Length; i++)
            {
                var hitCollider = hits[i].collider;
                if (hitCollider == null)
                {
                    continue;
                }

                if (hitCollider.transform.IsChildOf(transform))
                {
                    continue;
                }

                return true;
            }

            return false;
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

        private static bool ReadAimPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.rightButton.isPressed;
#else
            return Input.GetMouseButton(1);
#endif
        }

        private static bool ReadCrouchPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.leftCtrlKey.isPressed;
#else
            return Input.GetKey(KeyCode.LeftControl);
#endif
        }

        private static bool ReadSneakPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
#else
            return Input.GetKey(KeyCode.LeftShift);
#endif
        }

        private static bool ReadToggleCursorPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Tab);
#endif
        }
    }
}
