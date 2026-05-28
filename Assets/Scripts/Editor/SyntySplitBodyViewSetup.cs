#if UNITY_EDITOR
using ShooterPrototype.Player;
using UnityEditor;
using UnityEngine;

namespace ShooterPrototype.EditorTools
{
    public static class SyntySplitBodyViewSetup
    {
        private const string FirstPersonViewName = "FirstPersonView";

        [MenuItem("Shooter Prototype/Setup/Enable Split FP/TP Body On Selected Player")]
        public static void EnableSplitBodyOnSelectedPlayer()
        {
            var selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogError("[SyntySplitBodyViewSetup] Select a player prefab instance or root.");
                return;
            }

            WireSplitBodyView(selected);
            EditorUtility.SetDirty(selected);
            Debug.Log($"[SyntySplitBodyViewSetup] Split FP/TP body enabled on '{selected.name}'.");
        }

        public static void WireSplitBodyView(GameObject playerRoot)
        {
            if (playerRoot == null)
            {
                return;
            }

            var cameraPivot = FindChild(playerRoot.transform, "CameraPivot");
            var thirdPersonBody = FindChild(playerRoot.transform, "ThirdPersonBody");
            if (cameraPivot == null || thirdPersonBody == null)
            {
                Debug.LogError("[SyntySplitBodyViewSetup] CameraPivot or ThirdPersonBody is missing.");
                return;
            }

            var firstPersonView = EnsureFirstPersonView(cameraPivot);
            var weaponAnchor = EnsureWeaponAnchorUnder(firstPersonView, thirdPersonBody);
            RemoveDuplicateShoulderAnchorsFromWeaponAnchor(weaponAnchor);
            var leftShoulderAnchor = ResolveShoulderIkAnchor(playerRoot, weaponAnchor, "LeftShoulderIkAnchor");
            var rightShoulderAnchor = ResolveShoulderIkAnchor(playerRoot, weaponAnchor, "RightShoulderIkAnchor");
            CleanupLegacyFirstPersonContent(thirdPersonBody, weaponAnchor);

            var weaponMount = playerRoot.GetComponent<PlayerWeaponMount>();
            if (weaponMount != null && weaponAnchor != null)
            {
                var serializedMount = new SerializedObject(weaponMount);
                serializedMount.FindProperty("weaponParent").objectReferenceValue = weaponAnchor;
                serializedMount.FindProperty("cameraPivot").objectReferenceValue = cameraPivot;
                serializedMount.ApplyModifiedPropertiesWithoutUndo();
            }

            var viewPresentation = playerRoot.GetComponent<PlayerViewPresentation>();
            if (viewPresentation == null)
            {
                viewPresentation = playerRoot.AddComponent<PlayerViewPresentation>();
            }

            var viewSerialized = new SerializedObject(viewPresentation);
            viewSerialized.FindProperty("firstPersonRoot").objectReferenceValue = firstPersonView.gameObject;
            viewSerialized.FindProperty("thirdPersonRoot").objectReferenceValue = thirdPersonBody.gameObject;
            viewSerialized.FindProperty("useSingleModelForFirstPerson").boolValue = false;
            viewSerialized.FindProperty("armNameContains").stringValue = "Hand";
            viewSerialized.FindProperty("weaponNameContains").stringValue = "Weapon";
            viewSerialized.ApplyModifiedPropertiesWithoutUndo();

            var splitPresentation = playerRoot.GetComponent<SyntySplitBodyPresentation>();
            if (splitPresentation == null)
            {
                splitPresentation = playerRoot.AddComponent<SyntySplitBodyPresentation>();
            }

            splitPresentation.Configure(firstPersonView.gameObject, thirdPersonBody.gameObject);

            var handAttachedMount = playerRoot.GetComponent<SyntyHandAttachedWeaponMount>();
            if (handAttachedMount != null)
            {
                var handAttachedSerialized = new SerializedObject(handAttachedMount);
                handAttachedSerialized.FindProperty("attachWeaponToHandsInFirstPerson").boolValue = false;
                handAttachedSerialized.ApplyModifiedPropertiesWithoutUndo();
                handAttachedMount.enabled = false;
            }

            var handBinder = playerRoot.GetComponent<SyntyWeaponHandBinder>();
            if (handBinder != null)
            {
                var handBinderSerialized = new SerializedObject(handBinder);
                handBinderSerialized.FindProperty("onlyApplyInLocalFirstPerson").boolValue = true;
                handBinderSerialized.FindProperty("lockUpperBodyBeforeIk").boolValue = false;
                handBinderSerialized.FindProperty("resetArmPoseBeforeIk").boolValue = false;
                handBinderSerialized.FindProperty("applyIkBeforeRender").boolValue = false;
                handBinderSerialized.FindProperty("useDetachedShoulderAnchorsInFirstPerson").boolValue = true;
                handBinderSerialized.FindProperty("leftShoulderAnchor").objectReferenceValue = leftShoulderAnchor;
                handBinderSerialized.FindProperty("rightShoulderAnchor").objectReferenceValue = rightShoulderAnchor;
                handBinderSerialized.ApplyModifiedPropertiesWithoutUndo();
                handBinder.enabled = true;
                handBinder.SetHandIkEnabled(true);
            }

            var armGate = playerRoot.GetComponent<SyntyFirstPersonArmLocomotionGate>();
            if (armGate != null)
            {
                var armGateSerialized = new SerializedObject(armGate);
                armGateSerialized.FindProperty("suppressArmLocomotionInFirstPerson").boolValue = true;
                armGateSerialized.FindProperty("disableSkeletonAnimatorInFirstPerson").boolValue = true;
                armGateSerialized.ApplyModifiedPropertiesWithoutUndo();
                armGate.SetSuppressArmLocomotionInFirstPerson(true);
                armGate.SetDisableSkeletonAnimatorInFirstPerson(true);
            }

            var binder = playerRoot.GetComponent<SyntyCharacterVisualBinder>();
            if (binder != null)
            {
                var binderSerialized = new SerializedObject(binder);
                binderSerialized.FindProperty("showSyntyMeshInFirstPerson").boolValue = false;
                binderSerialized.FindProperty("showArmsOnlyInFirstPerson").boolValue = false;
                binderSerialized.ApplyModifiedPropertiesWithoutUndo();
            }

            viewPresentation.RefreshViewMode();
        }

        private static Transform EnsureWeaponAnchorUnder(Transform firstPersonView, Transform thirdPersonBody)
        {
            var existingOnFirstPerson = firstPersonView.Find("WeaponAnchor");
            if (existingOnFirstPerson != null)
            {
                return existingOnFirstPerson;
            }

            var existingOnBody = thirdPersonBody.Find("WeaponAnchor");
            if (existingOnBody != null)
            {
                if (TryReparentTransform(existingOnBody, firstPersonView))
                {
                    return existingOnBody;
                }

                var duplicate = new GameObject("WeaponAnchor");
                duplicate.transform.SetParent(firstPersonView, false);
                duplicate.transform.position = existingOnBody.position;
                duplicate.transform.rotation = existingOnBody.rotation;
                duplicate.transform.localScale = existingOnBody.localScale;
                existingOnBody.gameObject.SetActive(false);
                return duplicate.transform;
            }

            var created = new GameObject("WeaponAnchor");
            created.transform.SetParent(firstPersonView, false);
            created.transform.localPosition = Vector3.zero;
            created.transform.localRotation = Quaternion.identity;
            created.transform.localScale = Vector3.one;
            return created.transform;
        }

        private static void RemoveDuplicateShoulderAnchorsFromWeaponAnchor(Transform weaponAnchor)
        {
            if (weaponAnchor == null)
            {
                return;
            }

            RemoveChildAnchorIfPresent(weaponAnchor, "LeftShoulderIkAnchor");
            RemoveChildAnchorIfPresent(weaponAnchor, "RightShoulderIkAnchor");
        }

        private static void RemoveChildAnchorIfPresent(Transform parent, string anchorName)
        {
            var anchor = parent.Find(anchorName);
            if (anchor == null)
            {
                return;
            }

            Object.DestroyImmediate(anchor.gameObject);
        }

        private static Transform ResolveShoulderIkAnchor(
            GameObject playerRoot,
            Transform weaponAnchor,
            string anchorName)
        {
            if (weaponAnchor != null)
            {
                var weaponModel = FindChild(weaponAnchor, "WeaponModel");
                var onWeapon = FindChild(weaponModel != null ? weaponModel : weaponAnchor, anchorName);
                if (onWeapon != null && !IsDirectChildOf(weaponAnchor, onWeapon))
                {
                    return onWeapon;
                }
            }

            var weaponMount = playerRoot.GetComponent<PlayerWeaponMount>();
            if (weaponMount == null)
            {
                return null;
            }

            var serialized = new SerializedObject(weaponMount);
            var weaponPrefabProperty = serialized.FindProperty("weaponPrefab");
            var weaponPrefab = weaponPrefabProperty != null
                ? weaponPrefabProperty.objectReferenceValue as GameObject
                : null;
            if (weaponPrefab != null && FindChild(weaponPrefab.transform, anchorName) == null)
            {
                Debug.LogWarning(
                    $"[SyntySplitBodyViewSetup] {anchorName} not found on weapon prefab '{weaponPrefab.name}'. " +
                    "Shoulder IK anchors are expected to live on the weapon itself.");
            }

            return null;
        }

        private static bool IsDirectChildOf(Transform parent, Transform candidate)
        {
            return candidate != null && candidate.parent == parent;
        }

        private static void CleanupLegacyFirstPersonContent(Transform thirdPersonBody, Transform activeWeaponAnchor)
        {
            var generatedArms = thirdPersonBody.Find("GeneratedFirstPersonArms");
            if (generatedArms != null)
            {
                Object.DestroyImmediate(generatedArms.gameObject);
            }

            var legacyAnchor = thirdPersonBody.Find("WeaponAnchor");
            if (legacyAnchor != null &&
                (activeWeaponAnchor == null || legacyAnchor != activeWeaponAnchor))
            {
                legacyAnchor.gameObject.SetActive(false);
            }
        }

        private static bool TryReparentTransform(Transform child, Transform newParent)
        {
            if (child == null || newParent == null)
            {
                return false;
            }

            if (PrefabUtility.IsPartOfPrefabInstance(child.gameObject))
            {
                return false;
            }

            child.SetParent(newParent, true);
            return true;
        }

        private static Transform EnsureFirstPersonView(Transform cameraPivot)
        {
            var existing = cameraPivot.Find(FirstPersonViewName);
            if (existing != null)
            {
                return existing;
            }

            var firstPersonViewObject = new GameObject(FirstPersonViewName);
            var firstPersonView = firstPersonViewObject.transform;
            firstPersonView.SetParent(cameraPivot, false);
            firstPersonView.localPosition = Vector3.zero;
            firstPersonView.localRotation = Quaternion.identity;
            firstPersonView.localScale = Vector3.one;
            return firstPersonView;
        }

        private static Transform FindChild(Transform root, string childName)
        {
            if (root == null)
            {
                return null;
            }

            if (string.Equals(root.name, childName, System.StringComparison.Ordinal))
            {
                return root;
            }

            var all = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < all.Length; i++)
            {
                var current = all[i];
                if (current != null && string.Equals(current.name, childName, System.StringComparison.Ordinal))
                {
                    return current;
                }
            }

            return null;
        }
    }
}
#endif
