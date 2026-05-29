using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Applies network look pitch to remote TP spine and arms instead of tilting the weapon attach target.
    /// </summary>
    [DefaultExecutionOrder(400)]
    public sealed class RemoteLookPitchPosture : MonoBehaviour
    {
        [SerializeField] private Transform syntyRoot;
        [SerializeField] private ProceduralLocomotionRig locomotionRig;

        [Header("Pitch")]
        [SerializeField] private float lookPitchMax = 50f;
        [SerializeField] private float pitchSmoothTime = 0.1f;
        [SerializeField] private float crouchPitchCompensation = 22f;
        [SerializeField] private float crouchMovePitchCompensation = 34f;
        [SerializeField] private float crouchMoveSpeedThreshold = 0.08f;
        [SerializeField] private float crouchPitchSmoothTime = 0.12f;

        [Header("Sprint Lean")]
        [SerializeField] private float sprintPitchDegrees = 48f;
        [SerializeField] private float sprintPitchSmoothTime = 0.14f;

        [Header("Spine")]
        [SerializeField] private float spinePitchShare = 0.5f;
        [SerializeField] private float hipsPitchShare = 0.12f;

        [Header("Arms")]
        [SerializeField] private float claviclePitchShare = 0.22f;
        [SerializeField] private float rightShoulderPitchShare = 0.18f;
        [SerializeField] private float leftShoulderPitchShare = 0.12f;

        private Transform hipsBone;
        private Transform[] spineBones;
        private float[] spineWeights;
        private Transform clavicleLeft;
        private Transform clavicleRight;
        private Transform shoulderLeft;
        private Transform shoulderRight;
        private float smoothedPitch;
        private float pitchVelocity;
        private float smoothedCrouchPitch;
        private float crouchPitchVelocity;
        private float smoothedSprintPitch;
        private float sprintPitchVelocity;

        public float CurrentPostureLeanPitch => smoothedCrouchPitch + smoothedSprintPitch;

        public void Configure(Transform thirdPersonBody, ProceduralLocomotionRig rig)
        {
            locomotionRig = rig;
            syntyRoot = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
            enabled = syntyRoot != null && locomotionRig != null;
            ResolveBones();
        }

        private void Awake()
        {
            if (locomotionRig == null)
            {
                locomotionRig = GetComponentInChildren<ProceduralLocomotionRig>(true);
            }

            if (syntyRoot == null)
            {
                var thirdPersonBody = transform.Find("ThirdPersonBody");
                syntyRoot = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
            }

            ResolveBones();
        }

        private void LateUpdate()
        {
            if (!ShouldApply() || syntyRoot == null || locomotionRig == null)
            {
                return;
            }

            ResolveBones();

            var clampedPitch = Mathf.Clamp(locomotionRig.NetworkLookPitch, -lookPitchMax, lookPitchMax);
            smoothedPitch = Mathf.SmoothDamp(
                smoothedPitch,
                clampedPitch,
                ref pitchVelocity,
                Mathf.Max(0.01f, pitchSmoothTime));

            var targetCrouchPitch = ResolveTargetCrouchPitch();
            smoothedCrouchPitch = Mathf.SmoothDamp(
                smoothedCrouchPitch,
                targetCrouchPitch,
                ref crouchPitchVelocity,
                Mathf.Max(0.01f, crouchPitchSmoothTime));

            var targetSprintPitch = ResolveTargetSprintPitch();
            smoothedSprintPitch = Mathf.SmoothDamp(
                smoothedSprintPitch,
                targetSprintPitch,
                ref sprintPitchVelocity,
                Mathf.Max(0.01f, sprintPitchSmoothTime));

            var totalPitch = smoothedPitch + smoothedCrouchPitch + smoothedSprintPitch;
            if (Mathf.Abs(totalPitch) <= 0.01f)
            {
                return;
            }

            ApplySpinePitch(totalPitch);
            ApplyArmPitch(totalPitch);
        }

        private float ResolveTargetCrouchPitch()
        {
            if (!locomotionRig.NetworkCrouching || locomotionRig.CurrentJumpState != 0)
            {
                return 0f;
            }

            if (IsCrouchMoving())
            {
                return Mathf.Abs(crouchMovePitchCompensation);
            }

            return Mathf.Abs(crouchPitchCompensation);
        }

        private float ResolveTargetSprintPitch()
        {
            if (locomotionRig.CurrentJumpState != 0 ||
                locomotionRig.NetworkCrouching ||
                !locomotionRig.NetworkSprinting ||
                !IsLocomoting())
            {
                return 0f;
            }

            return Mathf.Abs(sprintPitchDegrees);
        }

        private bool IsLocomoting()
        {
            if (locomotionRig.GetNetworkAnimSpeed01() > crouchMoveSpeedThreshold)
            {
                return true;
            }

            var moveX = locomotionRig.NetworkMoveInputX;
            var moveZ = locomotionRig.NetworkMoveInputZ;
            return moveX * moveX + moveZ * moveZ > crouchMoveSpeedThreshold * crouchMoveSpeedThreshold;
        }

        private bool IsCrouchMoving()
        {
            return IsLocomoting();
        }

        private bool ShouldApply()
        {
            return GetComponent<RemoteThirdPersonPlayerBootstrap>() != null;
        }

        private void ApplySpinePitch(float totalPitch)
        {
            if (hipsBone != null && hipsPitchShare > 0.0001f)
            {
                var hipsPitch = totalPitch * hipsPitchShare;
                hipsBone.localRotation = hipsBone.localRotation * Quaternion.Euler(hipsPitch, 0f, 0f);
            }

            if (spineBones == null || spineWeights == null)
            {
                return;
            }

            var spinePitch = totalPitch * spinePitchShare;
            for (var i = 0; i < spineBones.Length; i++)
            {
                var bone = spineBones[i];
                if (bone == null)
                {
                    continue;
                }

                var weight = i < spineWeights.Length ? spineWeights[i] : 1f;
                var pitch = spinePitch * weight;
                if (Mathf.Abs(pitch) <= 0.01f)
                {
                    continue;
                }

                bone.localRotation = bone.localRotation * Quaternion.Euler(pitch, 0f, 0f);
            }
        }

        private void ApplyArmPitch(float totalPitch)
        {
            ApplyBonePitch(clavicleLeft, totalPitch * claviclePitchShare);
            ApplyBonePitch(clavicleRight, totalPitch * claviclePitchShare);
            ApplyBonePitch(shoulderLeft, totalPitch * leftShoulderPitchShare);
            ApplyBonePitch(shoulderRight, totalPitch * rightShoulderPitchShare);
        }

        private static void ApplyBonePitch(Transform bone, float pitch)
        {
            if (bone == null || Mathf.Abs(pitch) <= 0.01f)
            {
                return;
            }

            bone.localRotation = bone.localRotation * Quaternion.Euler(pitch, 0f, 0f);
        }

        private void ResolveBones()
        {
            if (syntyRoot == null)
            {
                return;
            }

            hipsBone = hipsBone != null
                ? hipsBone
                : FindBone(syntyRoot, "Hips", "mixamorig:Hips", "pelvis");

            if (spineBones == null || spineBones.Length == 0)
            {
                spineBones = new[]
                {
                    FindBone(syntyRoot, "Spine_01", "mixamorig:Spine", "mixamorig:Spine1"),
                    FindBone(syntyRoot, "Spine_02", "mixamorig:Spine1", "mixamorig:Spine2"),
                    FindBone(syntyRoot, "Spine_03", "mixamorig:Spine2", "mixamorig:Spine3")
                };
                spineWeights = new[] { 0.22f, 0.52f, 0.26f };
            }

            clavicleLeft = clavicleLeft != null ? clavicleLeft : FindBone(syntyRoot, "Clavicle_L");
            clavicleRight = clavicleRight != null ? clavicleRight : FindBone(syntyRoot, "Clavicle_R");
            shoulderLeft = shoulderLeft != null ? shoulderLeft : FindBone(syntyRoot, "Shoulder_L");
            shoulderRight = shoulderRight != null ? shoulderRight : FindBone(syntyRoot, "Shoulder_R");
        }

        private static Transform FindBone(Transform root, params string[] names)
        {
            if (root == null || names == null)
            {
                return null;
            }

            var all = root.GetComponentsInChildren<Transform>(true);
            for (var n = 0; n < names.Length; n++)
            {
                var targetName = names[n];
                if (string.IsNullOrEmpty(targetName))
                {
                    continue;
                }

                for (var i = 0; i < all.Length; i++)
                {
                    var current = all[i];
                    if (current != null &&
                        string.Equals(current.name, targetName, System.StringComparison.OrdinalIgnoreCase))
                    {
                        return current;
                    }
                }
            }

            return null;
        }
    }
}
