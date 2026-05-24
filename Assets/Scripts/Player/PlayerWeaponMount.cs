using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ShooterPrototype.Player
{
    [DefaultExecutionOrder(110)]
    public sealed class PlayerWeaponMount : MonoBehaviour
    {
        [Header("Weapon")]
        [SerializeField] private GameObject weaponPrefab;
        [SerializeField] private Transform weaponParent;
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private Vector3 localPosition = Vector3.zero;
        [SerializeField] private Vector3 localEulerAngles = Vector3.zero;
        [SerializeField] private Vector3 localScale = new Vector3(0.35476f, 0.35476f, 0.35476f);
        [SerializeField] private bool disableWeaponColliders = true;

        [Header("Hand targets on weapon")]
        [SerializeField] private string leftHandTargetName = "LeftHandTarget";
        [SerializeField] private string rightHandTargetName = "RightHandTarget";
        [SerializeField] private string sightTargetName = "AimPoint";
        [SerializeField] private string reloadMagHandTargetName = "MagHandTarget";
        [SerializeField] private Vector3 reloadMagPullLocalOffset = new Vector3(0f, -0.28f, -0.42f);

        [Header("Weapon walk bob")]
        [SerializeField] private bool enableWalkBob = true;
        [SerializeField] private float bobPositionX = 0.012f;
        [SerializeField] private float bobPositionY = 0.01f;
        [SerializeField] private float bobRotationZ = 3.2f;
        [SerializeField] private float bobSmoothing = 0.08f;
        [SerializeField] private float idleFreezeSpeedThreshold = 0.015f;
        [SerializeField] private bool enableIdleSway = true;
        [SerializeField] private float idleSwaySpeed = 1.0f;
        [SerializeField] private float idleSwayPositionX = 0.0008f;
        [SerializeField] private float idleSwayPositionY = 0.00055f;
        [SerializeField] private float idleSwayRotationZ = 0.18f;

        [Header("Camera pitch tilt")]
        [SerializeField] private bool enableCameraPitchTilt = true;
        [SerializeField] private float cameraPitchInfluence = 0.45f;
        [SerializeField] private float cameraPitchMin = -80f;
        [SerializeField] private float cameraPitchMax = 80f;
        [SerializeField] private float cameraPitchSmoothing = 0.06f;
        [SerializeField] private float cameraPitchArcVertical = 0.02f;
        [SerializeField] private float cameraPitchArcBackward = 0.03f;
        [SerializeField] private float cameraPitchYawInfluence = 2.2f;

        [Header("Aim Down Sights (ADS)")]
        [SerializeField] private bool enableAimDownSights = true;
        [SerializeField] private Vector3 hipAnchorLocalPosition = new Vector3(0.21f, 1.2f, 0.365f);
        [SerializeField] private Vector3 hipAnchorLocalEuler = new Vector3(-0.752f, -3.043f, 4.539f);
        [SerializeField] private Vector3 adsAnchorLocalPosition = new Vector3(0f, 1.26f, 0f);
        [SerializeField] private Vector3 adsAnchorLocalEuler = new Vector3(0f, -1f, 0f);
        [SerializeField] private float adsSmoothTime = 0.08f;
        [SerializeField] private float adsBobPositionMultiplier = 0.2f;
        [SerializeField] private float adsBobRotationMultiplier = 0.12f;
        [SerializeField] private float adsPitchArcMultiplier = 0.35f;
        [SerializeField] private float adsPitchTiltMultiplier = 0.55f;
        [SerializeField] private bool adsLockToCameraPivot = true;
        [SerializeField] private Vector3 adsCameraLocalPosition = new Vector3(0f, -0.14f, 0.35f);
        [SerializeField] private Vector3 adsCameraLocalEuler = new Vector3(0f, -1f, 0f);
        [SerializeField] private bool adsUseSightLock = true;
        [SerializeField] private Vector3 adsSightCameraOffset = new Vector3(0f, -0.01f, 0.06f);
        [SerializeField] private Vector3 adsSightEulerOffset = Vector3.zero;
        [SerializeField] private float adsFollowPitchDownLimit = -40f;
        [SerializeField] private float adsFollowPitchUpLimit = 30f;
        [Header("Crouch")]
        [SerializeField] private float crouchWeaponDrop = 0.18f;
        [SerializeField] private float crouchBlendSmoothTime = 0.16f;
        [SerializeField] private float crouchWeaponDropAdsMultiplier = 0.85f;
        [Header("Weapon Collision")]
        [SerializeField] private bool enableWeaponCollisionAvoidance = true;
        [SerializeField] private float wallCheckDistance = 0.95f;
        [SerializeField] private float wallCheckRadius = 0.09f;
        [SerializeField] private LayerMask wallCheckMask = ~0;
        [SerializeField] private float wallPushBack = 0.46f;
        [SerializeField] private float wallPushUp = 0.05f;
        [SerializeField] private float wallTiltX = 24f;
        [SerializeField] private float wallTiltZ = -10f;
        [SerializeField] private float wallAvoidSmoothTime = 0.035f;
        [SerializeField, Range(0f, 1f)] private float wallAvoidMinBlendOnHit = 0.22f;
        [SerializeField] private float wallAvoidResponsePower = 0.7f;
        [SerializeField] private float adsWallPitchDown = 20f;
        [SerializeField] private float adsWallDropDown = 0.12f;
        [SerializeField] private float adsWallHardDropDown = 0.22f;
        [SerializeField, Range(0f, 1f)] private float adsWallHardDropStartBlend = 0.08f;
        [SerializeField, Range(0f, 1f)] private float adsWallPitchFollowMin = 0f;
        [SerializeField, Range(0f, 1f)] private float adsWallPitchFollowStartBlend = 0.05f;

        private GameObject weaponInstance;
        private ProceduralLocomotionRig locomotionRig;
        private Vector3 baseLocalPosition;
        private Quaternion baseLocalRotation;
        private Vector3 bobPositionVelocity;
        private float bobRotationVelocity;
        private float pitchRotationVelocity;
        private float pitchYawVelocity;
        private float smoothedPitchOffset;
        private float smoothedPitchYawOffset;
        private Vector3 anchorPositionVelocity;
        private float anchorRotXVelocity;
        private float anchorRotYVelocity;
        private float anchorRotZVelocity;
        private float adsBlend;
        private float adsBlendVelocity;
        private Transform sightTarget;
        private Vector3 sightLocalPositionOnAnchor;
        private Quaternion sightLocalRotationOnAnchor = Quaternion.identity;
        private bool hasSightCalibration;
        private bool useNetworkState;
        private float networkLookPitch;
        private float networkAdsBlend;
        private float networkCrouchBlend;
        private float networkWallAvoidBlend;
        private float crouchBlend;
        private float crouchBlendVelocity;
        private bool localReloading;
        private float wallAvoidBlend;
        private float wallAvoidBlendVelocity;
        private FpsCharacterController fpsController;
        private Transform leftHandTargetTransform;
        private Transform rightHandTargetTransform;
        private Transform magHandTargetTransform;
        private Vector3 magHandTargetBaseLocalPos;
        private Coroutine reloadRoutine;

        private void Awake()
        {
            EnsureWeaponMounted();
        }

        public float AdsBlend => adsBlend;
        public float AdsFollowPitchDownLimit => adsFollowPitchDownLimit;
        public Transform MountedWeaponRoot => weaponInstance != null ? weaponInstance.transform : null;
        public float CurrentWallAvoidBlend => Mathf.Clamp01(wallAvoidBlend);

        public bool PlayReloadAnimation(float durationSeconds)
        {
            if (durationSeconds <= 0f)
            {
                return false;
            }

            if (magHandTargetTransform == null)
            {
                return false;
            }

            if (reloadRoutine != null)
            {
                StopCoroutine(reloadRoutine);
                reloadRoutine = null;
                magHandTargetTransform.localPosition = magHandTargetBaseLocalPos;
            }

            reloadRoutine = StartCoroutine(ReloadMagRoutine(durationSeconds));
            return true;
        }

        public void SetNetworkMode(bool enabled)
        {
            useNetworkState = enabled;
            if (enabled)
            {
                adsBlend = 0f;
                adsBlendVelocity = 0f;
                networkAdsBlend = 0f;
                networkCrouchBlend = 0f;
                networkWallAvoidBlend = 0f;
            }

            wallAvoidBlend = 0f;
            wallAvoidBlendVelocity = 0f;
        }

        public void SetNetworkLookPitch(float lookPitch)
        {
            networkLookPitch = lookPitch;
        }

        public void SetNetworkAimState(bool isAiming)
        {
            networkAdsBlend = isAiming ? 1f : 0f;
        }

        public void SetNetworkCrouchState(bool isCrouching)
        {
            networkCrouchBlend = isCrouching ? 1f : 0f;
        }

        public void SetNetworkWallAvoidBlend(float blend)
        {
            networkWallAvoidBlend = Mathf.Clamp01(blend);
        }

        public void SetLocalReloading(bool reloading)
        {
            localReloading = reloading;
        }

        private void OnEnable()
        {
            EnsureWeaponMounted();
        }

        private void LateUpdate()
        {
            if (weaponInstance == null)
            {
                return;
            }

            var localAimHeld = false;
            var amount = 0f;
            var phase = 0f;
            if (enableWalkBob && locomotionRig != null)
            {
                phase = locomotionRig.CurrentAnimPhase01 * Mathf.PI * 2f;
                var speed = Mathf.Clamp01(locomotionRig.CurrentSpeed01);
                amount = speed;
            }

            if (enableAimDownSights && !useNetworkState)
            {
                localAimHeld = ReadAimPressed();
                var targetBlend = (localAimHeld && !localReloading) ? 1f : 0f;
                adsBlend = Mathf.SmoothDamp(
                    adsBlend,
                    targetBlend,
                    ref adsBlendVelocity,
                    Mathf.Max(0.01f, adsSmoothTime));
            }
            else
            {
                if (useNetworkState)
                {
                    adsBlend = Mathf.MoveTowards(
                        adsBlend,
                        networkAdsBlend,
                        Time.deltaTime / Mathf.Max(0.01f, adsSmoothTime));
                }
                else
                {
                    adsBlend = 0f;
                }
            }

            var adsPitchFollowBlend = Mathf.Clamp01(adsBlend);
            if (!useNetworkState && (!localAimHeld || localReloading))
            {
                // Local: stop ADS pitch-follow immediately on release/reload,
                // even while visual ADS blend is still fading out.
                adsPitchFollowBlend = 0f;
            }

            if (fpsController == null)
            {
                fpsController = GetComponent<FpsCharacterController>();
            }
            var targetCrouchBlend = useNetworkState
                ? networkCrouchBlend
                : (fpsController != null ? fpsController.CrouchBlend01 : 0f);
            crouchBlend = Mathf.SmoothDamp(
                crouchBlend,
                Mathf.Clamp01(targetCrouchBlend),
                ref crouchBlendVelocity,
                Mathf.Max(0.01f, crouchBlendSmoothTime));

            if (weaponParent != null)
            {
                var smooth = Mathf.Max(0.01f, adsSmoothTime);
                var parentTransform = weaponParent.parent;
                var hipLocalPosition = hipAnchorLocalPosition;
                var hipLocalRotation = Quaternion.Euler(hipAnchorLocalEuler);

                var wallTargetBlend = useNetworkState
                    ? Mathf.Clamp01(networkWallAvoidBlend)
                    : ComputeWallAvoidanceTargetBlend();

                Vector3 adsWorldPosition;
                Quaternion adsWorldRotation;
                if ((adsUseSightLock || adsLockToCameraPivot) && cameraPivot != null)
                {
                    var cameraWorldRotation = GetAimReferenceRotation(wallTargetBlend);
                    adsWorldPosition = cameraPivot.position + (cameraWorldRotation * adsCameraLocalPosition);
                    adsWorldRotation = cameraWorldRotation * Quaternion.Euler(adsCameraLocalEuler);
                }
                else if (parentTransform != null)
                {
                    adsWorldPosition = parentTransform.TransformPoint(adsAnchorLocalPosition);
                    adsWorldRotation = parentTransform.rotation * Quaternion.Euler(adsAnchorLocalEuler);
                }
                else
                {
                    adsWorldPosition = adsAnchorLocalPosition;
                    adsWorldRotation = Quaternion.Euler(adsAnchorLocalEuler);
                }

                var adsLocalPosition = parentTransform != null
                    ? parentTransform.InverseTransformPoint(adsWorldPosition)
                    : adsWorldPosition;
                var adsLocalRotation = parentTransform != null
                    ? Quaternion.Inverse(parentTransform.rotation) * adsWorldRotation
                    : adsWorldRotation;

                var targetAnchorLocalPosition = Vector3.Lerp(hipLocalPosition, adsLocalPosition, adsBlend);
                if (useNetworkState)
                {
                    var adsDropScale = Mathf.Lerp(1f, Mathf.Clamp01(crouchWeaponDropAdsMultiplier), Mathf.Clamp01(adsBlend));
                    targetAnchorLocalPosition.y -= crouchWeaponDrop * crouchBlend * adsDropScale;
                }
                else
                {
                    // Local first-person: apply crouch drop only outside ADS to keep sight alignment.
                    var hipDropScale = 1f - Mathf.Clamp01(adsBlend);
                    targetAnchorLocalPosition.y -= crouchWeaponDrop * crouchBlend * hipDropScale;
                }
                var targetAnchorLocalRotation = Quaternion.Slerp(hipLocalRotation, adsLocalRotation, adsBlend);
                ApplyWallAvoidance(ref targetAnchorLocalPosition, ref targetAnchorLocalRotation, wallTargetBlend);

                weaponParent.localPosition = Vector3.SmoothDamp(
                    weaponParent.localPosition,
                    targetAnchorLocalPosition,
                    ref anchorPositionVelocity,
                    smooth);

                var currentAnchorEuler = weaponParent.localRotation.eulerAngles;
                var targetAnchorEuler = targetAnchorLocalRotation.eulerAngles;
                var smoothedAnchorX = Mathf.SmoothDampAngle(currentAnchorEuler.x, targetAnchorEuler.x, ref anchorRotXVelocity, smooth);
                var smoothedAnchorY = Mathf.SmoothDampAngle(currentAnchorEuler.y, targetAnchorEuler.y, ref anchorRotYVelocity, smooth);
                var smoothedAnchorZ = Mathf.SmoothDampAngle(currentAnchorEuler.z, targetAnchorEuler.z, ref anchorRotZVelocity, smooth);
                weaponParent.localRotation = Quaternion.Euler(smoothedAnchorX, smoothedAnchorY, smoothedAnchorZ);
            }

            var isAdsCameraFollow = adsPitchFollowBlend > 0.001f && cameraPivot != null && (adsUseSightLock || adsLockToCameraPivot);
            var hipPitchInfluence = 1f - adsPitchFollowBlend;
            var bobPosAmount = isAdsCameraFollow
                ? 0f
                : amount * Mathf.Lerp(1f, Mathf.Clamp01(adsBobPositionMultiplier), adsBlend);
            var bobRotAmount = isAdsCameraFollow
                ? 0f
                : amount * Mathf.Lerp(1f, Mathf.Clamp01(adsBobRotationMultiplier), adsBlend);
            var idleSwayAmount = 0f;
            if (enableIdleSway && !isAdsCameraFollow)
            {
                var threshold = Mathf.Max(0.001f, idleFreezeSpeedThreshold * 2f);
                idleSwayAmount = Mathf.Clamp01((threshold - amount) / threshold) * (1f - Mathf.Clamp01(adsBlend));
            }
            var idlePhase = Time.unscaledTime * Mathf.Max(0.1f, idleSwaySpeed);

            var targetPos = baseLocalPosition + new Vector3(
                Mathf.Sin(phase) * bobPositionX * bobPosAmount,
                Mathf.Cos(phase * 2f) * bobPositionY * bobPosAmount,
                0f);
            targetPos += new Vector3(
                Mathf.Sin(idlePhase) * idleSwayPositionX * idleSwayAmount,
                Mathf.Cos(idlePhase * 1.7f) * idleSwayPositionY * idleSwayAmount,
                0f);

            var zAngleOffset = Mathf.Sin(phase) * bobRotationZ * bobRotAmount;
            zAngleOffset += Mathf.Sin(idlePhase * 0.85f) * idleSwayRotationZ * idleSwayAmount;
            var targetRot = baseLocalRotation * Quaternion.Euler(0f, 0f, zAngleOffset);

            var bobSmooth = Mathf.Max(0.01f, bobSmoothing);
            var smoothedPos = Vector3.SmoothDamp(
                weaponInstance.transform.localPosition,
                targetPos,
                ref bobPositionVelocity,
                bobSmooth);

            var currentZ = weaponInstance.transform.localRotation.eulerAngles.z;
            var desiredZ = targetRot.eulerAngles.z;
            var smoothedZ = Mathf.SmoothDampAngle(
                currentZ,
                desiredZ,
                ref bobRotationVelocity,
                bobSmooth);

            var targetPitchOffset = 0f;
            var targetYawOffset = 0f;
            if (enableCameraPitchTilt && cameraPivot != null && hipPitchInfluence > 0.0001f)
            {
                var pitch = useNetworkState
                    ? networkLookPitch
                    : NormalizeAngleSigned(cameraPivot.localEulerAngles.x);
                var clampedPitch = Mathf.Clamp(pitch, cameraPitchMin, cameraPitchMax);
                targetPitchOffset = clampedPitch * cameraPitchInfluence * hipPitchInfluence;

                var maxAbsPitch = Mathf.Max(1f, Mathf.Max(Mathf.Abs(cameraPitchMin), Mathf.Abs(cameraPitchMax)));
                var pitchNormalized = Mathf.Clamp(clampedPitch / maxAbsPitch, -1f, 1f);
                var arcSin = Mathf.Sin(pitchNormalized * Mathf.PI * 0.5f);
                var arcCos = 1f - Mathf.Cos(Mathf.Abs(pitchNormalized) * Mathf.PI * 0.5f);
                var pitchArcScale = Mathf.Lerp(1f, Mathf.Clamp01(adsPitchArcMultiplier), adsBlend);
                var pitchTiltScale = Mathf.Lerp(1f, Mathf.Clamp01(adsPitchTiltMultiplier), adsBlend);
                var pitchBlend = hipPitchInfluence;

                targetPos += new Vector3(
                    0f,
                    arcSin * cameraPitchArcVertical * pitchArcScale * pitchBlend,
                    -arcCos * cameraPitchArcBackward * pitchArcScale * pitchBlend);

                targetYawOffset = arcSin * cameraPitchYawInfluence * pitchTiltScale * pitchBlend;
                targetPitchOffset *= pitchTiltScale;
            }
            var pitchSmooth = Mathf.Max(0.01f, cameraPitchSmoothing);
            smoothedPitchOffset = Mathf.SmoothDampAngle(
                smoothedPitchOffset,
                targetPitchOffset,
                ref pitchRotationVelocity,
                pitchSmooth);
            smoothedPitchYawOffset = Mathf.SmoothDampAngle(
                smoothedPitchYawOffset,
                targetYawOffset,
                ref pitchYawVelocity,
                pitchSmooth);

            weaponInstance.transform.localPosition = smoothedPos;
            weaponInstance.transform.localRotation = Quaternion.Euler(
                baseLocalRotation.eulerAngles.x + smoothedPitchOffset,
                baseLocalRotation.eulerAngles.y + smoothedPitchYawOffset,
                smoothedZ);
        }

        private void EnsureWeaponMounted()
        {
            if (weaponInstance != null || weaponPrefab == null)
            {
                return;
            }

            var parent = weaponParent != null ? weaponParent : transform;
            weaponInstance = Instantiate(weaponPrefab, parent);
            weaponInstance.name = "WeaponModel";
            weaponInstance.transform.localPosition = localPosition;
            weaponInstance.transform.localRotation = Quaternion.Euler(localEulerAngles);
            weaponInstance.transform.localScale = localScale;
            baseLocalPosition = weaponInstance.transform.localPosition;
            baseLocalRotation = weaponInstance.transform.localRotation;

            if (cameraPivot == null)
            {
                var foundPivot = FindChildRecursive(transform, "CameraPivot");
                if (foundPivot != null)
                {
                    cameraPivot = foundPivot;
                }
            }

            if (fpsController == null)
            {
                fpsController = GetComponent<FpsCharacterController>();
            }

            if (weaponParent != null)
            {
                weaponParent.localPosition = hipAnchorLocalPosition;
                weaponParent.localRotation = Quaternion.Euler(hipAnchorLocalEuler);
            }

            if (disableWeaponColliders)
            {
                var colliders = weaponInstance.GetComponentsInChildren<Collider>(true);
                for (var i = 0; i < colliders.Length; i++)
                {
                    colliders[i].enabled = false;
                }
            }

            locomotionRig = GetComponent<ProceduralLocomotionRig>();
            if (locomotionRig == null)
            {
                locomotionRig = GetComponentInChildren<ProceduralLocomotionRig>(true);
            }

            if (locomotionRig == null)
            {
                return;
            }

            leftHandTargetTransform = FindChildRecursive(weaponInstance.transform, leftHandTargetName);
            rightHandTargetTransform = FindChildRecursive(weaponInstance.transform, rightHandTargetName);
            if (leftHandTargetTransform != null && rightHandTargetTransform != null)
            {
                locomotionRig.SetHandAttachments(leftHandTargetTransform, rightHandTargetTransform);
            }
            magHandTargetTransform = FindReloadMagTarget(weaponInstance.transform);
            if (magHandTargetTransform != null)
            {
                magHandTargetBaseLocalPos = magHandTargetTransform.localPosition;
            }

            sightTarget = FindSightTarget(weaponInstance.transform);
            if (weaponParent != null && sightTarget != null)
            {
                sightLocalPositionOnAnchor = weaponParent.InverseTransformPoint(sightTarget.position);
                sightLocalRotationOnAnchor = Quaternion.Inverse(weaponParent.rotation) * sightTarget.rotation;
                hasSightCalibration = true;
            }
        }

        private static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null || string.IsNullOrWhiteSpace(childName))
            {
                return null;
            }

            var all = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t != null && string.Equals(t.name, childName, System.StringComparison.Ordinal))
                {
                    return t;
                }
            }

            return null;
        }

        private Transform FindSightTarget(Transform root)
        {
            if (root == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(sightTargetName))
            {
                var direct = FindChildRecursive(root, sightTargetName);
                if (direct != null)
                {
                    return direct;
                }
            }

            var fallbackNames = new[] { "SightTarget", "ADS", "Aim", "Muzzle" };
            for (var i = 0; i < fallbackNames.Length; i++)
            {
                var t = FindChildRecursive(root, fallbackNames[i]);
                if (t != null)
                {
                    return t;
                }
            }
            return null;
        }

        private Transform FindReloadMagTarget(Transform root)
        {
            if (root == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(reloadMagHandTargetName))
            {
                var named = FindChildRecursive(root, reloadMagHandTargetName);
                if (named != null)
                {
                    return named;
                }
            }

            var fallback = new[] { "MagazineHandTarget", "MagTarget", "MagHand", "MagPivot" };
            for (var i = 0; i < fallback.Length; i++)
            {
                var t = FindChildRecursive(root, fallback[i]);
                if (t != null)
                {
                    return t;
                }
            }

            return null;
        }

        private System.Collections.IEnumerator ReloadMagRoutine(float durationSeconds)
        {
            if (magHandTargetTransform == null)
            {
                yield break;
            }

            var totalDuration = Mathf.Max(0.08f, durationSeconds);
            var pullDuration = totalDuration * 0.42f;
            var insertDuration = totalDuration * 0.58f;
            var hiddenLocalPos = magHandTargetBaseLocalPos + reloadMagPullLocalOffset;
            var t = 0f;

            while (t < pullDuration)
            {
                t += Time.deltaTime;
                var blend = Mathf.Clamp01(t / Mathf.Max(0.01f, pullDuration));
                var eased = Mathf.SmoothStep(0f, 1f, blend);
                magHandTargetTransform.localPosition = Vector3.Lerp(magHandTargetBaseLocalPos, hiddenLocalPos, eased);
                yield return null;
            }

            t = 0f;
            while (t < insertDuration)
            {
                t += Time.deltaTime;
                var blend = Mathf.Clamp01(t / Mathf.Max(0.01f, insertDuration));
                var eased = Mathf.SmoothStep(0f, 1f, blend);
                magHandTargetTransform.localPosition = Vector3.Lerp(hiddenLocalPos, magHandTargetBaseLocalPos, eased);
                yield return null;
            }

            magHandTargetTransform.localPosition = magHandTargetBaseLocalPos;
            reloadRoutine = null;
        }

        private Quaternion GetAimReferenceRotation(float wallTargetBlend)
        {
            float pitch;
            if (!useNetworkState)
            {
                if (cameraPivot == null)
                {
                    return transform.rotation;
                }

                pitch = NormalizeAngleSigned(cameraPivot.localEulerAngles.x);
            }
            else
            {
                pitch = networkLookPitch;
            }

            var clampedPitch = Mathf.Clamp(pitch, cameraPitchMin, cameraPitchMax);
            var effectiveWallBlend = Mathf.Max(Mathf.Clamp01(wallTargetBlend), Mathf.Clamp01(wallAvoidBlend));
            var wallFollowBlock = Mathf.InverseLerp(
                Mathf.Clamp01(adsWallPitchFollowStartBlend),
                1f,
                effectiveWallBlend);
            var adsWallInfluence = Mathf.Clamp01(adsBlend) * wallFollowBlock;
            if (Mathf.Abs(clampedPitch) > 0.0001f && adsWallInfluence > 0.0001f)
            {
                // When ADS weapon is pressed into wall, suppress pitch follow in both directions
                // so the weapon does not move into the camera on look up/down.
                var followScale = Mathf.Lerp(1f, Mathf.Clamp01(adsWallPitchFollowMin), adsWallInfluence);
                clampedPitch *= followScale;
            }

            return transform.rotation * Quaternion.Euler(clampedPitch, 0f, 0f);
        }

        private void ApplyWallAvoidance(
            ref Vector3 targetAnchorLocalPosition,
            ref Quaternion targetAnchorLocalRotation,
            float targetBlend)
        {
            if (!enableWeaponCollisionAvoidance)
            {
                targetBlend = 0f;
            }

            wallAvoidBlend = Mathf.SmoothDamp(
                wallAvoidBlend,
                Mathf.Clamp01(targetBlend),
                ref wallAvoidBlendVelocity,
                Mathf.Max(0.01f, wallAvoidSmoothTime));
            var blend = Mathf.Clamp01(wallAvoidBlend);
            if (blend <= 0.0001f)
            {
                return;
            }

            targetAnchorLocalPosition.z -= Mathf.Max(0f, wallPushBack) * blend;
            targetAnchorLocalPosition.y += Mathf.Max(0f, wallPushUp) * blend;
            var adsWallBlend = Mathf.Clamp01(adsBlend) * blend;
            if (adsWallBlend > 0.0001f)
            {
                // In ADS, force the weapon downward near walls to keep it clearly out of the camera.
                targetAnchorLocalPosition.y -= Mathf.Max(0f, adsWallDropDown) * adsWallBlend;
                var adsHardDropBlend = Mathf.Clamp01(adsBlend) * Mathf.InverseLerp(
                    Mathf.Clamp01(adsWallHardDropStartBlend),
                    1f,
                    Mathf.Clamp01(targetBlend));
                targetAnchorLocalPosition.y -= Mathf.Max(0f, adsWallHardDropDown) * adsHardDropBlend;
            }

            var tiltX = -Mathf.Max(0f, wallTiltX) * blend;
            if (adsWallBlend > 0.0001f)
            {
                tiltX += Mathf.Max(0f, adsWallPitchDown) * adsWallBlend;
            }
            var avoidRotation = Quaternion.Euler(
                tiltX,
                0f,
                wallTiltZ * blend);
            targetAnchorLocalRotation *= avoidRotation;
        }

        private float ComputeWallAvoidanceTargetBlend()
        {
            if (!enableWeaponCollisionAvoidance || cameraPivot == null)
            {
                return 0f;
            }

            var distance = Mathf.Max(0.05f, wallCheckDistance);
            var radius = Mathf.Max(0.01f, wallCheckRadius);
            var ray = new Ray(cameraPivot.position, cameraPivot.forward);
            if (!Physics.SphereCast(ray, radius, out var hit, distance, wallCheckMask, QueryTriggerInteraction.Ignore))
            {
                return 0f;
            }

            if (hit.collider != null && hit.collider.transform.IsChildOf(transform))
            {
                return 0f;
            }

            var normalized = 1f - Mathf.Clamp01(hit.distance / distance);
            normalized = Mathf.Pow(Mathf.Clamp01(normalized), Mathf.Max(0.01f, wallAvoidResponsePower));
            var minBlend = Mathf.Clamp01(wallAvoidMinBlendOnHit);
            return Mathf.Clamp01(Mathf.Max(minBlend, normalized));
        }

        private static float NormalizeAngleSigned(float angle)
        {
            var normalized = angle % 360f;
            if (normalized > 180f)
            {
                normalized -= 360f;
            }

            if (normalized < -180f)
            {
                normalized += 360f;
            }

            return normalized;
        }

        private static bool ReadAimPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.rightButton.isPressed;
#else
            return Input.GetMouseButton(1);
#endif
        }
    }
}
