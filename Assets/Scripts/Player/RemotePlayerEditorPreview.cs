using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Drives a remote TP prefab in Play Mode without multiplayer (editor testing).
    /// </summary>
    [DefaultExecutionOrder(90)]
    public sealed class RemotePlayerEditorPreview : MonoBehaviour
    {
        public const string PreviewObjectName = "EditorRemotePreview";

        [Header("Input")]
        [SerializeField] private bool mirrorLocalPlayerInput;
        [SerializeField] private float turnSpeed = 140f;

        [Header("Movement")]
        [SerializeField] private float walkSpeed = 3.3f;
        [SerializeField] private float sprintSpeed = 5.94f;
        [SerializeField] private float crouchSpeedMultiplier = 0.55f;
        [SerializeField] private float gravity = -18f;

        private CharacterController characterController;
        private ProceduralLocomotionRig locomotionRig;
        private SyntyLocomotionDriver syntyDriver;
        private PlayerWeaponMount weaponMount;
        private float verticalVelocity;
        private bool grounded = true;

        public static RemotePlayerEditorPreview Spawn(GameObject remotePrefab, Vector3 position, float yaw)
        {
            var existing = GameObject.Find(PreviewObjectName);
            if (existing != null)
            {
                Destroy(existing);
            }

            if (remotePrefab == null)
            {
                Debug.LogError("[RemotePlayerEditorPreview] Remote prefab is missing.");
                return null;
            }

            var instance = Instantiate(remotePrefab, position, Quaternion.Euler(0f, yaw, 0f));
            instance.name = PreviewObjectName;
            WireAsNetworkRemote(instance);

            var preview = instance.GetComponent<RemotePlayerEditorPreview>();
            if (preview == null)
            {
                preview = instance.AddComponent<RemotePlayerEditorPreview>();
            }

            Debug.Log(
                "[RemotePlayerEditorPreview] Spawned. Controls: Arrow keys move, Right Ctrl crouch, " +
                "Right Shift sprint, [ / ] turn, Keypad0 jump. Enable mirrorLocalPlayerInput to copy local WASD.");
            return preview;
        }

        public static void DestroyPreview()
        {
            var existing = GameObject.Find(PreviewObjectName);
            if (existing != null)
            {
                Destroy(existing);
            }
        }

        private static void WireAsNetworkRemote(GameObject root)
        {
            var presentation = root.GetComponent<PlayerViewPresentation>();
            presentation?.Configure(false);

            root.GetComponent<SyntySplitBodyPresentation>()?.ApplyViewMode();
            root.GetComponent<RemoteThirdPersonPlayerBootstrap>()?.ApplyRemoteThirdPersonMode();

            var fpsController = root.GetComponent<FpsCharacterController>();
            if (fpsController != null)
            {
                fpsController.enabled = false;
            }

            var locomotionRig = root.GetComponentInChildren<ProceduralLocomotionRig>(true);
            locomotionRig?.SetNetworkMode(true);

            var syntyDriver = root.GetComponentInChildren<SyntyLocomotionDriver>(true);
            syntyDriver?.SetNetworkMode(true);

            var weaponMount = root.GetComponent<PlayerWeaponMount>();
            if (weaponMount != null)
            {
                weaponMount.SetNetworkMode(true);
            }

            var sync = root.GetComponent<MatchPresenceSync>();
            if (sync != null)
            {
                sync.enabled = false;
            }

            var localMarker = root.GetComponent<LocalPlayerMarker>();
            if (localMarker != null)
            {
                localMarker.enabled = false;
            }

            foreach (var camera in root.GetComponentsInChildren<Camera>(true))
            {
                if (camera != null)
                {
                    camera.enabled = false;
                }
            }

            foreach (var listener in root.GetComponentsInChildren<AudioListener>(true))
            {
                if (listener != null)
                {
                    listener.enabled = false;
                }
            }
        }

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            locomotionRig = GetComponentInChildren<ProceduralLocomotionRig>(true);
            syntyDriver = GetComponentInChildren<SyntyLocomotionDriver>(true);
            weaponMount = GetComponent<PlayerWeaponMount>();
        }

        private void Update()
        {
            if (mirrorLocalPlayerInput)
            {
                DriveFromLocalPlayer();
                return;
            }

            DriveFromPreviewInput();
        }

        private void DriveFromLocalPlayer()
        {
            var localMarker = FindObjectOfType<LocalPlayerMarker>();
            var localFps = localMarker != null ? localMarker.GetComponent<FpsCharacterController>() : null;
            if (localFps == null || locomotionRig == null)
            {
                return;
            }

            var moveInputX = localFps.NetworkMoveInputX;
            var moveInputZ = localFps.NetworkMoveInputZ;
            var speed01 = Mathf.Clamp01(localFps.MoveInputMagnitude);
            if (localFps.IsSprinting && speed01 > 0.05f)
            {
                speed01 = Mathf.Min(1f, speed01 * 1.2f);
            }

            ApplyNetworkLocomotion(
                moveInputX,
                moveInputZ,
                speed01,
                localFps.IsGrounded,
                localFps.IsCrouching,
                localFps.IsSprinting,
                localFps.IsGrounded ? 0 : (localFps.VerticalVelocity > 0.05f ? 1 : 2),
                localFps.CurrentLookPitch);

            if (characterController != null)
            {
                var worldMove = transform.TransformDirection(new Vector3(moveInputX, 0f, moveInputZ));
                if (worldMove.sqrMagnitude > 1f)
                {
                    worldMove.Normalize();
                }

                var speed = localFps.IsCrouching ? walkSpeed * crouchSpeedMultiplier : walkSpeed;
                if (localFps.IsSprinting)
                {
                    speed = sprintSpeed;
                }

                characterController.Move(worldMove * (speed * Time.deltaTime));
            }
        }

        private void DriveFromPreviewInput()
        {
            var turnInput = 0f;
            if (Input.GetKey(KeyCode.LeftBracket))
            {
                turnInput -= 1f;
            }

            if (Input.GetKey(KeyCode.RightBracket))
            {
                turnInput += 1f;
            }

            if (Mathf.Abs(turnInput) > 0.01f)
            {
                transform.Rotate(0f, turnInput * turnSpeed * Time.deltaTime, 0f);
            }

            var moveInput = ReadArrowInput();
            if (moveInput.sqrMagnitude > 1f)
            {
                moveInput.Normalize();
            }

            var isCrouching = Input.GetKey(KeyCode.RightControl);
            var isSprinting = Input.GetKey(KeyCode.RightShift) && !isCrouching && moveInput.y > 0.1f;
            var jumpPressed = Input.GetKeyDown(KeyCode.Keypad0);

            grounded = characterController == null || characterController.isGrounded;
            if (grounded && verticalVelocity < 0f)
            {
                verticalVelocity = -2f;
            }

            if (jumpPressed && grounded && !isCrouching)
            {
                verticalVelocity = 6.5f;
                grounded = false;
            }

            verticalVelocity += gravity * Time.deltaTime;
            var jumpState = grounded ? 0 : (verticalVelocity > 0.05f ? 1 : 2);

            var speed01 = moveInput.sqrMagnitude > 0.01f
                ? Mathf.Clamp01(moveInput.magnitude)
                : 0f;
            if (isSprinting && speed01 > 0.05f)
            {
                speed01 = Mathf.Min(1f, speed01 * 1.2f);
            }

            ApplyNetworkLocomotion(
                moveInput.x,
                moveInput.y,
                speed01,
                grounded,
                isCrouching,
                isSprinting,
                jumpState,
                0f);

            if (characterController == null)
            {
                return;
            }

            var worldMove = transform.TransformDirection(new Vector3(moveInput.x, 0f, moveInput.y));
            if (worldMove.sqrMagnitude > 1f)
            {
                worldMove.Normalize();
            }

            var horizontalSpeed = isCrouching ? walkSpeed * crouchSpeedMultiplier : walkSpeed;
            if (isSprinting)
            {
                horizontalSpeed = sprintSpeed;
            }

            var motion = worldMove * (horizontalSpeed * Time.deltaTime);
            motion.y = verticalVelocity * Time.deltaTime;
            characterController.Move(motion);
            grounded = characterController.isGrounded;
        }

        private void ApplyNetworkLocomotion(
            float moveInputX,
            float moveInputZ,
            float speed01,
            bool isGrounded,
            bool isCrouching,
            bool isSprinting,
            int jumpState,
            float lookPitch)
        {
            locomotionRig?.SetNetworkMoveInput(moveInputX, moveInputZ);
            locomotionRig?.SetNetworkAnimationState(
                speed01,
                isGrounded,
                jumpState,
                0f,
                isCrouching,
                isSprinting);
            locomotionRig?.SetNetworkLookPitch(lookPitch);

            weaponMount?.SetNetworkCrouchState(isCrouching);
            weaponMount?.SetNetworkSprintState(isSprinting);
            weaponMount?.SetNetworkLookPitch(lookPitch);
        }

        private static Vector2 ReadArrowInput()
        {
            var x = 0f;
            var y = 0f;
            if (Input.GetKey(KeyCode.LeftArrow))
            {
                x -= 1f;
            }

            if (Input.GetKey(KeyCode.RightArrow))
            {
                x += 1f;
            }

            if (Input.GetKey(KeyCode.UpArrow))
            {
                y += 1f;
            }

            if (Input.GetKey(KeyCode.DownArrow))
            {
                y -= 1f;
            }

            return new Vector2(x, y);
        }
    }
}
