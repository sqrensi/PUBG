#if UNITY_EDITOR
using ShooterPrototype.Player;
using UnityEditor;
using UnityEngine;

namespace ShooterPrototype.EditorTools
{
    public static class PlayerRemotePrefabCreator
    {
        public const string SourcePrefabPath = PlayerCleanPrefabCreator.TargetPrefabPath;
        public const string TargetPrefabPath = "Assets/Prefabs/Player/PlayerCleanRemote.prefab";

        [MenuItem("Shooter Prototype/Setup/Create PlayerClean Remote Prefab (TP for network)")]
        public static void CreatePlayerCleanRemotePrefab()
        {
            var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
            if (sourcePrefab == null)
            {
                Debug.LogError(
                    $"[PlayerRemotePrefabCreator] Source prefab not found: {SourcePrefabPath}. " +
                    "Run Setup PlayerClean Character Arms first.");
                return;
            }

            var instance = PrefabUtility.InstantiatePrefab(sourcePrefab) as GameObject;
            if (instance == null)
            {
                Debug.LogError("[PlayerRemotePrefabCreator] Failed to instantiate source prefab.");
                return;
            }

            try
            {
                instance.name = "PlayerCleanRemote";
                ConfigureRemoteThirdPersonPrefab(instance);
                SavePrefab(instance, TargetPrefabPath);
                AssignSpawnPrefabs(SourcePrefabPath, TargetPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[PlayerRemotePrefabCreator] Saved remote TP prefab: {TargetPrefabPath}");
        }

        public static void ConfigureRemoteThirdPersonPrefab(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var bootstrap = root.GetComponent<RemoteThirdPersonPlayerBootstrap>();
            if (bootstrap == null)
            {
                bootstrap = root.AddComponent<RemoteThirdPersonPlayerBootstrap>();
            }

            bootstrap.ApplyRemoteThirdPersonMode();
            EnsureLineHitboxRig(root);
            WireWeaponMountReferences(root);
            RemoveLocalOnlyComponents(root);
            WireNetworkDefaults(root);
        }

        private static void WireWeaponMountReferences(GameObject root)
        {
            var weaponMount = root.GetComponent<PlayerWeaponMount>();
            if (weaponMount == null)
            {
                return;
            }

            var thirdPersonBody = FindChild(root.transform, "ThirdPersonBody");
            var tpWeaponAnchor = thirdPersonBody != null ? FindChild(thirdPersonBody, "WeaponAnchor") : null;
            var cameraPivot = root.transform.Find("CameraPivot");
            if (tpWeaponAnchor == null)
            {
                return;
            }

            weaponMount.ConfigureWeaponParent(tpWeaponAnchor, cameraPivot);
            weaponMount.SetFirstPersonRigidHandIk(false);

            var serialized = new SerializedObject(weaponMount);
            var weaponParentProperty = serialized.FindProperty("weaponParent");
            if (weaponParentProperty != null)
            {
                weaponParentProperty.objectReferenceValue = tpWeaponAnchor;
            }

            if (cameraPivot != null)
            {
                var cameraPivotProperty = serialized.FindProperty("cameraPivot");
                if (cameraPivotProperty != null)
                {
                    cameraPivotProperty.objectReferenceValue = cameraPivot;
                }
            }

            var hipLockProperty = serialized.FindProperty("hipLockToCameraPivot");
            if (hipLockProperty != null)
            {
                hipLockProperty.boolValue = false;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureLineHitboxRig(GameObject root)
        {
            var thirdPersonBody = FindChild(root.transform, "ThirdPersonBody");
            if (thirdPersonBody == null)
            {
                return;
            }

            var hitboxRig = root.GetComponent<PlayerLineHitboxRig>();
            if (hitboxRig == null)
            {
                hitboxRig = root.AddComponent<PlayerLineHitboxRig>();
            }

            var serialized = new SerializedObject(hitboxRig);
            SetSerializedProperty(serialized, "autoBuildOnAwake", true);
            SetSerializedProperty(serialized, "enableArmHitboxes", false);
            SetSerializedProperty(serialized, "torsoRadius", 0.12f);
            SetSerializedProperty(serialized, "neckRadius", 0.075f);
            SetSerializedProperty(serialized, "armRadius", 0.065f);
            SetSerializedProperty(serialized, "legRadius", 0.08f);
            SetSerializedProperty(serialized, "headRadius", 0.12f);
            SetSerializedReference(serialized, "shoulderAnchor", FindChild(thirdPersonBody, "ShoulderAnchor"));
            SetSerializedReference(serialized, "hipAnchor", FindChild(thirdPersonBody, "HipAnchor"));
            SetSerializedReference(serialized, "leftHandTarget", FindChild(thirdPersonBody, "LeftHandTarget"));
            SetSerializedReference(serialized, "rightHandTarget", FindChild(thirdPersonBody, "RightHandTarget"));
            SetSerializedReference(serialized, "leftFootTarget", FindChild(thirdPersonBody, "LeftFootTarget"));
            SetSerializedReference(serialized, "rightFootTarget", FindChild(thirdPersonBody, "RightFootTarget"));
            SetSerializedReference(serialized, "headCenter", FindChild(thirdPersonBody, "HeadTarget"));
            serialized.ApplyModifiedPropertiesWithoutUndo();
            hitboxRig.BuildOrRefreshHitboxes();
        }

        private static void RemoveLocalOnlyComponents(GameObject root)
        {
            var sync = root.GetComponent<MatchPresenceSync>();
            if (sync != null)
            {
                Object.DestroyImmediate(sync);
            }

            var localMarker = root.GetComponent<LocalPlayerMarker>();
            if (localMarker != null)
            {
                Object.DestroyImmediate(localMarker);
            }

            var handBinder = root.GetComponent<SyntyWeaponHandBinder>();
            if (handBinder != null)
            {
                handBinder.enabled = false;
            }

            var fpsController = root.GetComponent<FpsCharacterController>();
            if (fpsController != null)
            {
                fpsController.enabled = false;
            }
        }

        private static void WireNetworkDefaults(GameObject root)
        {
            var weaponMount = root.GetComponent<PlayerWeaponMount>();
            if (weaponMount != null)
            {
                var serialized = new SerializedObject(weaponMount);
                SetSerializedProperty(serialized, "hipLockToCameraPivot", false);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            var viewPresentation = root.GetComponent<PlayerViewPresentation>();
            if (viewPresentation != null)
            {
                var viewSerialized = new SerializedObject(viewPresentation);
                SetSerializedProperty(viewSerialized, "isLocalPlayer", false);
                viewSerialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        public static void AssignSpawnPrefabs(string localPrefabPath, string remotePrefabPath)
        {
            var localPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(localPrefabPath);
            var remotePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(remotePrefabPath);
            if (localPrefab == null || remotePrefab == null)
            {
                return;
            }

            var spawnManagers = Object.FindObjectsByType<PlayerSpawnManager>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (var i = 0; i < spawnManagers.Length; i++)
            {
                var manager = spawnManagers[i];
                if (manager == null)
                {
                    continue;
                }

                var serialized = new SerializedObject(manager);
                SetSerializedReference(serialized, "playerPrefab", localPrefab);
                SetSerializedReference(serialized, "remotePlayerPrefab", remotePrefab);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(manager);
            }
        }

        private static void SetSerializedProperty(SerializedObject serialized, string propertyName, bool value)
        {
            var property = serialized.FindProperty(propertyName);
            if (property != null)
            {
                property.boolValue = value;
            }
        }

        private static void SetSerializedProperty(SerializedObject serialized, string propertyName, float value)
        {
            var property = serialized.FindProperty(propertyName);
            if (property != null)
            {
                property.floatValue = value;
            }
        }

        private static void SetSerializedReference(
            SerializedObject serialized,
            string propertyName,
            Object value)
        {
            var property = serialized.FindProperty(propertyName);
            if (property != null)
            {
                property.objectReferenceValue = value;
            }
        }

        private static void SavePrefab(GameObject instance, string targetPrefabPath)
        {
            SyntyPlayerPrefabCreator.EnsurePlayerPrefabFolder();
            PrefabUtility.SaveAsPrefabAsset(instance, targetPrefabPath);
            AssetDatabase.ImportAsset(targetPrefabPath, ImportAssetOptions.ForceUpdate);

            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(targetPrefabPath);
            if (saved != null)
            {
                EditorGUIUtility.PingObject(saved);
                Selection.activeObject = saved;
            }
        }

        private static Transform FindChild(Transform root, string childName)
        {
            if (root == null)
            {
                return null;
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
