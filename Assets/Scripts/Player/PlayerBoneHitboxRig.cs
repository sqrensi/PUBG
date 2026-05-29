using UnityEngine;

namespace ShooterPrototype.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerBoneHitboxRig : MonoBehaviour
    {
        private const string HitboxChildPrefix = "BoneHitbox_";

        [Header("Build")]
        [SerializeField] private bool autoBuildOnAwake = true;
        [SerializeField] private Transform syntyRoot;
        [SerializeField] private bool setAsTrigger = true;
        [SerializeField] private int hitboxLayer = -1;
        [SerializeField] private bool enableArmHitboxes;

        [Header("Radii")]
        [SerializeField] private float headRadius = 0.13f;
        [SerializeField] private float headCenterOffset = 0.045f;
        [SerializeField] private float neckRadius = 0.06f;
        [SerializeField] private float neckHeight = 0.16f;
        [SerializeField] private float torsoRadius = 0.11f;
        [SerializeField] private float torsoHeight = 0.3f;
        [SerializeField] private float hipsRadius = 0.12f;
        [SerializeField] private float hipsHeight = 0.23f;
        [SerializeField] private float upperLegRadius = 0.07f;
        [SerializeField] private float upperLegHeight = 0.41f;
        [SerializeField] private float lowerLegRadius = 0.06f;
        [SerializeField] private float lowerLegHeight = 0.39f;
        [SerializeField] private float footRadius = 0.07f;
        [SerializeField] private float footHeight = 0.18f;
        [SerializeField] private float armRadius = 0.055f;
        [SerializeField] private float upperArmHeight = 0.27f;
        [SerializeField] private float lowerArmHeight = 0.25f;

        private void Awake()
        {
            PlayerHitboxCleanup.RemoveLegacyLineHitboxes(gameObject);
            ResolveSyntyRoot();
            if (autoBuildOnAwake)
            {
                BuildOrRefreshHitboxes();
            }
        }

        [ContextMenu("Build/Refresh Bone Hitboxes")]
        public void BuildOrRefreshHitboxes()
        {
            ResolveSyntyRoot();
            if (syntyRoot == null)
            {
                return;
            }

            RemoveLegacyLineHitboxes();
            BuildHead();
            BuildNeck();
            BuildTorso();
            BuildLegs();
            if (enableArmHitboxes)
            {
                BuildArms();
            }
            else
            {
                DisableArmHitboxes();
            }
        }

        [ContextMenu("Remove Bone Hitboxes")]
        public void RemoveHitboxes()
        {
            if (syntyRoot == null)
            {
                return;
            }

            var markers = syntyRoot.GetComponentsInChildren<PlayerBoneHitbox>(true);
            for (var i = 0; i < markers.Length; i++)
            {
                var marker = markers[i];
                if (marker == null)
                {
                    continue;
                }

                var hitboxTransform = marker.transform;
                if (Application.isPlaying)
                {
                    Destroy(hitboxTransform.gameObject);
                }
                else
                {
                    DestroyImmediate(hitboxTransform.gameObject);
                }
            }
        }

        public void Configure(Transform visualRoot)
        {
            syntyRoot = visualRoot;
        }

        private void ResolveSyntyRoot()
        {
            if (syntyRoot != null)
            {
                return;
            }

            var thirdPersonBody = transform.Find("ThirdPersonBody");
            syntyRoot = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
        }

        private void RemoveLegacyLineHitboxes()
        {
            PlayerHitboxCleanup.RemoveLegacyLineHitboxes(gameObject);
        }

        private void BuildHead()
        {
            var bone = FindBone("Head", "mixamorig:Head");
            EnsureSphereHitbox(bone, "Head", PlayerBoneHitZone.Head, headRadius, new Vector3(0f, headCenterOffset, 0f));
        }

        private void BuildNeck()
        {
            var bone = FindBone("Neck", "Neck_M", "mixamorig:Neck");
            EnsureCapsuleHitbox(bone, "Neck", PlayerBoneHitZone.Neck, neckRadius, neckHeight);
        }

        private void BuildTorso()
        {
            var spine = FindBone("Spine_02", "Spine_01", "Spine_03", "mixamorig:Spine2", "mixamorig:Spine1", "mixamorig:Spine");
            EnsureCapsuleHitbox(spine, "Torso", PlayerBoneHitZone.Body, torsoRadius, torsoHeight);

            var hips = FindBone("Hips", "mixamorig:Hips", "pelvis");
            EnsureCapsuleHitbox(hips, "Hips", PlayerBoneHitZone.Body, hipsRadius, hipsHeight);
        }

        private void BuildLegs()
        {
            var leftUpperLeg = FindBone("Thigh_L", "UpperLeg_L", "mixamorig:LeftUpLeg");
            EnsureCapsuleHitbox(leftUpperLeg, "LeftUpperLeg", PlayerBoneHitZone.Leg, upperLegRadius, upperLegHeight);

            var rightUpperLeg = FindBone("Thigh_R", "UpperLeg_R", "mixamorig:RightUpLeg");
            EnsureCapsuleHitbox(rightUpperLeg, "RightUpperLeg", PlayerBoneHitZone.Leg, upperLegRadius, upperLegHeight);

            var leftLowerLeg = FindBone("Shin_L", "Knee_L", "LowerLeg_L", "mixamorig:LeftLeg");
            EnsureCapsuleHitbox(leftLowerLeg, "LeftLowerLeg", PlayerBoneHitZone.Leg, lowerLegRadius, lowerLegHeight);

            var rightLowerLeg = FindBone("Shin_R", "Knee_R", "LowerLeg_R", "mixamorig:RightLeg");
            EnsureCapsuleHitbox(rightLowerLeg, "RightLowerLeg", PlayerBoneHitZone.Leg, lowerLegRadius, lowerLegHeight);

            var leftFoot = FindBone("Foot_L", "Ball_L", "Toes_L", "mixamorig:LeftFoot", "mixamorig:LeftToeBase");
            EnsureCapsuleHitbox(leftFoot, "LeftFoot", PlayerBoneHitZone.Leg, footRadius, footHeight);

            var rightFoot = FindBone("Foot_R", "Ball_R", "Toes_R", "mixamorig:RightFoot", "mixamorig:RightToeBase");
            EnsureCapsuleHitbox(rightFoot, "RightFoot", PlayerBoneHitZone.Leg, footRadius, footHeight);
        }

        private void BuildArms()
        {
            var leftUpperArm = FindBone("Shoulder_L", "UpperArm_L", "mixamorig:LeftArm");
            EnsureCapsuleHitbox(leftUpperArm, "LeftUpperArm", PlayerBoneHitZone.Body, armRadius, upperArmHeight);

            var rightUpperArm = FindBone("Shoulder_R", "UpperArm_R", "mixamorig:RightArm");
            EnsureCapsuleHitbox(rightUpperArm, "RightUpperArm", PlayerBoneHitZone.Body, armRadius, upperArmHeight);

            var leftLowerArm = FindBone("Elbow_L", "LowerArm_L", "mixamorig:LeftForeArm");
            EnsureCapsuleHitbox(leftLowerArm, "LeftLowerArm", PlayerBoneHitZone.Body, armRadius, lowerArmHeight);

            var rightLowerArm = FindBone("Elbow_R", "LowerArm_R", "mixamorig:RightForeArm");
            EnsureCapsuleHitbox(rightLowerArm, "RightLowerArm", PlayerBoneHitZone.Body, armRadius, lowerArmHeight);
        }

        private void DisableArmHitboxes()
        {
            DisableNamedHitbox("LeftUpperArm");
            DisableNamedHitbox("RightUpperArm");
            DisableNamedHitbox("LeftLowerArm");
            DisableNamedHitbox("RightLowerArm");
        }

        private void DisableNamedHitbox(string suffix)
        {
            if (syntyRoot == null)
            {
                return;
            }

            var hitboxName = HitboxChildPrefix + suffix;
            var existing = syntyRoot.Find(hitboxName);
            if (existing == null)
            {
                var all = syntyRoot.GetComponentsInChildren<Transform>(true);
                for (var i = 0; i < all.Length; i++)
                {
                    var tr = all[i];
                    if (tr != null && string.Equals(tr.name, hitboxName, System.StringComparison.Ordinal))
                    {
                        existing = tr;
                        break;
                    }
                }
            }

            if (existing == null)
            {
                return;
            }

            var collider = existing.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
            }
        }

        private void EnsureSphereHitbox(
            Transform bone,
            string suffix,
            PlayerBoneHitZone zone,
            float radius,
            Vector3 center = default)
        {
            if (bone == null)
            {
                return;
            }

            var hitboxTransform = EnsureHitboxTransform(bone, suffix);
            var sphere = hitboxTransform.GetComponent<SphereCollider>();
            if (sphere == null)
            {
                sphere = hitboxTransform.gameObject.AddComponent<SphereCollider>();
            }

            sphere.enabled = true;
            sphere.isTrigger = setAsTrigger;
            sphere.radius = Mathf.Max(0.01f, radius);
            sphere.center = center;
            ApplyColliderLayer(hitboxTransform.gameObject);
            EnsureMarker(hitboxTransform.gameObject, zone);
        }

        private void EnsureCapsuleHitbox(
            Transform bone,
            string suffix,
            PlayerBoneHitZone zone,
            float radius,
            float height)
        {
            if (bone == null)
            {
                return;
            }

            var hitboxTransform = EnsureHitboxTransform(bone, suffix);
            var capsule = hitboxTransform.GetComponent<CapsuleCollider>();
            if (capsule == null)
            {
                capsule = hitboxTransform.gameObject.AddComponent<CapsuleCollider>();
            }

            capsule.enabled = true;
            capsule.isTrigger = setAsTrigger;
            capsule.direction = 1;
            capsule.radius = Mathf.Max(0.01f, radius);
            capsule.height = Mathf.Max(capsule.radius * 2f, height);
            capsule.center = Vector3.zero;
            ApplyColliderLayer(hitboxTransform.gameObject);
            EnsureMarker(hitboxTransform.gameObject, zone);
        }

        private Transform EnsureHitboxTransform(Transform bone, string suffix)
        {
            var hitboxName = HitboxChildPrefix + suffix;
            Transform hitboxTransform = null;
            for (var i = 0; i < bone.childCount; i++)
            {
                var child = bone.GetChild(i);
                if (child != null && string.Equals(child.name, hitboxName, System.StringComparison.Ordinal))
                {
                    hitboxTransform = child;
                    break;
                }
            }

            if (hitboxTransform == null)
            {
                var hitboxObject = new GameObject(hitboxName);
                hitboxTransform = hitboxObject.transform;
                hitboxTransform.SetParent(bone, false);
            }

            hitboxTransform.localPosition = Vector3.zero;
            hitboxTransform.localRotation = Quaternion.identity;
            hitboxTransform.localScale = Vector3.one;
            return hitboxTransform;
        }

        private void ApplyColliderLayer(GameObject hitboxObject)
        {
            if (hitboxLayer >= 0 && hitboxLayer <= 31)
            {
                hitboxObject.layer = hitboxLayer;
            }
        }

        private static void EnsureMarker(GameObject hitboxObject, PlayerBoneHitZone zone)
        {
            var marker = hitboxObject.GetComponent<PlayerBoneHitbox>();
            if (marker == null)
            {
                marker = hitboxObject.AddComponent<PlayerBoneHitbox>();
            }

            marker.Configure(zone);
        }

        private Transform FindBone(params string[] names)
        {
            if (syntyRoot == null || names == null)
            {
                return null;
            }

            var all = syntyRoot.GetComponentsInChildren<Transform>(true);
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
                    if (current == null || IsExcludedBoneBranch(current))
                    {
                        continue;
                    }

                    if (string.Equals(current.name, targetName, System.StringComparison.OrdinalIgnoreCase))
                    {
                        return current;
                    }
                }
            }

            return null;
        }

        private static bool IsExcludedBoneBranch(Transform bone)
        {
            var current = bone;
            while (current != null)
            {
                var name = current.name;
                if (string.Equals(name, "RemoteWeaponTarget", System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "WeaponModel", System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "GeneratedFirstPersonArms", System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }
    }
}
