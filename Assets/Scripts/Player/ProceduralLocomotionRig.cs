using UnityEngine;

namespace ShooterPrototype.Player
{
    public sealed class ProceduralLocomotionRig : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private FpsCharacterController fpsController;
        [SerializeField] private Transform rootTransform;
        [SerializeField] private Transform shoulderAnchor;
        [SerializeField] private Transform hipAnchor;
        [SerializeField] private Transform leftHandTarget;
        [SerializeField] private Transform rightHandTarget;
        [SerializeField] private Transform leftFootTarget;
        [SerializeField] private Transform rightFootTarget;
        [SerializeField] private LineRenderer leftLegLine;
        [SerializeField] private LineRenderer rightLegLine;

        [Header("Optional hand attachments")]
        [SerializeField] private Transform leftHandAttachment;
        [SerializeField] private Transform rightHandAttachment;

        [Header("Walk")]
        [SerializeField] private float walkCycleSpeed = 2.8f;
        [SerializeField] private float remoteIdleDeadzone = 0.025f;
        [SerializeField] private float localSpeedSmoothTime = 0.09f;
        [SerializeField] private float footStepLength = 0.2f;
        [SerializeField] private float footStepHeight = 0.09f;
        [SerializeField] private float handSwingLength = 0.08f;
        [SerializeField] private float handSwingHeight = 0.04f;
        [SerializeField] private float bodyBobHeight = 0.03f;
        [SerializeField] private float targetSmoothTime = 0.035f;

        [Header("Jump / Land")]
        [SerializeField] private float jumpHandRaise = 0.07f;
        [SerializeField] private float jumpFootTuck = 0.11f;
        [SerializeField] private float airPoseBlendSpeed = 11f;
        [SerializeField] private float landBobKick = 0.05f;
        [SerializeField] private float landBobRecover = 10f;
        [SerializeField] private float remoteGroundedSwitchDelay = 0.1f;

        [Header("Leg curve")]
        [SerializeField] private float legBendForward = 0.05f;
        [SerializeField] private float legBendOutward = 0.03f;
        [Header("Crouch")]
        [SerializeField] private float crouchTransitionSmoothTime = 0.16f;
        [SerializeField] private float crouchRootDrop = 0.16f;
        [SerializeField] private float crouchShoulderDrop = 0.28f;
        [SerializeField] private float crouchHipDrop = 0.1f;
        [SerializeField] private float crouchLegShorten = 0.2f;
        [SerializeField] private float crouchTorsoFollowDrop = 0.08f;
        [SerializeField] private float crouchLineDropY = 0.2f;
        [SerializeField] private bool useNetworkState;
        [SerializeField] private bool debugArmJitterLogs;
        [SerializeField] private float debugLogInterval = 0.5f;

        private Vector3 baseRootLocalPos;
        private Vector3 baseShoulderLocalPos;
        private Vector3 baseHipLocalPos;
        private Vector3 baseLeftHandLocalPos;
        private Vector3 baseRightHandLocalPos;
        private Vector3 baseLeftFootLocalPos;
        private Vector3 baseRightFootLocalPos;

        private Vector3 leftHandVelocity;
        private Vector3 rightHandVelocity;
        private Vector3 leftFootVelocity;
        private Vector3 rightFootVelocity;

        private float walkPhase;
        private float airborneBlend;
        private float landImpulse;
        private bool wasGroundedLastFrame = true;
        private Vector3 lastRootWorldPos;
        private float networkSpeed01;
        private bool networkGrounded = true;
        private bool pendingNetworkGrounded = true;
        private float pendingNetworkGroundedSince = -1f;
        private int networkJumpState;
        private float networkAnimPhase01;
        private bool networkCrouching;
        private float crouchBlend;
        private float crouchBlendVelocity;
        private float currentSpeed01;
        private float speedSmoothVelocity;
        private bool currentGrounded = true;
        private int currentJumpState;
        private ThreadArmRig threadArmRig;
        private float nextDebugLogAt;
        private Vector3 previousLeftHandTargetPos;
        private Vector3 previousRightHandTargetPos;
        private PlayerWeaponMount weaponMount;
        private LineRenderer torsoLine;
        private LineRenderer neckLine;
        private Vector3 torsoLineBaseLocalPos;
        private Vector3 neckLineBaseLocalPos;

        public float CurrentAnimPhase01 => Mathf.Repeat(walkPhase / (Mathf.PI * 2f), 1f);
        public float CurrentSpeed01 => currentSpeed01;
        public bool CurrentGrounded => currentGrounded;
        public int CurrentJumpState => currentJumpState;

        private void Awake()
        {
            if (rootTransform == null)
            {
                rootTransform = transform;
            }

            if (fpsController == null)
            {
                fpsController = GetComponentInParent<FpsCharacterController>();
            }
            threadArmRig = GetComponentInChildren<ThreadArmRig>(true);

            CacheBasePose();
            CacheBodyLines();
            lastRootWorldPos = rootTransform.position;
            ForceLegLineSetup(leftLegLine);
            ForceLegLineSetup(rightLegLine);
            ConfigureBodyLineShadows();
            previousLeftHandTargetPos = leftHandTarget != null ? leftHandTarget.position : Vector3.zero;
            previousRightHandTargetPos = rightHandTarget != null ? rightHandTarget.position : Vector3.zero;
        }

        public void ConfigureRuntime(
            Transform root,
            Transform shoulder,
            Transform hip,
            Transform leftHand,
            Transform rightHand,
            Transform leftFoot,
            Transform rightFoot,
            LineRenderer leftLeg,
            LineRenderer rightLeg,
            FpsCharacterController controller = null)
        {
            rootTransform = root != null ? root : transform;
            shoulderAnchor = shoulder;
            hipAnchor = hip;
            leftHandTarget = leftHand;
            rightHandTarget = rightHand;
            leftFootTarget = leftFoot;
            rightFootTarget = rightFoot;
            leftLegLine = leftLeg;
            rightLegLine = rightLeg;
            fpsController = controller;

            CacheBasePose();
            CacheBodyLines();
            ForceLegLineSetup(leftLegLine);
            ForceLegLineSetup(rightLegLine);
        }

        public void SetHandAttachments(Transform leftAttachment, Transform rightAttachment)
        {
            leftHandAttachment = leftAttachment;
            rightHandAttachment = rightAttachment;

            if (threadArmRig == null)
            {
                threadArmRig = GetComponentInChildren<ThreadArmRig>(true);
            }

            if (threadArmRig != null)
            {
                threadArmRig.SetHandTargets(
                    leftHandAttachment != null ? leftHandAttachment : leftHandTarget,
                    rightHandAttachment != null ? rightHandAttachment : rightHandTarget);
            }
        }

        public void SetNetworkMode(bool enabled)
        {
            useNetworkState = enabled;
        }

        public void SetNetworkAnimationState(float speed01, bool grounded, int jumpState, float animPhase01, bool isCrouching = false)
        {
            useNetworkState = true;
            networkSpeed01 = Mathf.Clamp01(speed01);
            if (grounded != networkGrounded)
            {
                if (pendingNetworkGrounded != grounded)
                {
                    pendingNetworkGrounded = grounded;
                    pendingNetworkGroundedSince = Time.unscaledTime;
                }
                else if (pendingNetworkGroundedSince >= 0f &&
                         (Time.unscaledTime - pendingNetworkGroundedSince) >= Mathf.Max(0f, remoteGroundedSwitchDelay))
                {
                    networkGrounded = pendingNetworkGrounded;
                }
            }
            else
            {
                pendingNetworkGroundedSince = -1f;
            }
            networkJumpState = Mathf.Clamp(jumpState, 0, 2);
            networkAnimPhase01 = Mathf.Repeat(animPhase01, 1f);
            networkCrouching = isCrouching;
        }

        private void LateUpdate()
        {
            if (rootTransform == null ||
                shoulderAnchor == null ||
                hipAnchor == null ||
                leftHandTarget == null ||
                rightHandTarget == null ||
                leftFootTarget == null ||
                rightFootTarget == null)
            {
                return;
            }

            var dt = Mathf.Max(0.0001f, Time.deltaTime);
            var grounded = useNetworkState ? networkGrounded : IsGrounded();
            var localInputSpeed01 = fpsController != null
                ? Mathf.Clamp01(fpsController.MoveInputMagnitude)
                : Mathf.Clamp01(GetHorizontalSpeed() / 5.5f);
            var rawSpeed01 = useNetworkState
                ? Mathf.Lerp(currentSpeed01, networkSpeed01, Mathf.Clamp01(dt * 14f))
                : localInputSpeed01;
            var speed01 = useNetworkState
                ? rawSpeed01
                : Mathf.SmoothDamp(
                    currentSpeed01,
                    rawSpeed01,
                    ref speedSmoothVelocity,
                    Mathf.Max(0.01f, localSpeedSmoothTime),
                    Mathf.Infinity,
                    dt);
            var motionSpeed01 = (useNetworkState && speed01 < Mathf.Max(0f, remoteIdleDeadzone)) ? 0f : speed01;
            var smoothTime = useNetworkState
                ? Mathf.Max(0.01f, targetSmoothTime * 1.8f)
                : targetSmoothTime;

            if (!grounded && wasGroundedLastFrame)
            {
                landImpulse = 0f;
            }
            else if (grounded && !wasGroundedLastFrame)
            {
                landImpulse = landBobKick;
            }

            wasGroundedLastFrame = grounded;
            airborneBlend = Mathf.MoveTowards(
                airborneBlend,
                grounded ? 0f : 1f,
                airPoseBlendSpeed * dt);

            // Always advance phase continuously; for remote players apply only gentle correction
            // to network phase to prevent visible jitter from packet timing variance.
            // For remote avatars we don't force idle phase progression.
            var phaseMin = useNetworkState ? 0f : 0.2f;
            walkPhase += dt * walkCycleSpeed * Mathf.Lerp(phaseMin, 1.8f, motionSpeed01);
            if (useNetworkState)
            {
                var currentDeg = walkPhase * Mathf.Rad2Deg;
                var targetDeg = networkAnimPhase01 * 360f;
                var deltaDeg = Mathf.DeltaAngle(currentDeg, targetDeg);
                var correctionDeg = Mathf.Clamp(deltaDeg, -8f, 8f) * Mathf.Clamp01(dt * 3f);
                walkPhase += correctionDeg * Mathf.Deg2Rad;
            }
            var bob = Mathf.Sin(walkPhase * 2f) * bodyBobHeight * motionSpeed01 * (1f - airborneBlend);

            landImpulse = Mathf.MoveTowards(landImpulse, 0f, landBobRecover * dt);

            var isCrouching = useNetworkState
                ? networkCrouching
                : (fpsController != null && fpsController.IsCrouching);
            crouchBlend = Mathf.SmoothDamp(
                crouchBlend,
                isCrouching ? 1f : 0f,
                ref crouchBlendVelocity,
                Mathf.Max(0.01f, crouchTransitionSmoothTime));

            var torsoFollowDrop = crouchTorsoFollowDrop * crouchBlend;
            rootTransform.localPosition = baseRootLocalPos + Vector3.down * (crouchRootDrop * crouchBlend);
            shoulderAnchor.localPosition = baseShoulderLocalPos + new Vector3(0f, bob - landImpulse - crouchShoulderDrop * crouchBlend - torsoFollowDrop, 0f);
            hipAnchor.localPosition = baseHipLocalPos + new Vector3(0f, bob * 0.55f - landImpulse * 0.6f - crouchHipDrop * crouchBlend - torsoFollowDrop * 0.8f, 0f);
            ApplyCrouchLineDrop(crouchBlend);

            var leftStep = EvaluateWalkOffset(walkPhase, motionSpeed01, -1f);
            var rightStep = EvaluateWalkOffset(walkPhase + Mathf.PI, motionSpeed01, 1f);

            if (leftHandAttachment != null)
            {
                // ThreadArmRig follows weapon attachments directly.
                // Do not chase proxy hand targets here (prevents dual-filter jitter).
                leftHandVelocity = Vector3.zero;
            }
            else
            {
                var leftHandLocal = baseLeftHandLocalPos +
                                    new Vector3(
                                        -leftStep.x * handSwingLength,
                                        leftStep.y * handSwingHeight + airborneBlend * jumpHandRaise,
                                        leftStep.x * handSwingLength * 0.4f);
                MoveTargetLocal(leftHandTarget, leftHandLocal, ref leftHandVelocity, smoothTime);
            }

            if (rightHandAttachment != null)
            {
                // Same as left: one authoritative source for hand endpoints.
                rightHandVelocity = Vector3.zero;
            }
            else
            {
                var rightHandLocal = baseRightHandLocalPos +
                                     new Vector3(
                                         -rightStep.x * handSwingLength,
                                         rightStep.y * handSwingHeight + airborneBlend * jumpHandRaise,
                                         rightStep.x * handSwingLength * 0.4f);
                MoveTargetLocal(rightHandTarget, rightHandLocal, ref rightHandVelocity, smoothTime);
            }

            var crouchStepScale = Mathf.Lerp(1f, 0.45f, crouchBlend);
            var leftFootLocal = baseLeftFootLocalPos +
                                new Vector3(
                                    leftStep.x * footStepLength,
                                    leftStep.y * footStepHeight * crouchStepScale + airborneBlend * jumpFootTuck,
                                    Mathf.Abs(leftStep.x) * 0.05f);
            var rightFootLocal = baseRightFootLocalPos +
                                 new Vector3(
                                     rightStep.x * footStepLength,
                                     rightStep.y * footStepHeight * crouchStepScale + airborneBlend * jumpFootTuck,
                                     Mathf.Abs(rightStep.x) * 0.05f);
            leftFootLocal.y += crouchLegShorten * crouchBlend;
            rightFootLocal.y += crouchLegShorten * crouchBlend;

            MoveTargetLocal(leftFootTarget, leftFootLocal, ref leftFootVelocity, smoothTime);
            MoveTargetLocal(rightFootTarget, rightFootLocal, ref rightFootVelocity, smoothTime);

            UpdateLegCurve(leftLegLine, hipAnchor.position, leftFootTarget.position, -1f);
            UpdateLegCurve(rightLegLine, hipAnchor.position, rightFootTarget.position, 1f);

            currentSpeed01 = motionSpeed01;
            currentGrounded = grounded;
            currentJumpState = grounded
                ? 0
                : (useNetworkState
                    ? networkJumpState
                    : (fpsController != null && fpsController.VerticalVelocity > 0.05f ? 1 : 2));
            lastRootWorldPos = rootTransform.position;

            EmitDebugArmJitterLog(motionSpeed01, smoothTime);
        }

        private void CacheBasePose()
        {
            baseRootLocalPos = rootTransform != null ? rootTransform.localPosition : Vector3.zero;
            baseShoulderLocalPos = shoulderAnchor != null ? shoulderAnchor.localPosition : Vector3.zero;
            baseHipLocalPos = hipAnchor != null ? hipAnchor.localPosition : Vector3.zero;
            baseLeftHandLocalPos = leftHandTarget != null ? leftHandTarget.localPosition : Vector3.zero;
            baseRightHandLocalPos = rightHandTarget != null ? rightHandTarget.localPosition : Vector3.zero;
            baseLeftFootLocalPos = leftFootTarget != null ? leftFootTarget.localPosition : Vector3.zero;
            baseRightFootLocalPos = rightFootTarget != null ? rightFootTarget.localPosition : Vector3.zero;
        }

        private void CacheBodyLines()
        {
            torsoLine = FindLineByName("TorsoLine");
            neckLine = FindLineByName("NeckLine");
            torsoLineBaseLocalPos = torsoLine != null ? torsoLine.transform.localPosition : Vector3.zero;
            neckLineBaseLocalPos = neckLine != null ? neckLine.transform.localPosition : Vector3.zero;
        }

        private void ApplyCrouchLineDrop(float blend)
        {
            var lineDrop = Mathf.Max(0f, crouchLineDropY) * Mathf.Clamp01(blend);
            if (torsoLine != null)
            {
                torsoLine.transform.localPosition = torsoLineBaseLocalPos + Vector3.down * lineDrop;
            }

            if (neckLine != null)
            {
                neckLine.transform.localPosition = neckLineBaseLocalPos + Vector3.down * lineDrop;
            }
        }

        private LineRenderer FindLineByName(string lineName)
        {
            if (string.IsNullOrWhiteSpace(lineName))
            {
                return null;
            }

            var lines = GetComponentsInChildren<LineRenderer>(true);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line != null && string.Equals(line.name, lineName, System.StringComparison.Ordinal))
                {
                    return line;
                }
            }

            return null;
        }

        private Vector2 EvaluateWalkOffset(float phase, float speed01, float side)
        {
            var x = Mathf.Sin(phase) * speed01;
            var y = Mathf.Max(0f, Mathf.Sin(phase + side * 0.25f)) * speed01;
            return new Vector2(x, y);
        }

        private void MoveTargetLocal(Transform target, Vector3 localPosition, ref Vector3 velocityRef, float smoothTime)
        {
            if (target == null)
            {
                return;
            }

            var worldTarget = target.parent != null
                ? target.parent.TransformPoint(localPosition)
                : localPosition;
            target.position = Vector3.SmoothDamp(
                target.position,
                worldTarget,
                ref velocityRef,
                Mathf.Max(0.005f, smoothTime));
        }

        private bool IsGrounded()
        {
            if (fpsController != null)
            {
                return fpsController.IsGrounded;
            }

            return Physics.Raycast(rootTransform.position + Vector3.up * 0.05f, Vector3.down, 0.2f);
        }

        private float GetHorizontalSpeed()
        {
            if (fpsController != null)
            {
                return fpsController.HorizontalSpeed;
            }

            var delta = rootTransform.position - lastRootWorldPos;
            delta.y = 0f;
            return delta.magnitude / Mathf.Max(0.0001f, Time.deltaTime);
        }

        private void ForceLegLineSetup(LineRenderer line)
        {
            if (line == null)
            {
                return;
            }

            line.positionCount = 3;
            line.useWorldSpace = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            line.receiveShadows = true;
            line.generateLightingData = true;
        }

        private void ConfigureBodyLineShadows()
        {
            var lines = GetComponentsInChildren<LineRenderer>(true);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line == null)
                {
                    continue;
                }

                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                line.receiveShadows = true;
                line.generateLightingData = true;
            }
        }

        private void UpdateLegCurve(LineRenderer line, Vector3 start, Vector3 end, float sideSign)
        {
            if (line == null)
            {
                return;
            }

            var mid = (start + end) * 0.5f;
            var control = mid +
                          transform.forward * legBendForward +
                          transform.right * (legBendOutward * sideSign);
            line.SetPosition(0, start);
            line.SetPosition(1, control);
            line.SetPosition(2, end);
        }

        private void EmitDebugArmJitterLog(float motionSpeed01, float smoothTime)
        {
            if (!debugArmJitterLogs || !Debug.isDebugBuild || useNetworkState || Time.unscaledTime < nextDebugLogAt)
            {
                return;
            }

            nextDebugLogAt = Time.unscaledTime + Mathf.Max(0.1f, debugLogInterval);

            var leftPos = leftHandTarget != null ? leftHandTarget.position : Vector3.zero;
            var rightPos = rightHandTarget != null ? rightHandTarget.position : Vector3.zero;
            var leftDelta = (leftPos - previousLeftHandTargetPos).magnitude;
            var rightDelta = (rightPos - previousRightHandTargetPos).magnitude;

            previousLeftHandTargetPos = leftPos;
            previousRightHandTargetPos = rightPos;

            var leftAttachPos = leftHandAttachment != null ? leftHandAttachment.position : Vector3.zero;
            var rightAttachPos = rightHandAttachment != null ? rightHandAttachment.position : Vector3.zero;
            var leftAttachDelta = leftHandAttachment != null ? Vector3.Distance(leftPos, leftAttachPos) : -1f;
            var rightAttachDelta = rightHandAttachment != null ? Vector3.Distance(rightPos, rightAttachPos) : -1f;

            Debug.Log(
                $"[arm-jitter][rig] speed={motionSpeed01:F3} grounded={currentGrounded} ads={(GetComponent<PlayerWeaponMount>()?.AdsBlend ?? 0f):F2} leftΔ={leftDelta:F5} rightΔ={rightDelta:F5} leftErr={leftAttachDelta:F5} rightErr={rightAttachDelta:F5} smooth={smoothTime:F3}");
        }
    }
}
