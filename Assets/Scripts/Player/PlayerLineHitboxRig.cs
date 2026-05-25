using UnityEngine;

namespace ShooterPrototype.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerLineHitboxRig : MonoBehaviour
    {
        [Header("Build")]
        [SerializeField] private bool autoBuildOnAwake = true;
        [SerializeField] private string hitboxRootName = "HitboxesRoot";
        [SerializeField] private bool setAsTrigger = true;
        [SerializeField] private int hitboxLayer = -1;

        [Header("Radii")]
        [SerializeField] private float torsoRadius = 0.085f;
        [SerializeField] private float neckRadius = 0.05f;
        [SerializeField] private float armRadius = 0.065f;
        [SerializeField] private float legRadius = 0.06f;
        [SerializeField] private float headRadius = 0.22f;
        [SerializeField] private bool enableArmHitboxes = false;

        [Header("References (optional, auto-find by name if empty)")]
        [SerializeField] private Transform shoulderAnchor;
        [SerializeField] private Transform hipAnchor;
        [SerializeField] private Transform leftHandTarget;
        [SerializeField] private Transform rightHandTarget;
        [SerializeField] private Transform leftFootTarget;
        [SerializeField] private Transform rightFootTarget;
        [SerializeField] private Transform headCenter;

        private Transform hitboxRoot;
        private CapsuleCollider torsoCapsule;
        private CapsuleCollider neckCapsule;
        private CapsuleCollider leftArmCapsule;
        private CapsuleCollider rightArmCapsule;
        private CapsuleCollider leftLegCapsule;
        private CapsuleCollider rightLegCapsule;
        private SphereCollider headSphere;

        private void Awake()
        {
            ResolveReferences();
            if (autoBuildOnAwake)
            {
                BuildOrRefreshHitboxes();
            }
        }

        private void LateUpdate()
        {
            if (hitboxRoot == null)
            {
                return;
            }

            UpdateAllHitboxes();
        }

        [ContextMenu("Build/Refresh Hitboxes")]
        public void BuildOrRefreshHitboxes()
        {
            ResolveReferences();
            EnsureRoot();
            EnsureColliders();
            ApplyColliderSettings();
            UpdateAllHitboxes();
        }

        [ContextMenu("Remove Hitboxes")]
        public void RemoveHitboxes()
        {
            if (hitboxRoot != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(hitboxRoot.gameObject);
                }
                else
                {
                    DestroyImmediate(hitboxRoot.gameObject);
                }
            }
            hitboxRoot = null;
        }

        private void ResolveReferences()
        {
            shoulderAnchor = shoulderAnchor != null ? shoulderAnchor : FindChildRecursive(transform, "ShoulderAnchor");
            hipAnchor = hipAnchor != null ? hipAnchor : FindChildRecursive(transform, "HipAnchor");
            leftHandTarget = leftHandTarget != null ? leftHandTarget : FindChildRecursive(transform, "LeftHandTarget");
            rightHandTarget = rightHandTarget != null ? rightHandTarget : FindChildRecursive(transform, "RightHandTarget");
            leftFootTarget = leftFootTarget != null ? leftFootTarget : FindChildRecursive(transform, "LeftFootTarget");
            rightFootTarget = rightFootTarget != null ? rightFootTarget : FindChildRecursive(transform, "RightFootTarget");

            if (headCenter == null)
            {
                headCenter = FindChildRecursive(transform, "WhiteMaskHead");
            }
            if (headCenter == null)
            {
                headCenter = FindChildRecursive(transform, "Head");
            }
            if (headCenter == null)
            {
                headCenter = shoulderAnchor;
            }
        }

        private void EnsureRoot()
        {
            if (hitboxRoot == null)
            {
                var existing = FindChildRecursive(transform, hitboxRootName);
                if (existing != null)
                {
                    hitboxRoot = existing;
                }
            }

            if (hitboxRoot == null)
            {
                var rootObj = new GameObject(hitboxRootName);
                hitboxRoot = rootObj.transform;
                hitboxRoot.SetParent(transform, false);
            }
        }

        private void EnsureColliders()
        {
            torsoCapsule = torsoCapsule != null ? torsoCapsule : EnsureCapsule("TorsoHitbox");
            neckCapsule = neckCapsule != null ? neckCapsule : EnsureCapsule("NeckHitbox");
            leftLegCapsule = leftLegCapsule != null ? leftLegCapsule : EnsureCapsule("LeftLegHitbox");
            rightLegCapsule = rightLegCapsule != null ? rightLegCapsule : EnsureCapsule("RightLegHitbox");
            headSphere = headSphere != null ? headSphere : EnsureSphere("HeadHitbox");
            if (enableArmHitboxes)
            {
                leftArmCapsule = leftArmCapsule != null ? leftArmCapsule : EnsureCapsule("LeftArmHitbox");
                rightArmCapsule = rightArmCapsule != null ? rightArmCapsule : EnsureCapsule("RightArmHitbox");
            }
            else
            {
                DisableArmHitboxes();
            }
        }

        private void ApplyColliderSettings()
        {
            var colliders = hitboxRoot.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                var c = colliders[i];
                if (c == null)
                {
                    continue;
                }

                c.isTrigger = setAsTrigger;
                if (hitboxLayer >= 0 && hitboxLayer <= 31)
                {
                    c.gameObject.layer = hitboxLayer;
                }
            }
        }

        private void UpdateAllHitboxes()
        {
            UpdateCapsuleBetween(torsoCapsule, hipAnchor, shoulderAnchor, torsoRadius);
            UpdateCapsuleBetween(neckCapsule, shoulderAnchor, headCenter, neckRadius);
            if (enableArmHitboxes)
            {
                UpdateCapsuleBetween(leftArmCapsule, shoulderAnchor, leftHandTarget, armRadius);
                UpdateCapsuleBetween(rightArmCapsule, shoulderAnchor, rightHandTarget, armRadius);
            }
            else
            {
                DisableArmHitboxes();
            }
            UpdateCapsuleBetween(leftLegCapsule, hipAnchor, leftFootTarget, legRadius);
            UpdateCapsuleBetween(rightLegCapsule, hipAnchor, rightFootTarget, legRadius);
            UpdateHeadSphere();
        }

        private void DisableArmHitboxes()
        {
            if (leftArmCapsule != null)
            {
                leftArmCapsule.enabled = false;
            }

            if (rightArmCapsule != null)
            {
                rightArmCapsule.enabled = false;
            }
        }

        private void UpdateCapsuleBetween(CapsuleCollider capsule, Transform a, Transform b, float radius)
        {
            if (capsule == null || a == null || b == null)
            {
                if (capsule != null)
                {
                    capsule.enabled = false;
                }
                return;
            }

            capsule.enabled = true;
            var tr = capsule.transform;
            var start = a.position;
            var end = b.position;
            var direction = end - start;
            var length = direction.magnitude;
            if (length <= 0.0001f)
            {
                tr.position = start;
                tr.rotation = Quaternion.identity;
                capsule.radius = Mathf.Max(0.01f, radius);
                capsule.height = capsule.radius * 2f;
                capsule.center = Vector3.zero;
                capsule.direction = 1;
                return;
            }

            tr.position = (start + end) * 0.5f;
            tr.rotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
            capsule.direction = 1;
            capsule.radius = Mathf.Max(0.01f, radius);
            capsule.height = Mathf.Max(capsule.radius * 2f, length + capsule.radius * 2f);
            capsule.center = Vector3.zero;
        }

        private void UpdateHeadSphere()
        {
            if (headSphere == null || headCenter == null)
            {
                if (headSphere != null)
                {
                    headSphere.enabled = false;
                }
                return;
            }

            headSphere.enabled = true;
            headSphere.transform.position = headCenter.position;
            headSphere.transform.rotation = Quaternion.identity;
            headSphere.radius = Mathf.Max(0.01f, headRadius);
            headSphere.center = Vector3.zero;
        }

        private CapsuleCollider EnsureCapsule(string name)
        {
            var t = FindOrCreateChild(hitboxRoot, name);
            var capsule = t.GetComponent<CapsuleCollider>();
            if (capsule == null)
            {
                capsule = t.gameObject.AddComponent<CapsuleCollider>();
            }
            return capsule;
        }

        private SphereCollider EnsureSphere(string name)
        {
            var t = FindOrCreateChild(hitboxRoot, name);
            var sphere = t.GetComponent<SphereCollider>();
            if (sphere == null)
            {
                sphere = t.gameObject.AddComponent<SphereCollider>();
            }
            return sphere;
        }

        private static Transform FindOrCreateChild(Transform parent, string childName)
        {
            var existing = FindChildRecursive(parent, childName);
            if (existing != null)
            {
                return existing;
            }

            var obj = new GameObject(childName);
            obj.transform.SetParent(parent, false);
            return obj.transform;
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
                var tr = all[i];
                if (tr != null && string.Equals(tr.name, childName, System.StringComparison.Ordinal))
                {
                    return tr;
                }
            }

            return null;
        }
    }
}
