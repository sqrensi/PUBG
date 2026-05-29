using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Procedural crouch posture for remote players: spine bend on top of normal locomotion clips.
    /// </summary>
    [DefaultExecutionOrder(305)]
    public sealed class SyntyCrouchPosture : MonoBehaviour
    {
        [SerializeField] private Transform syntyRoot;
        [SerializeField] private ProceduralLocomotionRig locomotionRig;
        [SerializeField] private bool remoteOnly = true;

        [Header("Blend")]
        [SerializeField] private float crouchBlendSmoothTime = 0.16f;

        [Header("Spine Bend")]
        [SerializeField] private float spinePitchDegrees = 40f;
        [SerializeField] private float hipsPitchDegrees = 20f;

        private Transform[] spineBones;
        private Transform hipsBone;
        private float[] spineWeights;
        private float crouchBlend;
        private float crouchBlendVelocity;

        public void Configure(Transform thirdPersonBody, ProceduralLocomotionRig rig)
        {
            Configure(thirdPersonBody, rig, spinePitchDegrees, hipsPitchDegrees);
        }

        public void Configure(
            Transform thirdPersonBody,
            ProceduralLocomotionRig rig,
            float spinePitch,
            float hipsPitch)
        {
            locomotionRig = rig;
            syntyRoot = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
            remoteOnly = true;
            spinePitchDegrees = spinePitch;
            hipsPitchDegrees = hipsPitch;
            enabled = true;
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
            if (!ShouldApply() || syntyRoot == null)
            {
                crouchBlend = Mathf.SmoothDamp(
                    crouchBlend,
                    0f,
                    ref crouchBlendVelocity,
                    Mathf.Max(0.01f, crouchBlendSmoothTime));
                return;
            }

            ResolveBones();
            var targetBlend = 0f;
            if (locomotionRig != null &&
                locomotionRig.NetworkCrouching &&
                locomotionRig.CurrentJumpState == 0)
            {
                targetBlend = 1f;
            }

            crouchBlend = Mathf.SmoothDamp(
                crouchBlend,
                targetBlend,
                ref crouchBlendVelocity,
                Mathf.Max(0.01f, crouchBlendSmoothTime));

            if (crouchBlend <= 0.0001f)
            {
                return;
            }

            ApplyPosture(crouchBlend);
        }

        private bool ShouldApply()
        {
            if (!remoteOnly)
            {
                return true;
            }

            return GetComponent<RemoteThirdPersonPlayerBootstrap>() != null;
        }

        private void ApplyPosture(float blend)
        {
            if (hipsBone != null && Mathf.Abs(hipsPitchDegrees) > 0.01f)
            {
                var hipsOffset = Quaternion.Euler(hipsPitchDegrees * blend, 0f, 0f);
                hipsBone.localRotation = hipsBone.localRotation * hipsOffset;
            }

            if (spineBones == null || spineWeights == null)
            {
                return;
            }

            for (var i = 0; i < spineBones.Length; i++)
            {
                var bone = spineBones[i];
                if (bone == null)
                {
                    continue;
                }

                var weight = i < spineWeights.Length ? spineWeights[i] : 1f;
                var pitch = spinePitchDegrees * weight * blend;
                if (Mathf.Abs(pitch) <= 0.01f)
                {
                    continue;
                }

                bone.localRotation = bone.localRotation * Quaternion.Euler(pitch, 0f, 0f);
            }
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
