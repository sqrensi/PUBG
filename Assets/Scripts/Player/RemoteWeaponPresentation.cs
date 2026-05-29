using System;
using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Third-person weapon for remote players. Keeps prefab-baked WeaponModel on RemoteWeaponTarget.
    /// Tune RemoteWeaponTarget local pose on the remote prefab to adjust grip.
    /// </summary>
    [DefaultExecutionOrder(450)]
    public sealed class RemoteWeaponPresentation : MonoBehaviour
    {
        [Header("Weapon")]
        [SerializeField] private GameObject weaponPrefab;
        [SerializeField] private Transform attachTarget;
        [SerializeField] private string defaultWeaponPrefabPath = "Assets/Prefabs/AK-47/rifle_001.prefab";
        [SerializeField] private string attachTargetName = "RemoteWeaponTarget";
        [SerializeField] private string rightHandBoneName = "Hand_R";
        [SerializeField] private Vector3 weaponLocalScale = new Vector3(0.35476f, 0.35476f, 0.35476f);

        [Header("Grip Alignment")]
        [SerializeField] private bool alignGripToAttachTarget = true;
        [SerializeField] private string gripTargetName = "RemoteRightHandTarget";
        [SerializeField] private string gripFallbackName = "RightHandTarget";
        [SerializeField] private string leftHandTargetName = "RemoteLeftHandTarget";
        [SerializeField] private string leftHandTargetFallbackName = "LeftHandTarget";

        [Header("Attach Target Defaults")]
        [SerializeField] private Vector3 defaultAttachLocalPosition = new Vector3(-0.0522f, 0.0951f, 0.0199f);
        [SerializeField] private Vector3 defaultAttachLocalEuler = new Vector3(-42.105f, 202.324f, -107.611f);

        [Header("Posture Lean Compensation")]
        [SerializeField] private bool enablePostureLeanCompensation = true;
        [SerializeField] private float postureLeanCompensation = 0.65f;
        [SerializeField] private float postureLeanSmoothTime = 0.12f;

        [Header("Look Pitch Tilt")]
        [SerializeField] private bool enableLookPitchTilt = false;
        [SerializeField] private float lookPitchInfluence = 1f;
        [SerializeField] private float lookPitchWeaponMax = 50f;
        [SerializeField] private float lookPitchSmoothTime = 0.1f;
        [SerializeField] private float crouchPitchCompensation = 30f;
        [SerializeField] private float crouchPitchSmoothTime = 0.12f;

        private Transform weaponRoot;
        private Transform thirdPersonBody;
        private Vector3 baseAttachLocalPosition;
        private Quaternion baseAttachLocalRotation;
        private bool hasBaseAttachPose;
        private float networkLookPitch;
        private bool networkCrouching;
        private float smoothedPitchOffset;
        private float pitchOffsetVelocity;
        private float smoothedCrouchPitchOffset;
        private float crouchPitchOffsetVelocity;
        private float smoothedPostureLeanCompensation;
        private float postureLeanCompensationVelocity;
        private RemoteLookPitchPosture lookPitchPosture;

        public Transform WeaponRoot => weaponRoot;
        public Transform AttachTarget => attachTarget;

        public void SetNetworkLookPitch(float lookPitch)
        {
            networkLookPitch = lookPitch;
        }

        public void SetNetworkCrouchState(bool crouching)
        {
            networkCrouching = crouching;
        }

        private void LateUpdate()
        {
            if (lookPitchPosture == null)
            {
                lookPitchPosture = GetComponent<RemoteLookPitchPosture>();
            }

            ApplyAttachTargetLookPitchTilt();
        }

        private void OnEnable()
        {
            EnsureAttached();
        }

        private void Start()
        {
            EnsureAttached();
        }

        public void Configure(Transform body, GameObject prefab = null)
        {
            thirdPersonBody = body;
            if (prefab != null)
            {
                weaponPrefab = prefab;
            }

            if (weaponPrefab == null)
            {
                weaponPrefab = LoadDefaultWeaponPrefab();
            }

            EnsureAttachTarget(body);
        }

        public void EnsureAttached()
        {
            if (thirdPersonBody == null)
            {
                thirdPersonBody = transform.Find("ThirdPersonBody");
            }

            EnsureAttachTarget(thirdPersonBody);
            if (attachTarget == null)
            {
                Debug.LogWarning("[RemoteWeaponPresentation] RemoteWeaponTarget not found on remote player.");
                return;
            }

            CaptureBaseAttachPose();
            EnsureHierarchyActive(attachTarget);

            if (!IsWeaponRootAlive())
            {
                weaponRoot = null;
            }

            if (weaponRoot == null)
            {
                weaponRoot = FindExistingWeaponModel(transform);
            }

            if (weaponRoot != null)
            {
                PreservePrefabWeaponHierarchy();
                WireRemoteLeftHandIk();
                return;
            }

            if (weaponPrefab == null)
            {
                weaponPrefab = LoadDefaultWeaponPrefab();
            }

            if (weaponPrefab == null)
            {
                Debug.LogWarning("[RemoteWeaponPresentation] weaponPrefab is not assigned on remote player.");
                return;
            }

            SpawnWeaponOnTarget();
        }

        private void PreservePrefabWeaponHierarchy()
        {
            if (weaponRoot == null)
            {
                return;
            }

            if (weaponRoot.parent != null &&
                string.Equals(weaponRoot.parent.name, attachTargetName, StringComparison.Ordinal))
            {
                attachTarget = weaponRoot.parent;
            }

            EnsureHierarchyActive(weaponRoot);
            SetWeaponRenderersEnabled(true);
        }

        private void SpawnWeaponOnTarget()
        {
            var instance = Instantiate(weaponPrefab, attachTarget);
            instance.name = "WeaponModel";
            weaponRoot = instance.transform;
            weaponRoot.localScale = weaponLocalScale;

            if (alignGripToAttachTarget)
            {
                AlignWeaponGripToTarget();
            }
            else
            {
                weaponRoot.localPosition = Vector3.zero;
                weaponRoot.localRotation = Quaternion.identity;
            }

            EnsureHierarchyActive(weaponRoot);
            SetWeaponRenderersEnabled(true);
            WireRemoteLeftHandIk();
        }

        private void EnsureAttachTarget(Transform body)
        {
            if (IsValidSceneTransform(attachTarget) &&
                (body == null || attachTarget.IsChildOf(body)))
            {
                return;
            }

            attachTarget = ResolveAttachTarget(body);
        }

        private static bool IsValidSceneTransform(Transform value)
        {
            return value != null && value.gameObject.scene.IsValid();
        }

        private GameObject LoadDefaultWeaponPrefab()
        {
            if (weaponPrefab != null)
            {
                return weaponPrefab;
            }

#if UNITY_EDITOR
            if (!string.IsNullOrWhiteSpace(defaultWeaponPrefabPath))
            {
                return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(defaultWeaponPrefabPath);
            }
#endif
            return null;
        }

        private bool IsWeaponRootAlive()
        {
            return weaponRoot != null;
        }

        private static Transform FindExistingWeaponModel(Transform searchRoot)
        {
            if (searchRoot == null)
            {
                return null;
            }

            Transform underTarget = null;
            Transform fallback = null;
            var all = searchRoot.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < all.Length; i++)
            {
                var candidate = all[i];
                if (candidate == null ||
                    !string.Equals(candidate.name, "WeaponModel", StringComparison.Ordinal))
                {
                    continue;
                }

                fallback = candidate;
                if (candidate.parent != null &&
                    string.Equals(candidate.parent.name, "RemoteWeaponTarget", StringComparison.Ordinal))
                {
                    underTarget = candidate;
                }
            }

            return underTarget != null ? underTarget : fallback;
        }

        public static Transform EnsureAttachTargetOnHand(
            Transform handBone,
            string targetName = "RemoteWeaponTarget",
            Vector3? localPosition = null,
            Vector3? localEuler = null)
        {
            if (handBone == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(targetName))
            {
                targetName = "RemoteWeaponTarget";
            }

            var existing = handBone.Find(targetName);
            if (existing != null)
            {
                return existing;
            }

            var targetObject = new GameObject(targetName);
            var target = targetObject.transform;
            target.SetParent(handBone, false);
            target.localPosition = localPosition ?? new Vector3(-0.0522f, 0.0951f, 0.0199f);
            target.localRotation = Quaternion.Euler(localEuler ?? new Vector3(-42.105f, 202.324f, -107.611f));
            target.localScale = Vector3.one;
            return target;
        }

        private void CaptureBaseAttachPose()
        {
            if (attachTarget == null)
            {
                return;
            }

            baseAttachLocalPosition = attachTarget.localPosition;
            baseAttachLocalRotation = attachTarget.localRotation;
            hasBaseAttachPose = true;
        }

        private void ApplyAttachTargetLookPitchTilt()
        {
            if (attachTarget == null)
            {
                return;
            }

            if (!hasBaseAttachPose)
            {
                CaptureBaseAttachPose();
            }

            if (!hasBaseAttachPose)
            {
                return;
            }

            attachTarget.localPosition = baseAttachLocalPosition;
            attachTarget.localRotation = baseAttachLocalRotation;

            var totalWeaponPitch = 0f;

            if (enableLookPitchTilt)
            {
                var weaponPitchLimit = Mathf.Clamp(lookPitchWeaponMax, 1f, 89f);
                var clampedPitch = Mathf.Clamp(networkLookPitch, -weaponPitchLimit, weaponPitchLimit);
                var targetPitchOffset = clampedPitch * lookPitchInfluence;
                smoothedPitchOffset = Mathf.SmoothDamp(
                    smoothedPitchOffset,
                    targetPitchOffset,
                    ref pitchOffsetVelocity,
                    Mathf.Max(0.01f, lookPitchSmoothTime));
                totalWeaponPitch += smoothedPitchOffset;
            }
            else
            {
                smoothedPitchOffset = Mathf.SmoothDamp(
                    smoothedPitchOffset,
                    0f,
                    ref pitchOffsetVelocity,
                    Mathf.Max(0.01f, lookPitchSmoothTime));
            }

            var targetCrouchOffset = networkCrouching ? -crouchPitchCompensation : 0f;
            smoothedCrouchPitchOffset = Mathf.SmoothDamp(
                smoothedCrouchPitchOffset,
                targetCrouchOffset,
                ref crouchPitchOffsetVelocity,
                Mathf.Max(0.01f, crouchPitchSmoothTime));

            var targetPostureCompensation = 0f;
            if (enablePostureLeanCompensation && lookPitchPosture != null)
            {
                targetPostureCompensation = -lookPitchPosture.CurrentPostureLeanPitch * postureLeanCompensation;
            }

            smoothedPostureLeanCompensation = Mathf.SmoothDamp(
                smoothedPostureLeanCompensation,
                targetPostureCompensation,
                ref postureLeanCompensationVelocity,
                Mathf.Max(0.01f, postureLeanSmoothTime));

            attachTarget.localRotation = baseAttachLocalRotation *
                Quaternion.Euler(
                    totalWeaponPitch + smoothedCrouchPitchOffset + smoothedPostureLeanCompensation,
                    0f,
                    0f);
        }

        public Transform ResolveAttachTarget(Transform body)
        {
            if (body == null)
            {
                return null;
            }

            var syntyVisual = body.Find("SyntyVisual");
            if (syntyVisual == null)
            {
                return null;
            }

            var handBone = FindBone(syntyVisual, rightHandBoneName, "mixamorig:RightHand");
            if (handBone == null)
            {
                return null;
            }

            return EnsureAttachTargetOnHand(
                handBone,
                attachTargetName,
                defaultAttachLocalPosition,
                defaultAttachLocalEuler);
        }

        private void AlignWeaponGripToTarget()
        {
            if (weaponRoot == null || attachTarget == null)
            {
                return;
            }

            if (weaponRoot.parent != attachTarget)
            {
                weaponRoot.SetParent(attachTarget, false);
            }

            var grip = FindChildRecursive(weaponRoot, gripTargetName)
                ?? FindChildRecursive(weaponRoot, gripFallbackName);
            if (grip != null)
            {
                weaponRoot.position += attachTarget.position - grip.position;
            }

            weaponRoot.localRotation = Quaternion.identity;
        }

        private void WireRemoteLeftHandIk()
        {
            if (weaponRoot == null)
            {
                return;
            }

            if (thirdPersonBody == null)
            {
                thirdPersonBody = transform.Find("ThirdPersonBody");
            }

            var syntyVisual = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
            if (syntyVisual == null)
            {
                return;
            }

            var leftGrip = PlayerWeaponMount.FindWeaponGripAnchor(weaponRoot, leftHandTargetName)
                ?? PlayerWeaponMount.FindWeaponGripAnchor(weaponRoot, leftHandTargetFallbackName);
            if (leftGrip == null)
            {
                Debug.LogWarning("[RemoteWeaponPresentation] Left-hand grip target not found on remote weapon.");
                return;
            }

            var handBinder = GetComponent<RemoteLeftHandIkBinder>();
            if (handBinder == null)
            {
                handBinder = gameObject.AddComponent<RemoteLeftHandIkBinder>();
            }

            handBinder.Configure(syntyVisual, leftGrip);
        }

        private void SetWeaponRenderersEnabled(bool enabled)
        {
            if (weaponRoot == null)
            {
                return;
            }

            var renderers = weaponRoot.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].enabled = enabled;
                }
            }
        }

        private static void EnsureHierarchyActive(Transform node)
        {
            if (node == null)
            {
                return;
            }

            var current = node;
            while (current != null)
            {
                if (!current.gameObject.activeSelf)
                {
                    current.gameObject.SetActive(true);
                }

                current = current.parent;
            }
        }

        private static Transform FindBone(Transform root, params string[] names)
        {
            if (root == null || names == null)
            {
                return null;
            }

            Transform targetWithAttachPoint = null;
            Transform fallback = null;
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
                    if (current == null ||
                        !string.Equals(current.name, targetName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    fallback ??= current;
                    if (current.Find("RemoteWeaponTarget") != null)
                    {
                        targetWithAttachPoint = current;
                    }
                }
            }

            return targetWithAttachPoint != null ? targetWithAttachPoint : fallback;
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
                var current = all[i];
                if (current != null && string.Equals(current.name, childName, StringComparison.Ordinal))
                {
                    return current;
                }
            }

            return null;
        }
    }
}
