using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ShooterPrototype.Player
{
    public enum WeaponWallCheckMode
    {
        CameraRay = 0,
        WeaponCapsule = 1,
    }

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
        [SerializeField] private string remoteLeftHandTargetName = "RemoteLeftHandTarget";
        [SerializeField] private string remoteRightHandTargetName = "RemoteRightHandTarget";
        [SerializeField] private string sightTargetName = "AimPoint";
        [SerializeField] private string reloadMagHandTargetName = "MagHandTarget";
        [SerializeField] private Vector3 reloadMagPullLocalOffset = new Vector3(0f, -0.28f, -0.42f);

        [Header("Weapon walk bob")]
        [SerializeField] private bool enableWalkBob = true;
        [SerializeField] private float bobPositionX = 0.012f;
        [SerializeField] private float bobPositionY = 0.01f;
        [SerializeField] private float bobRotationZ = 3.2f;
        [SerializeField] private float bobSmoothing = 0.08f;
        [SerializeField] private float walkBobFrequencyMultiplier = 1.22f;
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

        [Header("Hip fire (camera lock)")]
        [SerializeField] private bool hipLockToCameraPivot = true;
        [SerializeField] private Vector3 hipCameraLocalPosition = new Vector3(0.21f, -0.16f, 0.38f);
        [SerializeField] private Vector3 hipCameraLocalEuler = new Vector3(-0.75f, -3f, 4.5f);
        [SerializeField] private Vector3 hipAnchorLocalPosition = new Vector3(0.21f, 1.2f, 0.365f);
        [SerializeField] private Vector3 hipAnchorLocalEuler = new Vector3(-0.752f, -3.043f, 4.539f);

        [Header("Aim Down Sights (ADS)")]
        [SerializeField] private bool enableAimDownSights = true;
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
        [SerializeField] private bool enableAdsCameraZoom = true;
        [SerializeField] private float adsCameraFov = 55f;
        [SerializeField] private float adsCameraZoomSmoothTime = 0.08f;
        [Header("Sprint")]
        [SerializeField] private Vector3 sprintWeaponLocalOffset = new Vector3(-0.13f, 0f, -0.16f);
        [SerializeField] private Vector3 sprintWeaponLocalEuler = new Vector3(3.18f, -57.1f, -20f);
        [SerializeField] private float sprintBlendSmoothTime = 0.14f;
        [SerializeField] private float sprintBobAmplitudeMultiplier = 1.7f;
        [SerializeField] private float sprintBobFrequencyMultiplier = 0.82f;
        [SerializeField] private float sprintBobSmoothTime = 0.06f;
        [Header("Crouch")]
        [SerializeField] private float crouchWeaponDrop = 0.18f;
        [SerializeField] private float crouchBlendSmoothTime = 0.16f;
        [SerializeField] private float crouchWeaponDropAdsMultiplier = 0.85f;
        [Header("Weapon Collision")]
        [SerializeField] private bool enableWeaponCollisionAvoidance = true;
        [SerializeField] private WeaponWallCheckMode wallCheckMode = WeaponWallCheckMode.CameraRay;
        [SerializeField] private bool localPlayerWallAvoidanceOnly = true;
        [SerializeField] private bool useWeaponBlockLayerOnly = true;
        [SerializeField] private string muzzleTargetName = "Muzzle";
        [SerializeField] private float wallCheckDistance = 0.95f;
        [SerializeField] private float wallCheckRadius = 0.09f;
        [SerializeField] private LayerMask wallCheckMask;
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
        private Transform muzzleTransform;
        private Vector3 sightLocalPositionOnAnchor;
        private Quaternion sightLocalRotationOnAnchor = Quaternion.identity;
        private bool hasSightCalibration;
        private bool useNetworkState;
        private float networkLookPitch;
        private float networkAdsBlend;
        private float networkCrouchBlend;
        private float networkWallAvoidBlend;
        private bool networkSprinting;
        private float sprintBlend;
        private float sprintBlendVelocity;
        private float previousSprintTarget;
        private float crouchBlend;
        private float crouchBlendVelocity;
        private bool localReloading;
        private float wallAvoidBlend;
        private float wallAvoidBlendVelocity;
        private FpsCharacterController fpsController;
        private Camera localPlayerCamera;
        private float baseCameraFov = -1f;
        private float adsCameraFovVelocity;
        private Transform leftHandTargetTransform;
        private Transform rightHandTargetTransform;
        private Transform remoteLeftHandTargetTransform;
        private Transform remoteRightHandTargetTransform;
        private Transform magHandTargetTransform;
        private Vector3 magHandTargetBaseLocalPos;
        private Coroutine reloadRoutine;
        private bool handAttachedWeaponActive;
        private bool firstPersonRigidHandIk;
        private SyntyWeaponHandBinder handBinder;

        private void Awake()
        {
            if (GetComponent<RemoteThirdPersonPlayerBootstrap>() != null)
            {
                return;
            }

            EnsureWeaponMounted();
        }

        public float AdsBlend => adsBlend;
        public float AdsFollowPitchDownLimit => adsFollowPitchDownLimit;
        public Transform MountedWeaponRoot => weaponInstance != null ? weaponInstance.transform : null;
        public Transform WeaponAnchorTransform => weaponParent;

        public Transform RightHandGripTarget => rightHandTargetTransform;
        public Transform LeftHandGripTarget => leftHandTargetTransform;
        public Transform RemoteRightHandGripTarget => remoteRightHandTargetTransform;
        public Transform RemoteLeftHandGripTarget => remoteLeftHandTargetTransform;

        public Transform ResolveRightHandGripTarget(bool forRemote)
        {
            if (forRemote && remoteRightHandTargetTransform != null)
            {
                return remoteRightHandTargetTransform;
            }

            return rightHandTargetTransform;
        }

        public Transform ResolveLeftHandGripTarget(bool forRemote)
        {
            if (forRemote && remoteLeftHandTargetTransform != null)
            {
                return remoteLeftHandTargetTransform;
            }

            return leftHandTargetTransform;
        }
        public float CurrentWallAvoidBlend => Mathf.Clamp01(wallAvoidBlend);

        public void SetHandAttachedWeaponActive(bool active)
        {
            handAttachedWeaponActive = active;
        }

        public void EnsureMounted()
        {
            EnsureWeaponMounted();
        }

        public void SetThirdPersonWeaponRenderersEnabled(bool enabled)
        {
            if (weaponInstance == null)
            {
                return;
            }

            var renderers = weaponInstance.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].enabled = enabled;
                }
            }
        }

        public void ConfigureWeaponParent(Transform parent, Transform pivot = null)
        {
            if (parent != null)
            {
                weaponParent = parent;
            }

            if (pivot != null)
            {
                cameraPivot = pivot;
            }

            if (weaponInstance != null && weaponParent != null &&
                !handAttachedWeaponActive &&
                weaponInstance.transform.parent != weaponParent)
            {
                weaponInstance.transform.SetParent(weaponParent, true);
            }
        }

        public void SetFirstPersonRigidHandIk(bool enabled)
        {
            if (firstPersonRigidHandIk == enabled)
            {
                return;
            }

            firstPersonRigidHandIk = enabled;
            if (enabled)
            {
                smoothedPitchOffset = 0f;
                smoothedPitchYawOffset = 0f;
                pitchRotationVelocity = 0f;
                pitchYawVelocity = 0f;
                bobPositionVelocity = Vector3.zero;
                bobRotationVelocity = 0f;
            }
        }

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

        public void SetNetworkSprintState(bool isSprinting)
        {
            networkSprinting = isSprinting;
        }

        public void SetLocalReloading(bool reloading)
        {
            localReloading = reloading;
        }

        private void OnEnable()
        {
            if (GetComponent<RemoteThirdPersonPlayerBootstrap>() != null)
            {
                return;
            }

            EnsureWeaponMounted();
        }

        private void LateUpdate()
        {
            if (handBinder == null)
            {
                handBinder = GetComponent<SyntyWeaponHandBinder>();
            }

            handBinder?.SyncFirstPersonRigidHandIkMode();

            if (weaponInstance == null)
            {
                return;
            }

            if (handAttachedWeaponActive)
            {
                if (!useNetworkState)
                {
                    UpdateAdsCameraZoom();
                }

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

            if (fpsController == null)
            {
                fpsController = GetComponent<FpsCharacterController>();
            }

            var localSprinting = !useNetworkState && fpsController != null && fpsController.IsSprinting;
            var sprintTarget = useNetworkState
                ? (networkSprinting ? 1f : 0f)
                : (localSprinting ? 1f : 0f);
            sprintBlend = Mathf.SmoothDamp(
                sprintBlend,
                sprintTarget,
                ref sprintBlendVelocity,
                Mathf.Max(0.01f, sprintBlendSmoothTime));

            if (sprintTarget > 0.5f && previousSprintTarget <= 0.5f)
            {
                bobPositionVelocity = Vector3.zero;
                bobRotationVelocity = 0f;
            }
            previousSprintTarget = sprintTarget;

            if (enableAimDownSights && !useNetworkState)
            {
                localAimHeld = ReadAimPressed();
                var targetBlend = (localAimHeld && !localReloading && !localSprinting) ? 1f : 0f;
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

            UpdateAdsCameraZoom();

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

                var wallTargetBlend = useNetworkState
                    ? Mathf.Clamp01(networkWallAvoidBlend)
                    : ComputeWallAvoidanceTargetBlend();

                var sprintPitchFollowScale = 1f - Mathf.Clamp01(sprintBlend);
                var cameraWorldRotation = cameraPivot != null
                    ? GetAimReferenceRotation(wallTargetBlend, sprintPitchFollowScale)
                    : transform.rotation;

                Vector3 hipBodyWorldPosition;
                Quaternion hipBodyWorldRotation;
                if (parentTransform != null)
                {
                    hipBodyWorldPosition = parentTransform.TransformPoint(hipAnchorLocalPosition);
                    hipBodyWorldRotation = parentTransform.rotation * Quaternion.Euler(hipAnchorLocalEuler);
                }
                else
                {
                    hipBodyWorldPosition = hipAnchorLocalPosition;
                    hipBodyWorldRotation = Quaternion.Euler(hipAnchorLocalEuler);
                }

                Vector3 hipWorldPosition;
                Quaternion hipWorldRotation;
                if (hipLockToCameraPivot && cameraPivot != null && !useNetworkState)
                {
                    var lockedWorldPosition = cameraPivot.position + (cameraWorldRotation * hipCameraLocalPosition);
                    var lockedWorldRotation = cameraWorldRotation * Quaternion.Euler(hipCameraLocalEuler);
                    hipWorldPosition = Vector3.Lerp(lockedWorldPosition, hipBodyWorldPosition, sprintBlend);
                    hipWorldRotation = Quaternion.Slerp(lockedWorldRotation, hipBodyWorldRotation, sprintBlend);
                }
                else
                {
                    hipWorldPosition = hipBodyWorldPosition;
                    hipWorldRotation = hipBodyWorldRotation;
                }

                var hipLocalPosition = parentTransform != null
                    ? parentTransform.InverseTransformPoint(hipWorldPosition)
                    : hipWorldPosition;
                var hipLocalRotation = parentTransform != null
                    ? Quaternion.Inverse(parentTransform.rotation) * hipWorldRotation
                    : hipWorldRotation;

                Vector3 adsWorldPosition;
                Quaternion adsWorldRotation;
                if ((adsUseSightLock || adsLockToCameraPivot) && cameraPivot != null)
                {
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
                if (sprintBlend > 0.0001f)
                {
                    targetAnchorLocalPosition += sprintWeaponLocalOffset * sprintBlend;
                    targetAnchorLocalRotation *= Quaternion.Euler(sprintWeaponLocalEuler * sprintBlend);
                }
                ApplyWallAvoidance(ref targetAnchorLocalPosition, ref targetAnchorLocalRotation, wallTargetBlend);

                var rigidAnchorFollow = firstPersonRigidHandIk &&
                                        !useNetworkState &&
                                        hipLockToCameraPivot &&
                                        adsBlend < 0.001f &&
                                        sprintBlend < 0.001f;

                if (rigidAnchorFollow)
                {
                    weaponParent.localPosition = targetAnchorLocalPosition;
                    weaponParent.localRotation = targetAnchorLocalRotation;
                    anchorPositionVelocity = Vector3.zero;
                    anchorRotXVelocity = 0f;
                    anchorRotYVelocity = 0f;
                    anchorRotZVelocity = 0f;
                }
                else
                {
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
            }

            var rigidHandViewModel = firstPersonRigidHandIk && !useNetworkState;
            var isHipCameraFollow = hipLockToCameraPivot && cameraPivot != null && !useNetworkState
                && adsPitchFollowBlend < 0.001f && sprintBlend < 0.001f;
            var isAdsCameraFollow = adsPitchFollowBlend > 0.001f && cameraPivot != null && (adsUseSightLock || adsLockToCameraPivot);
            var isCameraLocked = isHipCameraFollow || isAdsCameraFollow;
            var suppressLocomotionBob = isAdsCameraFollow;
            var hipPitchInfluence = 1f - adsPitchFollowBlend;
            if ((hipLockToCameraPivot && !useNetworkState && adsPitchFollowBlend < 0.001f && sprintBlend < 0.001f) ||
                rigidHandViewModel)
            {
                // Hip camera lock handles pitch on the anchor; skip extra hip arc/tilt on the model.
                hipPitchInfluence = 0f;
            }
            if (sprintBlend > 0.0001f)
            {
                // During sprint, suppress camera pitch-driven weapon tilt completely.
                hipPitchInfluence = 0f;
            }

            var bobPosAmount = suppressLocomotionBob
                ? 0f
                : amount * Mathf.Lerp(1f, Mathf.Clamp01(adsBobPositionMultiplier), adsBlend);
            var bobRotAmount = suppressLocomotionBob
                ? 0f
                : amount * Mathf.Lerp(1f, Mathf.Clamp01(adsBobRotationMultiplier), adsBlend);
            if (sprintBlend > 0.0001f)
            {
                var sprintAmp = Mathf.Lerp(1f, Mathf.Max(1f, sprintBobAmplitudeMultiplier), sprintBlend);
                bobPosAmount *= sprintAmp;
                bobRotAmount *= sprintAmp;
            }

            var crouchBobScale = 1f - Mathf.Clamp01(crouchBlend);
            bobPosAmount *= crouchBobScale;
            bobRotAmount *= crouchBobScale;

            var idleSwayAmount = 0f;
            if (enableIdleSway && !isCameraLocked && !rigidHandViewModel && sprintBlend < 0.95f && crouchBobScale > 0.05f)
            {
                var threshold = Mathf.Max(0.001f, idleFreezeSpeedThreshold * 2f);
                idleSwayAmount = Mathf.Clamp01((threshold - amount) / threshold) *
                                   (1f - Mathf.Clamp01(adsBlend)) *
                                   (1f - sprintBlend);
            }

            var walkFreq = Mathf.Max(0.1f, walkBobFrequencyMultiplier);
            var sprintFreq = Mathf.Clamp(sprintBobFrequencyMultiplier, 0.1f, 2f);
            phase *= Mathf.Lerp(walkFreq, sprintFreq, sprintBlend);
            var idlePhase = Time.unscaledTime * Mathf.Max(0.1f, idleSwaySpeed);

            var targetPos = baseLocalPosition;
            if (bobPosAmount > 0.0001f)
            {
                targetPos += new Vector3(
                    Mathf.Sin(phase) * bobPositionX * bobPosAmount,
                    Mathf.Cos(phase * 2f) * bobPositionY * bobPosAmount,
                    0f);
            }

            if (idleSwayAmount > 0.0001f)
            {
                targetPos += new Vector3(
                    Mathf.Sin(idlePhase) * idleSwayPositionX * idleSwayAmount,
                    Mathf.Cos(idlePhase * 1.7f) * idleSwayPositionY * idleSwayAmount,
                    0f);
            }

            var zAngleOffset = 0f;
            if (bobRotAmount > 0.0001f)
            {
                zAngleOffset += Mathf.Sin(phase) * bobRotationZ * bobRotAmount;
            }

            if (idleSwayAmount > 0.0001f)
            {
                zAngleOffset += Mathf.Sin(idlePhase * 0.85f) * idleSwayRotationZ * idleSwayAmount;
            }

            var targetRot = baseLocalRotation * Quaternion.Euler(0f, 0f, zAngleOffset);

            var bobSmooth = Mathf.Lerp(
                Mathf.Max(0.01f, bobSmoothing),
                Mathf.Max(0.05f, sprintBobSmoothTime),
                sprintBlend);
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
            if (!rigidHandViewModel &&
                enableCameraPitchTilt &&
                cameraPivot != null &&
                hipPitchInfluence > 0.0001f)
            {
                var pitch = useNetworkState
                    ? networkLookPitch
                    : NormalizeAngleSigned(cameraPivot.localEulerAngles.x);
                var clampedPitch = Mathf.Clamp(pitch, cameraPitchMin, cameraPitchMax);
                var hipWeight = 1f - Mathf.Clamp01(adsBlend);
                var adsWeight = 1f - hipWeight;
                var pitchArcScale = Mathf.Lerp(1f, Mathf.Clamp01(adsPitchArcMultiplier), adsBlend);
                var pitchTiltScale = Mathf.Lerp(1f, Mathf.Clamp01(adsPitchTiltMultiplier), adsBlend);
                var pitchBlend = hipPitchInfluence;

                var maxAbsPitch = Mathf.Max(1f, Mathf.Max(Mathf.Abs(cameraPitchMin), Mathf.Abs(cameraPitchMax)));
                var pitchNormalized = Mathf.Clamp(clampedPitch / maxAbsPitch, -1f, 1f);
                var arcSin = Mathf.Sin(pitchNormalized * Mathf.PI * 0.5f);
                var arcCos = 1f - Mathf.Cos(Mathf.Abs(pitchNormalized) * Mathf.PI * 0.5f);

                // Hip fire without camera lock: move weapon along a vertical arc.
                if (hipWeight > 0.0001f && !(hipLockToCameraPivot && !useNetworkState))
                {
                    const float hipArcMaxAngle = 38f;
                    const float hipArcRadius = 0.16f;
                    var hipAngleRad = -pitchNormalized * hipArcMaxAngle * Mathf.Deg2Rad;
                    targetPos += new Vector3(
                        0f,
                        Mathf.Sin(hipAngleRad) * hipArcRadius * hipWeight * pitchBlend,
                        (1f - Mathf.Cos(hipAngleRad)) * hipArcRadius * hipWeight * pitchBlend);
                }

                // ADS: keep subtle tilt + positional arc.
                if (adsWeight > 0.0001f)
                {
                    targetPitchOffset = clampedPitch * cameraPitchInfluence * pitchTiltScale * adsWeight * pitchBlend;
                    targetYawOffset = arcSin * cameraPitchYawInfluence * pitchTiltScale * adsWeight * pitchBlend;
                    targetPos += new Vector3(
                        0f,
                        arcSin * cameraPitchArcVertical * pitchArcScale * adsWeight * pitchBlend,
                        -arcCos * cameraPitchArcBackward * pitchArcScale * adsWeight * pitchBlend);
                }
            }
            var pitchSmooth = Mathf.Max(0.01f, cameraPitchSmoothing);
            if (rigidHandViewModel)
            {
                smoothedPitchOffset = 0f;
                smoothedPitchYawOffset = 0f;
                pitchRotationVelocity = 0f;
                pitchYawVelocity = 0f;
            }
            else
            {
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
            }

            if (rigidHandViewModel)
            {
                weaponInstance.transform.localPosition = smoothedPos;
                if (wallAvoidBlend > 0.05f)
                {
                    var localPos = weaponInstance.transform.localPosition;
                    localPos.z = Mathf.Min(localPos.z, baseLocalPosition.z);
                    weaponInstance.transform.localPosition = localPos;
                }

                weaponInstance.transform.localRotation = Quaternion.Euler(
                    baseLocalRotation.eulerAngles.x,
                    baseLocalRotation.eulerAngles.y,
                    smoothedZ);
            }
            else
            {
                weaponInstance.transform.localPosition = smoothedPos;
                if (wallAvoidBlend > 0.05f)
                {
                    var localPos = weaponInstance.transform.localPosition;
                    localPos.z = Mathf.Min(localPos.z, baseLocalPosition.z);
                    weaponInstance.transform.localPosition = localPos;
                }

                weaponInstance.transform.localRotation = Quaternion.Euler(
                    baseLocalRotation.eulerAngles.x + smoothedPitchOffset,
                    baseLocalRotation.eulerAngles.y + smoothedPitchYawOffset,
                    smoothedZ);
            }
        }

        /// <summary>
        /// Removes duplicate WeaponModel objects outside the managed attach hierarchy.
        /// </summary>
        public static void RemoveStrayWeaponModels(Transform searchRoot, Transform keepUnder = null)
        {
            if (searchRoot == null)
            {
                return;
            }

            var transforms = searchRoot.GetComponentsInChildren<Transform>(true);
            for (var i = transforms.Length - 1; i >= 0; i--)
            {
                var candidate = transforms[i];
                if (!string.Equals(candidate.name, "WeaponModel", StringComparison.Ordinal))
                {
                    continue;
                }

                if (keepUnder != null && candidate.IsChildOf(keepUnder))
                {
                    continue;
                }

                DestroyWeaponModelObject(candidate.gameObject);
            }
        }

        /// <summary>
        /// Remote prefabs may bake a WeaponModel on the hand bone while this component also spawns one at runtime.
        /// </summary>
        public static void RemoveExtraWeaponModels(Transform searchRoot, GameObject keep = null)
        {
            if (searchRoot == null)
            {
                return;
            }

            var transforms = searchRoot.GetComponentsInChildren<Transform>(true);
            for (var i = transforms.Length - 1; i >= 0; i--)
            {
                var candidate = transforms[i];
                if (!string.Equals(candidate.name, "WeaponModel", StringComparison.Ordinal))
                {
                    continue;
                }

                if (keep != null && candidate.gameObject == keep)
                {
                    continue;
                }

                DestroyWeaponModelObject(candidate.gameObject);
            }
        }

        private static void DestroyWeaponModelObject(GameObject weaponModel)
        {
            if (weaponModel == null)
            {
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEngine.Object.DestroyImmediate(weaponModel);
                return;
            }
#endif
            Destroy(weaponModel);
        }

        private void EnsureWeaponMounted()
        {
            if (weaponInstance != null)
            {
                return;
            }

            if (weaponPrefab == null)
            {
                if (TryAdoptExistingWeaponModel())
                {
                    FinalizeWeaponMount(skipAnchorReposition: true);
                }

                return;
            }

            RemoveExtraWeaponModels(transform);

            var parent = weaponParent != null ? weaponParent : transform;
            weaponInstance = Instantiate(weaponPrefab, parent);
            weaponInstance.name = "WeaponModel";
            weaponInstance.transform.localPosition = localPosition;
            weaponInstance.transform.localRotation = Quaternion.Euler(localEulerAngles);
            weaponInstance.transform.localScale = localScale;
            baseLocalPosition = weaponInstance.transform.localPosition;
            baseLocalRotation = weaponInstance.transform.localRotation;

            FinalizeWeaponMount(skipAnchorReposition: false);
            RemoveExtraWeaponModels(transform, weaponInstance);
        }

        private bool TryAdoptExistingWeaponModel()
        {
            var transforms = transform.GetComponentsInChildren<Transform>(true);
            GameObject adopted = null;
            for (var i = 0; i < transforms.Length; i++)
            {
                var candidate = transforms[i];
                if (candidate == null ||
                    !string.Equals(candidate.name, "WeaponModel", StringComparison.Ordinal))
                {
                    continue;
                }

                adopted = candidate.gameObject;
            }

            if (adopted == null)
            {
                return false;
            }

            weaponInstance = adopted;
            baseLocalPosition = weaponInstance.transform.localPosition;
            baseLocalRotation = weaponInstance.transform.localRotation;
            return true;
        }

        private void FinalizeWeaponMount(bool skipAnchorReposition)
        {
            if (weaponInstance == null)
            {
                return;
            }

            if (cameraPivot == null)
            {
                var foundPivot = FindChildRecursive(transform, "CameraPivot");
                if (foundPivot != null)
                {
                    cameraPivot = foundPivot;
                }
            }

            if (localPlayerCamera == null)
            {
                if (cameraPivot != null)
                {
                    localPlayerCamera = cameraPivot.GetComponentInChildren<Camera>(true);
                }

                if (localPlayerCamera == null)
                {
                    localPlayerCamera = GetComponentInChildren<Camera>(true);
                }
            }

            if (localPlayerCamera != null && baseCameraFov <= 0f)
            {
                baseCameraFov = localPlayerCamera.fieldOfView;
            }

            if (fpsController == null)
            {
                fpsController = GetComponent<FpsCharacterController>();
            }

            if (!skipAnchorReposition && weaponParent != null)
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

            CacheWeaponHandTargets();

            if (locomotionRig != null &&
                leftHandTargetTransform != null &&
                rightHandTargetTransform != null)
            {
                locomotionRig.SetHandAttachments(leftHandTargetTransform, rightHandTargetTransform);
            }

            magHandTargetTransform = FindReloadMagTarget(weaponInstance.transform);
            if (magHandTargetTransform != null)
            {
                magHandTargetBaseLocalPos = magHandTargetTransform.localPosition;
            }

            sightTarget = FindSightTarget(weaponInstance.transform);
            muzzleTransform = FindChildRecursive(weaponInstance.transform, muzzleTargetName);
            if (muzzleTransform == null)
            {
                muzzleTransform = FindChildRecursive(weaponInstance.transform, "Muzzle");
            }

            if (weaponParent != null && sightTarget != null)
            {
                sightLocalPositionOnAnchor = weaponParent.InverseTransformPoint(sightTarget.position);
                sightLocalRotationOnAnchor = Quaternion.Inverse(weaponParent.rotation) * sightTarget.rotation;
                hasSightCalibration = true;
            }
        }

        private void CacheWeaponHandTargets()
        {
            if (weaponInstance == null)
            {
                leftHandTargetTransform = null;
                rightHandTargetTransform = null;
                remoteLeftHandTargetTransform = null;
                remoteRightHandTargetTransform = null;
                return;
            }

            leftHandTargetTransform = FindChildRecursive(weaponInstance.transform, leftHandTargetName);
            rightHandTargetTransform = FindChildRecursive(weaponInstance.transform, rightHandTargetName);

            if (GetComponent<RemoteThirdPersonPlayerBootstrap>() != null)
            {
                remoteLeftHandTargetTransform = FindWeaponGripAnchor(
                    weaponInstance.transform,
                    remoteLeftHandTargetName);
                remoteRightHandTargetTransform = FindWeaponGripAnchor(
                    weaponInstance.transform,
                    remoteRightHandTargetName);
            }
            else
            {
                remoteLeftHandTargetTransform = null;
                remoteRightHandTargetTransform = null;
            }
        }

        /// <summary>
        /// Read-only lookup for grip anchors authored on the weapon prefab.
        /// Prefers a direct child of the weapon root and never deletes or modifies transforms.
        /// </summary>
        internal static Transform FindWeaponGripAnchor(Transform weaponRoot, string anchorName)
        {
            if (weaponRoot == null || string.IsNullOrWhiteSpace(anchorName))
            {
                return null;
            }

            var directChild = weaponRoot.Find(anchorName);
            if (directChild != null)
            {
                return directChild;
            }

            var all = weaponRoot.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < all.Length; i++)
            {
                var candidate = all[i];
                if (candidate == null ||
                    candidate == weaponRoot ||
                    !string.Equals(candidate.name, anchorName, StringComparison.Ordinal))
                {
                    continue;
                }

                return candidate;
            }

            return null;
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

        private Quaternion GetAimReferenceRotation(float wallTargetBlend, float pitchFollowScale = 1f)
        {
            pitchFollowScale = Mathf.Clamp01(pitchFollowScale);
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
            if (adsBlend < 0.999f)
            {
                ResolveHipWeaponPitchLimits(out var weaponDownLimit, out var weaponUpLimit);
                weaponDownLimit = Mathf.Lerp(weaponDownLimit, cameraPitchMin, adsBlend);
                weaponUpLimit = Mathf.Lerp(weaponUpLimit, cameraPitchMax, adsBlend);
                clampedPitch = Mathf.Clamp(clampedPitch, weaponDownLimit, weaponUpLimit);
            }
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

            clampedPitch *= pitchFollowScale;

            return transform.rotation * Quaternion.Euler(clampedPitch, 0f, 0f);
        }

        private void ResolveHipWeaponPitchLimits(out float downLimit, out float upLimit)
        {
            if (fpsController == null)
            {
                fpsController = GetComponent<FpsCharacterController>();
            }

            var maxPitch = fpsController != null ? fpsController.HipMaxLookAngle : 50f;
            maxPitch = Mathf.Clamp(maxPitch, 1f, 89f);
            downLimit = -maxPitch;
            upLimit = maxPitch;
        }

        private void ApplyWallAvoidance(
            ref Vector3 targetAnchorLocalPosition,
            ref Quaternion targetAnchorLocalRotation,
            float targetBlend)
        {
            var target = enableWeaponCollisionAvoidance ? Mathf.Clamp01(targetBlend) : 0f;
            if (target > wallAvoidBlend)
            {
                wallAvoidBlend = Mathf.MoveTowards(
                    wallAvoidBlend,
                    target,
                    Time.deltaTime / Mathf.Max(0.01f, wallAvoidSmoothTime));
            }
            else
            {
                wallAvoidBlend = Mathf.SmoothDamp(
                    wallAvoidBlend,
                    target,
                    ref wallAvoidBlendVelocity,
                    Mathf.Max(0.01f, wallAvoidSmoothTime));
            }

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
            if (!enableWeaponCollisionAvoidance ||
                (localPlayerWallAvoidanceOnly && useNetworkState) ||
                cameraPivot == null)
            {
                return 0f;
            }

            return wallCheckMode == WeaponWallCheckMode.CameraRay
                ? ComputeCameraRayWallAvoidBlend()
                : ComputeWeaponCapsuleWallAvoidBlend();
        }

        private float ComputeCameraRayWallAvoidBlend()
        {
            var direction = cameraPivot.forward;
            var distance = Mathf.Max(0.05f, wallCheckDistance);
            var layerMask = GetWallTraceLayerMask();
            if (layerMask == 0)
            {
                return 0f;
            }

            var origin = cameraPivot.position;
            var hits = Physics.RaycastAll(
                origin,
                direction,
                distance,
                layerMask,
                QueryTriggerInteraction.Ignore);
            var bestBlend = 0f;
            for (var i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (!IsValidCameraRayWallHit(hit.collider))
                {
                    continue;
                }

                bestBlend = Mathf.Max(bestBlend, ComputeWallAvoidBlend(hit.distance, distance));
            }

            return bestBlend;
        }

        private float ComputeWeaponCapsuleWallAvoidBlend()
        {
            var direction = cameraPivot.forward;
            var distance = Mathf.Max(0.05f, wallCheckDistance);
            var radius = Mathf.Max(0.01f, wallCheckRadius);
            var layerMask = GetWallTraceLayerMask();
            if (layerMask == 0)
            {
                return 0f;
            }

            GetWeaponTraceCapsule(out var capsuleStart, out var capsuleEnd, direction);
            var castDistance = distance + radius;
            var bestBlend = 0f;

            var castHits = Physics.CapsuleCastAll(
                capsuleStart,
                capsuleEnd,
                radius,
                direction,
                castDistance,
                layerMask,
                QueryTriggerInteraction.Ignore);
            for (var i = 0; i < castHits.Length; i++)
            {
                var hit = castHits[i];
                if (!IsValidWeaponTraceHit(hit.collider))
                {
                    continue;
                }

                var surfaceDistance = Mathf.Max(0f, hit.distance - radius);
                bestBlend = Mathf.Max(bestBlend, ComputeWallAvoidBlend(surfaceDistance, distance));
            }

            bestBlend = Mathf.Max(
                bestBlend,
                ComputeOverlapCapsuleWallBlend(capsuleStart, capsuleEnd, radius, direction));
            return bestBlend;
        }

        private int GetWallTraceLayerMask()
        {
            if (wallCheckMode == WeaponWallCheckMode.CameraRay)
            {
                return wallCheckMask.value != 0 ? wallCheckMask.value : Physics.DefaultRaycastLayers;
            }

            if (useWeaponBlockLayerOnly && WeaponBlockLayers.IsConfigured)
            {
                return WeaponBlockLayers.Mask;
            }

            return wallCheckMask.value != 0 ? wallCheckMask.value : WeaponBlockLayers.Mask;
        }

        private void GetWeaponTraceCapsule(out Vector3 capsuleStart, out Vector3 capsuleEnd, Vector3 fallbackDirection)
        {
            capsuleStart = cameraPivot.position;
            capsuleEnd = GetMuzzleWorldPosition(fallbackDirection);

            var minimumLength = 0.12f;
            if ((capsuleEnd - capsuleStart).sqrMagnitude < minimumLength * minimumLength)
            {
                var direction = fallbackDirection.sqrMagnitude > 0.0001f
                    ? fallbackDirection.normalized
                    : Vector3.forward;
                capsuleEnd = capsuleStart + direction * minimumLength;
            }
        }

        private Vector3 GetMuzzleWorldPosition(Vector3 fallbackDirection)
        {
            if (muzzleTransform == null && weaponInstance != null)
            {
                muzzleTransform = FindChildRecursive(weaponInstance.transform, muzzleTargetName);
                if (muzzleTransform == null)
                {
                    muzzleTransform = FindChildRecursive(weaponInstance.transform, "Muzzle");
                }
            }

            if (muzzleTransform != null)
            {
                return muzzleTransform.position;
            }

            return GetWeaponWallCheckPoint() + fallbackDirection.normalized * 0.45f;
        }

        private float ComputeOverlapCapsuleWallBlend(
            Vector3 capsuleStart,
            Vector3 capsuleEnd,
            float radius,
            Vector3 castDirection)
        {
            var layerMask = GetWallTraceLayerMask();
            if (layerMask == 0)
            {
                return 0f;
            }

            var hits = Physics.OverlapCapsule(
                capsuleStart,
                capsuleEnd,
                radius,
                layerMask,
                QueryTriggerInteraction.Ignore);
            var bestBlend = 0f;
            var probeCenter = (capsuleStart + capsuleEnd) * 0.5f;

            for (var i = 0; i < hits.Length; i++)
            {
                var hitCollider = hits[i];
                if (!IsValidWeaponTraceHit(hitCollider))
                {
                    continue;
                }

                if (TryGetWallProbePenetration(hitCollider, probeCenter, radius, castDirection, out var penetration))
                {
                    bestBlend = Mathf.Max(bestBlend, Mathf.Clamp01(penetration / radius));
                }
            }

            if (bestBlend <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01(Mathf.Max(Mathf.Clamp01(wallAvoidMinBlendOnHit), bestBlend));
        }

        private bool IsValidCameraRayWallHit(Collider collider)
        {
            return collider != null &&
                   collider.enabled &&
                   !collider.isTrigger &&
                   !IsOwnedCollider(collider) &&
                   !IsCharacterCollider(collider);
        }

        private bool IsValidWeaponTraceHit(Collider collider)
        {
            return collider != null &&
                   !IsOwnedCollider(collider) &&
                   IsWeaponTraceCollider(collider);
        }

        private static bool IsCharacterCollider(Collider collider)
        {
            if (collider == null)
            {
                return false;
            }

            if (collider.GetComponentInParent<CharacterController>(true) != null ||
                collider.GetComponentInParent<FpsCharacterController>(true) != null ||
                collider.GetComponentInParent<PlayerBoneHitboxRig>(true) != null)
            {
                return true;
            }

            var root = collider.transform.root;
            if (root == null)
            {
                return false;
            }

            var rootName = root.name;
            return rootName.IndexOf("Player", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rootName.IndexOf("Remote_", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsWeaponTraceCollider(Collider collider)
        {
            if (collider == null || !collider.enabled || collider.isTrigger)
            {
                return false;
            }

            if (collider is MeshCollider)
            {
                return false;
            }

            return collider is BoxCollider or SphereCollider or CapsuleCollider;
        }

        private Vector3 GetWeaponWallCheckPoint()
        {
            if (cameraPivot == null)
            {
                return transform.position;
            }

            if (weaponParent == null || weaponParent.parent == null)
            {
                return cameraPivot.position + cameraPivot.forward * hipAnchorLocalPosition.z;
            }

            var parent = weaponParent.parent;
            var hipWorld = parent.TransformPoint(hipAnchorLocalPosition);
            if (adsBlend <= 0.001f)
            {
                return hipWorld;
            }

            var adsWorld = parent.TransformPoint(adsAnchorLocalPosition);
            return Vector3.Lerp(hipWorld, adsWorld, adsBlend);
        }

        private static bool TryGetWallProbePenetration(
            Collider collider,
            Vector3 center,
            float radius,
            Vector3 castDirection,
            out float penetration)
        {
            penetration = 0f;
            if (collider == null || !collider.enabled || !IsWeaponTraceCollider(collider))
            {
                return false;
            }

            var closest = collider.ClosestPoint(center);
            penetration = radius - Vector3.Distance(center, closest);
            if (penetration > 0.001f)
            {
                return true;
            }

            if (!collider.bounds.Contains(center))
            {
                return false;
            }

            penetration = radius;
            return true;
        }

        private float ComputeWallAvoidBlend(float surfaceDistance, float maxDistance)
        {
            var normalized = 1f - Mathf.Clamp01(surfaceDistance / maxDistance);
            normalized = Mathf.Pow(Mathf.Clamp01(normalized), Mathf.Max(0.01f, wallAvoidResponsePower));
            return Mathf.Clamp01(Mathf.Max(Mathf.Clamp01(wallAvoidMinBlendOnHit), normalized));
        }

        private bool IsOwnedCollider(Collider collider)
        {
            return collider != null && collider.transform.IsChildOf(transform);
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

        private void UpdateAdsCameraZoom()
        {
            if (useNetworkState || !enableAdsCameraZoom)
            {
                return;
            }

            if (localPlayerCamera == null)
            {
                if (cameraPivot != null)
                {
                    localPlayerCamera = cameraPivot.GetComponentInChildren<Camera>(true);
                }

                if (localPlayerCamera == null)
                {
                    localPlayerCamera = GetComponentInChildren<Camera>(true);
                }
            }

            if (localPlayerCamera == null)
            {
                return;
            }

            if (baseCameraFov <= 0f)
            {
                baseCameraFov = localPlayerCamera.fieldOfView;
            }

            var targetAdsFov = Mathf.Clamp(adsCameraFov, 20f, 179f);
            var targetFov = Mathf.Lerp(baseCameraFov, targetAdsFov, Mathf.Clamp01(adsBlend));
            localPlayerCamera.fieldOfView = Mathf.SmoothDamp(
                localPlayerCamera.fieldOfView,
                targetFov,
                ref adsCameraFovVelocity,
                Mathf.Max(0.01f, adsCameraZoomSmoothTime));
        }
    }
}
