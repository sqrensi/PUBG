using UnityEngine;
using UnityEngine.Serialization;

namespace ShooterPrototype.Player
{
    [DefaultExecutionOrder(120)]
    public sealed class SyntyLocomotionDriver : MonoBehaviour
    {
        public static readonly int SpeedHash = Animator.StringToHash("Speed");
        public static readonly int MoveXHash = Animator.StringToHash("MoveX");
        public static readonly int MoveYHash = Animator.StringToHash("MoveY");
        public static readonly int GroundedHash = Animator.StringToHash("Grounded");
        public static readonly int SprintingHash = Animator.StringToHash("Sprinting");
        public static readonly int CrouchingHash = Animator.StringToHash("Crouching");
        public static readonly int JumpStateHash = Animator.StringToHash("JumpState");
        public static readonly int LookPitchHash = Animator.StringToHash("LookPitch");

        [Header("References")]
        [SerializeField] private Animator animator;
        [SerializeField] private FpsCharacterController fpsController;
        [SerializeField] private ProceduralLocomotionRig locomotionRig;

        [Header("Speed")]
        [SerializeField] private float walkSpeedReference = 3.3f;
        [SerializeField] private float sprintSpeedReference = 5.94f;
        [SerializeField] private float animatorSpeedSmoothTime = 0.1f;
        [SerializeField] private float moveSmoothTime = 0.08f;
        [SerializeField] private float networkMoveSmoothTime = 0.12f;
        [SerializeField] private float networkStopSmoothTime = 0.05f;
        [SerializeField] private bool useConstantAnimatorPlaybackSpeedForNetwork = true;
        [SerializeField] private float lookPitchSmoothTime = 0.07f;

        [Header("Animation Playback")]
        [SerializeField] private float walkAnimationSpeed = 0.35f;
        [FormerlySerializedAs("runAnimationSpeed")]
        [SerializeField] private float sprintAnimationSpeed = 1f;
        [SerializeField] private float crouchAnimationSpeed = 0.55f;
        [SerializeField] private float nonSprintAnimSpeedCap = 0.68f;
        [SerializeField] private float networkLocomotionDeadZone = 0.1f;
        [SerializeField] private float networkMoveInputDeadZone = 0.12f;
        [SerializeField] private float forwardMoveLateralDeadZone = 0.1f;

        private float smoothedSpeed;
        private float speedVelocity;
        private float smoothedMoveX;
        private float smoothedMoveY;
        private float moveXVelocity;
        private float moveYVelocity;
        private float smoothedLookPitch;
        private float lookPitchVelocity;
        private bool useNetworkState;

        public void Configure(Animator targetAnimator, FpsCharacterController controller, ProceduralLocomotionRig rig)
        {
            animator = targetAnimator;
            fpsController = controller;
            locomotionRig = rig;
        }

        public void SetNetworkMode(bool enabled)
        {
            useNetworkState = enabled;
        }

        private void Awake()
        {
            if (fpsController == null)
            {
                fpsController = GetComponent<FpsCharacterController>();
            }

            if (locomotionRig == null)
            {
                locomotionRig = GetComponentInChildren<ProceduralLocomotionRig>(true);
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
            }
        }

        private void Update()
        {
            if (animator == null || !animator.enabled)
            {
                return;
            }

            var dt = Mathf.Max(0.0001f, Time.deltaTime);
            ResolveAnimationState(
                out var targetSpeed,
                out var moveX,
                out var moveY,
                out var grounded,
                out var sprinting,
                out var crouching,
                out var jumpState,
                out var lookPitch);

            if (useNetworkState)
            {
                ApplyNetworkLocomotionDeadZone(ref targetSpeed, ref moveX, ref moveY);
            }

            smoothedSpeed = Mathf.SmoothDamp(
                smoothedSpeed,
                targetSpeed,
                ref speedVelocity,
                Mathf.Max(0.01f, ResolveNetworkSpeedSmoothTime(targetSpeed)),
                Mathf.Infinity,
                dt);
            var moveSmooth = Mathf.Max(
                0.01f,
                useNetworkState ? ResolveNetworkMoveSmoothTime(moveX, moveY) : moveSmoothTime);
            smoothedMoveX = Mathf.SmoothDamp(smoothedMoveX, moveX, ref moveXVelocity, moveSmooth, Mathf.Infinity, dt);
            smoothedMoveY = Mathf.SmoothDamp(smoothedMoveY, moveY, ref moveYVelocity, moveSmooth, Mathf.Infinity, dt);

            var animatorCrouching = crouching;
            var animatorSprinting = sprinting && !animatorCrouching;
            if (useNetworkState)
            {
                StabilizeNetworkMoveDirection(ref smoothedMoveX, ref smoothedMoveY, animatorSprinting);
            }

            smoothedLookPitch = Mathf.SmoothDamp(
                smoothedLookPitch,
                lookPitch,
                ref lookPitchVelocity,
                Mathf.Max(0.01f, lookPitchSmoothTime),
                Mathf.Infinity,
                dt);

            var speedForAnimator = smoothedSpeed;
            if (!animatorSprinting)
            {
                speedForAnimator = Mathf.Min(speedForAnimator, nonSprintAnimSpeedCap);
            }

            animator.SetFloat(SpeedHash, speedForAnimator);
            animator.SetFloat(MoveXHash, smoothedMoveX);
            animator.SetFloat(MoveYHash, smoothedMoveY);
            animator.SetBool(GroundedHash, grounded);
            animator.SetBool(SprintingHash, animatorSprinting);
            animator.SetBool(CrouchingHash, animatorCrouching);
            animator.SetInteger(JumpStateHash, jumpState);
            animator.SetFloat(LookPitchHash, smoothedLookPitch);
            animator.speed = useNetworkState && useConstantAnimatorPlaybackSpeedForNetwork
                ? 1f
                : ResolveAnimatorPlaybackSpeed(speedForAnimator, animatorSprinting, crouching, jumpState);
        }

        private float ResolveAnimatorPlaybackSpeed(float speed, bool sprinting, bool crouching, int jumpState)
        {
            if (jumpState != 0)
            {
                return 1f;
            }

            if (speed <= 0.05f)
            {
                return 1f;
            }

            if (crouching)
            {
                return Mathf.Max(0.1f, crouchAnimationSpeed);
            }

            if (sprinting)
            {
                return Mathf.Max(0.1f, sprintAnimationSpeed);
            }

            return Mathf.Max(0.1f, walkAnimationSpeed);
        }

        private void ResolveAnimationState(
            out float targetSpeed,
            out float moveX,
            out float moveY,
            out bool grounded,
            out bool sprinting,
            out bool crouching,
            out int jumpState,
            out float lookPitch)
        {
            moveX = 0f;
            moveY = 0f;

            if (useNetworkState && locomotionRig != null)
            {
                targetSpeed = locomotionRig.GetNetworkAnimSpeed01();
                grounded = locomotionRig.CurrentGrounded;
                jumpState = locomotionRig.CurrentJumpState;
                sprinting = locomotionRig.NetworkSprinting;
                crouching = locomotionRig.NetworkCrouching;
                lookPitch = locomotionRig.NetworkLookPitch;
                moveX = locomotionRig.NetworkMoveInputX;
                moveY = locomotionRig.NetworkMoveInputZ;

                if (targetSpeed > 0.05f)
                {
                    var moveDir = new Vector2(moveX, moveY);
                    if (moveDir.sqrMagnitude < 0.0025f)
                    {
                        moveY = 1f;
                    }
                    else if (moveDir.sqrMagnitude > 1f)
                    {
                        moveDir.Normalize();
                        moveX = moveDir.x;
                        moveY = moveDir.y;
                    }
                }

                return;
            }

            grounded = fpsController != null && fpsController.IsGrounded;
            sprinting = fpsController != null && fpsController.IsSprinting;
            crouching = fpsController != null && fpsController.IsCrouching;
            lookPitch = fpsController != null ? fpsController.CurrentLookPitch : 0f;

            if (fpsController != null)
            {
                moveX = fpsController.NetworkMoveInputX;
                moveY = fpsController.NetworkMoveInputZ;
                var moveDir = new Vector2(moveX, moveY);
                if (moveDir.sqrMagnitude > 1f)
                {
                    moveDir.Normalize();
                    moveX = moveDir.x;
                    moveY = moveDir.y;
                }
            }

            var horizontalSpeed = fpsController != null ? fpsController.HorizontalSpeed : 0f;
            var speedReference = sprinting ? sprintSpeedReference : walkSpeedReference;
            targetSpeed = Mathf.Clamp01(horizontalSpeed / Mathf.Max(0.01f, speedReference));
            if (sprinting && targetSpeed > 0.05f)
            {
                targetSpeed = Mathf.Lerp(targetSpeed, 1f, 0.35f);
            }

            if (!grounded)
            {
                jumpState = fpsController != null && fpsController.VerticalVelocity > 0.05f ? 1 : 2;
            }
            else
            {
                jumpState = 0;
            }
        }

        private void ApplyNetworkLocomotionDeadZone(ref float targetSpeed, ref float moveX, ref float moveY)
        {
            var moveMagSq = moveX * moveX + moveY * moveY;
            var moveDeadSq = networkMoveInputDeadZone * networkMoveInputDeadZone;
            if (moveMagSq < moveDeadSq)
            {
                moveX = 0f;
                moveY = 0f;
            }
            else if (moveMagSq > 0.0001f)
            {
                var invMag = 1f / Mathf.Sqrt(moveMagSq);
                moveX *= invMag;
                moveY *= invMag;
            }

            if (targetSpeed < networkLocomotionDeadZone)
            {
                targetSpeed = 0f;
            }
        }

        private float ResolveNetworkSpeedSmoothTime(float targetSpeed)
        {
            return targetSpeed < smoothedSpeed ? networkStopSmoothTime : networkMoveSmoothTime;
        }

        private float ResolveNetworkMoveSmoothTime(float moveX, float moveY)
        {
            var targetMag = moveX * moveX + moveY * moveY;
            var currentMag = smoothedMoveX * smoothedMoveX + smoothedMoveY * smoothedMoveY;
            return targetMag < currentMag ? networkStopSmoothTime : networkMoveSmoothTime;
        }

        private void StabilizeNetworkMoveDirection(ref float moveX, ref float moveY, bool sprinting)
        {
            var magnitude = Mathf.Sqrt(moveX * moveX + moveY * moveY);
            if (magnitude <= 0.001f)
            {
                return;
            }

            moveX /= magnitude;
            moveY /= magnitude;

            if (sprinting || Mathf.Abs(moveX) >= forwardMoveLateralDeadZone)
            {
                return;
            }

            if (moveY > 0.82f)
            {
                moveX = 0f;
                moveY = 1f;
            }
        }
    }
}
