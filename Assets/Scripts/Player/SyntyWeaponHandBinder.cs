using System.Collections.Generic;
using UnityEngine;

namespace ShooterPrototype.Player
{
    [DefaultExecutionOrder(600)]
    public sealed class SyntyWeaponHandBinder : MonoBehaviour
    {
        private struct LimbRestPose
        {
            public Quaternion upperLocal;
            public Quaternion lowerLocal;
            public Quaternion handLocal;
        }

        private struct BoneRestPose
        {
            public Transform bone;
            public Quaternion localRotation;
            public Vector3 localPosition;
            public bool restoreLocalPosition;
        }

        private static readonly string[] UpperBodyStabilizerBoneNames =
        {
            "Hips",
            "Spine_01",
            "Spine_02",
            "Spine_03",
            "Chest",
            "UpperChest",
            "Clavicle_L",
            "Clavicle_R"
        };

        [Header("Bones")]
        [SerializeField] private Transform leftShoulderBone;
        [SerializeField] private Transform rightShoulderBone;
        [SerializeField] private Transform leftUpperArm;
        [SerializeField] private Transform leftLowerArm;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightUpperArm;
        [SerializeField] private Transform rightLowerArm;
        [SerializeField] private Transform rightHand;

        [Header("Targets")]
        [SerializeField] private Transform leftGripTarget;
        [SerializeField] private Transform rightGripTarget;

        [Header("Detached FP Shoulders")]
        [SerializeField] private bool useDetachedShoulderAnchorsInFirstPerson = true;
        [SerializeField] private Transform leftShoulderAnchor;
        [SerializeField] private Transform rightShoulderAnchor;

        [Header("IK")]
        [SerializeField] private bool onlyApplyInLocalFirstPerson = true;
        [SerializeField] private bool lockUpperBodyBeforeIk = false;
        [SerializeField] private bool resetArmPoseBeforeIk = false;
        [SerializeField] private bool applyIkBeforeRender = false;
        [SerializeField] private float handIkWeight = 1f;
        [SerializeField] private bool applyLeftHand = true;
        [SerializeField] private bool applyRightHand = true;

        private PlayerWeaponMount weaponMount;
        private PlayerViewPresentation viewPresentation;
        private SyntyFirstPersonArmLocomotionGate animatorGate;
        private LimbRestPose leftRestPose;
        private LimbRestPose rightRestPose;
        private readonly List<BoneRestPose> upperBodyRestPoses = new List<BoneRestPose>(8);
        private bool hasRestPose;
        private int lastIkAppliedFrame = -1;

        public void ConfigureFromVisualRoot(Transform syntyRoot)
        {
            if (syntyRoot == null)
            {
                return;
            }

            leftShoulderBone = leftShoulderBone != null
                ? leftShoulderBone
                : FindBone(syntyRoot, "Clavicle_L", "Shoulder_L", "mixamorig:LeftShoulder");
            rightShoulderBone = rightShoulderBone != null
                ? rightShoulderBone
                : FindBone(syntyRoot, "Clavicle_R", "Shoulder_R", "mixamorig:RightShoulder");
            leftUpperArm = leftUpperArm != null
                ? leftUpperArm
                : FindBone(syntyRoot, "Shoulder_L", "UpperArm_L", "mixamorig:LeftArm");
            leftLowerArm = leftLowerArm != null
                ? leftLowerArm
                : FindBone(syntyRoot, "Elbow_L", "LowerArm_L", "mixamorig:LeftForeArm");
            leftHand = leftHand != null ? leftHand : FindBone(syntyRoot, "Hand_L", "mixamorig:LeftHand");
            rightUpperArm = rightUpperArm != null
                ? rightUpperArm
                : FindBone(syntyRoot, "Shoulder_R", "UpperArm_R", "mixamorig:RightArm");
            rightLowerArm = rightLowerArm != null
                ? rightLowerArm
                : FindBone(syntyRoot, "Elbow_R", "LowerArm_R", "mixamorig:RightForeArm");
            rightHand = rightHand != null ? rightHand : FindBone(syntyRoot, "Hand_R", "mixamorig:RightHand");
            ResolveUpperBodyStabilizerBones(syntyRoot);
            CaptureRestPosesFromPrefabState();
        }

        public void ConfigureShoulderAnchors(Transform leftAnchor, Transform rightAnchor)
        {
            leftShoulderAnchor = leftAnchor;
            rightShoulderAnchor = rightAnchor;
        }

        public void SetDetachedShoulderAnchorsEnabled(bool enabled)
        {
            useDetachedShoulderAnchorsInFirstPerson = enabled;
        }

        public void SetGripTargets(Transform leftTarget, Transform rightTarget)
        {
            leftGripTarget = leftTarget;
            rightGripTarget = rightTarget;
        }

        public void SetHandIkEnabled(bool enabled)
        {
            if (enabled)
            {
                handIkWeight = 1f;
                applyLeftHand = true;
                applyRightHand = true;
                return;
            }

            handIkWeight = 0f;
            applyLeftHand = false;
            applyRightHand = false;
        }

        private void Awake()
        {
            weaponMount = GetComponent<PlayerWeaponMount>();
            viewPresentation = GetComponent<PlayerViewPresentation>();
        }

        private void Start()
        {
            if (upperBodyRestPoses.Count == 0)
            {
                var syntyRoot = leftUpperArm != null ? leftUpperArm.root : null;
                if (syntyRoot != null)
                {
                    ResolveUpperBodyStabilizerBones(syntyRoot);
                }
            }

            CaptureRestPosesFromPrefabState();
            ApplyDetachedShoulderIkDefaults();
            RefreshBeforeRenderSubscription();
        }

        private void ApplyDetachedShoulderIkDefaults()
        {
            if (!useDetachedShoulderAnchorsInFirstPerson)
            {
                return;
            }

            resetArmPoseBeforeIk = false;
            applyIkBeforeRender = false;
        }

        private void OnEnable()
        {
            ApplyDetachedShoulderIkDefaults();
            RefreshBeforeRenderSubscription();
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= HandleBeforeRender;
            weaponMount?.SetFirstPersonRigidHandIk(false);
        }

        private void RefreshBeforeRenderSubscription()
        {
            Application.onBeforeRender -= HandleBeforeRender;
            if (applyIkBeforeRender)
            {
                Application.onBeforeRender += HandleBeforeRender;
            }
        }

        private void LateUpdate()
        {
            if (applyIkBeforeRender)
            {
                return;
            }

            ApplyWeaponHandIkIfNeeded();
        }

        private void HandleBeforeRender()
        {
            ApplyWeaponHandIkIfNeeded();
        }

        private void ApplyWeaponHandIkIfNeeded()
        {
            if (Time.frameCount == lastIkAppliedFrame)
            {
                return;
            }

            lastIkAppliedFrame = Time.frameCount;
            ApplyWeaponHandIk();
        }

        public void SyncFirstPersonRigidHandIkMode()
        {
            if (weaponMount == null)
            {
                weaponMount = GetComponent<PlayerWeaponMount>();
            }

            if (weaponMount == null)
            {
                return;
            }

            if (ShouldApplyWeaponHandIk())
            {
                ResolveGripTargets();
                ResolveShoulderAnchors();
            }

            var rigid = ShouldApplyWeaponHandIk() &&
                        useDetachedShoulderAnchorsInFirstPerson &&
                        leftShoulderAnchor != null &&
                        rightShoulderAnchor != null &&
                        leftGripTarget != null &&
                        rightGripTarget != null;
            weaponMount.SetFirstPersonRigidHandIk(rigid);
        }

        private void ApplyWeaponHandIk()
        {
            if (!ShouldApplyWeaponHandIk())
            {
                return;
            }

            ResolveGripTargets();
            if (leftGripTarget == null || rightGripTarget == null)
            {
                return;
            }

            ResolveShoulderAnchors();
            var useDetachedShoulders = useDetachedShoulderAnchorsInFirstPerson &&
                                       leftShoulderAnchor != null &&
                                       rightShoulderAnchor != null;

            if (useDetachedShoulders)
            {
                ApplyDetachedShoulderAnchor(leftShoulderAnchor, leftShoulderBone, leftUpperArm);
                ApplyDetachedShoulderAnchor(rightShoulderAnchor, rightShoulderBone, rightUpperArm);
            }
            else if (ShouldStabilizeBonesBeforeIk())
            {
                if (lockUpperBodyBeforeIk && hasRestPose)
                {
                    ApplyUpperBodyRestPose();
                }

                if (resetArmPoseBeforeIk && hasRestPose)
                {
                    ApplyLimbRestPose(leftUpperArm, leftLowerArm, leftHand, leftRestPose);
                    ApplyLimbRestPose(rightUpperArm, rightLowerArm, rightHand, rightRestPose);
                }
            }
            else if (resetArmPoseBeforeIk && hasRestPose)
            {
                ApplyPartialLimbRestPose(leftLowerArm, leftHand, leftRestPose);
                ApplyPartialLimbRestPose(rightLowerArm, rightHand, rightRestPose);
            }

            var weight = Mathf.Clamp01(handIkWeight);
            if (applyLeftHand)
            {
                SolveTwoBoneIk(
                    leftUpperArm,
                    leftLowerArm,
                    leftHand,
                    leftGripTarget.position,
                    leftGripTarget.rotation,
                    weight);
            }

            if (applyRightHand)
            {
                SolveTwoBoneIk(
                    rightUpperArm,
                    rightLowerArm,
                    rightHand,
                    rightGripTarget.position,
                    rightGripTarget.rotation,
                    weight);
            }
        }

        private bool ShouldApplyWeaponHandIk()
        {
            if (handIkWeight <= 0.0001f || (!applyLeftHand && !applyRightHand))
            {
                return false;
            }

            if (!onlyApplyInLocalFirstPerson)
            {
                return true;
            }

            if (viewPresentation == null)
            {
                viewPresentation = GetComponent<PlayerViewPresentation>();
            }

            return viewPresentation != null && viewPresentation.IsLocalPlayerView;
        }

        private bool ShouldStabilizeBonesBeforeIk()
        {
            if (animatorGate == null)
            {
                animatorGate = GetComponent<SyntyFirstPersonArmLocomotionGate>();
            }

            if (animatorGate != null && animatorGate.IsSkeletonAnimatorFrozen)
            {
                return false;
            }

            var animator = GetComponentInChildren<Animator>(true);
            return animator != null && animator.enabled;
        }

        private void ResolveUpperBodyStabilizerBones(Transform syntyRoot)
        {
            upperBodyRestPoses.Clear();
            if (syntyRoot == null)
            {
                return;
            }

            var found = new List<Transform>(UpperBodyStabilizerBoneNames.Length);
            for (var i = 0; i < UpperBodyStabilizerBoneNames.Length; i++)
            {
                var bone = FindBone(syntyRoot, UpperBodyStabilizerBoneNames[i]);
                if (bone == null || found.Contains(bone))
                {
                    continue;
                }

                found.Add(bone);
            }

            found.Sort((a, b) => GetBoneDepth(a, syntyRoot).CompareTo(GetBoneDepth(b, syntyRoot)));
            for (var i = 0; i < found.Count; i++)
            {
                var bone = found[i];
                var restoreLocalPosition = string.Equals(bone.name, "Hips", System.StringComparison.Ordinal);
                upperBodyRestPoses.Add(new BoneRestPose
                {
                    bone = bone,
                    localRotation = bone.localRotation,
                    localPosition = bone.localPosition,
                    restoreLocalPosition = restoreLocalPosition
                });
            }
        }

        private void CaptureRestPosesFromPrefabState()
        {
            if (leftUpperArm == null || rightUpperArm == null)
            {
                return;
            }

            var animator = GetComponentInChildren<Animator>(true);
            var animatorWasEnabled = animator != null && animator.enabled;
            if (animator != null)
            {
                animator.enabled = false;
            }

            for (var i = 0; i < upperBodyRestPoses.Count; i++)
            {
                var entry = upperBodyRestPoses[i];
                if (entry.bone == null)
                {
                    continue;
                }

                entry.localRotation = entry.bone.localRotation;
                if (entry.restoreLocalPosition)
                {
                    entry.localPosition = entry.bone.localPosition;
                }

                upperBodyRestPoses[i] = entry;
            }

            leftRestPose = CaptureLimbRestPose(leftUpperArm, leftLowerArm, leftHand);
            rightRestPose = CaptureLimbRestPose(rightUpperArm, rightLowerArm, rightHand);
            hasRestPose = true;

            if (animator != null)
            {
                animator.enabled = animatorWasEnabled;
            }
        }

        private void ResolveShoulderAnchors()
        {
            if (weaponMount == null)
            {
                weaponMount = GetComponent<PlayerWeaponMount>();
            }

            var weaponRoot = weaponMount != null ? weaponMount.MountedWeaponRoot : null;
            if (weaponRoot != null)
            {
                var onWeaponLeft = FindBone(weaponRoot, "LeftShoulderIkAnchor");
                var onWeaponRight = FindBone(weaponRoot, "RightShoulderIkAnchor");
                if (onWeaponLeft != null)
                {
                    leftShoulderAnchor = onWeaponLeft;
                }

                if (onWeaponRight != null)
                {
                    rightShoulderAnchor = onWeaponRight;
                }

                if (leftShoulderAnchor != null && rightShoulderAnchor != null)
                {
                    return;
                }
            }

            if (leftShoulderAnchor != null && rightShoulderAnchor != null)
            {
                return;
            }

            if (viewPresentation == null)
            {
                viewPresentation = GetComponent<PlayerViewPresentation>();
            }

            Transform searchRoot = null;
            if (viewPresentation != null && viewPresentation.FirstPersonViewRoot != null)
            {
                searchRoot = viewPresentation.FirstPersonViewRoot.transform;
            }
            else
            {
                var cameraPivot = transform.Find("CameraPivot");
                searchRoot = cameraPivot != null ? cameraPivot.Find("FirstPersonView") : null;
            }

            if (searchRoot == null)
            {
                return;
            }

            var weaponAnchor = searchRoot.Find("WeaponAnchor");

            if (leftShoulderAnchor == null)
            {
                leftShoulderAnchor = FindAnchor(searchRoot, weaponAnchor, weaponMount, "LeftShoulderIkAnchor");
            }

            if (rightShoulderAnchor == null)
            {
                rightShoulderAnchor = FindAnchor(searchRoot, weaponAnchor, weaponMount, "RightShoulderIkAnchor");
            }
        }

        private Transform FindAnchor(
            Transform firstPersonView,
            Transform weaponAnchor,
            PlayerWeaponMount mount,
            string anchorName)
        {
            var weaponRoot = mount != null ? mount.MountedWeaponRoot : null;
            if (weaponRoot != null)
            {
                var onWeaponRoot = FindBone(weaponRoot, anchorName);
                if (onWeaponRoot != null)
                {
                    return onWeaponRoot;
                }
            }

            return FindAnchor(firstPersonView, weaponAnchor, anchorName);
        }

        private static Transform FindAnchor(Transform firstPersonView, Transform weaponAnchor, string anchorName)
        {
            if (weaponAnchor != null)
            {
                var onWeaponAnchor = weaponAnchor.Find(anchorName);
                if (onWeaponAnchor != null)
                {
                    return onWeaponAnchor;
                }
            }

            return firstPersonView != null ? firstPersonView.Find(anchorName) : null;
        }

        private static void ApplyDetachedShoulderAnchor(
            Transform anchor,
            Transform shoulderBone,
            Transform upperArm)
        {
            if (anchor == null)
            {
                return;
            }

            if (shoulderBone != null)
            {
                shoulderBone.position = anchor.position;
                shoulderBone.rotation = anchor.rotation;
                return;
            }

            if (upperArm != null)
            {
                upperArm.position = anchor.position;
                upperArm.rotation = anchor.rotation;
            }
        }

        private void ApplyUpperBodyRestPose()
        {
            for (var i = 0; i < upperBodyRestPoses.Count; i++)
            {
                var entry = upperBodyRestPoses[i];
                if (entry.bone == null)
                {
                    continue;
                }

                if (entry.restoreLocalPosition)
                {
                    entry.bone.localPosition = entry.localPosition;
                }

                entry.bone.localRotation = entry.localRotation;
            }
        }

        private static int GetBoneDepth(Transform bone, Transform root)
        {
            if (bone == null)
            {
                return 0;
            }

            var depth = 0;
            var current = bone;
            while (current != null && current != root)
            {
                depth++;
                current = current.parent;
            }

            return depth;
        }

        private static LimbRestPose CaptureLimbRestPose(Transform upper, Transform lower, Transform hand)
        {
            return new LimbRestPose
            {
                upperLocal = upper != null ? upper.localRotation : Quaternion.identity,
                lowerLocal = lower != null ? lower.localRotation : Quaternion.identity,
                handLocal = hand != null ? hand.localRotation : Quaternion.identity
            };
        }

        private static void ApplyPartialLimbRestPose(Transform lower, Transform hand, LimbRestPose restPose)
        {
            if (lower != null)
            {
                lower.localRotation = restPose.lowerLocal;
            }

            if (hand != null)
            {
                hand.localRotation = restPose.handLocal;
            }
        }

        private static void ApplyLimbRestPose(
            Transform upper,
            Transform lower,
            Transform hand,
            LimbRestPose restPose)
        {
            if (upper != null)
            {
                upper.localRotation = restPose.upperLocal;
            }

            if (lower != null)
            {
                lower.localRotation = restPose.lowerLocal;
            }

            if (hand != null)
            {
                hand.localRotation = restPose.handLocal;
            }
        }

        private void ResolveGripTargets()
        {
            if (leftGripTarget != null && rightGripTarget != null)
            {
                return;
            }

            if (weaponMount == null)
            {
                weaponMount = GetComponent<PlayerWeaponMount>();
            }

            var weaponRoot = weaponMount != null ? weaponMount.MountedWeaponRoot : null;
            if (weaponRoot == null)
            {
                return;
            }

            if (leftGripTarget == null)
            {
                leftGripTarget = FindBone(weaponRoot, "LeftHandTarget");
            }

            if (rightGripTarget == null)
            {
                rightGripTarget = FindBone(weaponRoot, "RightHandTarget");
            }
        }

        private static Transform FindBone(Transform root, params string[] boneNames)
        {
            if (root == null || boneNames == null || boneNames.Length == 0)
            {
                return null;
            }

            var all = root.GetComponentsInChildren<Transform>(true);
            for (var n = 0; n < boneNames.Length; n++)
            {
                var boneName = boneNames[n];
                if (string.IsNullOrWhiteSpace(boneName))
                {
                    continue;
                }

                for (var i = 0; i < all.Length; i++)
                {
                    var current = all[i];
                    if (current != null && string.Equals(current.name, boneName, System.StringComparison.Ordinal))
                    {
                        return current;
                    }
                }
            }

            return null;
        }

        private static void SolveTwoBoneIk(
            Transform upper,
            Transform lower,
            Transform hand,
            Vector3 targetPosition,
            Quaternion targetRotation,
            float weight)
        {
            if (upper == null || lower == null || hand == null || weight <= 0.0001f)
            {
                return;
            }

            var upperPos = upper.position;
            var lowerPos = lower.position;
            var handPos = hand.position;
            var upperToLower = (lowerPos - upperPos).magnitude;
            var lowerToHand = (handPos - lowerPos).magnitude;
            var upperToTarget = Vector3.Distance(upperPos, targetPosition);
            var maxReach = upperToLower + lowerToHand - 0.001f;
            var clampedTarget = targetPosition;
            if (upperToTarget > maxReach)
            {
                clampedTarget = upperPos + (targetPosition - upperPos).normalized * maxReach;
            }

            var oldUpperRot = upper.rotation;
            var oldLowerRot = lower.rotation;
            var oldHandRot = hand.rotation;

            var directionToTarget = (clampedTarget - upperPos).normalized;
            if (directionToTarget.sqrMagnitude > 0.0001f)
            {
                var upperForward = (handPos - upperPos).normalized;
                var rotationToTarget = Quaternion.FromToRotation(upperForward, directionToTarget);
                upper.rotation = rotationToTarget * upper.rotation;
            }

            lowerPos = lower.position;
            handPos = hand.position;
            var toTargetFromLower = (clampedTarget - lowerPos).normalized;
            if (toTargetFromLower.sqrMagnitude > 0.0001f)
            {
                var lowerForward = (handPos - lowerPos).normalized;
                var lowerRotation = Quaternion.FromToRotation(lowerForward, toTargetFromLower);
                lower.rotation = lowerRotation * lower.rotation;
            }

            hand.rotation = targetRotation;

            upper.rotation = Quaternion.Slerp(oldUpperRot, upper.rotation, weight);
            lower.rotation = Quaternion.Slerp(oldLowerRot, lower.rotation, weight);
            hand.rotation = Quaternion.Slerp(oldHandRot, hand.rotation, weight);
        }
    }
}
