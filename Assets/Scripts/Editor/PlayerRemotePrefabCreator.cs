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
                PlayerPrefabOptimization.StripRemoteThirdPersonPrefab(instance);
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

            EnsureRemoteWeaponPresentation(root);
            EnsureRemoteLeftHandIkBinder(root);
            EnsureRemoteShotEffects(root);
            bootstrap.ApplyRemoteThirdPersonMode();
            EnsureBoneHitboxRig(root);
            WireRemoteAnimatorController(root);
            RemoveLocalOnlyComponents(root);
        }

        private static void EnsureRemoteShotEffects(GameObject root)
        {
            var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
            var sourceWeaponController = sourcePrefab != null
                ? sourcePrefab.GetComponentInChildren<PlayerWeaponController>(true)
                : null;
            var sourceAudioController = ResolveSourceAudioController(sourcePrefab);
            PlayerPrefabOptimization.EnsureRemoteShotEffects(root, sourceWeaponController);
            PlayerPrefabOptimization.EnsureRemoteAudio(root, sourceAudioController);
        }

        private static PlayerAudioController ResolveSourceAudioController(GameObject sourcePrefab)
        {
            var audio = sourcePrefab != null
                ? sourcePrefab.GetComponentInChildren<PlayerAudioController>(true)
                : null;
            if (audio != null)
            {
                return audio;
            }

            var localPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerCleanPrefabCreator.SourcePrefabPath);
            return localPrefab != null ? localPrefab.GetComponent<PlayerAudioController>() : null;
        }

        private static void WireRemoteAnimatorController(GameObject root)
        {
            var bootstrap = root.GetComponent<RemoteThirdPersonPlayerBootstrap>();
            if (bootstrap == null)
            {
                return;
            }

            var remoteController = SyntyAnimationSetup.LoadRemoteAnimatorControllerAsset();
            if (remoteController == null)
            {
                Debug.LogWarning(
                    "[PlayerRemotePrefabCreator] SyntyRemoteLocomotion.controller missing. " +
                    "Run Rebuild Animation Controller (Blink local + Opsive remote) first.");
                return;
            }

            bootstrap.SetRemoteAnimatorController(remoteController);

            var serialized = new SerializedObject(bootstrap);
            var controllerProperty = serialized.FindProperty("remoteAnimatorController");
            if (controllerProperty != null)
            {
                controllerProperty.objectReferenceValue = remoteController;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void EnsureRemoteWeaponPresentation(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var thirdPersonBody = FindChild(root.transform, "ThirdPersonBody");
            var syntyVisual = thirdPersonBody != null ? FindChild(thirdPersonBody, "SyntyVisual") : null;
            var handBone = syntyVisual != null ? FindChild(syntyVisual, "Hand_R") : null;
            if (handBone == null)
            {
                Debug.LogWarning("[PlayerRemotePrefabCreator] Hand_R not found; remote weapon target skipped.");
                return;
            }

            var attachTarget = RemoteWeaponPresentation.EnsureAttachTargetOnHand(handBone);
            ClearWeaponModelsUnder(attachTarget);
            PlayerWeaponMount.RemoveStrayWeaponModels(root.transform, attachTarget);

            var sourceMount = LoadSourceWeaponMount();

            GameObject weaponPrefabAsset = null;
            if (sourceMount != null)
            {
                var sourceSerialized = new SerializedObject(sourceMount);
                var weaponPrefabProperty = sourceSerialized.FindProperty("weaponPrefab");
                if (weaponPrefabProperty != null)
                {
                    weaponPrefabAsset = weaponPrefabProperty.objectReferenceValue as GameObject;
                }
            }

            var presentation = root.GetComponent<RemoteWeaponPresentation>();
            if (presentation == null)
            {
                presentation = root.AddComponent<RemoteWeaponPresentation>();
            }

            var serialized = new SerializedObject(presentation);
            SetSerializedReference(serialized, "weaponPrefab", weaponPrefabAsset);
            SetSerializedReference(serialized, "attachTarget", attachTarget);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureRemoteLeftHandIkBinder(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var binder = root.GetComponent<RemoteLeftHandIkBinder>();
            if (binder == null)
            {
                binder = root.AddComponent<RemoteLeftHandIkBinder>();
            }

            binder.enabled = true;
        }

        private static PlayerWeaponMount LoadSourceWeaponMount()
        {
            var localPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerCleanPrefabCreator.SourcePrefabPath);
            return localPrefab != null ? localPrefab.GetComponent<PlayerWeaponMount>() : null;
        }

        private static void EnsureBoneHitboxRig(GameObject root)
        {
            var thirdPersonBody = FindChild(root.transform, "ThirdPersonBody");
            var syntyVisual = thirdPersonBody != null ? FindChild(thirdPersonBody, "SyntyVisual") : null;
            if (syntyVisual == null)
            {
                return;
            }

            PlayerHitboxCleanup.RemoveLegacyLineHitboxes(root);

            var hitboxRig = root.GetComponent<PlayerBoneHitboxRig>();
            if (hitboxRig == null)
            {
                hitboxRig = root.AddComponent<PlayerBoneHitboxRig>();
            }

            var serialized = new SerializedObject(hitboxRig);
            SetSerializedProperty(serialized, "autoBuildOnAwake", true);
            SetSerializedProperty(serialized, "enableArmHitboxes", false);
            SetSerializedReference(serialized, "syntyRoot", syntyVisual);
            SetSerializedProperty(serialized, "headRadius", 0.13f);
            SetSerializedProperty(serialized, "headCenterOffset", 0.045f);
            SetSerializedProperty(serialized, "neckRadius", 0.06f);
            SetSerializedProperty(serialized, "neckHeight", 0.16f);
            SetSerializedProperty(serialized, "torsoRadius", 0.11f);
            SetSerializedProperty(serialized, "torsoHeight", 0.3f);
            SetSerializedProperty(serialized, "hipsRadius", 0.12f);
            SetSerializedProperty(serialized, "hipsHeight", 0.23f);
            SetSerializedProperty(serialized, "upperLegRadius", 0.07f);
            SetSerializedProperty(serialized, "upperLegHeight", 0.41f);
            SetSerializedProperty(serialized, "lowerLegRadius", 0.06f);
            SetSerializedProperty(serialized, "lowerLegHeight", 0.39f);
            SetSerializedProperty(serialized, "footRadius", 0.07f);
            SetSerializedProperty(serialized, "footHeight", 0.18f);
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

        private static void ClearWeaponModelsUnder(Transform parent)
        {
            if (parent == null)
            {
                return;
            }

            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                if (child != null &&
                    string.Equals(child.name, "WeaponModel", System.StringComparison.Ordinal))
                {
                    Object.DestroyImmediate(child.gameObject);
                }
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
