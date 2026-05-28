using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Keeps gameplay anchors aligned with animated Synty bones for hitboxes and weapon mount references.
    /// </summary>
    [DefaultExecutionOrder(310)]
    public sealed class SyntyBoneAnchorSync : MonoBehaviour
    {
        [SerializeField] private Transform syntyRoot;
        [SerializeField] private Transform shoulderAnchor;
        [SerializeField] private Transform hipAnchor;
        [SerializeField] private Transform leftHandTarget;
        [SerializeField] private Transform rightHandTarget;
        [SerializeField] private Transform leftFootTarget;
        [SerializeField] private Transform rightFootTarget;
        [SerializeField] private Transform headTarget;

        [Header("Bones")]
        [SerializeField] private Transform spineBone;
        [SerializeField] private Transform hipsBone;
        [SerializeField] private Transform leftHandBone;
        [SerializeField] private Transform rightHandBone;
        [SerializeField] private Transform leftFootBone;
        [SerializeField] private Transform rightFootBone;
        [SerializeField] private Transform headBone;

        public void Configure(
            Transform visualRoot,
            Transform shoulder,
            Transform hip,
            Transform leftHand,
            Transform rightHand,
            Transform leftFoot,
            Transform rightFoot,
            Transform head)
        {
            syntyRoot = visualRoot;
            shoulderAnchor = shoulder;
            hipAnchor = hip;
            leftHandTarget = leftHand;
            rightHandTarget = rightHand;
            leftFootTarget = leftFoot;
            rightFootTarget = rightFoot;
            headTarget = head;
            ResolveBones();
        }

        private void Awake()
        {
            ResolveBones();
        }

        private void LateUpdate()
        {
            if (syntyRoot == null)
            {
                return;
            }

            ResolveBones();
            SyncAnchor(shoulderAnchor, spineBone != null ? spineBone : hipsBone);
            SyncAnchor(hipAnchor, hipsBone);
            SyncAnchor(leftFootTarget, leftFootBone);
            SyncAnchor(rightFootTarget, rightFootBone);
            SyncAnchor(headTarget, headBone);
        }

        private void ResolveBones()
        {
            if (syntyRoot == null)
            {
                return;
            }

            hipsBone = hipsBone != null ? hipsBone : FindBone(syntyRoot, "Hips", "mixamorig:Hips");
            spineBone = spineBone != null
                ? spineBone
                : FindBone(syntyRoot, "Spine_02", "Spine_01", "Spine_03", "mixamorig:Spine2", "mixamorig:Spine1", "mixamorig:Spine");
            headBone = headBone != null ? headBone : FindBone(syntyRoot, "Head", "mixamorig:Head");
            leftHandBone = leftHandBone != null ? leftHandBone : FindBone(syntyRoot, "Hand_L", "mixamorig:LeftHand");
            rightHandBone = rightHandBone != null ? rightHandBone : FindBone(syntyRoot, "Hand_R", "mixamorig:RightHand");
            leftFootBone = leftFootBone != null
                ? leftFootBone
                : FindBone(syntyRoot, "Ball_L", "Foot_L", "Toes_L", "mixamorig:LeftFoot", "mixamorig:LeftToeBase");
            rightFootBone = rightFootBone != null
                ? rightFootBone
                : FindBone(syntyRoot, "Ball_R", "Foot_R", "Toes_R", "mixamorig:RightFoot", "mixamorig:RightToeBase");
        }

        private static void SyncAnchor(Transform anchor, Transform bone)
        {
            if (anchor == null || bone == null)
            {
                return;
            }

            anchor.position = bone.position;
            anchor.rotation = bone.rotation;
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
                if (string.IsNullOrWhiteSpace(targetName))
                {
                    continue;
                }

                for (var i = 0; i < all.Length; i++)
                {
                    var current = all[i];
                    if (current != null && string.Equals(current.name, targetName, System.StringComparison.Ordinal))
                    {
                        return current;
                    }
                }
            }

            return null;
        }
    }
}
