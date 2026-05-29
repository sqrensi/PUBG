using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Left-hand IK for remote third-person players only. Keeps FP hand binding untouched.
    /// </summary>
    [DefaultExecutionOrder(620)]
    public sealed class RemoteLeftHandIkBinder : MonoBehaviour
    {
        [Header("Bones")]
        [SerializeField] private Transform leftShoulder;
        [SerializeField] private Transform leftUpperArm;
        [SerializeField] private Transform leftLowerArm;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform facingRoot;

        [Header("Grip")]
        [SerializeField] private Transform leftGripTarget;
        [SerializeField] private string leftHandTargetName = "RemoteLeftHandTarget";
        [SerializeField] private string leftHandTargetFallbackName = "LeftHandTarget";

        [Header("IK")]
        [SerializeField] private float handIkWeight = 1f;
        [SerializeField] private bool rotateShoulder = true;
        [SerializeField] private int positionSolveIterations = 6;
        [SerializeField] private float maxReachRotationWeight = 0.05f;

        private RemoteWeaponPresentation weaponPresentation;
        private bool hasValidatedArmChain;
        private bool hasBindSegmentLengths;
        private readonly List<Transform> ikChain = new List<Transform>(4);
        private readonly float[] segmentLengths = new float[3];

        public void Configure(Transform syntyVisualRoot, Transform leftTarget)
        {
            leftGripTarget = leftTarget;
            enabled = leftTarget != null;

            if (syntyVisualRoot != null)
            {
                ConfigureFromVisualRoot(syntyVisualRoot);
            }
        }

        public void ConfigureFromVisualRoot(Transform syntyRoot)
        {
            if (syntyRoot == null)
            {
                return;
            }

            facingRoot = facingRoot != null
                ? facingRoot
                : FindBone(syntyRoot, "Hips", "Spine", "mixamorig:Hips") ?? syntyRoot;

            ResolveArmChainFromSyntyRoot(syntyRoot);
            RebuildIkChain();
            CacheSegmentLengths(forceRecache: true);
            hasValidatedArmChain = IsValidArmChain(leftUpperArm, leftLowerArm, leftHand);
        }

        private void Awake()
        {
            weaponPresentation = GetComponent<RemoteWeaponPresentation>();
        }

        private void LateUpdate()
        {
            if (GetComponent<RemoteThirdPersonPlayerBootstrap>() == null)
            {
                return;
            }

            ApplyLeftHandIk();
        }

        private void ApplyLeftHandIk()
        {
            if (handIkWeight <= 0.0001f)
            {
                return;
            }

            EnsureArmChain();
            if (leftUpperArm == null || leftLowerArm == null || leftHand == null || ikChain.Count < 3)
            {
                return;
            }

            ResolveLeftGripTarget();
            if (leftGripTarget == null)
            {
                return;
            }

            EnsureFacingRoot();
            RebuildIkChain();
            CacheSegmentLengths(forceRecache: !hasBindSegmentLengths);

            var targetPosition = leftGripTarget.position;
            var weight = Mathf.Clamp01(handIkWeight);
            var oldHandRot = leftHand.rotation;
            var oldRotations = CaptureChainRotations();

            SolveHandPositionIk(targetPosition);

            var reachError = Vector3.Distance(leftHand.position, targetPosition);
            var rotationWeight = ComputeHandRotationWeight(reachError, weight);
            var targetRotation = leftGripTarget.rotation;
            leftHand.rotation = rotationWeight <= 0.0001f
                ? oldHandRot
                : Quaternion.Slerp(oldHandRot, targetRotation, rotationWeight);

            if (weight < 0.999f)
            {
                RestoreChainRotations(oldRotations, weight);
                leftHand.rotation = Quaternion.Slerp(oldHandRot, leftHand.rotation, weight);
            }
        }

        private void SolveHandPositionIk(Vector3 targetPosition)
        {
            if (rotateShoulder && leftShoulder != null && leftUpperArm != null)
            {
                RotateShoulderTowardTarget(targetPosition);
            }

            var iterations = Mathf.Clamp(positionSolveIterations, 1, 12);
            var poleHint = ComputeElbowPoleHint(targetPosition);
            SolveFabrikChain(targetPosition, poleHint, iterations);
        }

        private void RotateShoulderTowardTarget(Vector3 targetPosition)
        {
            var shoulderPos = leftShoulder.position;
            var toTarget = targetPosition - shoulderPos;
            if (toTarget.sqrMagnitude < 0.0001f)
            {
                return;
            }

            var currentUpperDir = leftUpperArm.position - shoulderPos;
            if (currentUpperDir.sqrMagnitude < 0.0001f)
            {
                return;
            }

            leftShoulder.rotation = Quaternion.FromToRotation(currentUpperDir, toTarget) * leftShoulder.rotation;
        }

        private void SolveFabrikChain(Vector3 targetPosition, Vector3 poleHint, int iterations)
        {
            var jointCount = ikChain.Count;
            if (jointCount < 3)
            {
                return;
            }

            var segmentCount = jointCount - 1;
            var rootPos = ikChain[0].position;
            var positions = new Vector3[jointCount];
            for (var i = 0; i < jointCount; i++)
            {
                positions[i] = ikChain[i].position;
            }

            var totalLength = 0f;
            for (var i = 0; i < segmentCount; i++)
            {
                if (segmentLengths[i] <= 0.0001f)
                {
                    segmentLengths[i] = Vector3.Distance(positions[i], positions[i + 1]);
                }

                totalLength += segmentLengths[i];
            }

            var rootToTarget = targetPosition - rootPos;
            var targetDistance = rootToTarget.magnitude;
            var reachableTarget = targetPosition;
            if (targetDistance > totalLength - 0.001f && targetDistance > 0.0001f)
            {
                reachableTarget = rootPos + rootToTarget.normalized * (totalLength - 0.001f);
            }

            for (var iteration = 0; iteration < iterations; iteration++)
            {
                positions[jointCount - 1] = reachableTarget;

                for (var i = jointCount - 2; i >= 0; i--)
                {
                    var direction = positions[i] - positions[i + 1];
                    if (direction.sqrMagnitude < 0.0001f)
                    {
                        direction = i > 0
                            ? positions[i] - positions[i - 1]
                            : Vector3.up;
                    }

                    positions[i] = positions[i + 1] + direction.normalized * segmentLengths[i];
                }

                positions[0] = rootPos;

                for (var i = 0; i < jointCount - 1; i++)
                {
                    var direction = positions[i + 1] - positions[i];
                    if (direction.sqrMagnitude < 0.0001f)
                    {
                        direction = i < jointCount - 2
                            ? positions[i + 2] - positions[i]
                            : reachableTarget - positions[i];
                    }

                    positions[i + 1] = positions[i] + direction.normalized * segmentLengths[i];
                }

                if (jointCount >= 3)
                {
                    ApplyElbowPoleBias(positions, poleHint, rootPos, reachableTarget, jointCount);
                }
            }

            ApplyChainRotations(positions);
        }

        private void ApplyChainRotations(Vector3[] targetPositions)
        {
            for (var i = 0; i < ikChain.Count - 1; i++)
            {
                var bone = ikChain[i];
                var currentDirection = ikChain[i + 1].position - bone.position;
                var desiredDirection = targetPositions[i + 1] - targetPositions[i];
                if (currentDirection.sqrMagnitude < 0.0001f || desiredDirection.sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                bone.rotation = Quaternion.FromToRotation(currentDirection, desiredDirection) * bone.rotation;
            }
        }

        private static void ApplyElbowPoleBias(
            Vector3[] positions,
            Vector3 poleHint,
            Vector3 rootPos,
            Vector3 targetPos,
            int jointCount)
        {
            if (positions.Length < 3)
            {
                return;
            }

            var rootToTarget = targetPos - rootPos;
            if (rootToTarget.sqrMagnitude < 0.0001f)
            {
                return;
            }

            var planeNormal = Vector3.Cross(rootToTarget, poleHint - rootPos);
            if (planeNormal.sqrMagnitude < 0.0001f)
            {
                return;
            }

            planeNormal.Normalize();
            var elbowIndex = jointCount >= 4 ? 2 : 1;
            var elbowOffset = positions[elbowIndex] - rootPos;
            var projectedElbow = rootPos + Vector3.ProjectOnPlane(elbowOffset, planeNormal);
            if ((projectedElbow - rootPos).sqrMagnitude > 0.0001f)
            {
                var desiredOffset = (poleHint - rootPos);
                desiredOffset = Vector3.ProjectOnPlane(desiredOffset, rootToTarget.normalized);
                if (desiredOffset.sqrMagnitude > 0.0001f)
                {
                    var currentOffset = projectedElbow - rootPos;
                    var blendedOffset = Vector3.Slerp(
                        currentOffset.normalized,
                        desiredOffset.normalized,
                        0.35f);
                    var elbowDistance = elbowOffset.magnitude;
                    positions[elbowIndex] = rootPos + blendedOffset.normalized * elbowDistance;
                }
            }
        }

        private Quaternion[] CaptureChainRotations()
        {
            var rotations = new Quaternion[ikChain.Count];
            for (var i = 0; i < ikChain.Count; i++)
            {
                rotations[i] = ikChain[i].rotation;
            }

            return rotations;
        }

        private void RestoreChainRotations(Quaternion[] rotations, float weight)
        {
            for (var i = 0; i < ikChain.Count; i++)
            {
                ikChain[i].rotation = Quaternion.Slerp(rotations[i], ikChain[i].rotation, weight);
            }
        }

        private float ComputeHandRotationWeight(float reachError, float weight)
        {
            if (weight <= 0.0001f)
            {
                return 0f;
            }

            var reachLimit = Mathf.Max(0.01f, maxReachRotationWeight);
            if (reachError <= reachLimit)
            {
                return weight;
            }

            var fadeRange = 0.12f;
            var reachBlend = 1f - Mathf.Clamp01((reachError - reachLimit) / fadeRange);
            return weight * reachBlend;
        }

        private void EnsureArmChain()
        {
            if (hasValidatedArmChain &&
                leftShoulder != null &&
                IsValidArmChain(leftUpperArm, leftLowerArm, leftHand))
            {
                return;
            }

            var thirdPersonBody = transform.Find("ThirdPersonBody");
            var syntyVisual = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
            if (syntyVisual == null)
            {
                return;
            }

            ResolveArmChainFromSyntyRoot(syntyVisual);
            hasValidatedArmChain = leftShoulder != null &&
                                   IsValidArmChain(leftUpperArm, leftLowerArm, leftHand);
        }

        private void RebuildIkChain()
        {
            ikChain.Clear();

            if (rotateShoulder &&
                leftShoulder != null &&
                leftUpperArm != null &&
                leftLowerArm != null &&
                leftHand != null &&
                leftUpperArm.IsChildOf(leftShoulder))
            {
                ikChain.Add(leftShoulder);
            }

            if (leftUpperArm != null)
            {
                ikChain.Add(leftUpperArm);
            }

            if (leftLowerArm != null)
            {
                ikChain.Add(leftLowerArm);
            }

            if (leftHand != null)
            {
                ikChain.Add(leftHand);
            }
        }

        private void CacheSegmentLengths(bool forceRecache = false)
        {
            if (hasBindSegmentLengths && !forceRecache)
            {
                return;
            }

            for (var i = 0; i < segmentLengths.Length; i++)
            {
                segmentLengths[i] = 0f;
            }

            for (var i = 0; i < ikChain.Count - 1 && i < segmentLengths.Length; i++)
            {
                segmentLengths[i] = Vector3.Distance(ikChain[i].position, ikChain[i + 1].position);
            }

            hasBindSegmentLengths = ikChain.Count >= 3;
        }

        private static bool IsValidArmChain(Transform upper, Transform lower, Transform hand)
        {
            if (upper == null || lower == null || hand == null)
            {
                return false;
            }

            if (lower.parent != upper || hand.parent != lower)
            {
                return false;
            }

            if (string.Equals(upper.name, "UpperArm_L", StringComparison.Ordinal) ||
                string.Equals(upper.name, "LowerArm_L", StringComparison.Ordinal))
            {
                return true;
            }

            return !string.Equals(upper.name, "Shoulder_L", StringComparison.Ordinal) &&
                   !string.Equals(upper.name, "Clavicle_L", StringComparison.Ordinal);
        }

        private void ResolveArmChainFromSyntyRoot(Transform syntyRoot)
        {
            var hand = FindBone(syntyRoot, "Hand_L", "mixamorig:LeftHand");
            var lower = FindBone(syntyRoot, "LowerArm_L", "Elbow_L", "mixamorig:LeftForeArm");
            var upper = FindBone(syntyRoot, "UpperArm_L", "mixamorig:LeftArm");

            if (hand == null)
            {
                return;
            }

            if (lower == null)
            {
                lower = hand.parent;
            }

            if (upper == null && lower != null)
            {
                upper = lower.parent;
            }

            var explicitUpperArm = FindBone(syntyRoot, "UpperArm_L");
            if (explicitUpperArm != null &&
                lower != null &&
                (lower.parent == explicitUpperArm || lower.IsChildOf(explicitUpperArm)))
            {
                upper = explicitUpperArm;
            }

            if (lower != null && hand.parent != lower)
            {
                lower = hand.parent;
            }

            if (upper != null && lower != null && lower.parent != upper)
            {
                upper = lower.parent;
            }

            if (upper != null &&
                (string.Equals(upper.name, "Shoulder_L", StringComparison.Ordinal) ||
                 string.Equals(upper.name, "Clavicle_L", StringComparison.Ordinal)))
            {
                var upperArm = FindBone(syntyRoot, "UpperArm_L");
                if (upperArm != null && lower != null && lower.IsChildOf(upperArm))
                {
                    upper = upperArm;
                }
            }

            leftHand = hand;
            leftLowerArm = lower;
            leftUpperArm = upper;
            leftShoulder = FindBone(syntyRoot, "Shoulder_L") ?? ResolveShoulderBone(upper);
        }

        private static Transform ResolveShoulderBone(Transform upperArm)
        {
            if (upperArm == null || upperArm.parent == null)
            {
                return null;
            }

            var parent = upperArm.parent;
            if (string.Equals(parent.name, "Shoulder_L", StringComparison.Ordinal))
            {
                return parent;
            }

            if (string.Equals(parent.name, "Clavicle_L", StringComparison.Ordinal))
            {
                var shoulder = parent.Find("Shoulder_L");
                return shoulder != null ? shoulder : parent;
            }

            return null;
        }

        private Vector3 ComputeElbowPoleHint(Vector3 gripPosition)
        {
            var root = ikChain.Count > 0 ? ikChain[0].position : leftUpperArm.position;
            var shoulderToGrip = gripPosition - root;
            if (shoulderToGrip.sqrMagnitude < 0.0001f)
            {
                return root + ResolveCharacterLeft() * 0.35f;
            }

            var characterLeft = ResolveCharacterLeft();
            if (characterLeft.sqrMagnitude > 0.0001f)
            {
                return root + characterLeft.normalized * 0.35f;
            }

            return root + Vector3.left * 0.35f;
        }

        private Vector3 ResolveCharacterLeft()
        {
            if (facingRoot != null)
            {
                return -facingRoot.right;
            }

            return -transform.right;
        }

        private void EnsureFacingRoot()
        {
            if (facingRoot != null)
            {
                return;
            }

            var thirdPersonBody = transform.Find("ThirdPersonBody");
            var syntyVisual = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
            if (syntyVisual == null)
            {
                return;
            }

            ConfigureFromVisualRoot(syntyVisual);
        }

        private void ResolveLeftGripTarget()
        {
            var weaponRoot = weaponPresentation != null ? weaponPresentation.WeaponRoot : null;
            if (weaponRoot == null)
            {
                return;
            }

            var resolved = PlayerWeaponMount.FindWeaponGripAnchor(weaponRoot, leftHandTargetName)
                ?? PlayerWeaponMount.FindWeaponGripAnchor(weaponRoot, leftHandTargetFallbackName);
            if (resolved == null)
            {
                return;
            }

            leftGripTarget = resolved;
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
                    if (current != null &&
                        string.Equals(current.name, boneName, StringComparison.Ordinal))
                    {
                        return current;
                    }
                }
            }

            return null;
        }
    }
}
